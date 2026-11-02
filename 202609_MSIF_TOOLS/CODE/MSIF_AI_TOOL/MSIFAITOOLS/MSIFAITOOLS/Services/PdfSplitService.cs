using System;
using System.Collections.Generic;
using System.IO;

namespace MSIFAITOOLS.Services
{
    /// <summary>日志级别，供界面按级别着色。</summary>
    internal enum LogKind
    {
        /// <summary>一般信息。</summary>
        Info,

        /// <summary>成功信息。</summary>
        Success,

        /// <summary>警告信息。</summary>
        Warning,

        /// <summary>错误信息。</summary>
        Error
    }

    /// <summary>单个文件的处理进度。</summary>
    internal sealed class PdfSplitProgress
    {
        public PdfSplitProgress(int index, int total, string fileName, string stage)
        {
            Index = index;
            Total = total;
            FileName = fileName;
            Stage = stage;
        }

        /// <summary>当前处理到第几个文件（从 1 开始）。</summary>
        public int Index { get; }

        /// <summary>待处理的 PDF 总数。</summary>
        public int Total { get; }

        /// <summary>当前文件名（含扩展名）。</summary>
        public string FileName { get; }

        /// <summary>当前阶段说明。</summary>
        public string Stage { get; }

        /// <summary>完成百分比（0-100）。</summary>
        public int Percent
        {
            get { return Total <= 0 ? 0 : (int)Math.Round(Index * 100.0 / Total); }
        }
    }

    /// <summary>批量分解的执行结果。</summary>
    internal sealed class PdfSplitResult
    {
        public PdfSplitResult(int total, int succeeded, IList<string> errors,
            string outputDirectory, string classifyDirectory, IList<PdfClassification> classifications)
        {
            Total = total;
            Succeeded = succeeded;
            Errors = new List<string>(errors ?? new string[0]).AsReadOnly();
            OutputDirectory = outputDirectory;
            ClassifyDirectory = classifyDirectory;
            Classifications = classifications == null
                ? new List<PdfClassification>().AsReadOnly()
                : classifications;
        }

        /// <summary>PDF 总数。</summary>
        public int Total { get; }

        /// <summary>完全成功的 PDF 数量。</summary>
        public int Succeeded { get; }

        /// <summary>失败明细，每条为 “文件名: 失败原因”。</summary>
        public IList<string> Errors { get; }

        /// <summary>分解输出根目录（&lt;文件夹&gt;\AiAppData；其下每个 PDF 一个以図番号命名的子文件夹）。</summary>
        public string OutputDirectory { get; }

        /// <summary>分类结果目录（&lt;文件夹&gt;\AiAppData\classify）。</summary>
        public string ClassifyDirectory { get; }

        /// <summary>每个 PDF 的分类结果（与文件名顺序一致）。</summary>
        public IList<PdfClassification> Classifications { get; }

        /// <summary>是否存在失败项。</summary>
        public bool HasErrors
        {
            get { return Errors.Count > 0; }
        }

        /// <summary>失败数量。</summary>
        public int Failed
        {
            get { return Total - Succeeded; }
        }
    }

