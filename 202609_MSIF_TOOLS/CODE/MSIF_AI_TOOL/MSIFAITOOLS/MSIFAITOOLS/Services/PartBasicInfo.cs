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
            MemberName = string.Empty;
            PartDrawingNo = Clean(partDrawingNo);
            FrameKind = Clean(frameKind);
            Quantity = Clean(quantity);
            BasicSize = Clean(basicSize);
            CutSize = Clean(cutSize);
            PartCode = Clean(partCode);
            HeightMin = string.Empty;
            HeightMax = string.Empty;
            WidthMin = string.Empty;
            WidthMax = string.Empty;
            UsageKind = string.Empty;
            SpecialNote = string.Empty;
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

        /// <summary>
        /// 部材名：用「番号」去该页目录的 <c>parts_info.json</c> 找圈号（<c>"text": "4"</c> 且
        /// <c>"display": "④4"</c> 的 <c>callouts</c>），取这些圈号所在零件图（<c>index</c>）的全部
        /// <c>texts</c>，<b>去掉含数字的文字</b>、<b>去掉「XX部」形式的位置标记</b>，
        /// 剩下的去重后用 <c>|</c> 连接（例如 <c>縦枠</c>、<c>鴨居|上レール</c>）。
        /// 由 <see cref="PartsInfoIndex.ResolveCalloutMemberName(int)"/> 计算；
        /// 番号对不上圈号 / 没有 <c>parts_info.json</c> 的行留空。
        /// <b>留空的行写库时会统一填成 <see cref="MemberNameFallback"/>＝「その他」</b>
        /// （<see cref="PartBasicInfoRepository.BindInsertParameters"/>），所以画面上不会出现空的「部材名」。
        /// </summary>
        public string MemberName { get; set; }

        /// <summary>
        /// 「部材名」为空时使用的默认值「その他」。
        /// 写库时由 <see cref="PartBasicInfoRepository.BindInsertParameters"/> 统一套用
        /// （老数据库打开时 <see cref="PartBasicInfoRepository"/> 也会把空值补成这个值），
        /// 所以画面上看到的「部材名」不会再有空白格。
        /// </summary>
        public const string MemberNameFallback = "その他";

        /// <summary>部品図番（表格没有该列时为空）。</summary>
        public string PartDrawingNo { get; private set; }

        /// <summary>区分（表格的「枠区分」列；数据库列名 = 区分）。</summary>
        public string FrameKind { get; private set; }

        /// <summary>数量（表格的「員数」列；数据库列名 = 数量。可能是数字或字母，故按文本保存）。</summary>
        public string Quantity { get; private set; }

        /// <summary>基本寸法。</summary>
        public string BasicSize { get; private set; }

        /// <summary>切断寸法。</summary>
        public string CutSize { get; private set; }

        /// <summary>部品コード。</summary>
        public string PartCode { get; private set; }

        /// <summary>H下限（特寸時使い分け表的 H 下限；没有这项数据时为空）。数据库列名 = H下限。</summary>
        public string HeightMin { get; set; }

        /// <summary>H上限（特寸時使い分け表的 H 上限；没有这项数据时为空）。数据库列名 = H上限。</summary>
        public string HeightMax { get; set; }

        /// <summary>W下限（特寸時使い分け表的 W 下限；没有这项数据时为空）。数据库列名 = W下限。</summary>
        public string WidthMin { get; set; }

        /// <summary>W上限（特寸時使い分け表的 W 上限；没有这项数据时为空）。数据库列名 = W上限。</summary>
        public string WidthMax { get; set; }

        /// <summary>使い分け（同一部品按品種 / 尺寸分用哪一个时的区分；画面上手工填）。数据库列名 = 使い分け。</summary>
        public string UsageKind { get; set; }

        /// <summary>
        /// 特注（追加対応特注寸法 等）。【CMD 分析】第 4 步把 tz.json 的「追加対応特注寸法」写在这里，
        /// 其余来源留空、可在画面上手工填。数据库列名 = 特注。
        /// </summary>
        public string SpecialNote { get; set; }

        /// <summary>坐标校验状态（取值见 <see cref="PartVerifyStatus"/>）。</summary>
        public string VerifyStatus { get; set; }

        /// <summary>备注：坐标校验明细、AI 提示等。</summary>
        public string Remark { get; set; }

        /// <summary>数据来源（取值见 <see cref="PartSourceKind"/>）：AI 分析 / CMD 分析・アッセンブリ図 / CMD 分析・要領図 / 手工追加。</summary>
        public string SourceKind { get; set; }

        /// <summary>
        /// 参照元（数据库列名 = 参照元）。<b>写库时一律写成该行的「図番」</b>
        /// （<see cref="PartBasicInfoRepository.BindInsertParameters"/>；
        /// 老数据库打开时 <see cref="PartBasicInfoRepository"/> 也会把旧值改成「図番」），
        /// 所以数据库 / 画面上的「参照元」永远等于「図番」。
        /// 这个属性在内存里保存的是来源简称（取值见 <see cref="PartSourceTag"/>：要領図 /
        /// アッセンブリ図 / tz.json / AI分析 / 手工追加），只作为「数据来源」为空时的兜底判定用。
        /// </summary>
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
    /// <b>注意</b>：数据库的「参照元」列现在写的是该行的「図番」
    /// （见 <see cref="PartBasicInfo.SourceTag"/>），这些简称只留在内存对象上，
    /// 以及老数据「数据来源」为空时的兜底判定里。
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
