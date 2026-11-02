using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace CadPartSplitter
{
    /// <summary>
    /// 对外接口：供其他程序（宿主工程）直接调用零件分割，只需给一个文件路径。
    ///
    /// 典型用法（引用 <c>CadPartSplitter.exe</c>，目标平台须为 x64）：
    /// <code>
    /// var result = CadPartSplitter.SplitApi.Split(@"D:\图纸\H310A20009.pdf");
    /// if (result.Success)
    ///     Console.WriteLine($"{result.PartCount} 个零件 → {result.OutputDirectory}");
    /// else
    ///     Console.WriteLine("失败：" + result.ErrorMessage);
    /// </code>
    ///
    /// 说明：
    ///   * 结果默认写到 <c>&lt;输入文件所在目录&gt;\AiAppData\&lt;主文件名&gt;</c>（每张图纸一个子文件夹），
    ///     与界面 / 命令行的约定一致；
    ///   * 参数用 <see cref="DetectorOptions"/> 的默认值，需要沿用界面上次调好的参数时
    ///     用 <c>GuiSettings.Load().ApplyTo(opts)</c> 或自己赋值；
    ///   * 方法是同步阻塞的（几百毫秒到数秒），界面线程调用请用 <see cref="SplitAsync"/>；
    ///   * 零件在 <see cref="SplitResult.Parts"/> 里，有线表格（部品表 / 表題欄等）在
    ///     <see cref="SplitResult.Tables"/> 里，两者同时也会落盘；
    ///   * 任何失败（文件不存在 / 格式不支持 / 处理异常）都以
    ///     <see cref="SplitResult.Success"/> = false + 退出码返回，不向外抛异常。
    /// </summary>
    public static class SplitApi
    {
        /// <summary>退出码：成功（含「未检测到零件」，与命令行一致）。</summary>
        public const int ExitCodeOk = 0;

        /// <summary>退出码：输入文件不存在或参数无效。</summary>
        public const int ExitCodeInvalidInput = 1;

        /// <summary>退出码：处理过程抛出异常。</summary>
        public const int ExitCodeFailed = 2;

        /// <summary>
        /// 分割指定文件（PDF / PNG / JPG / BMP / TIF），结果写到
        /// <c>&lt;文件所在目录&gt;\AiAppData\&lt;主文件名&gt;</c>，使用默认识别参数。
        /// 这是最常用的入口：只要把「选择文件路径」传进来即可。
        /// </summary>
        public static SplitResult Split(string inputFilePath)
        {
            return Split(inputFilePath, (DetectorOptions)null);
        }

        /// <summary>
        /// 分割指定文件并指定输出目录；<paramref name="outputDirectory"/> 为空时
        /// 退回 <c>&lt;文件所在目录&gt;\AiAppData\&lt;主文件名&gt;</c>。
        /// </summary>
        public static SplitResult Split(string inputFilePath, string outputDirectory)
        {
            var options = new DetectorOptions { OutputDirectory = outputDirectory };
            return Split(inputFilePath, options);
        }

        /// <summary>
        /// 分割指定文件并按 <paramref name="options"/> 执行（高级用法）；
        /// <paramref name="options"/> 为 null 时使用默认参数。
        /// 传入的 <see cref="DetectorOptions.Logger"/> 不会被丢弃，会与本接口的日志一并回调。
        /// </summary>
        public static SplitResult Split(string inputFilePath, DetectorOptions options)
        {
            var log = new StringBuilder();

            if (string.IsNullOrWhiteSpace(inputFilePath))
                return SplitResult.Fail(null, ExitCodeInvalidInput, "输入文件路径为空。", log.ToString());

            if (!File.Exists(inputFilePath))
                return SplitResult.Fail(inputFilePath, ExitCodeInvalidInput,
                                        "输入文件不存在：" + inputFilePath, log.ToString());

            DetectorOptions opts = options ?? new DetectorOptions();

            // 输出目录留空 → <输入文件所在目录>\AiAppData\<主文件名>（与界面 / 命令行一致）
            if (string.IsNullOrWhiteSpace(opts.OutputDirectory))
                opts.OutputDirectory = DetectorOptions.GetDefaultOutputDirectory(inputFilePath, opts.PdfPageIndex);

            // 日志：汇总进返回值，同时保留调用方自己的回调
            Action<string> callerLogger = opts.Logger;
            opts.Logger = line =>
            {
                lock (log)
                    log.AppendLine(line);

                if (callerLogger != null)
                    callerLogger(line);
            };

            var stopwatch = Stopwatch.StartNew();
            try
            {
                List<RuledTable> tables;
                List<PartInfo> parts = PartSplitterService.Run(inputFilePath, opts, out tables);
                stopwatch.Stop();

                string outputDir = opts.OutputDirectory;
                try
                {
                    outputDir = Path.GetFullPath(opts.OutputDirectory);
                }
                catch (Exception)
                {
                    // 非法路径（极少见）：按原样返回，让调用方自己判断
                }

                return new SplitResult
                {
                    Success             = true,
                    ExitCode            = ExitCodeOk,
                    InputFile           = SafeFullPath(inputFilePath),
                    OutputDirectory     = outputDir,
                    Parts               = parts,
                    Tables              = tables,
                    Message             = parts.Count > 0
                                            ? "共分割零件 " + parts.Count + " 个"
                                            : "未检测到零件",
                    Log                 = log.ToString(),
                    ElapsedMilliseconds = stopwatch.ElapsedMilliseconds
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                return new SplitResult
                {
                    Success             = false,
                    ExitCode            = ExitCodeFailed,
                    InputFile           = SafeFullPath(inputFilePath),
                    OutputDirectory     = opts.OutputDirectory,
                    Parts               = new List<PartInfo>(),
                    ErrorMessage        = ex.GetType().Name + "：" + ex.Message,
                    Log                 = log.ToString(),
                    ElapsedMilliseconds = stopwatch.ElapsedMilliseconds
                };
            }
        }

        /// <summary>异步分割（内部在线程池执行），适合 WinForms / WPF 界面直接 <c>await</c>。</summary>
        public static Task<SplitResult> SplitAsync(string inputFilePath)
        {
            return Task.Run(() => Split(inputFilePath));
        }

        /// <summary>异步分割到指定输出目录。</summary>
        public static Task<SplitResult> SplitAsync(string inputFilePath, string outputDirectory)
        {
            return Task.Run(() => Split(inputFilePath, outputDirectory));
        }

        /// <summary>取绝对路径；路径无效时原样返回。</summary>
        private static string SafeFullPath(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch (Exception)
            {
                return path;
            }
        }
    }
}
