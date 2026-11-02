using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace MSIFAITOOLS.Services
{
    /// <summary>
    /// CadPartSplitter 批量分割的执行结果。
    /// </summary>
    internal sealed class CadPartSplitResult
    {
        public CadPartSplitResult()
        {
            ExitCode = -1;
        }

        /// <summary>是否找到了 CadPartSplitter.exe。</summary>
        public bool ExeFound { get; set; }

        /// <summary>实际使用的 CadPartSplitter.exe 路径（未找到时为 null）。</summary>
        public string ExePath { get; set; }

        /// <summary>进程退出码为 0 时为 true（含「该文件夹下没有可处理的 PDF」这种情况）。</summary>
        public bool Success { get; set; }

        /// <summary>进程退出码（未能启动时为 -1）。0 = 全部成功，1 = 输入无效，2 = 有失败项。</summary>
        public int ExitCode { get; set; }

        /// <summary>失败原因（成功时为 null）。</summary>
        public string ErrorMessage { get; set; }

        /// <summary>输出末尾若干行（失败时便于排查）。</summary>
        public string OutputTail { get; set; }

        /// <summary>CadPartSplitter 自己输出的汇总行（形如「共 N 张：成功 X，失败 Y，零件合计 Z 个」）。</summary>
        public string Summary { get; set; }

        /// <summary>本次执行耗时（毫秒）。</summary>
        public long ElapsedMilliseconds { get; set; }
    }

    /// <summary>
    /// 调用同解决方案下的 <c>CadPartSplitter.exe</c>（图形界面 / 命令行双模式程序）做「零件分割」。
    ///
    /// 为什么用「启动子进程」而不是直接引用它的程序集：
    ///   * CadPartSplitter 以 x64 编译（OpenCvSharp4 / Docnet.Core 只带 win-x64 原生库），
    ///     而本工具是 AnyCPU，直接引用会出现 BadImageFormatException；
    ///   * 它自带一大套 NuGet 依赖（OpenCvSharp4、Docnet.Core、PdfPig 0.1.9 等），
    ///     引用进来会和本工具的 PdfPig 0.1.16 打架，还得把原生 DLL 一起拷到输出目录；
    ///   * 用命令行批量模式（<c>CadPartSplitter.exe &lt;输入文件夹&gt;</c>）即可一次跑完整个文件夹，
    ///     输出目录规则与本工具完全一致：&lt;输入文件夹&gt;\AiAppData\&lt;图纸名&gt;\，
    ///     两者结果（Part_NN.png / parts_info.json 与 xy.txt / table.txt / plu_*.txt）互不覆盖。
    ///
    /// 程序查找顺序（第一个存在的即采用）：
    ///   1) 环境变量 <see cref="ExePathEnvironmentVariable"/> 指定的路径；
    ///   2) 与 MSIFAITOOLS.exe 同目录的 CadPartSplitter.exe（部署时把 exe 及其 DLL 一起拷过来即可）；
    ///   3) 从 MSIFAITOOLS.exe 所在目录逐级向上，找 &lt;目录&gt;\CadPartSplitter\bin\Debug|Release\CadPartSplitter.exe
    ///      （开发期直接跑 MSIFAITOOLS\bin\Debug 时自动命中 CadPartSplitter 工程）。
    /// </summary>
    internal static class CadPartSplitterRunner
    {
        /// <summary>被调用的程序名。</summary>
        public const string ExeFileName = "CadPartSplitter.exe";

        /// <summary>可用该环境变量显式指定 CadPartSplitter.exe 的完整路径。</summary>
        public const string ExePathEnvironmentVariable = "CADPARTSPLITTER_EXE";

        /// <summary>最多缓存多少行输出（只用于失败时提示，不落盘，防止内存无限增长）。</summary>
        private const int MaxBufferedLines = 4000;

        /// <summary>失败时拼进错误信息的末尾行数。</summary>
        private const int TailLineCount = 8;

        /// <summary>
        /// 查找 CadPartSplitter.exe，找不到时返回 null。
        /// </summary>
        public static string FindExePath()
        {
            try
            {
                string fromEnvironment = Environment.GetEnvironmentVariable(ExePathEnvironmentVariable);

                if (!string.IsNullOrWhiteSpace(fromEnvironment) && File.Exists(fromEnvironment))
                {
                    return Path.GetFullPath(fromEnvironment);
                }

                foreach (string candidate in EnumerateCandidates())
                {
                    if (File.Exists(candidate))
                    {
                        return Path.GetFullPath(candidate);
                    }
                }
            }
            catch (Exception)
            {
                // 路径异常（非法字符 / 无权限）时按「找不到」处理，由调用方给出提示
            }

            return null;
        }

        /// <summary>
        /// 列出候选路径（见类注释里的查找顺序）。
        /// </summary>
        private static IEnumerable<string> EnumerateCandidates()
        {
            DirectoryInfo directory = null;

            try
            {
                directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            }
            catch (Exception)
            {
                // BaseDirectory 非法时只依赖环境变量
            }

            while (directory != null)
            {
                yield return Path.Combine(directory.FullName, ExeFileName);
                yield return Path.Combine(directory.FullName, "CadPartSplitter", "bin", "Debug", ExeFileName);
                yield return Path.Combine(directory.FullName, "CadPartSplitter", "bin", "Release", ExeFileName);
                yield return Path.Combine(directory.FullName, "CadPartSplitter", ExeFileName);

                directory = directory.Parent;
            }
        }

        /// <summary>
        /// 以命令行批量模式（<c>CadPartSplitter.exe &lt;文件夹&gt;</c>）分割该文件夹下的所有 PDF。
        ///
        /// 说明：
        ///   * 同步阻塞，几百毫秒到数分钟（取决于 PDF 数量），界面线程请放在 Task.Run 里调用；
        ///   * 输出目录由 CadPartSplitter 自己决定（&lt;文件夹&gt;\AiAppData\&lt;图纸名&gt;），
        ///     与本工具的分解输出目录相同，文件名互不冲突；
        ///   * 进程的 stdout / stderr 会被逐行回调给 <paramref name="onLog"/>（可能来自后台线程）；
        ///   * 任何异常（程序不存在 / 无法启动 / 崩溃）都不向外抛出，
        ///     以 <see cref="CadPartSplitResult.Success"/> = false + <see cref="CadPartSplitResult.ErrorMessage"/> 返回。
        /// </summary>
        /// <param name="folder">待处理的 PDF 文件夹。</param>
        /// <param name="onLog">逐行日志回调，可为 null。</param>
        /// <returns>执行结果。</returns>
        public static CadPartSplitResult RunBatch(string folder, Action<string> onLog)
        {
            CadPartSplitResult result = new CadPartSplitResult();

            string exePath = FindExePath();

            if (exePath == null)
            {
                result.ErrorMessage = "未找到 " + ExeFileName +
                    "（可把它放在本程序同一目录，或用环境变量 " + ExePathEnvironmentVariable + " 指定完整路径）";
                return result;
            }

            result.ExeFound = true;
            result.ExePath = exePath;

            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                result.ErrorMessage = "PDF 文件夹不存在：" + folder;
                return result;
            }

            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = exePath;
            startInfo.Arguments = QuoteArgument(folder);
            startInfo.WorkingDirectory = Path.GetDirectoryName(exePath) ?? folder;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            // 子进程内部把 Console.OutputEncoding 设成 UTF-8（见 CommandLineRunner），这里保持一致，避免中文乱码
            startInfo.StandardOutputEncoding = Encoding.UTF8;
            startInfo.StandardErrorEncoding = Encoding.UTF8;

            List<string> lines = new List<string>();
            Stopwatch watch = Stopwatch.StartNew();

            try
            {
                using (Process process = new Process())
                {
                    process.StartInfo = startInfo;

                    // 输出必须异步读走：否则管道写满后子进程会一直阻塞
                    DataReceivedEventHandler onData = delegate(object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data == null)
                        {
                            return;
                        }

                        lock (lines)
                        {
                            lines.Add(e.Data);

                            if (lines.Count > MaxBufferedLines)
                            {
                                lines.RemoveAt(0);
                            }
                        }

                        if (onLog != null)
                        {
                            onLog(e.Data);
                        }
                    };

                    process.OutputDataReceived += onData;
                    process.ErrorDataReceived += onData;

                    if (!process.Start())
                    {
                        watch.Stop();
                        result.ElapsedMilliseconds = watch.ElapsedMilliseconds;
                        result.ErrorMessage = "无法启动 " + exePath;
                        return result;
                    }

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    process.WaitForExit();
                    // 无参的 WaitForExit 会等异步输出回调全部结束，保证汇总行不丢
                    process.WaitForExit();

                    result.ExitCode = process.ExitCode;
                }

                watch.Stop();
                result.ElapsedMilliseconds = watch.ElapsedMilliseconds;

                lock (lines)
                {
                    result.OutputTail = BuildTail(lines);
                    result.Summary = FindSummary(lines);
                }

                result.Success = result.ExitCode == 0;

                if (!result.Success)
                {
                    result.ErrorMessage = DescribeExitCode(result.ExitCode);
                }

                return result;
            }
            catch (Exception ex)
            {
                watch.Stop();

                result.ElapsedMilliseconds = watch.ElapsedMilliseconds;
                result.Success = false;
                result.ErrorMessage = ex.GetType().Name + "：" + ex.Message;

                lock (lines)
                {
                    result.OutputTail = BuildTail(lines);
                }

                return result;
            }
        }

        /// <summary>
        /// 把退出码翻译成人话（取值范围见 CadPartSplitter.CommandLineRunner）。
        /// </summary>
        private static string DescribeExitCode(int exitCode)
        {
            switch (exitCode)
            {
                case 1:
                    return "输入无效（退出码 1）";

                case 2:
                    return "有部分 PDF 分割失败（退出码 2），详见上方 [CAD] 日志";

                default:
                    return "进程异常结束（退出码 " + exitCode + "）";
            }
        }

        /// <summary>
        /// 取输出末尾若干行，用于失败提示。
        /// </summary>
        private static string BuildTail(List<string> lines)
        {
            if (lines == null || lines.Count == 0)
            {
                return null;
            }

            int start = Math.Max(0, lines.Count - TailLineCount);
            StringBuilder builder = new StringBuilder();

            for (int i = start; i < lines.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    continue;
                }

                builder.AppendLine(lines[i]);
            }

            return builder.ToString().Trim();
        }

        /// <summary>
        /// 从输出中找出最后一条汇总行（「[DONE] …」或「[批量完成] …」）。
        /// </summary>
        private static string FindSummary(List<string> lines)
        {
            if (lines == null)
            {
                return null;
            }

            for (int i = lines.Count - 1; i >= 0; i--)
            {
                string line = lines[i];

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (line.StartsWith("[DONE]", StringComparison.Ordinal) ||
                    line.StartsWith("[批量完成]", StringComparison.Ordinal))
                {
                    return line.Trim();
                }
            }

            return null;
        }

        /// <summary>
        /// 给命令行参数加引号：去掉末尾的分隔符（保留「D:\」这类根路径），避免「\"」被当成转义。
        /// </summary>
        private static string QuoteArgument(string value)
        {
            string text = (value ?? string.Empty).Trim();

            while (text.Length > 3 &&
                   (text.EndsWith("\\", StringComparison.Ordinal) || text.EndsWith("\"", StringComparison.Ordinal)))
            {
                text = text.Substring(0, text.Length - 1);
            }

            return "\"" + text + "\"";
        }
    }
}
