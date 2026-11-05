using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace MSIFAITOOLS.Services
{
    /// <summary>
    /// 一个アッセンブリ図 的「按圈号分割」结果。
    /// 由 <see cref="CmdCalloutPartSplitter.Split"/> 生成，供【CMD 分析】写库。
    /// </summary>
    internal sealed class CmdCalloutSplitResult
    {
        public CmdCalloutSplitResult(string drawingNo)
        {
            DrawingNo = drawingNo ?? string.Empty;
            Rows = new List<PartBasicInfo>();
            Notes = new List<string>();
        }

        /// <summary>アッセンブリ図 の 図番。</summary>
        public string DrawingNo { get; private set; }

        /// <summary>分割后的全部行（未变动的行原样保留，顺序不变）。</summary>
        public List<PartBasicInfo> Rows { get; set; }

        /// <summary>处理过程中的提示（由调用方写进日志）。</summary>
        public List<string> Notes { get; private set; }

        /// <summary>被检查的アッセンブリ図 行数（来源标识 = アッセンブリ図）。</summary>
        public int AssemblyRowCount { get; set; }

        /// <summary>实际分割的部品行数（1 行 → 多条）。</summary>
        public int SplitGroupCount { get; set; }

        /// <summary>因分割新增的行数（= 分割后的条数 - 原行数）。</summary>
        public int AddedRowCount { get; set; }

        /// <summary>圈号在多个零件图里出现、但因文字互不包含 / 超过 3 个零件图而没分割的行数。</summary>
        public int BlockedCount { get; set; }

        /// <summary>缺少（或读不出）<c>parts_info.json</c> 的页目录个数。</summary>
        public int MissingFileCount { get; set; }
    }

    /// <summary>
    /// 【CMD 分析】的追加逻辑之二：按 <c>parts_info.json</c> 的<b>圈号</b>（<c>callouts</c>）
    /// 把アッセンブリ図 的部品行按位置分割（<b>不调用 AI</b>、不写任何文件）。
    ///
    /// 流程：
    /// <list type="number">
    ///   <item>対象是阶段 A 从 <c>01_アッセンブリ図_部品機能情報.csv</c> 登录进来的行
    ///         （「来源标识」= <see cref="PartSourceTag.CmdAssembly"/>，即 <c>アッセンブリ図</c>）；</item>
    ///   <item>用行上的「番号」去该页目录的 <c>parts_info.json</c> 找圈号 —— 也就是
    ///         <c>"text": "4"</c> 且 <c>"display": "④4"</c>（<c>no</c> 也必须是 4）的 <c>callouts</c> 数据；</item>
    ///   <item>这些圈号的 <c>index</c> 必须<b>不同</b>（同一张零件图上出现多次仍算一处）；</item>
    ///   <item>并且这些零件图的<b>全部 texts</b> 整体必须构成 <c>A ⊆ B</c> 或 <c>B ⊆ A</c> 的包含关系，
    ///         只有这样才是需要的数据；</item>
    ///   <item>满足时把该行按圈号个数拆成多条：用 <c>pdf_mm</c> 坐标判断排列方向
    ///         （左右 → 「右 / 左」、上下 → 「上 / 下」，3 处再补「中」），
    ///         把方向加在部品名前面 —— <c>縦枠上部スペーサー</c> →
    ///         <c>右縦枠上部スペーサー</c> + <c>左縦枠上部スペーサー</c>；</item>
    ///   <item>拆出来的第 1 条沿用原「番号」，其余接在该図番最大番号之后连续编号
    ///         （唯一索引是 図番 + 页码 + 番号 + 部品コード，不会冲突）。</item>
    /// </list>
    /// 判定规则、坐标容差、包含关系判定都在 <see cref="PartsInfoIndex"/> 里与阶段 B 共用。
    /// </summary>
    internal sealed class CmdCalloutPartSplitter
    {
        /// <summary>备注里「位置」的前缀。</summary>
        public const string RemarkPositionPrefix = "位置：";

        /// <summary>
        /// 分割一个アッセンブリ図 的明细行。
        /// </summary>
        /// <param name="outputDirectory">分解输出根目录（&lt;PDF 文件夹&gt;\AiAppData）。</param>
        /// <param name="drawingNo">アッセンブリ図 の 図番（= 文件夹名）。</param>
        /// <param name="parts">该図番的全部部品明细（<see cref="PartBasicInfoRepository.Query"/> 的结果）。</param>
        public CmdCalloutSplitResult Split(string outputDirectory, string drawingNo, IList<PartBasicInfo> parts)
        {
            CmdCalloutSplitResult result = new CmdCalloutSplitResult(drawingNo);

            if (parts == null || parts.Count == 0)
            {
                return result;
            }

            // 页码 → parts_info.json 索引（同一页只读一次；读不到时缓存 null）
            Dictionary<int, PartsInfoIndex> indexes = new Dictionary<int, PartsInfoIndex>();
            List<PartBasicInfo> rows = new List<PartBasicInfo>(parts.Count);
            int nextNumber = NextNumber(parts);

            foreach (PartBasicInfo part in parts)
            {
                if (part == null)
                {
                    continue;
                }

                if (!IsAssemblyRow(part))
                {
                    rows.Add(part);
                    continue;
                }

                // 已经按位置分割过的行（备注里带「位置：」）不再拆一次，避免前缀叠加
                if (IsAlreadySplit(part))
                {
                    rows.Add(part);
                    continue;
                }

                result.AssemblyRowCount++;

                PartsInfoIndex index = LoadPageIndex(outputDirectory, drawingNo, part.Page, indexes, result);

                if (index == null)
                {
                    rows.Add(part);
                    continue;
                }

                PartMarkSplitPlan plan = index.ResolveCalloutSplit(part.Number);

                if (plan == null)
                {
                    // 该番号在 parts_info.json 的 callouts 里没有（或圈号与 display 对不上）：保持原样
                    rows.Add(part);
                    continue;
                }

                if (!plan.ShouldSplit)
                {
                    if (plan.PointCount > 1)
                    {
                        result.BlockedCount++;

                        AddNote(result, string.Format(
                            "  番号 {0}（{1}）在 {2} 中 {3} 个零件图里出现，但{4} → 不分割",
                            part.Number, plan.MarkText, PartsInfoIndex.FileName, plan.PointCount,
                            plan.BlockReason.Length == 0 ? "无法命名" : plan.BlockReason));
                    }

                    rows.Add(part);
                    continue;
                }

                // 满足全部条件：按出现处数拆成多条，方向前缀加在部品名前面
                for (int prefixIndex = 0; prefixIndex < plan.Prefixes.Count; prefixIndex++)
                {
                    string prefix = plan.Prefixes[prefixIndex] ?? string.Empty;

                    // 第 1 条沿用原番号，其余接在该図番最大番号之后
                    int number = prefixIndex == 0 ? part.Number : nextNumber++;

                    rows.Add(Clone(part, number, prefix + part.PartName,
                        BuildPositionRemark(part.Remark, plan.MarkText, prefix)));
                }

                result.SplitGroupCount++;
                result.AddedRowCount += plan.Prefixes.Count - 1;

                AddNote(result, string.Format(
                    "  分割：番号 {0}（{1}）在 {2} 中 {3} 个零件图里出现、{4}排列 → 部品名加「{5}」（1 行 → {6} 条）",
                    part.Number, plan.MarkText, PartsInfoIndex.FileName, plan.PointCount,
                    plan.LayoutText, plan.JoinPrefixes(), plan.Prefixes.Count));
            }

            result.Rows = rows;

            return result;
        }

        /// <summary>
        /// 是否为阶段 A 从 <c>01_アッセンブリ図_部品機能情報.csv</c> 登录的行
        /// （「来源标识」= アッセンブリ図；老数据没有该列时看「数据来源」）。
        /// </summary>
        public static bool IsAssemblyRow(PartBasicInfo part)
        {
            if (part == null)
            {
                return false;
            }

            if (string.Equals(part.SourceTag, PartSourceTag.CmdAssembly, StringComparison.Ordinal))
            {
                return true;
            }

            return string.IsNullOrEmpty(part.SourceTag) &&
                string.Equals(part.SourceKind, PartSourceKind.CmdAssembly, StringComparison.Ordinal);
        }

        /// <summary>
        /// 该行是否已经按位置分割过（备注里已有「位置：」）——
        /// 重复执行时不再拆一次，避免部品名前缀叠加。
        /// </summary>
        private static bool IsAlreadySplit(PartBasicInfo part)
        {
            return part != null && !string.IsNullOrEmpty(part.Remark) &&
                part.Remark.IndexOf(RemarkPositionPrefix, StringComparison.Ordinal) >= 0;
        }

        /// <summary>该図番已有的最大「番号」+ 1（没有行时返回 1）。</summary>
        private static int NextNumber(IList<PartBasicInfo> parts)
        {
            int max = 0;

            foreach (PartBasicInfo part in parts)
            {
                if (part != null && part.Number > max)
                {
                    max = part.Number;
                }
            }

            return max + 1;
        }

        /// <summary>
        /// 读取该页目录的 <c>parts_info.json</c>：第 1 页是 <c>&lt;根&gt;\&lt;図番&gt;\</c>，
        /// 第 2 页起是 <c>&lt;根&gt;\&lt;図番&gt;_P2\</c> …。读不到时记一条提示并返回 null（同一页只提示一次）。
        /// </summary>
        private static PartsInfoIndex LoadPageIndex(string outputDirectory, string drawingNo, int page,
            Dictionary<int, PartsInfoIndex> cache, CmdCalloutSplitResult result)
        {
            PartsInfoIndex cached;

            if (cache.TryGetValue(page, out cached))
            {
                return cached;
            }

            int pageNumber = page <= 1 ? 1 : page;
            string folder = pageNumber == 1
                ? drawingNo
                : drawingNo + "_P" + pageNumber.ToString(CultureInfo.InvariantCulture);
            string path = Path.Combine(outputDirectory ?? string.Empty, folder, PartsInfoIndex.FileName);
            PartsInfoIndex index = PartsInfoIndex.Load(path);

            if (index == null || !string.IsNullOrEmpty(index.LoadError))
            {
                result.MissingFileCount++;

                AddNote(result, string.Format("{0}（第 {1} 页）：{2} → 不按圈号分割",
                    PartsInfoIndex.FileName, pageNumber,
                    index == null ? "读取失败" : index.LoadError));

                index = null;
            }

            cache[page] = index;

            return index;
        }

        /// <summary>
        /// 复制一行并换掉「番号 / 部品名 / 备注」——
        /// <see cref="PartBasicInfo"/> 的部品名只有构造函数能设，所以拆条时必须重建对象。
        /// </summary>
        private static PartBasicInfo Clone(PartBasicInfo source, int number, string partName, string remark)
        {
            PartBasicInfo clone = new PartBasicInfo(source.DrawingNo, source.Page, number, partName,
                source.PartDrawingNo, source.FrameKind, source.Quantity, source.BasicSize, source.CutSize,
                source.PartCode);

            clone.VerifyStatus = source.VerifyStatus;
            clone.Remark = remark;
            clone.SourceKind = source.SourceKind;
            clone.SourceTag = source.SourceTag;
            clone.SourceFile = source.SourceFile;
            clone.PdfFile = source.PdfFile;
            clone.LoginTime = source.LoginTime;

            return clone;
        }

        /// <summary>备注追加位置说明：<c>位置：④4 右</c>（原备注非空时用 <c>；</c> 接在后面）。</summary>
        private static string BuildPositionRemark(string remark, string markText, string prefix)
        {
            string position = RemarkPositionPrefix + (markText ?? string.Empty);

            if (!string.IsNullOrEmpty(prefix))
            {
                position += " " + prefix;
            }

            if (string.IsNullOrEmpty(remark))
            {
                return position;
            }

            return remark + "；" + position;
        }

        private static void AddNote(CmdCalloutSplitResult result, string note)
        {
            if (result != null && !string.IsNullOrEmpty(note))
            {
                result.Notes.Add(note);
            }
        }
    }
}
