using System;

namespace MSIFAITOOLS.Services
{
    /// <summary>
    /// 从 PDF 表格（plu_table.txt）中提取的一条部品基本信息。
    /// 字段与 SQLite 的「部品基本信息」表一一对应，一处改动需同步另一处。
    /// </summary>
    internal sealed class PartBasicInfo
    {
        public PartBasicInfo(string drawingNo, int page, int number, string partName, string partDrawingNo,
            string frameKind, string quantity, string basicSize, string cutSize, string partCode)
        {
            DrawingNo = Clean(drawingNo);
            Page = page < 0 ? 0 : page;
            Number = number;
            PartName = Clean(partName);
            PartDrawingNo = Clean(partDrawingNo);
            FrameKind = Clean(frameKind);
            Quantity = Clean(quantity);
            BasicSize = Clean(basicSize);
            CutSize = Clean(cutSize);
            PartCode = Clean(partCode);
            VerifyStatus = PartVerifyStatus.NotChecked;
            Remark = string.Empty;
            SourceKind = string.Empty;
            SourceTag = string.Empty;
            SourceFile = string.Empty;
            PdfFile = string.Empty;
            LoginTime = string.Empty;
        }

        /// <summary>図番（PDF 文件名去掉扩展名）。</summary>
        public string DrawingNo { get; private set; }

        /// <summary>表格所在页码（从 1 开始；无法判定时为 0）。</summary>
        public int Page { get; private set; }

        /// <summary>番号。</summary>
        public int Number { get; private set; }

        /// <summary>部品名。</summary>
        public string PartName { get; private set; }

        /// <summary>部品図番（表格没有该列时为空）。</summary>
        public string PartDrawingNo { get; private set; }

        /// <summary>枠区分。</summary>
        public string FrameKind { get; private set; }

        /// <summary>員数（可能是数字或字母，故按文本保存）。</summary>
        public string Quantity { get; private set; }

        /// <summary>基本寸法。</summary>
        public string BasicSize { get; private set; }

        /// <summary>切断寸法。</summary>
        public string CutSize { get; private set; }

        /// <summary>部品コード。</summary>
        public string PartCode { get; private set; }

        /// <summary>坐标校验状态（取值见 <see cref="PartVerifyStatus"/>）。</summary>
        public string VerifyStatus { get; set; }

        /// <summary>备注：坐标校验明细、AI 提示等。</summary>
        public string Remark { get; set; }

        /// <summary>数据来源（取值见 <see cref="PartSourceKind"/>）：AI 分析 / CMD 分析・アッセンブリ図 / CMD 分析・要領図 / 手工追加。</summary>
        public string SourceKind { get; set; }

        /// <summary>来源标识（取值见 <see cref="PartSourceTag"/>）：要領図 / アッセンブリ図 / AI分析 / 手工追加。</summary>
        public string SourceTag { get; set; }

        /// <summary>来源文件（plu_table.txt 的完整路径）。</summary>
        public string SourceFile { get; set; }

        /// <summary>来源 PDF 的完整路径。</summary>
        public string PdfFile { get; set; }

        /// <summary>登录时间（从数据库读出时填充；新提取的对象为空）。</summary>
        public string LoginTime { get; set; }

        /// <summary>用于日志/界面的简短描述。</summary>
        public string ToShortText()
        {
            return string.Format("#{0} {1}（{2}）", Number, PartName, PartCode);
        }

        private static string Clean(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }
    }

    /// <summary>部品基本信息的坐标校验状态（同时作为写入数据库的文字）。</summary>
    internal static class PartVerifyStatus
    {
        /// <summary>部品名 / 部品コード 在 xy.txt 的对应页中全部找到。</summary>
        public const string Passed = "坐标一致";

        /// <summary>有部品名 / 部品コード 未能在 xy.txt 中找到。</summary>
        public const string Failed = "坐标不符";

        /// <summary>没有 xy.txt（或无法解析），未做校验。</summary>
        public const string NotChecked = "未校验";

        /// <summary>全部状态（画面下拉框的候选项）。</summary>
        public static readonly string[] All = new string[] { Passed, Failed, NotChecked };
    }

