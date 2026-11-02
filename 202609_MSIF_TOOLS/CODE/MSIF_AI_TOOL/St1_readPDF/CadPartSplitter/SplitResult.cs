using System.Collections.Generic;

namespace CadPartSplitter
{
    /// <summary>
    /// <see cref="SplitApi"/> 的返回结果。调用方只读，无需自己构造。
    ///
    /// 字段约定：
    ///   * <see cref="Success"/> = true 也可能 <see cref="PartCount"/> = 0（图纸上没有可分割的零件），
    ///     这种情况 <see cref="ExitCode"/> 仍为 0，与命令行行为一致；
    ///   * <see cref="ErrorMessage"/> 只在失败时非空；
    ///   * <see cref="Log"/> 是本次处理的完整日志（与界面日志面板、命令行输出同一份内容）。
    /// </summary>
    public sealed class SplitResult
    {
        /// <summary>是否处理成功。</summary>
        public bool Success { get; internal set; }

        /// <summary>退出码：0 成功、1 输入文件不存在 / 参数无效、2 处理异常。</summary>
        public int ExitCode { get; internal set; }

        /// <summary>输入文件的绝对路径。</summary>
        public string InputFile { get; internal set; }

        /// <summary>结果输出目录（默认 <c>&lt;输入文件所在目录&gt;\AiAppData\&lt;主文件名&gt;</c>）。</summary>
        public string OutputDirectory { get; internal set; }

        /// <summary>分割出的零件列表（包含像素坐标、PDF 毫米坐标、文字与圈号）。</summary>
        public IList<PartInfo> Parts { get; internal set; } = new List<PartInfo>();

        /// <summary>零件数量。</summary>
        public int PartCount
        {
            get { return Parts == null ? 0 : Parts.Count; }
        }

        /// <summary>
        /// 识别到的「有线表格」（单元格四面都有框线的表格，如部品表 / 表題欄）。
        /// 仅 PDF 输入、且 <see cref="DetectorOptions.ExtractRuledTables"/> 开启时才有内容，
        /// 位图输入恒为空列表。表格内容同时落盘为 <c>tables_info.json</c> /
        /// <c>tables_text.txt</c> / <c>tables\table_NN.csv</c>。
        /// </summary>
        public IList<RuledTable> Tables { get; internal set; } = new List<RuledTable>();

        /// <summary>有线表格数量。</summary>
        public int TableCount
        {
            get { return Tables == null ? 0 : Tables.Count; }
        }

        /// <summary>结果摘要，例如「共分割零件 11 个」/「未检测到零件」。</summary>
        public string Message { get; internal set; }

        /// <summary>失败原因（成功时为 null）。</summary>
        public string ErrorMessage { get; internal set; }

        /// <summary>完整处理日志（逐行）。</summary>
        public string Log { get; internal set; }

        /// <summary>处理耗时（毫秒）。</summary>
        public long ElapsedMilliseconds { get; internal set; }

        /// <summary>失败结果的快捷构造。</summary>
        internal static SplitResult Fail(string inputFile, int exitCode, string errorMessage, string log)
        {
            return new SplitResult
            {
                Success      = false,
                ExitCode     = exitCode,
                InputFile    = inputFile,
                Parts        = new List<PartInfo>(),
                ErrorMessage = errorMessage,
                Log          = log
            };
        }

        /// <summary>调试用摘要。</summary>
        public override string ToString()
        {
            return Success
                ? "成功：" + Message + " → " + OutputDirectory
                : "失败（退出码 " + ExitCode + "）：" + ErrorMessage;
        }
    }
}