    /// <summary>
    /// PDF 批量分解服务（纯 .NET 实现，替代原 PDF_SPLIT.py）。
    /// 对指定文件夹第一层的所有 PDF 生成结果，统一输出到 &lt;文件夹&gt;\AiAppData 下的「図番号」子文件夹
    /// （不存在时自动创建，子文件夹名 = PDF 文件名去掉扩展名）：
    ///   1) &lt;文件夹&gt;\AiAppData\&lt;図番号&gt;\xy.txt        —— 每页文字及其坐标；
    ///      &lt;文件夹&gt;\AiAppData\&lt;図番号&gt;\xy_imgtext.txt —— 图面文字及其坐标
    ///      （按「番号」「タイプ名」表头的更靠左者作水平分界，只取分界左侧、页面高度 85% 以内的文字）；
    ///   2) &lt;文件夹&gt;\AiAppData\&lt;図番号&gt;\table.txt     —— 每页表格（单元格以 “|” 分隔）；
    ///   3) &lt;文件夹&gt;\AiAppData\&lt;図番号&gt;\plu_*.txt     —— pdfplumber 风格的五份结果
    ///      （plu_text / plu_layout / plu_table / plu_image / plu_all）；
    ///      其中 plu_image 为“图上文字”：按图片区域与文字坐标比对判定，表格文字不重复计入
    ///      （表格文字按“表格块外接矩形 + 有效 X 范围”判定，与表格左右并排的图纸标注仍算图上文字）；
    ///   4) &lt;文件夹&gt;\AiAppData\classify\classification.csv —— 每个 PDF 的分类结果
    ///      （基本図 / 加工図 / アッセンブリ図 / 要領図 / 未知）。
    /// </summary>
    internal sealed class PdfSplitService
    {
        /// <summary>
        /// 所有输出统一放到的根子文件夹名：&lt;PDF 文件夹&gt;\AiAppData（不存在时自动创建）。
        /// </summary>
        public const string OutputRootFolderName = "AiAppData";

        /// <summary>坐标文件名（每个 PDF 的「図番号」子文件夹内）。</summary>
        public const string XyFileName = "xy.txt";

        /// <summary>图面文字坐标文件名（只包含图纸区域的文字）。</summary>
        public const string XyImageTextFileName = "xy_imgtext.txt";

        /// <summary>表格文件名。</summary>
        public const string TableFileName = "table.txt";

        /// <summary>plu 纯文本文件名（对应 pdfplumber 的 extract_text()）。</summary>
        public const string PluTextFileName = "plu_text.txt";

        /// <summary>plu 版面文本文件名（对应 pdfplumber 的 extract_text(layout=True)）。</summary>
        public const string PluLayoutFileName = "plu_layout.txt";

        /// <summary>plu 表格文件名（对应 pdfplumber 的 extract_tables()）。</summary>
        public const string PluTableFileName = "plu_table.txt";

        /// <summary>plu 图上文字文件名（图片区域与文字坐标比对；无图片时退化为表格之外的文字）。</summary>
        public const string PluImageFileName = "plu_image.txt";

        /// <summary>plu 文本 + 表格合并文件名。</summary>
        public const string PluAllFileName = "plu_all.txt";

        /// <summary>
        /// 找出文件夹第一层中所有 PDF 文件，返回按序号（Ordinal）排序的文件名（不含路径）。
        /// 与原脚本一致：扩展名不区分大小写，且不进入子文件夹。
        /// </summary>
        /// <param name="folder">待扫描的文件夹。</param>
        public static string[] FindPdfFiles(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                throw new ArgumentException("文件夹路径不能为空。", nameof(folder));
            }

            if (!Directory.Exists(folder))
            {
                throw new DirectoryNotFoundException("找不到文件夹: " + folder);
            }

            List<string> names = new List<string>();

            foreach (string path in Directory.GetFiles(folder, "*", SearchOption.TopDirectoryOnly))
            {
                if (string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    names.Add(Path.GetFileName(path));
                }
            }

            // 使用序数比较，排序结果与 Python 的 sorted() 一致
            names.Sort(StringComparer.Ordinal);

            return names.ToArray();
        }

        /// <summary>
        /// 计算分解输出根目录（只计算路径，不创建目录）：&lt;PDF 文件夹&gt;\AiAppData。
        /// </summary>
        /// <param name="folder">PDF 所在文件夹。</param>
        public static string GetOutputRoot(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                throw new ArgumentException("PDF 文件夹不能为空。", nameof(folder));
            }

