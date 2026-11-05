using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace MSIFAITOOLS.Services
{
    /// <summary>
    /// <c>parts_info.json</c> 里一个位置标记（如 <c>A部</c>）的出现点：页面毫米坐标、左上原点。
    /// 坐标取文字框中心，因此同一标记在不同零件图（Part_01 / Part_02 …）里的位置可以直接比较。
    /// </summary>
    internal sealed class PartMarkPoint
    {
        public PartMarkPoint(double x, double y)
            : this(0, x, y)
        {
        }

        public PartMarkPoint(int partIndex, double x, double y)
        {
            PartIndex = partIndex;
            X = x;
            Y = y;
        }

        /// <summary>
        /// 该点来自哪个零件图（<c>parts_info.json</c> 每项的 <c>index</c>；没有该字段时用数组顺序）。
        /// 位置分割只认「不同的零件图」，同一张零件图里出现多次仍算一处。
        /// </summary>
        public int PartIndex { get; private set; }

        /// <summary>页面 X（mm，文字框中心）。</summary>
        public double X { get; private set; }

        /// <summary>页面 Y（mm，文字框中心）。</summary>
        public double Y { get; private set; }
    }

    /// <summary>位置标记在图纸上的排列方向。</summary>
    internal enum PartMarkLayout
    {
        /// <summary>没有分割（不足两处、文字互不包含、超过 3 个零件图）—— 不分割。</summary>
        None = 0,

        /// <summary>左右排列（沿 X 方向铺开）—— 部品名加「右 / 左」。</summary>
        Horizontal = 1,

        /// <summary>上下排列（沿 Y 方向铺开）—— 部品名加「上 / 下」。</summary>
        Vertical = 2
    }

    /// <summary>
    /// 一个位置标记（<c>項目 + 部</c>，如 <c>A部</c>）在 <c>parts_info.json</c> 里的出现情况：
    /// 出现在几个<b>不同的零件图</b>里、按什么方向排列、部品名该加哪些前缀（<c>右 / 左</c> 或 <c>上 / 下</c>）。
    /// </summary>
    internal sealed class PartMarkSplitPlan
    {
        public PartMarkSplitPlan(string markText, PartMarkLayout layout, IList<string> prefixes, int pointCount,
            string blockReason = null)
        {
            MarkText = markText ?? string.Empty;
            Layout = layout;
            Prefixes = prefixes ?? new List<string>();
            PointCount = pointCount;
            BlockReason = blockReason ?? string.Empty;
        }

        /// <summary>标记文字（如 <c>A部</c>）。</summary>
        public string MarkText { get; private set; }

        /// <summary>排列方向。</summary>
        public PartMarkLayout Layout { get; private set; }

        /// <summary>部品名前缀（按图纸上的顺序：右 → 左 / 上 → 下）；不分割时为空。</summary>
        public IList<string> Prefixes { get; private set; }

        /// <summary>去重后的出现处数（= 含有该标记的不同零件图个数）。</summary>
        public int PointCount { get; private set; }

        /// <summary>
        /// 出现 2 处以上却没分割的原因（日志用）；
        /// 例如「这些零件图的文字不构成 A ⊆ B / B ⊆ A 的包含关系」「超过 3 个零件图」。
        /// </summary>
        public string BlockReason { get; private set; }

        /// <summary>是否要把一条部品行拆成多条（出现 2 处以上且能命名）。</summary>
        public bool ShouldSplit
        {
            get { return Prefixes.Count > 1; }
        }

        /// <summary>排列方向的中文说法（日志用）。</summary>
        public string LayoutText
        {
            get
            {
                if (Layout == PartMarkLayout.Horizontal)
                {
                    return "左右";
                }

                return Layout == PartMarkLayout.Vertical ? "上下" : "同一处";
            }
        }

        /// <summary>前缀连成一行（日志用，如 <c>右 / 左</c>）。</summary>
        public string JoinPrefixes()
        {
            return Prefixes.Count == 0 ? "（无）" : string.Join(" / ", Prefixes);
        }
    }

    /// <summary>
    /// <c>parts_info.json</c>（CadPartSplitter 生成）里「文字 → 出现位置」的索引。
    ///
    /// 用途：要領図 的 01 清单里「項目」是 <c>A,M</c> 这样的字母（图纸上的 <c>A部</c> 位置），
    /// 装配图的 <c>parts_info.json</c> 里会把这些位置标成 <c>A部</c> / <c>M部</c> 的文字。
    /// 同一个标记出现在<b>不同的零件图</b>里时，说明该位置的部品在图上装了两处（例如左右两根縦枠），
    /// 这时按出现位置的排列方向，把「右 / 左」或「上 / 下」加到部品名前面：
    /// <c>縦枠上部スペーサー</c> → <c>右縦枠上部スペーサー</c> + <c>左縦枠上部スペーサー</c>。
    ///
    /// <b>分割记录的两条追加规则</b>（避免把碰巧同名标记当成两处而拆错）：
    /// <list type="number">
    ///   <item>多条数据必须来自<b> index 不同</b>的零件图 —— 同一张零件图上出现多次仍算一处；</item>
    ///   <item>这些零件图里的<b>全部 text</b>（<c>texts[].text</c> 归一化后的集合）整体必须构成
    ///         <c>A ⊆ B</c> 或 <c>B ⊆ A</c> 的包含关系（两两可比），只有满足该关系的数据才允许分割记录；
    ///         互不包含（各自都有对方没有的文字）说明是不同零件，不分割。</item>
    /// </list>
    ///
    /// <b>两类数据</b>：
    ///   · <c>texts[].text</c> 的位置标记（<c>A部</c> …）—— 阶段 B 追加要領図 部品时用
    ///     （<see cref="ResolveSplit"/>）；
    ///   · <c>callouts[]</c> 的圈号（<c>"text": "4"</c> / <c>"display": "④4"</c>）—— 按圈号分割
    ///     アッセンブリ図 部品时用（<see cref="ResolveCalloutSplit(int)"/>）；圈号必须
    ///     <c>text</c> / <c>display</c> / <c>no</c> 三者自洽才采用（圈字符与数字都要等于圈号）。
    ///
    /// 坐标一律用 <c>pdf_mm</c>（页面毫米、左上原点）：它是整页坐标，跨零件图可比；
    /// 没有 <c>pdf_mm</c> 的老文件退回 <c>pixel</c>（同一坐标系，只是单位不同）。
    /// </summary>
    internal sealed class PartsInfoIndex
    {
        /// <summary>CadPartSplitter 生成的零件信息文件名。</summary>
        public const string FileName = "parts_info.json";

        /// <summary>两处文字相距小于该值（mm）时视为同一处（PDF 文字层的重复提取）。</summary>
        private const double SamePointToleranceMm = 1.0;

        /// <summary>能自动命名的最大零件图个数（2 个：右/左、上/下；3 个：右/中/左、上/中/下）。</summary>
        private const int MaxSplitCount = 3;

        /// <summary>标记文字（归一化后）→ 出现点（每个点带着零件图 index）。</summary>
        private readonly Dictionary<string, List<PartMarkPoint>> _points;

        /// <summary>圈号（<c>callouts[].text</c> 的 ASCII 数字，如 <c>4</c>）→ 出现点（每个点带着零件图 index）。</summary>
        private readonly Dictionary<string, List<PartMarkPoint>> _calloutPoints;

        /// <summary>圈号 → 第一处 <c>display</c> 原文（日志用，如 <c>④4</c>）。</summary>
        private readonly Dictionary<string, string> _calloutDisplays;

        /// <summary>零件图 index → 该零件图里出现过的全部文字（归一化后），用于判定包含关系。</summary>
        private readonly Dictionary<int, HashSet<string>> _partTexts;

        private PartsInfoIndex(Dictionary<string, List<PartMarkPoint>> points,
            Dictionary<string, List<PartMarkPoint>> calloutPoints,
            Dictionary<string, string> calloutDisplays,
            Dictionary<int, HashSet<string>> partTexts, string loadError)
        {
            _points = points ??
                new Dictionary<string, List<PartMarkPoint>>(StringComparer.Ordinal);
            _calloutPoints = calloutPoints ??
                new Dictionary<string, List<PartMarkPoint>>(StringComparer.Ordinal);
            _calloutDisplays = calloutDisplays ??
                new Dictionary<string, string>(StringComparer.Ordinal);
            _partTexts = partTexts ?? new Dictionary<int, HashSet<string>>();
            LoadError = loadError;
        }

        /// <summary>读取 / 解析失败的原因；成功时为 null。</summary>
        public string LoadError { get; private set; }

        /// <summary>索引里出现过的标记文字个数。</summary>
        public int MarkCount
        {
            get { return _points.Count; }
        }

        /// <summary>索引里出现过的圈号个数（<c>callouts</c>）。</summary>
        public int CalloutCount
        {
            get { return _calloutPoints.Count; }
        }

        /// <summary>
        /// 读取并解析 <paramref name="path"/>。文件不存在 / 解析失败时不抛异常：
        /// 返回的索引为空，原因放在 <see cref="LoadError"/>（调用方据此决定是否提示）。
        /// </summary>
        public static PartsInfoIndex Load(string path)
        {
            Dictionary<string, List<PartMarkPoint>> points =
                new Dictionary<string, List<PartMarkPoint>>(StringComparer.Ordinal);
            Dictionary<string, List<PartMarkPoint>> calloutPoints =
                new Dictionary<string, List<PartMarkPoint>>(StringComparer.Ordinal);
            Dictionary<string, string> calloutDisplays =
                new Dictionary<string, string>(StringComparer.Ordinal);
            Dictionary<int, HashSet<string>> partTexts = new Dictionary<int, HashSet<string>>();

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return new PartsInfoIndex(points, calloutPoints, calloutDisplays, partTexts,
                    "找不到 " + FileName);
            }

            string json;

            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                return new PartsInfoIndex(points, calloutPoints, calloutDisplays, partTexts,
                    "读取失败（" + ex.GetType().Name + "：" + ex.Message + "）");
            }

            object root;

            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = int.MaxValue;
                serializer.RecursionLimit = 200;
                root = serializer.DeserializeObject(json);
            }
            catch (Exception ex)
            {
                return new PartsInfoIndex(points, calloutPoints, calloutDisplays, partTexts,
                    "解析失败（" + ex.GetType().Name + "：" + ex.Message + "）");
            }

            object[] parts = root as object[];

            if (parts == null)
            {
                return new PartsInfoIndex(points, calloutPoints, calloutDisplays, partTexts,
                    "格式不是零件数组");
            }

            // 数组顺序（老文件没有 index 字段时当零件图序号用）
            int partOrder = 0;

            foreach (object part in parts)
            {
                partOrder++;

                Dictionary<string, object> partNode = part as Dictionary<string, object>;

                if (partNode == null)
                {
                    continue;
                }

                // 零件图序号：优先取 json 的 index（同一页可以有多张零件图），缺失时按数组顺序
                int partIndex = ReadInt(partNode, "index");

                if (partIndex <= 0)
                {
                    partIndex = partOrder;
                }

                // 这张零件图里出现过的全部文字：「A ⊆ B / B ⊆ A」的包含关系判定要用
                HashSet<string> textSet;

                if (!partTexts.TryGetValue(partIndex, out textSet))
                {
                    textSet = new HashSet<string>(StringComparer.Ordinal);
                    partTexts.Add(partIndex, textSet);
                }

                object[] texts = ReadArray(partNode, "texts");

                if (texts != null)
                {
                    foreach (object text in texts)
                    {
                        Dictionary<string, object> textNode = text as Dictionary<string, object>;

                        if (textNode == null)
                        {
                            continue;
                        }

                        string key = NormalizeMark(ReadString(textNode, "text"));

                        if (key.Length == 0)
                        {
                            continue;
                        }

                        textSet.Add(key);

                        PartMarkPoint point = ReadPoint(textNode);

                        if (point == null)
                        {
                            continue;
                        }

                        List<PartMarkPoint> list;

                        if (!points.TryGetValue(key, out list))
                        {
                            list = new List<PartMarkPoint>();
                            points.Add(key, list);
                        }

                        // 点带上零件图 index：决定「几处」时按 index 去重
                        list.Add(new PartMarkPoint(partIndex, point.X, point.Y));
                    }
                }

                // 圈号（callout）：装配图部品表的「番号」就是圈在零件图上的编号。
                // 只有 text / display / no 三者自洽（如 text=4、display=④4、no=4）的数据才算有效。
                object[] callouts = ReadArray(partNode, "callouts");

                if (callouts == null)
                {
                    continue;
                }

                foreach (object callout in callouts)
                {
                    Dictionary<string, object> calloutNode = callout as Dictionary<string, object>;

                    if (calloutNode == null)
                    {
                        continue;
                    }

                    string numberText = OnlyAsciiDigits(NormalizeMark(ReadString(calloutNode, "text")));

                    if (numberText.Length == 0)
                    {
                        continue;
                    }

                    // no 与 text 不一致说明识别有误，丢弃
                    int calloutNo = ReadInt(calloutNode, "no");

                    if (calloutNo > 0 &&
                        !string.Equals(calloutNo.ToString(CultureInfo.InvariantCulture), numberText,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string display = ReadString(calloutNode, "display").Trim();

                    // display 的圈字符与数字都必须与 text 一致（④4 → 圈 4 + 数字 4）
                    if (!IsDisplayOfNumber(display, numberText))
                    {
                        continue;
                    }

                    PartMarkPoint point = ReadCalloutPoint(calloutNode, partIndex);

                    if (point == null)
                    {
                        continue;
                    }

                    List<PartMarkPoint> list;

                    if (!calloutPoints.TryGetValue(numberText, out list))
                    {
                        list = new List<PartMarkPoint>();
                        calloutPoints.Add(numberText, list);
                    }

                    list.Add(point);

                    if (!calloutDisplays.ContainsKey(numberText))
                    {
                        calloutDisplays.Add(numberText, display);
                    }
                }
            }

            return new PartsInfoIndex(points, calloutPoints, calloutDisplays, partTexts, null);
        }

        /// <summary>
        /// 判定位置标记 <paramref name="markText"/>（如 <c>A部</c>）在图纸上的排列：
        /// 出现在 <b>2 个不同零件图</b>里 → 左右排列给「右 / 左」、上下排列给「上 / 下」；
        /// 3 个 → 再补「中」。
        /// 找不到、只有 1 个零件图、超过 3 个零件图，或这些零件图的文字之间
        /// 不构成 <c>A ⊆ B</c> / <c>B ⊆ A</c> 的包含关系时，前缀为空（不分割，调用方保持原部品名）。
        /// </summary>
        public PartMarkSplitPlan ResolveSplit(string markText)
        {
            string mark = (markText ?? string.Empty).Trim();

            if (mark.Length == 0)
            {
                return null;
            }

            List<PartMarkPoint> raw;

            if (!_points.TryGetValue(NormalizeMark(mark), out raw) || raw == null || raw.Count == 0)
            {
                return null;
            }

            return BuildSplitPlan(mark, raw);
        }

        /// <summary>
        /// 判定圈号（装配图部品表的「番号」，图纸上写作 <c>④4</c>）在 <c>parts_info.json</c> 的
        /// <c>callouts</c> 里的排列，规则与 <see cref="ResolveSplit"/> 完全相同：
        /// 必须出现在 <b>index 不同</b>的零件图里，且这些零件图的<b>全部 texts</b> 整体构成
        /// <c>A ⊆ B</c> / <c>B ⊆ A</c> 的包含关系，才按排列方向给「右 / 左」「上 / 下」。
        /// 找不到该圈号时返回 null。
        /// </summary>
        /// <param name="number">番号（圈内数字）。</param>
        public PartMarkSplitPlan ResolveCalloutSplit(int number)
        {
            return ResolveCalloutSplit(number.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>同 <see cref="ResolveCalloutSplit(int)"/>，参数是圈号文字（如 <c>4</c>）。</summary>
        public PartMarkSplitPlan ResolveCalloutSplit(string numberText)
        {
            string key = OnlyAsciiDigits(NormalizeMark(numberText));

            if (key.Length == 0)
            {
                return null;
            }

            List<PartMarkPoint> raw;

            if (!_calloutPoints.TryGetValue(key, out raw) || raw == null || raw.Count == 0)
            {
                return null;
            }

            string display;

            if (!_calloutDisplays.TryGetValue(key, out display) || display.Length == 0)
            {
                display = "圈号 " + key;
            }

            return BuildSplitPlan(display, raw);
        }

        /// <summary>
        /// 按出现点生成分割方案（位置标记与圈号共用同一套规则）：
        /// ① 同一张零件图上出现多次只算一处；② 这些零件图的全部文字必须整体构成
        /// <c>A ⊆ B</c> / <c>B ⊆ A</c>；③ 按坐标定排列方向后给「右 / 左」「上 / 下」。
        /// </summary>
        private PartMarkSplitPlan BuildSplitPlan(string markText, IList<PartMarkPoint> raw)
        {
            string mark = markText ?? string.Empty;

            // ① 同一张零件图上出现多次只算一处：多条数据必须来自 index 不同的零件图
            List<PartMarkPoint> points = MergeDistinctParts(raw);

            if (points.Count < 2)
            {
                return new PartMarkSplitPlan(mark, PartMarkLayout.None, new List<string>(), points.Count);
            }

            // ② 这些零件图的全部文字必须整体构成 A ⊆ B / B ⊆ A 的包含关系（同一部品的不同位置视图），
            //    互不包含（各自都有对方没有的文字）说明是不同零件，不分割
            if (!AreTextsNested(points))
            {
                return new PartMarkSplitPlan(mark, PartMarkLayout.None, new List<string>(), points.Count,
                    "零件图（index " + JoinPartIndexes(points) + "）的文字互不包含");
            }

            double minX = points[0].X;
            double maxX = points[0].X;
            double minY = points[0].Y;
            double maxY = points[0].Y;

            foreach (PartMarkPoint point in points)
            {
                if (point.X < minX)
                {
                    minX = point.X;
                }

                if (point.X > maxX)
                {
                    maxX = point.X;
                }

                if (point.Y < minY)
                {
                    minY = point.Y;
                }

                if (point.Y > maxY)
                {
                    maxY = point.Y;
                }
            }

            // X 方向铺得比 Y 方向开 → 左右排列；否则上下排列（图纸上「左边一个、右边一个」）
            bool horizontal = (maxX - minX) >= (maxY - minY);

            if (points.Count > MaxSplitCount)
            {
                // 超过 3 个零件图无法用「右 / 中 / 左」表达：不分割，只把处数报告给调用方
                return new PartMarkSplitPlan(mark,
                    horizontal ? PartMarkLayout.Horizontal : PartMarkLayout.Vertical,
                    new List<string>(), points.Count,
                    "超过 " + MaxSplitCount + " 个零件图（index " + JoinPartIndexes(points) + "）");
            }

            List<PartMarkPoint> ordered = new List<PartMarkPoint>(points);

            if (horizontal)
            {
                // X 从大到小：右 → 左
                ordered.Sort(delegate(PartMarkPoint left, PartMarkPoint right)
                {
                    int result = right.X.CompareTo(left.X);
                    return result != 0 ? result : left.Y.CompareTo(right.Y);
                });
            }
            else
            {
                // Y 从小到大：上 → 下
                ordered.Sort(delegate(PartMarkPoint left, PartMarkPoint right)
                {
                    int result = left.Y.CompareTo(right.Y);
                    return result != 0 ? result : left.X.CompareTo(right.X);
                });
            }

            string[] horizontalNames = ordered.Count == 2
                ? new string[] { "右", "左" }
                : new string[] { "右", "中", "左" };
            string[] verticalNames = ordered.Count == 2
                ? new string[] { "上", "下" }
                : new string[] { "上", "中", "下" };

            return new PartMarkSplitPlan(mark,
                horizontal ? PartMarkLayout.Horizontal : PartMarkLayout.Vertical,
                new List<string>(horizontal ? horizontalNames : verticalNames),
                ordered.Count);
        }

        /// <summary>
        /// 按零件图 index 分组，每个 index 只保留一个点：<b>多条数据必须对应不同的 index</b>。
        /// 同一张零件图上出现多次（例如 Part_08 里的两处 <c>H部</c>）用坐标容差合并后只取第一处，
        /// 顺序按 index 首次出现的顺序，保证结果稳定。
        /// </summary>
        private static List<PartMarkPoint> MergeDistinctParts(IList<PartMarkPoint> points)
        {
            List<int> partOrder = new List<int>();
            Dictionary<int, List<PartMarkPoint>> byPart = new Dictionary<int, List<PartMarkPoint>>();

            foreach (PartMarkPoint point in points)
            {
                List<PartMarkPoint> list;

                if (!byPart.TryGetValue(point.PartIndex, out list))
                {
                    list = new List<PartMarkPoint>();
                    byPart.Add(point.PartIndex, list);
                    partOrder.Add(point.PartIndex);
                }

                list.Add(point);
            }

            List<PartMarkPoint> merged = new List<PartMarkPoint>(partOrder.Count);

            foreach (int partIndex in partOrder)
            {
                List<PartMarkPoint> samePart = MergeSamePoints(byPart[partIndex]);

                if (samePart.Count == 0)
                {
                    continue;
                }

                merged.Add(new PartMarkPoint(partIndex, samePart[0].X, samePart[0].Y));
            }

            return merged;
        }

        /// <summary>
        /// 判定这些零件图里出现过的<b>全部文字</b>是否整体构成包含关系：
        /// 按文字个数从少到多排序后，相邻两个必须满足「少的 ⊆ 多的」，
        /// 等价于任意两张图都有 <c>A ⊆ B</c> 或 <c>B ⊆ A</c> 的关系（同一部品的不同位置视图）。
        /// 互不包含（各自都有对方没有的文字）时返回 false —— 不分割。
        /// </summary>
        private bool AreTextsNested(IList<PartMarkPoint> points)
        {
            List<HashSet<string>> sets = new List<HashSet<string>>(points.Count);

            foreach (PartMarkPoint point in points)
            {
                HashSet<string> set;

                if (!_partTexts.TryGetValue(point.PartIndex, out set) || set == null || set.Count == 0)
                {
                    // 该零件图没读到文字，无法确认包含关系 → 不分割
                    return false;
                }

                sets.Add(set);
            }

            sets.Sort(delegate(HashSet<string> left, HashSet<string> right)
            {
                return left.Count.CompareTo(right.Count);
            });

            for (int index = 1; index < sets.Count; index++)
            {
                if (!IsSubset(sets[index - 1], sets[index]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary><paramref name="subset"/> 里的每一条文字都出现在 <paramref name="superset"/> 里。</summary>
        private static bool IsSubset(HashSet<string> subset, HashSet<string> superset)
        {
            foreach (string item in subset)
            {
                if (!superset.Contains(item))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>把零件图 index 连成 <c>2 / 3</c>（日志用）。</summary>
        private static string JoinPartIndexes(IList<PartMarkPoint> points)
        {
            List<string> values = new List<string>(points.Count);

            foreach (PartMarkPoint point in points)
            {
                values.Add(point.PartIndex.ToString(CultureInfo.InvariantCulture));
            }

            return string.Join(" / ", values.ToArray());
        }

        /// <summary>
        /// 把间距小于 <see cref="SamePointToleranceMm"/> 的点合并成一个：
        /// 同一处标记可能被 PDF 文字层重复提取（尤其在零件图重叠区域）。
        /// </summary>
        private static List<PartMarkPoint> MergeSamePoints(IList<PartMarkPoint> points)
        {
            List<PartMarkPoint> merged = new List<PartMarkPoint>();

            foreach (PartMarkPoint point in points)
            {
                bool found = false;

                for (int index = 0; index < merged.Count; index++)
                {
                    if (Math.Abs(merged[index].X - point.X) <= SamePointToleranceMm &&
                        Math.Abs(merged[index].Y - point.Y) <= SamePointToleranceMm)
                    {
                        merged[index] = new PartMarkPoint(merged[index].PartIndex,
                            (merged[index].X + point.X) / 2.0,
                            (merged[index].Y + point.Y) / 2.0);

                        found = true;

                        break;
                    }
                }

                if (!found)
                {
                    merged.Add(point);
                }
            }

            return merged;
        }

        /// <summary>取一条文字的坐标（优先 <c>pdf_mm</c>，老文件退回 <c>pixel</c>）；取不到返回 null。</summary>
        private static PartMarkPoint ReadPoint(Dictionary<string, object> textNode)
        {
            Dictionary<string, object> rect = ReadObject(textNode, "pdf_mm");

            if (rect == null)
            {
                rect = ReadObject(textNode, "pixel");
            }

            if (rect == null)
            {
                return null;
            }

            double x = ReadDouble(rect, "x");
            double y = ReadDouble(rect, "y");

            if (double.IsNaN(x) || double.IsNaN(y))
            {
                return null;
            }

            double width = ReadDouble(rect, "w");
            double height = ReadDouble(rect, "h");

            // 用文字框中心比较：同一标记两次提取的框可能略有出入
            return new PartMarkPoint(
                x + (double.IsNaN(width) ? 0.0 : width / 2.0),
                y + (double.IsNaN(height) ? 0.0 : height / 2.0));
        }

        /// <summary>取字符串值（缺失返回空字符串）。</summary>
        private static string ReadString(Dictionary<string, object> node, string name)
        {
            object value;

            if (node == null || !node.TryGetValue(name, out value) || value == null)
            {
                return string.Empty;
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        /// <summary>取对象值（缺失 / 类型不符返回 null）。</summary>
        private static Dictionary<string, object> ReadObject(Dictionary<string, object> node, string name)
        {
            object value;

            if (node == null || !node.TryGetValue(name, out value))
            {
                return null;
            }

            return value as Dictionary<string, object>;
        }

        /// <summary>取数组值（缺失 / 类型不符返回 null）。</summary>
        private static object[] ReadArray(Dictionary<string, object> node, string name)
        {
            object value;

            if (node == null || !node.TryGetValue(name, out value))
            {
                return null;
            }

            return value as object[];
        }

        /// <summary>取数值（缺失 / 转换失败返回 <see cref="double.NaN"/>）。</summary>
        private static double ReadDouble(Dictionary<string, object> node, string name)
        {
            object value;

            if (node == null || !node.TryGetValue(name, out value) || value == null)
            {
                return double.NaN;
            }

            try
            {
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return double.NaN;
            }
        }

        /// <summary>取整数（缺失 / 转换失败返回 0）。</summary>
        private static int ReadInt(Dictionary<string, object> node, string name)
        {
            object value;

            if (node == null || !node.TryGetValue(name, out value) || value == null)
            {
                return 0;
            }

            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>
        /// 取一个圈号（<c>callouts[]</c>）的坐标：优先 <c>pdf_mm</c> 的 <c>cx / cy</c>（圆心），
        /// 老文件退回 <c>pixel</c> 的圆心，最后退回带 <c>x / y / w / h</c> 的矩形中心；取不到返回 null。
        /// </summary>
        private static PartMarkPoint ReadCalloutPoint(Dictionary<string, object> calloutNode, int partIndex)
        {
            PartMarkPoint center = ReadCenter(ReadObject(calloutNode, "pdf_mm")) ??
                ReadCenter(ReadObject(calloutNode, "pixel"));

            if (center != null)
            {
                return new PartMarkPoint(partIndex, center.X, center.Y);
            }

            PartMarkPoint rect = ReadPoint(calloutNode);

            if (rect == null)
            {
                return null;
            }

            return new PartMarkPoint(partIndex, rect.X, rect.Y);
        }

        /// <summary>取 <c>cx / cy</c> 圆心坐标（缺失返回 null）。</summary>
        private static PartMarkPoint ReadCenter(Dictionary<string, object> rect)
        {
            if (rect == null)
            {
                return null;
            }

            double cx = ReadDouble(rect, "cx");
            double cy = ReadDouble(rect, "cy");

            if (double.IsNaN(cx) || double.IsNaN(cy))
            {
                return null;
            }

            return new PartMarkPoint(cx, cy);
        }

        /// <summary>只保留 ASCII 数字（全角数字已由 <see cref="NormalizeMark"/> 转成半角）。</summary>
        private static string OnlyAsciiDigits(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(value.Length);

            foreach (char ch in value)
            {
                if (ch >= '0' && ch <= '9')
                {
                    builder.Append(ch);
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// <paramref name="display"/>（如 <c>④4</c>）是否就是圈号 <paramref name="numberText"/> 的显示：
        /// 其中的 ASCII 数字必须等于圈号；有圈字符（①…⑳ / ⓪）时圈字符也必须等于圈号。
        /// 两者对不上说明圈号识别有误，这条 <c>callouts</c> 数据不采用。
        /// </summary>
        private static bool IsDisplayOfNumber(string display, string numberText)
        {
            if (string.IsNullOrEmpty(display) || string.IsNullOrEmpty(numberText))
            {
                return false;
            }

            string digits = OnlyAsciiDigits(NormalizeMark(display));

            if (!string.Equals(digits, numberText, StringComparison.Ordinal))
            {
                return false;
            }

            string circled = GetCircledDigits(display);

            return circled.Length == 0 || string.Equals(circled, numberText, StringComparison.Ordinal);
        }

        /// <summary>把圈字符（①…⑳ / ⓪）转成 ASCII 数字串；没有圈字符时返回空串。</summary>
        private static string GetCircledDigits(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(value.Length);

            foreach (char ch in value)
            {
                if (ch == '\u24EA')
                {
                    builder.Append('0');
                }
                else if (ch >= '\u2460' && ch <= '\u2468')
                {
                    builder.Append((char)('1' + (ch - '\u2460')));
                }
                else if (ch >= '\u2469' && ch <= '\u2473')
                {
                    builder.Append((10 + (ch - '\u2469')).ToString(CultureInfo.InvariantCulture));
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// 标记文字归一化：全角 ASCII 转半角、去掉空白、统一大写，
        /// 于是 <c>A部</c> / <c>ａ部</c> / <c> A部 </c> 都能对上（与品種 / 図番的匹配规则一致）。
        /// </summary>
        internal static string NormalizeMark(string value)
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
    }
}
