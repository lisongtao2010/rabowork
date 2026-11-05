using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MSIFAITOOLS.Services
{
    /// <summary>
    /// 从一个「アッセンブリ図」出发，顺着要領図清单收集「要領図的部品」的结果。
    /// 由 <see cref="CmdInstructionPartCollector.Collect"/> 生成，供【CMD 分析】入库。
    /// </summary>
    internal sealed class CmdInstructionPartResult
    {
        public CmdInstructionPartResult(string drawingNo)
        {
            DrawingNo = drawingNo ?? string.Empty;
            Parts = new List<PartBasicInfo>();
            Notes = new List<string>();
        }

        /// <summary>アッセンブリ図 の 図番。</summary>
        public string DrawingNo { get; private set; }

        /// <summary>命中的部品明细（尚未入库）。</summary>
        public List<PartBasicInfo> Parts { get; private set; }

        /// <summary>处理过程中的提示（由调用方写进日志）。</summary>
        public List<string> Notes { get; private set; }

        /// <summary><c>01_アッセンブリ図_要領図.csv</c> 里的要領図条数。</summary>
        public int YoryoRowCount { get; set; }

        /// <summary>实际读到的 <c>02_要領図_部品機能情報.csv</c> 个数。</summary>
        public int YoryoPartFileCount { get; set; }

        /// <summary>缺少（01 / 02）文件的个数。</summary>
        public int MissingFileCount { get; set; }

        /// <summary>
        /// 同一要領図 内「品種 + 部品名 + 部品コード」重复而被丢弃的行数。
        /// （品種为空的行<b>不再丢弃</b>，见 <see cref="SharedRowCount"/>。）
        /// </summary>
        public int SkippedRowCount { get; set; }

        /// <summary>
        /// 「品種」为空（含 02 文件没有「品種」列 = 単純部品表 / 普通部品表）的
        /// <b>共通部品</b>登录的条数。
        /// </summary>
        public int SharedRowCount { get; set; }

        /// <summary>
        /// 部品コード 无效（空、「―」「-」等占位符、纯数字 / 不含英文字母）而被丢弃的行数。
        /// </summary>
        public int InvalidCodeRowCount { get; set; }
    }

    /// <summary>
    /// 【CMD 分析】的追加步骤：把要領図的部品挂到アッセンブリ図上（<b>不调用 AI</b>、不写任何文件）。
    ///
    /// 流程：
    /// <list type="number">
    ///   <item>输入：一览中分类为「アッセンブリ図」的 PDF，它的「分类依据」来自
    ///         <c>&lt;输出根目录&gt;\classify\PdfInfoList.txt</c>；</item>
    ///   <item>读 <c>&lt;输出根目录&gt;\&lt;図番号&gt;\01_アッセンブリ図_要領図.csv</c>
    ///         （項目 / 要領図漢字 / 図番），取出该装配图引用的全部「要領図」図番；</item>
    ///   <item>按每个要領図図番找到 <c>&lt;输出根目录&gt;\&lt;図番&gt;\02_要領図_部品機能情報.csv</c>
    ///         （品種 / 部品名 / 枠区分 / 部品コード / 員数）；</item>
    ///   <item>「品種」出现在分类依据里的行保留 —— 分类依据的正文形如
    ///         <c>table_05.csv：H310 １７ラシッサ 片引戸 枠 アッセンブリ図</c>，先按空白 / 标点
    ///         切成单词做<b>完全一致</b>比较；一条都没命中时（依据里没有分隔符）再退回
    ///         <b>包含</b>比较，尽量不漏数据；</item>
    ///   <item>「品種」<b>为空</b>的行（02 是単純部品表 / 普通部品表，或整张表没有「品種」列）
    ///         代表<b>所有品種共用</b>的部品：<b>不再丢弃</b>，一律登录，备註写 <c>品種：共通</c>；</item>
    ///   <item>部品コード 必须是<b>含英文字母</b>的编码 —— 为空、「―」「-」「／」等占位符、
    ///         纯数字（尺寸 / 员数被误填）的行一律丢弃，不作为部品登录；</item>
    ///   <item>命中的行转成 <see cref="PartBasicInfo"/>：図番 = アッセンブリ図 の図番、
    ///         部品図番 = 要領図 の図番、枠区分 / 員数 取 02 文件的同名列（没有「枠区分」列时留空）、
    ///         備注 = <c>品種：xxx</c>（+ 項目；无品種行写 <c>品種：共通</c>）、校验状态 = 未校验；</item>
    ///   <item>部品名的<b>位置前缀</b>：把「項目」的第一项加「部」（<c>A,M</c> → <c>A部</c>），
    ///         到装配图的 <c>parts_info.json</c> 里找这个标记；标记必须出现在<b>不同的零件图</b>
    ///         （json 的 <c>index</c> 不同，同一张图上多次仍算一处），并且这些零件图的<b>全部 text</b>
    ///         整体构成 <c>A ⊆ B</c> / <c>B ⊆ A</c> 的包含关系时才分割；满足时按排列方向
    ///         （左右 / 上下）给部品名加「右 / 左」「上 / 下」——<c>縦枠上部スペーサー</c> 拆成
    ///         <c>右縦枠上部スペーサー</c> + <c>左縦枠上部スペーサー</c>（见 <see cref="PartsInfoIndex"/>）。</item>
    /// </list>
    ///
    /// 页码固定为 1、番号由调用方给起始值后连续编号（部品基本信息表的唯一索引是
    /// 図番 + 页码 + 番号 + 部品コード），重复执行不会产生重复行。
    ///
    /// 注：装配图<b>自身</b>的部品先由 <see cref="CmdCalloutPartSplitter"/> 按圈号（<c>callouts</c>）分割，
    /// 本步骤只负责追加要領図 的部品，两者互不影响。
    /// </summary>
    internal sealed class CmdInstructionPartCollector
    {
        /// <summary>アッセンブリ図 → 要領図 的对照表文件名（CadPartSplitter 生成）。</summary>
        public const string YoryoListFileName = "01_アッセンブリ図_要領図.csv";

        /// <summary>要領図 的部品明细文件名（CadPartSplitter 生成；旧名 <c>LT_STEP2.csv</c>）。</summary>
        public const string YoryoPartFileName = "02_要領図_部品機能情報.csv";

        /// <summary>改名前的旧文件名：老分解结果里可能还留着它，按新名字取不到时兜底用。</summary>
        public const string LegacyYoryoPartFileName = "LT_STEP2.csv";

        /// <summary>备注里「品種」的前缀。</summary>
        public const string RemarkKindPrefix = "品種：";

        /// <summary>
        /// 没有品種（或 02 文件没有「品種」列）的行算「所有品種共用」的部品，
        /// 备注里写 <c>品種：共通</c>。
        /// </summary>
        public const string SharedKindText = "共通";

        /// <summary>
        /// 共通部品的去重键前缀：以 <c>#</c> 开头，不会和归一化后的品種文字（字母 / 数字 / 汉字）相同，
        /// 因此「同一部品既有品種行又有共通行」时两条都会保留。
        /// </summary>
        private const string SharedDedupeKey = "#共通";

        /// <summary>01_アッセンブリ図_要領図.csv 的列名。</summary>
        private const string HeaderItem = "項目";

        private const string HeaderYoryoName = "要領図漢字";
        private const string HeaderYoryoDrawingNo = "図番";

        /// <summary>02_要領図_部品機能情報.csv 的列名。</summary>
        private const string HeaderKind = "品種";

        private const string HeaderPartName = "部品名";

        /// <summary>
        /// 枠区分 的列名（単純部品表 / 普通部品表转来的 02 有这一列；品種表转来的留空）。
        /// </summary>
        private const string HeaderFrameKind = "枠区分";

        private const string HeaderPartCode = "部品コード";
        private const string HeaderQuantity = "員数";

        /// <summary>位置标记的结尾字（parts_info.json 里写作 <c>A部</c> / <c>F部</c> …）。</summary>
        private const string MarkSuffix = "部";

        /// <summary>没有位置前缀时用「一个空前缀」占位：部品名原样、只生成一条。</summary>
        private static readonly string[] EmptyPrefixes = new string[] { string.Empty };

        /// <summary>分词用的分隔符（半角 / 全角空格、常见中英文标点）。</summary>
        private static readonly char[] WordSeparators = new char[]
        {
            ' ', '\u3000', '\t', '\r', '\n', ',', '、', '，', '/', '／', '・', '|', '｜',
            ':', '：', ';', '；', '(', ')', '（', '）', '「', '」', '【', '】', '［', '］'
        };

        /// <summary>
        /// 读一个要領図 的 <c>02_要領図_部品機能情報.csv</c>，把「品種 ∈ 分类依据」的行转成部品基本信息。
        /// 部品コード 为空 / 不含英文字母（<c>―</c>、<c>-</c>、纯数字等占位内容）的行会被丢弃。
        /// 部品名会按 <paramref name="partsInfo"/> 里「項目 + 部」标记的排列加位置前缀（右 / 左、上 / 下）。
        /// </summary>
        private void CollectFromYoryoParts(PdfClassification assembly, string outputDirectory, string yoryoNo,
            string item, string yoryoName, string evidence, int startNumber, HashSet<string> addedKeys,
            PartsInfoIndex partsInfo, CmdInstructionPartResult result)
        {
            string path = LocateOutputFile(outputDirectory, yoryoNo, YoryoPartFileName);

            if (path == null)
            {
                result.MissingFileCount++;
                AddNote(result, string.Format("  要領図 {0}：缺少 {1}", yoryoNo, YoryoPartFileName));

                return;
            }

            List<string[]> rows;

            try
            {
                rows = ReadCsv(path);
            }
            catch (Exception ex)
            {
                AddNote(result, string.Format("  要領図 {0}：{1} 读取失败（{2}：{3}）", yoryoNo,
                    YoryoPartFileName, ex.GetType().Name, ex.Message));

                return;
            }

            if (rows.Count == 0)
            {
                AddNote(result, string.Format("  要領図 {0}：{1} 为空", yoryoNo, YoryoPartFileName));
                return;
            }

            string[] header = rows[0];
            int kindCol = IndexOfHeader(header, HeaderKind);
            int nameCol = IndexOfHeader(header, HeaderPartName);
            int frameKindCol = IndexOfHeader(header, HeaderFrameKind);
            int codeCol = IndexOfHeader(header, HeaderPartCode);
            int quantityCol = IndexOfHeader(header, HeaderQuantity);

            if (kindCol < 0)
            {
                // 単純部品表 / 普通部品表转来的 02：没有「品種」列 → 全部行按「无品種（共通）」登录
                AddNote(result, string.Format(
                    "  要領図 {0}：表头没有「{1}」列 → 全部行按无品種（共通）登录（表头：{2}）",
                    yoryoNo, HeaderKind, JoinHeader(header)));
            }

            if (nameCol < 0 && codeCol < 0)
            {
                AddNote(result, string.Format("  要領図 {0}：表头没有「部品名 / 部品コード」列，已跳过（表头：{1}）",
                    yoryoNo, JoinHeader(header)));

                return;
            }

            // 有「部品コード」列时才校验编码：没有该列的老格式文件仍按原规则（部品名或部品コード 非空）收集
            bool requireValidCode = codeCol >= 0;

            // 先看这个文件里有哪些品種，再一次性判定「命中分类依据」的品種集合。
            // 品種为空的行 = 所有品種共用的部品（単純部品表 / 普通部品表），单独计数、不参与品種匹配
            Dictionary<string, string> kindTexts =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int sharedRowTotal = 0;

            for (int rowIndex = 1; rowIndex < rows.Count; rowIndex++)
            {
                string[] row = rows[rowIndex];

                if (IsEmptyRow(row))
                {
                    continue;
                }

                string raw = Cell(row, kindCol).Trim();
                string key = NormalizeKey(raw);

                if (IsBlankKind(key))
                {
                    sharedRowTotal++;

                    continue;
                }

                if (kindTexts.ContainsKey(key))
                {
                    continue;
                }

                kindTexts.Add(key, raw);
            }

            HashSet<string> matched = ResolveMatchedKinds(kindTexts.Keys, evidence);
            List<string> fileRows = new List<string>();

            result.YoryoPartFileCount++;

            if (matched.Count == 0 && sharedRowTotal == 0)
            {
                if (kindTexts.Count == 0)
                {
                    AddNote(result, string.Format("  要領図 {0}：{1} 里没有品種、也没有部品行",
                        yoryoNo, YoryoPartFileName));
                }
                else
                {
                    AddNote(result, string.Format("  要領図 {0}：品種（{1}）都不在分类依据里，跳过",
                        yoryoNo, JoinLimited(new List<string>(kindTexts.Values), 4)));
                }

                return;
            }

            if (kindTexts.Count > 0 && matched.Count == 0)
            {
                AddNote(result, string.Format(
                    "  要領図 {0}：品種（{1}）都不在分类依据里 → 只登录无品種（共通）的 {2} 行",
                    yoryoNo, JoinLimited(new List<string>(kindTexts.Values), 4), sharedRowTotal));
            }
            else if (sharedRowTotal > 0)
            {
                AddNote(result, string.Format("  要領図 {0}：另有 {1} 行没有品種（共通部品），一并登录",
                    yoryoNo, sharedRowTotal));
            }

            int rowCount = 0;
            int expandedCount = 0;
            int invalidCodeCount = 0;
            int sharedCount = 0;

            // 「A部」这类位置标记在装配图上的出现情况：2 处以上才给部品名加「右 / 左」「上 / 下」
            PartMarkSplitPlan splitPlan = partsInfo == null ? null : partsInfo.ResolveSplit(BuildItemMark(item));

            for (int rowIndex = 1; rowIndex < rows.Count; rowIndex++)
            {
                string[] row = rows[rowIndex];

                if (IsEmptyRow(row))
                {
                    continue;
                }

                string rawKind = Cell(row, kindCol).Trim();
                string kindKey = NormalizeKey(rawKind);

                // 品種为空（或只写了「―」「-」「／」「共通」「なし」等占位符）= 共通部品（不分品種），
                // 一律登录；有品種时必须是命中分类依据的品種（其余品種的部品不属于这张装配图）
                bool isShared = IsBlankKind(kindKey);

                if (!isShared && !matched.Contains(kindKey))
                {
                    continue;
                }

                string partName = Cell(row, nameCol);
                string partCode = Cell(row, codeCol);

                // 部品コード 必须是「含英文字母」的编码：
                // 空、「―」「-」「／」等占位符、纯数字（尺寸 / 员数被误填）都不算部品，跳过。
                if (requireValidCode && !IsValidPartCode(partCode))
                {
                    result.InvalidCodeRowCount++;
                    invalidCodeCount++;

                    continue;
                }

                if (!requireValidCode && partName.Length == 0 && partCode.Length == 0)
                {
                    continue;
                }

                // 同一要領図 里同品種 / 同部品 / 同コード 只取一次
                //（共通部品用固定键，不与同名同碼的「有品種」行互相吞掉）
                string dedupeKey = (isShared ? SharedDedupeKey : kindKey) + "#" +
                    Normalize(partName) + "#" + Normalize(partCode);

                if (!addedKeys.Add(dedupeKey))
                {
                    result.SkippedRowCount++;

                    continue;
                }

                // 部品名的位置前缀：項目（A,M → A）在 parts_info.json 里出现 2 处以上时，
                // 按排列方向拆成「右… / 左…」或「上… / 下…」多条（部品名为空时不加前缀）
                IList<string> namePrefixes = EmptyPrefixes;

                if (splitPlan != null && splitPlan.ShouldSplit && partName.Length > 0)
                {
                    namePrefixes = splitPlan.Prefixes;
                }

                foreach (string prefix in namePrefixes)
                {
                    string displayName = prefix.Length == 0 ? partName : prefix + partName;

                    PartBasicInfo part = new PartBasicInfo(
                        assembly.Stem,
                        1,
                        startNumber + result.Parts.Count,
                        displayName,
                        yoryoNo,
                        Cell(row, frameKindCol),
                        Cell(row, quantityCol),
                        string.Empty,
                        string.Empty,
                        partCode);

                    part.Remark = BuildRemark(isShared ? SharedKindText : kindTexts[kindKey], item);
                    part.SourceKind = PartSourceKind.CmdYoryo;
                    part.SourceTag = PartSourceTag.CmdYoryo;
                    part.SourceFile = path;
                    part.PdfFile = assembly.FilePath ?? string.Empty;
                    part.VerifyStatus = PartVerifyStatus.NotChecked;

                    result.Parts.Add(part);
                }

                rowCount++;

                if (isShared)
                {
                    sharedCount++;
                    result.SharedRowCount++;
                }
                else if (!fileRows.Contains(kindTexts[kindKey]))
                {
                    fileRows.Add(kindTexts[kindKey]);
                }

                expandedCount += namePrefixes.Count;
            }

            string countText = (rowCount - sharedCount) + " 行";

            if (sharedCount > 0)
            {
                countText += " + 无品種（共通）" + sharedCount + " 行";
            }

            AddNote(result, string.Format("  要領図 {0}（{1}）：品種命中 {2} / 命中品種 {3}",
                yoryoNo, Shorten(yoryoName, 18), countText, JoinLimited(fileRows, 4)));

            if (invalidCodeCount > 0)
            {
                AddNote(result, string.Format(
                    "  要領図 {0}：部品コード 为空 / 无效（不含英文字母）{1} 条，已跳过",
                    yoryoNo, invalidCodeCount));
            }

            if (splitPlan != null && expandedCount > rowCount)
            {
                AddNote(result, string.Format(
                    "  要領図 {0}：{1} 在 {2} 中 {3} 个零件图里出现、{4}排列 → 部品名加「{5}」（{6} 行 → {7} 条）",
                    yoryoNo, splitPlan.MarkText, PartsInfoIndex.FileName, splitPlan.PointCount,
                    splitPlan.LayoutText, splitPlan.JoinPrefixes(), rowCount, expandedCount));
            }
            else if (splitPlan != null && splitPlan.PointCount > 1)
            {
                // 出现在多个零件图里却没分割：文字互不包含（不是同一部品的不同位置）/ 超过 3 个零件图
                AddNote(result, string.Format(
                    "  要領図 {0}：{1} 在 {2} 中 {3} 个零件图里出现，但{4} → 部品名不加位置前缀",
                    yoryoNo, splitPlan.MarkText, PartsInfoIndex.FileName, splitPlan.PointCount,
                    splitPlan.BlockReason.Length == 0 ? "部品名为空" : splitPlan.BlockReason));
            }
        }

        /// <summary>
        /// 收集该アッセンブリ図 名下、分类依据命中的要領図部品。
        /// </summary>
        /// <param name="assembly">一览中的アッセンブリ図（<see cref="PdfClassification.Evidence"/> 即分类依据）。</param>
        /// <param name="outputDirectory">分解输出根目录（&lt;PDF 文件夹&gt;\AiAppData）。</param>
        /// <param name="startNumber">本次追加行的起始「番号」（接在已有行之后）。</param>
        public CmdInstructionPartResult Collect(PdfClassification assembly, string outputDirectory,
            int startNumber)
        {
            if (assembly == null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }

            string drawingNo = assembly.Stem;
            CmdInstructionPartResult result = new CmdInstructionPartResult(drawingNo);
            string evidence = assembly.Evidence ?? string.Empty;

            string yoryoPath = LocateOutputFile(outputDirectory, drawingNo, YoryoListFileName);

            if (yoryoPath == null)
            {
                result.MissingFileCount++;

                AddNote(result, string.Format("缺少 {0}（{1}）：跳过要領図部品",
                    YoryoListFileName, Path.Combine(outputDirectory ?? string.Empty, drawingNo)));

                return result;
            }

            List<string[]> yoryoRows;

            try
            {
                yoryoRows = ReadCsv(yoryoPath);
            }
            catch (Exception ex)
            {
                AddNote(result, string.Format("{0}：读取失败（{1}：{2}）", YoryoListFileName,
                    ex.GetType().Name, ex.Message));

                return result;
            }

            if (yoryoRows.Count == 0)
            {
                AddNote(result, YoryoListFileName + "：文件为空");
                return result;
            }

            string[] yoryoHeader = yoryoRows[0];
            int drawingNoCol = IndexOfHeader(yoryoHeader, HeaderYoryoDrawingNo);
            int yoryoNameCol = IndexOfHeader(yoryoHeader, HeaderYoryoName);
            int itemCol = IndexOfHeader(yoryoHeader, HeaderItem);

            if (drawingNoCol < 0)
            {
                AddNote(result, YoryoListFileName + "：表头没有「" + HeaderYoryoDrawingNo +
                    "」列（表头：" + JoinHeader(yoryoHeader) + "）");

                return result;
            }

            // 要領図 図番 → 項目 / 要領図漢字（同一図番出现多次时合并項目）
            List<string> yoryoOrder = new List<string>();
            Dictionary<string, string> items = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int rowIndex = 1; rowIndex < yoryoRows.Count; rowIndex++)
            {
                string[] row = yoryoRows[rowIndex];

                if (IsEmptyRow(row))
                {
                    continue;
                }

                string yoryoNo = CleanDrawingNo(Cell(row, drawingNoCol));

                if (yoryoNo.Length == 0)
                {
                    continue;
                }

                result.YoryoRowCount++;

                if (!items.ContainsKey(yoryoNo))
                {
                    yoryoOrder.Add(yoryoNo);
                    items.Add(yoryoNo, Cell(row, itemCol));
                    names.Add(yoryoNo, Cell(row, yoryoNameCol));
                }
                else
                {
                    // 同一要領図 在多个「項目」下出现：項目合并显示（A,M / B / F …）
                    string extra = Cell(row, itemCol);

                    if (extra.Length > 0 && items[yoryoNo].IndexOf(extra, StringComparison.Ordinal) < 0)
                    {
                        items[yoryoNo] = items[yoryoNo].Length == 0
                            ? extra
                            : items[yoryoNo] + " / " + extra;
                    }
                }
            }

            if (yoryoOrder.Count == 0)
            {
                AddNote(result, YoryoListFileName + "：没有读到要領図図番");
                return result;
            }

            AddNote(result, string.Format("{0}：{1} 行 → 要領図 {2} 个（{3}）",
                YoryoListFileName, result.YoryoRowCount, yoryoOrder.Count,
                JoinLimited(yoryoOrder, 6)));

            HashSet<string> addedKeys = new HashSet<string>(StringComparer.Ordinal);

            // 位置标记（A部 / B部 …）索引：装配图文件夹里的 parts_info.json，用来给部品名加「右 / 左」「上 / 下」
            PartsInfoIndex partsInfo = LoadPartsInfo(outputDirectory, drawingNo, result);

            foreach (string yoryoNo in yoryoOrder)
            {
                CollectFromYoryoParts(assembly, outputDirectory, yoryoNo, items[yoryoNo], names[yoryoNo],
                    evidence, startNumber, addedKeys, partsInfo, result);
            }

            return result;
        }

        /// <summary>
        /// 读取装配图的 <c>parts_info.json</c>（多页 PDF 时取第一个存在的页目录）。
        /// 文件不存在时返回 null（不给部品名加前缀，保持老行为）；解析失败时记一条提示后返回 null。
        /// </summary>
        private static PartsInfoIndex LoadPartsInfo(string outputDirectory, string drawingNo,
            CmdInstructionPartResult result)
        {
            string path = LocateOutputFile(outputDirectory, drawingNo, PartsInfoIndex.FileName);

            if (path == null)
            {
                return null;
            }

            PartsInfoIndex index = PartsInfoIndex.Load(path);

            if (index == null)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(index.LoadError))
            {
                AddNote(result, string.Format("{0}：{1} → 部品名不按位置分割",
                    PartsInfoIndex.FileName, index.LoadError));

                return null;
            }

            return index;
        }

        /// <summary>
        /// 把 01 的「項目」变成 <c>parts_info.json</c> 里的位置标记文字：
        /// 按逗号等分隔符取<b>第一项</b>，再补「部」字 —— <c>A,M</c> → <c>A</c> → <c>A部</c>。
        /// 为空返回空串；已经以「部」结尾时原样返回。
        /// </summary>
        private static string BuildItemMark(string item)
        {
            string text = (item ?? string.Empty).Trim();

            if (text.Length == 0)
            {
                return string.Empty;
            }

            string first = string.Empty;
            string[] parts = text.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);

            foreach (string part in parts)
            {
                string token = part.Trim();

                if (token.Length > 0)
                {
                    first = token;

                    break;
                }
            }

            if (first.Length == 0)
            {
                return string.Empty;
            }

            return first.EndsWith(MarkSuffix, StringComparison.Ordinal) ? first : first + MarkSuffix;
        }

        // ── 品種匹配 ─────────────────────────────────────────────────────────

        /// <summary>
        /// 判定哪些品種出现在分类依据里。
        /// 先按空白 / 标点把依据切成单词做完全一致比较；一个都没命中时退回包含比较
        /// （依据里的各个词可能没有分隔符，例如 <c>H310１７ラシッサ片引戸枠アッセンブリ図</c>）。
        /// </summary>
        /// <param name="kindKeys">该 02 文件里出现过的品種（已归一化）。</param>
        /// <param name="evidence">分类依据原文。</param>
        private static HashSet<string> ResolveMatchedKinds(IEnumerable<string> kindKeys, string evidence)
        {
            HashSet<string> matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> kindList = new List<string>();

            foreach (string kindKey in kindKeys)
            {
                if (!string.IsNullOrEmpty(kindKey))
                {
                    kindList.Add(kindKey);
                }
            }

            if (kindList.Count == 0)
            {
                return matched;
            }

            // ① 单词完全一致
            List<string> tokens = Tokenize(evidence);

            if (tokens.Count > 0)
            {
                Dictionary<string, bool> tokenSet = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

                foreach (string token in tokens)
                {
                    string key = NormalizeKey(token);

                    if (key.Length > 0 && !tokenSet.ContainsKey(key))
                    {
                        tokenSet.Add(key, true);
                    }
                }

                foreach (string kindKey in kindList)
                {
                    if (tokenSet.ContainsKey(kindKey))
                    {
                        matched.Add(kindKey);
                    }
                }
            }

            if (matched.Count > 0)
            {
                return matched;
            }

            // ② 退回包含比较
            string flat = NormalizeKey(evidence);

            if (flat.Length == 0)
            {
                return matched;
            }

            foreach (string kindKey in kindList)
            {
                if (flat.IndexOf(kindKey, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    matched.Add(kindKey);
                }
            }

            return matched;
        }

        /// <summary>把分类依据按空白与常见标点切成单词（去掉空串）。</summary>
        private static List<string> Tokenize(string text)
        {
            List<string> tokens = new List<string>();

            if (string.IsNullOrEmpty(text))
            {
                return tokens;
            }

            string[] parts = text.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);

            foreach (string part in parts)
            {
                string token = part.Trim();

                if (token.Length > 0)
                {
                    tokens.Add(token);
                }
            }

            return tokens;
        }

        /// <summary>
        /// 匹配用的归一化：全角 ASCII（含全角数字 / 字母）转半角，去掉空白，便于与 PDF 正文比较。
        /// </summary>
        private static string NormalizeKey(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(value.Length);

            foreach (char ch in value)
            {
                char current = ch;

                if (ch >= '\uFF01' && ch <= '\uFF5E')
                {
                    current = (char)(ch - 0xFEE0);
                }
                else if (ch == '\u3000')
                {
                    current = ' ';
                }

                if (current == ' ' || current == '\t' || current == '\r' || current == '\n')
                {
                    continue;
                }

                builder.Append(current);
            }

            return builder.ToString().ToUpperInvariant();
        }

        /// <summary>
        /// 「品種」格子为空、或只写了占位内容（<c>―</c> / <c>-</c> / <c>／</c> / <c>・</c> /
        /// <c>共 通</c> / <c>なし</c> / <c>無</c> …）= 没有品種，按<b>共通部品</b>处理。
        /// </summary>
        /// <param name="kindKey">已归一化的品種文字（见 <see cref="NormalizeKey"/>）。</param>
        private static bool IsBlankKind(string kindKey)
        {
            if (string.IsNullOrEmpty(kindKey))
            {
                return true;
            }

            if (string.Equals(kindKey, SharedKindText, StringComparison.Ordinal) ||
                string.Equals(kindKey, "なし", StringComparison.Ordinal) ||
                string.Equals(kindKey, "ナシ", StringComparison.Ordinal) ||
                string.Equals(kindKey, "無", StringComparison.Ordinal))
            {
                return true;
            }

            // 只有破折号 / 斜线 / 中点 / 波浪线之类的占位符也算「没有品種」
            foreach (char ch in kindKey)
            {
                if (ch == '-' || ch == '\u2015' || ch == '\u2014' || ch == '/' ||
                    ch == '\u30FB' || ch == '~' || ch == '*' || ch == '_' || ch == '.')
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        /// <summary>
        /// 部品コード 是否有效：去掉空白、全角转半角并大写后，必须至少含一个英文字母（A-Z）。
        /// 因此「空」「―」「-」「/」「・」等占位符、纯数字（尺寸 / 员数被误填）都判为无效。
        /// </summary>
        private static bool IsValidPartCode(string value)
        {
            string key = NormalizeKey(value);

            if (key.Length == 0)
            {
                return false;
            }

            foreach (char ch in key)
            {
                if (ch >= 'A' && ch <= 'Z')
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>把清单里的図番整理成文件夹名（去掉可能的「.pdf」与路径）。</summary>
        private static string CleanDrawingNo(string value)
        {
            string text = (value ?? string.Empty).Trim();

            if (text.Length == 0)
            {
                return string.Empty;
            }

            if (text.IndexOf('\\') >= 0 || text.IndexOf('/') >= 0)
            {
                text = Path.GetFileName(text);
            }

            if (text.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(0, text.Length - 4);
            }

            return text.Trim();
        }

        /// <summary>备注文本：<c>品種：片引戸；項目：A,M</c>。</summary>
        private static string BuildRemark(string kindText, string item)
        {
            string remark = RemarkKindPrefix + (kindText ?? string.Empty).Trim();

            string itemText = (item ?? string.Empty).Trim();

            if (itemText.Length > 0)
            {
                remark += "；項目：" + itemText;
            }

            return remark;
        }

        // ── 文件定位 ─────────────────────────────────────────────────────────

        /// <summary>
        /// 在输出根目录下找 <c>&lt;図番&gt;\&lt;fileName&gt;</c>；没有再找
        /// <c>&lt;図番&gt;_P2\&lt;fileName&gt;</c>（多页 PDF 的第 2 页起）等分页目录。
        /// 改名前的旧名字（<see cref="LegacyYoryoPartFileName"/> 等）也认。都找不到返回 null。
        /// </summary>
        public static string LocateOutputFile(string outputDirectory, string drawingNo, string fileName)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory) || string.IsNullOrWhiteSpace(drawingNo) ||
                string.IsNullOrWhiteSpace(fileName) || !Directory.Exists(outputDirectory))
            {
                return null;
            }

            string stem = drawingNo.Trim();
            string direct = FindInFolder(Path.Combine(outputDirectory, stem), fileName);

            if (direct != null)
            {
                return direct;
            }

            string[] folders;

            try
            {
                folders = Directory.GetDirectories(outputDirectory, stem + "_P*");
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }

            List<string> ordered = new List<string>(folders);
            ordered.Sort(StringComparer.OrdinalIgnoreCase);

            foreach (string folder in ordered)
            {
                string path = FindInFolder(folder, fileName);

                if (path != null)
                {
                    return path;
                }
            }

            return null;
        }

        /// <summary>在 <paramref name="folder"/> 里找 <paramref name="fileName"/>；找不到时再试改名前的旧名字。</summary>
        private static string FindInFolder(string folder, string fileName)
        {
            if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(fileName) || !Directory.Exists(folder))
            {
                return null;
            }

            string path = Path.Combine(folder, fileName);

            if (File.Exists(path))
            {
                return path;
            }

            if (string.Equals(fileName, YoryoPartFileName, StringComparison.OrdinalIgnoreCase))
            {
                path = Path.Combine(folder, LegacyYoryoPartFileName);

                if (File.Exists(path))
                {
                    return path;
                }
            }

            return null;
        }

        /// <summary>该路径是否是要領図 的部品明细文件（<c>02_要領図_部品機能情報.csv</c> 或旧名 <c>LT_STEP2.csv</c>）。</summary>
        public static bool IsYoryoPartFile(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            return path.EndsWith(YoryoPartFileName, StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(LegacyYoryoPartFileName, StringComparison.OrdinalIgnoreCase);
        }

        // ── CSV 读取（RFC 4180，与 CadPartSplitter 的 CsvFile 一致） ─────────

        /// <summary>
        /// 读取 CSV：逗号分隔、双引号包裹的字段内可含逗号 / 换行、内部引号用两个引号转义；
        /// BOM 由 <see cref="File.ReadAllText(string, Encoding)"/> 自动丢弃。
        /// </summary>
        private static List<string[]> ReadCsv(string path)
        {
            List<string[]> rows = new List<string[]>();
            string text = File.ReadAllText(path, Encoding.UTF8);

            if (text.Length == 0)
            {
                return rows;
            }

            List<string> row = new List<string>();
            StringBuilder field = new StringBuilder();
            bool inQuotes = false;
            int i = 0;

            while (i < text.Length)
            {
                char ch = text[i];

                if (inQuotes)
                {
                    if (ch == '\"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '\"')
                        {
                            field.Append('\"');
                            i += 2;

                            continue;
                        }

                        inQuotes = false;
                        i++;

                        continue;
                    }

                    field.Append(ch);
                    i++;

                    continue;
                }

                switch (ch)
                {
                    case '\"':
                        inQuotes = true;
                        i++;
                        break;

                    case ',':
                        row.Add(field.ToString());
                        field.Length = 0;
                        i++;
                        break;

                    case '\r':
                        i++;
                        break;

                    case '\n':
                        row.Add(field.ToString());
                        field.Length = 0;
                        rows.Add(row.ToArray());
                        row.Clear();
                        i++;
                        break;

                    default:
                        field.Append(ch);
                        i++;
                        break;
                }
            }

            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                rows.Add(row.ToArray());
            }

            return rows;
        }

        // ── 小工具 ───────────────────────────────────────────────────────────

        /// <summary>取指定列的文本（列号为 -1 或越界时返回空字符串），去掉首尾空白。</summary>
        private static string Cell(string[] row, int column)
        {
            if (row == null || column < 0 || column >= row.Length)
            {
                return string.Empty;
            }

            return (row[column] ?? string.Empty).Trim();
        }

        /// <summary>先按完全一致、再按「包含」找表头列，找不到返回 -1。</summary>
        private static int IndexOfHeader(string[] header, string name)
        {
            if (header == null || string.IsNullOrEmpty(name))
            {
                return -1;
            }

            for (int index = 0; index < header.Length; index++)
            {
                if (string.Equals(Normalize(header[index]), name, StringComparison.Ordinal))
                {
                    return index;
                }
            }

            for (int index = 0; index < header.Length; index++)
            {
                if (Normalize(header[index]).IndexOf(name, StringComparison.Ordinal) >= 0)
                {
                    return index;
                }
            }

            return -1;
        }

        /// <summary>整行都是空白（尾部空行、合并单元格造成的空行）。</summary>
        private static bool IsEmptyRow(string[] row)
        {
            if (row == null)
            {
                return true;
            }

            foreach (string cell in row)
            {
                if (Normalize(cell).Length > 0)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>归一化：去掉半角 / 全角空格与换行（PDF 表头常被拆成两行）。</summary>
        private static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace(" ", string.Empty)
                        .Replace("\u3000", string.Empty)
                        .Replace("\r", string.Empty)
                        .Replace("\n", string.Empty)
                        .Trim();
        }

        /// <summary>把表头拼成一行，便于在日志里看清「为什么没匹配上」。</summary>
        private static string JoinHeader(string[] header)
        {
            if (header == null || header.Length == 0)
            {
                return "（空）";
            }

            List<string> cells = new List<string>();

            foreach (string cell in header)
            {
                string text = Normalize(cell);
                cells.Add(text.Length == 0 ? "''" : text);
            }

            return string.Join(" | ", cells.ToArray());
        }

        /// <summary>用「/」连接前 <paramref name="max"/> 项，超出部分用「…」表示。</summary>
        private static string JoinLimited(IList<string> values, int max)
        {
            if (values == null || values.Count == 0)
            {
                return "（无）";
            }

            List<string> shown = new List<string>();

            for (int index = 0; index < values.Count && index < max; index++)
            {
                shown.Add(values[index]);
            }

            string text = string.Join(" / ", shown.ToArray());

            return values.Count > max ? text + " …" : text;
        }

        /// <summary>截断过长文本（日志里显示要領図名称用）。</summary>
        private static string Shorten(string value, int max)
        {
            string text = (value ?? string.Empty).Trim();

            if (max <= 0 || text.Length <= max)
            {
                return text.Length == 0 ? "（无名称）" : text;
            }

            return text.Substring(0, max) + "…";
        }

        /// <summary>往结果里追加一条提示。</summary>
        private static void AddNote(CmdInstructionPartResult result, string note)
        {
            if (result != null && !string.IsNullOrEmpty(note))
            {
                result.Notes.Add(note);
            }
        }
    }
}