    /// <summary>
    /// 部品基本信息的「数据来源」（同时作为写入数据库的文字）。
    /// 用于区分同一张装配图下的行是哪个功能登录进来的：
    /// 【AI 分析】读 plu_table.txt；【CMD 分析】阶段 A 读 01_アッセンブリ図_部品機能情報.csv，
    /// 阶段 B 读 02_要領図_部品機能情報.csv；画面上还能手工追加行。
    /// </summary>
    internal static class PartSourceKind
    {
        /// <summary>【AI 分析】：用 AI 从 plu_table.txt 提取（<c>AiPartAnalyzer</c>）。</summary>
        public const string AiAnalysis = "AI分析";

        /// <summary>【CMD 分析】阶段 A：读 01_アッセンブリ図_部品機能情報.csv（<c>CmdPartAnalyzer</c>）。</summary>
        public const string CmdAssembly = "CMD分析・アッセンブリ図";

        /// <summary>【CMD 分析】阶段 B：读 02_要領図_部品機能情報.csv，挂在装配图名下（<c>CmdInstructionPartCollector</c>）。</summary>
        public const string CmdYoryo = "CMD分析・要領図";

        /// <summary>【CMD 分析】第 4 步：读 <c>classify\tz.json</c>（Excel 部品表），挂在装配图名下（<c>CmdTzPartCollector</c>）。</summary>
        public const string CmdTz = "CMD分析・tz.json";

        /// <summary>部品基本信息画面上手工追加的行。</summary>
        public const string Manual = "手工追加";

        /// <summary>全部取值（说明 / 下拉用）。</summary>
        public static readonly string[] All =
            new string[] { AiAnalysis, CmdAssembly, CmdYoryo, CmdTz, Manual };
    }

    /// <summary>
    /// 部品基本信息的「来源标识」：<see cref="PartSourceKind"/> 的简称（同时作为写入数据库的文字）。
    /// 用于在画面上一眼区分这一行的部品来自「要領図」还是「アッセンブリ図」自身的部品表。
    /// </summary>
    internal static class PartSourceTag
    {
        /// <summary>【AI 分析】（<see cref="PartSourceKind.AiAnalysis"/> 的简称）。</summary>
        public const string AiAnalysis = "AI分析";

        /// <summary>【CMD 分析】阶段 A：装配图自身的部品表（<see cref="PartSourceKind.CmdAssembly"/> 的简称）。</summary>
        public const string CmdAssembly = "アッセンブリ図";

        /// <summary>【CMD 分析】阶段 B：要領図 的部品（<see cref="PartSourceKind.CmdYoryo"/> 的简称）。</summary>
        public const string CmdYoryo = "要領図";

        /// <summary>【CMD 分析】第 4 步：Excel 部品表 tz.json 的部品（<see cref="PartSourceKind.CmdTz"/> 的简称）。</summary>
        public const string CmdTz = "tz.json";

        /// <summary>部品基本信息画面上手工追加的行（<see cref="PartSourceKind.Manual"/> 的简称）。</summary>
        public const string Manual = "手工追加";

        /// <summary>全部取值（说明 / 下拉用）。</summary>
        public static readonly string[] All =
            new string[] { AiAnalysis, CmdAssembly, CmdYoryo, CmdTz, Manual };

        /// <summary>把「数据来源」（<see cref="PartSourceKind"/>）换成对应的「来源标识」；不认识的返回空。</summary>
        public static string FromSourceKind(string sourceKind)
        {
            if (sourceKind == PartSourceKind.CmdYoryo)
            {
                return CmdYoryo;
            }

            if (sourceKind == PartSourceKind.CmdAssembly)
            {
                return CmdAssembly;
            }

            if (sourceKind == PartSourceKind.AiAnalysis)
            {
                return AiAnalysis;
            }

            if (sourceKind == PartSourceKind.Manual)
            {
                return Manual;
            }

            if (sourceKind == PartSourceKind.CmdTz)
            {
                return CmdTz;
            }

            return string.Empty;
        }
    }
}
