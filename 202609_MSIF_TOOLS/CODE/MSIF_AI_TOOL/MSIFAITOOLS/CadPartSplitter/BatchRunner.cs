using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace CadPartSplitter
{
    /// <summary>
    /// 批量处理里单个图纸的结果（成功 / 失败、零件数、输出目录、耗时）。
    /// </summary>
    public sealed class BatchItemResult
    {
        /// <summary>输入 PDF 的完整路径。</summary>
        public string InputFile { get; set; }

        /// <summary>这张图纸结果的输出目录（<c>&lt;输出根目录&gt;\&lt;图纸名&gt;</c>）。</summary>
        public string OutputDirectory { get; set; }

        /// <summary>是否处理成功（单张失败不影响后面的图纸）。</summary>
        public bool Success { get; set; }

        /// <summary>分割出的零件数量；失败时为 0。</summary>
        public int PartCount { get; set; }

        /// <summary>失败原因（<c>异常类型：消息</c>）；成功时为 null。</summary>
        public string ErrorMessage { get; set; }

        /// <summary>这张图纸的处理耗时（毫秒）。</summary>
        public long ElapsedMilliseconds { get; set; }
    }

    /// <summary>
    /// 批量处理：把一个文件夹里的 PDF 逐张交给 <see cref="PartSplitterService.Run(string, DetectorOptions)"/>，
    /// 一套识别参数走完整个文件夹。
    ///
    /// 约定：
    ///   · 只处理 <c>*.pdf</c>（扩展名比较不分大小写）；默认只看当前文件夹，
    ///     <c>recursive = true</c> 时才连子文件夹一起扫（命令行 <c>--recursive</c>）；
    ///   · 每张图纸仍各自落到独立子文件夹：<c>&lt;输出根目录&gt;\&lt;图纸名&gt;</c>，
    ///     输出根目录留空时取 <c>&lt;输入文件夹&gt;\AiAppData</c> —— 与单文件的
    ///     <c>&lt;图纸目录&gt;\AiAppData\&lt;图纸名&gt;</c> 完全同构，下游 AI 环节不用区分单张 / 批量；
    ///   · 逐张串行处理（PDFium / OpenCV 都是进程级重资源，并行只会互相抢内存），
    ///     单张失败只记 <c>[ERROR]</c> 并继续下一张，最后汇总成功 / 失败 / 零件总数；
    ///   · 页码（<see cref="DetectorOptions.PdfPageIndex"/>）对每张图同样生效；
    ///     某张 PDF 页数不够时记为失败并写明原因（不静默换页）。
    /// </summary>
    public static class BatchRunner
    {
        /// <summary>批量模式下要处理的扩展名（比较不分大小写）。</summary>
        public const string PdfExtension = ".pdf";

        /// <summary>
        /// 列出文件夹下的 PDF（仅当前层，按完整路径排序）。
        /// 目录不存在或无权访问时返回空列表，不抛异常。
        /// </summary>
        public static List<string> FindPdfFiles(string directory)
        {
            return FindPdfFiles(directory, false);
        }

        /// <summary>
        /// 列出文件夹下的 PDF（<paramref name="recursive"/> = true 时含子文件夹），按完整路径排序。
        /// 排序是为了让处理顺序稳定（日志与结果可复现）。
        /// </summary>
        public static List<string> FindPdfFiles(string directory, bool recursive)
        {
            var files = new List<string>();

            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return files;

            string[] all;

            try
            {
                all = Directory.GetFiles(
                    directory,
                    "*",
                    recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
            }
            catch (UnauthorizedAccessException)
            {
                return files;
            }
            catch (DirectoryNotFoundException)
            {
                return files;
            }

            foreach (string file in all)
            {
                // 不用 "*.pdf" 通配：Windows 上它会把 *.pdfx、*.pdf_ 之类的长扩展名也一起匹配进来
                if (string.Equals(Path.GetExtension(file), PdfExtension, StringComparison.OrdinalIgnoreCase))
                    files.Add(file);
            }

            files.Sort(StringComparer.OrdinalIgnoreCase);
            return files;
        }

        /// <summary>
        /// 批量处理一个文件夹（仅当前层 PDF）。参数见
        /// <see cref="Run(string, DetectorOptions, bool, Action{int, int, string})"/>。
        /// </summary>
        public static List<BatchItemResult> Run(string directory, DetectorOptions options)
        {
            return Run(directory, options, false, null);
        }

        /// <summary>
        /// 批量处理一个文件夹，逐张返回结果（顺序与 <see cref="FindPdfFiles"/> 一致）。
        ///
        /// <paramref name="options"/> 的 <see cref="DetectorOptions.OutputDirectory"/> 在批量模式下当作
        /// 「输出根目录」：留空取 <c>&lt;输入文件夹&gt;\AiAppData</c>，每张图纸再拼 <c>\&lt;图纸名&gt;</c>。
        /// 循环中会临时改写该属性，结束时（含异常）还原成调用方传进来的值。
        /// </summary>
        /// <param name="directory">输入文件夹。</param>
        /// <param name="options">识别参数（含日志回调）；为 null 时抛 <see cref="ArgumentNullException"/>。</param>
        /// <param name="recursive">是否连子文件夹一起处理。</param>
        /// <param name="progress">每张处理完的回调：<c>(已完成数, 总数, 刚处理完的文件)</c>，可为 null。</param>
        public static List<BatchItemResult> Run(
            string directory,
            DetectorOptions options,
            bool recursive,
            Action<int, int, string> progress)
        {
            if (options == null)
                throw new ArgumentNullException("options");

            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("输入文件夹为空。", "directory");

            if (!Directory.Exists(directory))
                throw new DirectoryNotFoundException("输入文件夹不存在：" + directory);

            string inputDir = Path.GetFullPath(directory);
            List<string> files = FindPdfFiles(inputDir, recursive);

            options.Log("[INFO] 批量输入  ：" + inputDir + (recursive ? "（含子文件夹）" : "（仅当前文件夹）"));
            options.Log("[INFO] 找到 PDF  ：" + files.Count + " 个");

            if (files.Count == 0)
            {
                options.Log("[WARN] 该文件夹下没有 PDF，未做任何处理。");
                return new List<BatchItemResult>();
            }

            // 输出根目录：留空 → <输入文件夹>\AiAppData（每张图纸再各自一个子文件夹）
            string outputRoot = string.IsNullOrWhiteSpace(options.OutputDirectory)
                                    ? DetectorOptions.GetDefaultBatchOutputDirectory(inputDir)
                                    : options.OutputDirectory;

            options.Log("[INFO] 输出根目录：" + Path.GetFullPath(outputRoot));

            var results = new List<BatchItemResult>(files.Count);
            string originalOutput = options.OutputDirectory;   // 逐张改写，结束时还原
            var totalWatch = Stopwatch.StartNew();

            try
            {
                for (int i = 0; i < files.Count; i++)
                {
                    string file = files[i];
                    string subfolder = DetectorOptions.GetDrawingFolderName(file, options.PdfPageIndex);
                    string outputDir = string.IsNullOrEmpty(subfolder)
                                           ? outputRoot
                                           : Path.Combine(outputRoot, subfolder);

                    var item = new BatchItemResult
                    {
                        InputFile       = file,
                        OutputDirectory = outputDir
                    };

                    options.Log(string.Empty);
                    options.Log("[INFO] [" + (i + 1) + "/" + files.Count + "] " + Path.GetFileName(file));

                    var watch = Stopwatch.StartNew();

                    try
                    {
                        // 页码超出该 PDF 的页数 → 明确失败，不静默换页（不同图纸页数可能不一样）
                        int pages = TryGetPageCount(file);
                        if (pages > 0 && options.PdfPageIndex >= pages)
                        {
                            throw new InvalidOperationException(
                                "页码 " + options.PdfPageIndex + " 超出该 PDF 的页数"
                                + "（共 " + pages + " 页，页码 0 ~ " + (pages - 1) + "）");
                        }

                        options.OutputDirectory = outputDir;
                        List<PartInfo> parts = PartSplitterService.Run(file, options);

                        watch.Stop();
                        item.Success             = true;
                        item.PartCount           = parts.Count;
                        item.ElapsedMilliseconds = watch.ElapsedMilliseconds;
                        options.Log("[DONE] " + Path.GetFileName(file) + "：" + parts.Count + " 个零件 → " + outputDir);
                    }
                    catch (Exception ex)
                    {
                        watch.Stop();
                        item.Success             = false;
                        item.ErrorMessage        = ex.GetType().Name + "：" + ex.Message;
                        item.ElapsedMilliseconds = watch.ElapsedMilliseconds;
                        options.Log("[ERROR] " + Path.GetFileName(file) + "：" + item.ErrorMessage);
                    }

                    results.Add(item);
                    RaiseProgress(progress, i + 1, files.Count, file);
                }
            }
            finally
            {
                options.OutputDirectory = originalOutput;
            }

            totalWatch.Stop();
            LogSummary(options, results, totalWatch.ElapsedMilliseconds);

            // 流水线最后一步：图纸文件夹里的 Excel 部品表 → <输出根目录>\classify\tz.json
            // （整批共用一份，与 classify\PdfInfoList.txt 同目录；读「カメラ」sheet 的
            //  品種 / 部品名 / 枠区分 / 部品コード / 追加対応特注寸法 五列）。
            // 逐张处理完之后扫一次：同一批图纸的 Excel 只读一遍，不会每张图纸重写一次。
            if (options.BuildTzJson)
            {
                TzJsonBuilder.Build(
                    inputDir,
                    PdfInfoListBuilder.GetClassifyDirectory(outputRoot, null),
                    recursive,
                    options);
            }

            return results;
        }

        /// <summary>把成功 / 失败 / 零件总数写进日志（命令行与界面共用）。</summary>
        private static void LogSummary(DetectorOptions options, List<BatchItemResult> results, long elapsedMs)
        {
            int ok = 0;
            int failed = 0;
            int partTotal = 0;

            foreach (BatchItemResult item in results)
            {
                if (item.Success)
                {
                    ok++;
                    partTotal += item.PartCount;
                }
                else
                {
                    failed++;
                }
            }

            options.Log(string.Empty);
            options.Log("[批量完成] 共 " + results.Count + " 张：成功 " + ok + "，失败 " + failed
                        + "，零件合计 " + partTotal + " 个，耗时 "
                        + (elapsedMs / 1000.0).ToString("0.0") + " 秒。");
        }

        /// <summary>进度回调；回调抛异常只当作界面刷新失败，不打断批量处理。</summary>
        private static void RaiseProgress(Action<int, int, string> progress, int done, int total, string file)
        {
            if (progress == null)
                return;

            try
            {
                progress(done, total, file);
            }
            catch (Exception)
            {
                // 忽略：进度只影响界面刷新
            }
        }

        /// <summary>
        /// 读 PDF 页数；读不出来（文件被占用 / 不是有效 PDF）时返回 0，
        /// 让后面的渲染阶段去报具体错误，不在这里提前打断。
        /// </summary>
        private static int TryGetPageCount(string pdfPath)
        {
            try
            {
                return PdfRenderer.GetPageCount(pdfPath);
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }
}