            return Path.Combine(folder, OutputRootFolderName);
        }

        /// <summary>
        /// 计算单个 PDF 的输出目录（只计算路径，不创建目录）：&lt;输出根目录&gt;\&lt;図番号&gt;。
        /// 该目录下直接存放 xy.txt、table.txt、plu_*.txt（文件名不再包含 PDF 文件名）。
        /// </summary>
        /// <param name="outputRoot">输出根目录（见 <see cref="GetOutputRoot"/>）。</param>
        /// <param name="stem">図番号（PDF 文件名去掉扩展名）。</param>
        public static string GetPdfOutputDirectory(string outputRoot, string stem)
        {
            if (string.IsNullOrWhiteSpace(outputRoot))
            {
                throw new ArgumentException("输出根目录不能为空。", nameof(outputRoot));
            }

            if (string.IsNullOrWhiteSpace(stem))
            {
                throw new ArgumentException("図番号不能为空。", nameof(stem));
            }

            return Path.Combine(outputRoot, stem);
        }

        /// <summary>
        /// 计算分类结果目录（只计算路径，不创建目录）：&lt;输出根目录&gt;\classify。
        /// </summary>
        /// <param name="outputRoot">输出根目录（见 <see cref="GetOutputRoot"/>）。</param>
        public static string GetClassifyDirectory(string outputRoot)
        {
            if (string.IsNullOrWhiteSpace(outputRoot))
            {
                throw new ArgumentException("输出根目录不能为空。", nameof(outputRoot));
            }

            return Path.Combine(outputRoot, PdfClassifier.ClassifyFolderName);
        }

        /// <summary>
        /// 统计“分解产物已齐全”的 PDF 数量（用于判断是否需要询问用户重新分解）。
        /// 每个 PDF 需在其「図番号」子文件夹（&lt;文件夹&gt;\AiAppData\&lt;図番号&gt;）中同时存在以下 txt：
        /// xy.txt、xy_imgtext.txt、table.txt、plu_text.txt、plu_layout.txt、
        /// plu_table.txt、plu_image.txt、plu_all.txt。
        /// </summary>
        /// <param name="folder">PDF 所在文件夹。</param>
        /// <param name="pdfNames">PDF 文件名集合（含扩展名，通常来自 <see cref="FindPdfFiles"/>）。</param>
        /// <param name="missing">不为 null 时收集缺失文件说明（最多 20 条）。</param>
        /// <returns>产物齐全的 PDF 数量。</returns>
        public static int CountCompletedOutputs(string folder, IList<string> pdfNames,
            IList<string> missing)
        {
            if (string.IsNullOrWhiteSpace(folder) || pdfNames == null || pdfNames.Count == 0)
            {
                return 0;
            }

            string outputRoot = GetOutputRoot(folder);

            int completed = 0;

            foreach (string name in pdfNames)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                // 図番号 = PDF 文件名（不含扩展名），同时作为输出子文件夹名
                string stem = Path.GetFileNameWithoutExtension(name);
                string pdfOutputDirectory = GetPdfOutputDirectory(outputRoot, stem);
                string[] paths = new string[]
                {
                    Path.Combine(pdfOutputDirectory, XyFileName),
                    Path.Combine(pdfOutputDirectory, XyImageTextFileName),
                    Path.Combine(pdfOutputDirectory, TableFileName),
                    Path.Combine(pdfOutputDirectory, PluTextFileName),
                    Path.Combine(pdfOutputDirectory, PluLayoutFileName),
                    Path.Combine(pdfOutputDirectory, PluTableFileName),
                    Path.Combine(pdfOutputDirectory, PluImageFileName),
                    Path.Combine(pdfOutputDirectory, PluAllFileName)
                };

                bool allExist = true;

                foreach (string path in paths)
                {
                    if (File.Exists(path))
                    {
                        continue;
                    }

                    allExist = false;

                    if (missing != null && missing.Count < 20)
                    {
                        missing.Add(stem + "\\" + Path.GetFileName(path));
                    }
                }

                if (allExist)
                {
                    completed++;
                }
            }

            return completed;
        }

        /// <summary>
        /// 批量分解文件夹中的所有 PDF。
        /// </summary>
        /// <param name="folder">PDF 所在文件夹。</param>
        /// <param name="onProgress">进度回调，可为 null。</param>
        /// <param name="onLog">日志回调，可为 null。</param>
        /// <returns>分解结果。</returns>
        public PdfSplitResult Split(string folder, Action<PdfSplitProgress> onProgress, Action<LogKind, string> onLog)
        {
            string[] pdfNames = FindPdfFiles(folder);

            if (pdfNames.Length == 0)
            {
                throw new InvalidOperationException("该文件夹中没有找到 PDF 文件！");
            }

            // 所有输出集中到 <PDF 文件夹>\AiAppData 下（不存在时自动创建），避免污染所选文件夹根目录
            string outputRoot = GetOutputRoot(folder);
            string classifyDirectory = GetClassifyDirectory(outputRoot);
            Directory.CreateDirectory(outputRoot);
            Directory.CreateDirectory(classifyDirectory);

            ReportLog(onLog, LogKind.Info, "输出根目录：" + outputRoot);
            ReportLog(onLog, LogKind.Info,
                string.Format("每个 PDF 输出到：{0}{1}<図番号>{1}", outputRoot, Path.DirectorySeparatorChar));
            ReportLog(onLog, LogKind.Info, "分类结果目录：" + classifyDirectory);

            List<string> errors = new List<string>();
            int total = pdfNames.Length;
            int succeeded = 0;

            for (int index = 0; index < total; index++)
            {
                string fileName = pdfNames[index];
                string pdfPath = Path.Combine(folder, fileName);
                // 図番号 = PDF 文件名（不含扩展名），同时作为输出子文件夹名
                string stem = Path.GetFileNameWithoutExtension(fileName);

                // 每个 PDF 一个独立子文件夹：<输出根目录>\<図番号>
                string pdfOutputDirectory = GetPdfOutputDirectory(outputRoot, stem);
                Directory.CreateDirectory(pdfOutputDirectory);

                ReportProgress(onProgress, new PdfSplitProgress(index + 1, total, fileName, "正在处理"));
                ReportLog(onLog, LogKind.Info,
                    string.Format("[{0}/{1}] 正在处理: {2}", index + 1, total, fileName));

                bool failed = false;

                // 1) 文字 + 坐标（替代 PyMuPDF）
                if (!RunStep(onLog, errors, fileName, "坐标提取 (PdfPig)", delegate
                    {
                        PdfTextExtractor.WriteWordCoordinates(pdfPath, Path.Combine(pdfOutputDirectory, XyFileName));
                    }))
                {
                    failed = true;
                }

                // 2) 图面文字 + 坐标（只取图纸区域的文字）
                if (!RunStep(onLog, errors, fileName, "图面文字提取 (PdfPig)", delegate
                    {
                        PdfTextExtractor.WriteImageTextCoordinates(pdfPath,
                            Path.Combine(pdfOutputDirectory, XyImageTextFileName));
                    }))
                {
                    failed = true;
                }

                // 3) 表格（替代 MarkItDown 的表格输出：单元格以 “|” 分隔）
                if (!RunStep(onLog, errors, fileName, "表格提取 (PdfPig)", delegate
                    {
                        PdfTextExtractor.WriteTableText(pdfPath, Path.Combine(pdfOutputDirectory, TableFileName));
                    }))
                {
                    failed = true;
                }

                // 4) plu：pdfplumber 风格五份输出（一次解析，同时写 5 个文件）
                if (!RunStep(onLog, errors, fileName, "plu 提取", delegate
                    {
                        PdfTextExtractor.WritePluSet(
                            pdfPath,
                            Path.Combine(pdfOutputDirectory, PluTextFileName),
                            Path.Combine(pdfOutputDirectory, PluLayoutFileName),
                            Path.Combine(pdfOutputDirectory, PluTableFileName),
                            Path.Combine(pdfOutputDirectory, PluAllFileName),
                            Path.Combine(pdfOutputDirectory, PluImageFileName));
                    }))
                {
                    failed = true;
                }

                if (!failed)
                {
                    succeeded++;
                    ReportLog(onLog, LogKind.Success,
                        fileName + " 已生成坐标、图面文字、表格及 plu 五种文件。");
                }
            }

            if (errors.Count > 0)
            {
                foreach (string message in errors)
                {
                    ReportLog(onLog, LogKind.Error, "ERROR: " + message);
                }

                ReportLog(onLog, LogKind.Error,
                    string.Format("FINISHED {0}/{1}（有失败）", succeeded, total));
            }
            else
            {
                ReportLog(onLog, LogKind.Success, string.Format("SUCCESS {0}/{1}", succeeded, total));
            }

            // 分解完成后按図番号规则分类（分类失败不影响分解结果）
            IList<PdfClassification> classifications =
                ClassifyAll(folder, pdfNames, outputRoot, classifyDirectory, onLog);

            return new PdfSplitResult(total, succeeded, errors, outputRoot, classifyDirectory,
                classifications);
        }

        /// <summary>对本次处理的 PDF 分类，写出汇总 CSV，并把每项结果写入日志。</summary>
        private static IList<PdfClassification> ClassifyAll(string folder, string[] pdfNames, string outputRoot,
            string classifyDirectory, Action<LogKind, string> onLog)
        {
            IList<PdfClassification> items;

            try
            {
                items = PdfClassifier.ClassifyFiles(folder, pdfNames, outputRoot);
            }
            catch (Exception ex)
            {
                ReportLog(onLog, LogKind.Error, "分类失败：" + ex.Message);
                return new List<PdfClassification>();
            }

            ReportLog(onLog, LogKind.Info, "--------------------------------------------------");
            ReportLog(onLog, LogKind.Info, "分类结果：" + PdfClassifier.BuildSummaryText(items));

            foreach (PdfClassification item in items)
            {
                bool needChoice = item.NeedsUserChoice;

                ReportLog(onLog, needChoice ? LogKind.Warning : LogKind.Info,
                    string.Format("  {0} → {1}{2}（{3}）",
                        item.FileName, item.CategoryName,
                        needChoice ? "，需在画面上选择" : string.Empty,
                        item.Evidence));
            }

            try
            {
                string summaryPath = PdfClassifier.SaveSummary(classifyDirectory, items);
                ReportLog(onLog, LogKind.Info, "分类汇总已保存：" + summaryPath);
            }
            catch (Exception ex)
            {
                ReportLog(onLog, LogKind.Warning, "分类汇总保存失败：" + ex.Message);
            }

            return items;
        }

        /// <summary>执行单个提取步骤：捕获异常并记录到错误列表，返回是否成功。</summary>
        private static bool RunStep(Action<LogKind, string> onLog, List<string> errors,
            string fileName, string stepName, Action action)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception ex)
            {
                errors.Add(fileName + ": " + stepName + " 失败: " + ex.Message);
                ReportLog(onLog, LogKind.Error, fileName + " " + stepName + "失败：" + ex.Message);
                return false;
            }
        }

        private static void ReportProgress(Action<PdfSplitProgress> onProgress, PdfSplitProgress progress)
        {
            if (onProgress == null)
            {
                return;
            }

            try
            {
                onProgress(progress);
            }
            catch (Exception)
            {
                // 界面回调异常不应中断批量处理
            }
        }

        private static void ReportLog(Action<LogKind, string> onLog, LogKind kind, string message)
        {
            if (onLog == null)
            {
                return;
            }

            try
            {
                onLog(kind, message);
            }
            catch (Exception)
            {
                // 界面回调异常不应中断批量处理
            }
        }
    }
}
