using System.Collections.Generic;

namespace MSIFAITOOLS.Services
{
    /// <summary>
    /// 【CMD 分析】内部的一步：编号 + 代号 + 名字 + 一句话说明。
    /// 只是「命名」用的只读数据，<b>不参与任何分析逻辑</b>。
    /// </summary>
    internal sealed class CmdAnalysisStep
    {
        public CmdAnalysisStep(int number, string code, string name, string purpose)
        {
            Number = number;
            Code = code == null ? string.Empty : code.Trim();
            Name = name == null ? string.Empty : name.Trim();
            Purpose = purpose == null ? string.Empty : purpose.Trim();
        }

        /// <summary>步骤号：0 = 按钮执行之前的准备；1 / 2 / 3 / 4 = 点一次【CMD 分析】后依次执行的步骤。</summary>
        public int Number { get; private set; }

        /// <summary>代号（<c>S0</c> / <c>S1</c> / <c>S2</c> / <c>S3</c> / <c>S4</c>），口头简称用。</summary>
        public string Code { get; private set; }

        /// <summary>步骤名（日志与文档统一用这一个名字）。</summary>
        public string Name { get; private set; }

        /// <summary>一句话说明这一步做什么。</summary>
        public string Purpose { get; private set; }

        /// <summary>步骤标签：<c>第 1 步 · 装配图部品入库</c>。</summary>
        public string Label
        {
            get { return string.Format("第 {0} 步 · {1}", Number, Name); }
        }

        /// <summary>日志前缀：<c>【第 1 步 · 装配图部品入库】</c>。</summary>
        public string LogHeader
        {
            get { return "【" + Label + "】"; }
        }

        public override string ToString()
        {
            return Label;
        }
    }

    /// <summary>
    /// 【CMD 分析】按钮的步骤划分 —— <b>步骤名的唯一定义处</b>。
    /// <see cref="CmdPartAnalysisService"/> 的日志、界面提示与
    /// <c>docs\CMD分析_步骤命名.md</c> 都按这里的名字写，避免各处叫法不一致。
    ///
    /// 点一次【CMD 分析】依次执行 4 步（按钮名字不变，仍是「一次性跑完」）：
    /// <list type="number">
    ///   <item><b>第 1 步 装配图部品入库</b>（旧称「阶段 A」，<see cref="CmdPartAnalyzer"/>）：
    ///         读 <c>01_アッセンブリ図_部品機能情報.csv</c> → 建行 → <c>xy.txt</c> 坐标校验 → 入库；</item>
    ///   <item><b>第 2 步 圈号拆分</b>（旧称「阶段 A2」，<see cref="CmdCalloutPartSplitter"/>）：
    ///         按 <c>parts_info.json</c> 的圈号（<c>callouts</c>）把装配图部品按位置拆成多条；</item>
    ///   <item><b>第 3 步 要領図部品追加</b>（旧称「阶段 B」，<see cref="CmdInstructionPartCollector"/>）：
    ///         顺着 <c>01_アッセンブリ図_要領図.csv</c> 读各要領図的
    ///         <c>02_要領図_部品機能情報.csv</c>，把部品挂到装配图名下。</item>
    ///   <item><b>第 4 步 tz.json 部品追加</b>（<see cref="CmdTzPartCollector"/>）：
    ///         读 <c>classify\PdfInfoList.txt</c> 里「アッセンブリ図」的分类依据，
    ///         把 <c>classify\tz.json</c>（Excel 部品表）中「品種」命中该依据的明细挂到同一装配图名下。</item>
    /// </list>
    /// 这 4 步之前还有一步不属于本按钮：<b>第 0 步 分解与分类</b>
    /// （【开始分解】= CadPartSplitter 分割 + 本工具分解 + <c>classify\PdfInfoList.txt</c>）。
    /// </summary>
    internal static class CmdAnalysisSteps
    {
        /// <summary>第 0 步（按钮外）的编号。</summary>
        public const int PrepareNumber = 0;

        /// <summary>第 0 步（按钮外）的代号。</summary>
        public const string PrepareCode = "S0";

        /// <summary>第 0 步（按钮外）的名字。</summary>
        public const string PrepareName = "分解与分类";

        /// <summary>第 1 步的编号。</summary>
        public const int AssemblyNumber = 1;

        /// <summary>第 1 步的代号。</summary>
        public const string AssemblyCode = "S1";

        /// <summary>第 1 步的名字（旧称「阶段 A」）。</summary>
        public const string AssemblyName = "装配图部品入库";

        /// <summary>第 2 步的编号。</summary>
        public const int CalloutSplitNumber = 2;

        /// <summary>第 2 步的代号。</summary>
        public const string CalloutSplitCode = "S2";

        /// <summary>第 2 步的名字（旧称「阶段 A2」）。</summary>
        public const string CalloutSplitName = "圈号拆分";

        /// <summary>第 3 步的编号。</summary>
        public const int YoryoAppendNumber = 3;

        /// <summary>第 3 步的代号。</summary>
        public const string YoryoAppendCode = "S3";

        /// <summary>第 3 步的名字（旧称「阶段 B」）。</summary>
        public const string YoryoAppendName = "要領図部品追加";

        /// <summary>第 4 步的编号。</summary>
        public const int TzAppendNumber = 4;

        /// <summary>第 4 步的代号。</summary>
        public const string TzAppendCode = "S4";

        /// <summary>第 4 步的名字。</summary>
        public const string TzAppendName = "tz.json 部品追加";

        /// <summary>第 0 步：分解与分类（按钮外，由【开始分解】完成）。</summary>
        public static readonly CmdAnalysisStep PrepareStep = new CmdAnalysisStep(PrepareNumber, PrepareCode,
            PrepareName, "【开始分解】调用 CadPartSplitter 分割并生成分类清单 PdfInfoList.txt");

        /// <summary>第 1 步：装配图部品入库。</summary>
        public static readonly CmdAnalysisStep AssemblyStep = new CmdAnalysisStep(AssemblyNumber, AssemblyCode,
            AssemblyName, "读 01_アッセンブリ図_部品機能情報.csv → 建行 → xy.txt 校验 → 入库");

        /// <summary>第 2 步：圈号拆分。</summary>
        public static readonly CmdAnalysisStep CalloutSplitStep = new CmdAnalysisStep(CalloutSplitNumber,
            CalloutSplitCode, CalloutSplitName, "按 parts_info.json 的圈号把装配图部品按位置拆成多条");

        /// <summary>第 3 步：要領図部品追加。</summary>
        public static readonly CmdAnalysisStep YoryoAppendStep = new CmdAnalysisStep(YoryoAppendNumber,
            YoryoAppendCode, YoryoAppendName, "读 01_アッセンブリ図_要領図.csv → 各 02_要領図_部品機能情報.csv → 追加建行");

        /// <summary>第 4 步：tz.json 部品追加。</summary>
        public static readonly CmdAnalysisStep TzAppendStep = new CmdAnalysisStep(TzAppendNumber,
            TzAppendCode, TzAppendName, "读 classify\\tz.json → 品種命中分类依据的明细 → 追加建行");

        /// <summary>全部步骤（第 0 步在前），供文档 / 界面提示列出。</summary>
        public static IList<CmdAnalysisStep> All
        {
            get
            {
                return new CmdAnalysisStep[]
                {
                    PrepareStep, AssemblyStep, CalloutSplitStep, YoryoAppendStep, TzAppendStep
                };
            }
        }

        /// <summary>
        /// 日志里的「执行步骤」计划行：
        /// <c>执行步骤：共 4 步 —— 第 1 步 · 装配图部品入库 → 第 2 步 · 圈号拆分 → 第 3 步 · 要領図部品追加 → 第 4 步 · tz.json 部品追加</c>。
        /// </summary>
        public static string PlanText
        {
            get
            {
                return string.Format("执行步骤：共 {0} 步 —— {1} → {2} → {3} → {4}",
                    TzAppendNumber, AssemblyStep.Label, CalloutSplitStep.Label, YoryoAppendStep.Label,
                    TzAppendStep.Label);
            }
        }

        /// <summary>
        /// 按 <see cref="CmdAnalysisStepKind"/> 取步骤对象；<see cref="CmdAnalysisStepKind.All"/> 返回 <c>null</c>
        /// （表示「全部步骤」，不对应单一步骤）。
        /// </summary>
        public static CmdAnalysisStep GetStep(CmdAnalysisStepKind kind)
        {
            switch (kind)
            {
                case CmdAnalysisStepKind.Assembly:
                    return AssemblyStep;

                case CmdAnalysisStepKind.CalloutSplit:
                    return CalloutSplitStep;

                case CmdAnalysisStepKind.YoryoAppend:
                    return YoryoAppendStep;

                case CmdAnalysisStepKind.TzAppend:
                    return TzAppendStep;

                default:
                    return null;
            }
        }

        /// <summary>
        /// 日志里的「本次执行哪几步」计划行：
        /// 全跑时是 <see cref="PlanText"/>，单步执行时是
        /// <c>执行步骤：仅 第 2 步 · 圈号拆分（分步执行）</c>。
        /// </summary>
        public static string DescribeRun(CmdAnalysisStepKind kind)
        {
            CmdAnalysisStep step = GetStep(kind);

            return step == null
                ? PlanText
                : string.Format("执行步骤：仅 {0}（分步执行）", step.Label);
        }

        /// <summary>
        /// 分步执行的按钮文字（主按钮传 <see cref="CmdAnalysisStepKind.All"/> 时返回 <c>CMD 分析</c>）。
        /// 名字本身已带编号，界面直接用这个名字，保证按钮 / 日志 / 文档叫法一致。
        /// </summary>
        public static string ButtonText(CmdAnalysisStepKind kind)
        {
            CmdAnalysisStep step = GetStep(kind);

            return step == null ? "CMD 分析" : step.Label;
        }
    }

    /// <summary>
    /// 【CMD 分析】要执行哪一步：主按钮走 <see cref="All"/>，界面上的四个子按钮各走一步。
    /// 值取自 <see cref="CmdAnalysisSteps"/> 的步骤编号，保证「第 N 步」只有一处定义。
    /// </summary>
    internal enum CmdAnalysisStepKind
    {
        /// <summary>一次跑完 4 步（= 主按钮【CMD 分析】，旧行为）。</summary>
        All = 0,

        /// <summary>第 1 步 装配图部品入库。</summary>
        Assembly = CmdAnalysisSteps.AssemblyNumber,

        /// <summary>第 2 步 圈号拆分。</summary>
        CalloutSplit = CmdAnalysisSteps.CalloutSplitNumber,

        /// <summary>第 3 步 要領図部品追加。</summary>
        YoryoAppend = CmdAnalysisSteps.YoryoAppendNumber,

        /// <summary>第 4 步 tz.json 部品追加。</summary>
        TzAppend = CmdAnalysisSteps.TzAppendNumber
    }
}
