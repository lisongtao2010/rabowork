using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UglyToad.PdfPig;

namespace CadPartSplitter
{
    /// <summary>
    /// 从 PDF 文字层提取「每张零件图范围内的文字 + 坐标」。
    ///
    /// 换算原理：
    ///   PdfPig 给出的词框是 PDF 用户坐标（pt，原点左下、Y 向上）；
    ///   本工具的处理图像是同一页按 DPI 渲染的结果（原点左上、Y 向下）。
    ///   用「渲染像素尺寸 / 页面 pt 尺寸」得到两个方向的缩放比，即可把词框映射到页面像素坐标：
    ///
    ///       scaleX = 图像宽px / 页面宽pt      scaleY = 图像高px / 页面高pt
    ///       x_px   = Left  × scaleX
    ///       y_px   = (页面高pt − Top) × scaleY        ← Y 轴翻转
    ///       w_px   = (Right − Left) × scaleX
    ///       h_px   = (Top − Bottom) × scaleY          ← 注意 Top > Bottom
    ///
    /// 归属判定：词框与零件图框的交集面积 / 词框面积 ≥ <see cref="MinOverlapRatio"/> 即算「在该图上」，
    /// 这样跨在裁切边缘上的文字不会因为中心点刚好在框外而丢失。
    ///
    /// 位图输入（扫描件 / 截图）没有文字层，本类不参与（不做 OCR）。
    /// </summary>
    public static class PartTextExtractor
    {
        /// <summary>词框落入零件图框的面积占比阈值。</summary>
        private const double MinOverlapRatio = 0.50;

        /// <summary>相邻行单元格数相差超过该值时视为「另一张表」（同一张表内个别空白单元格不会触发）。</summary>
        private const int CellCountJumpTol = 2;

        /// <summary>
        /// 读取 PDF 文字层，一次完成两件事：
        ///   ① 为每个 <see cref="PartInfo"/> 填充 <see cref="PartInfo.Texts"/>（框内词级文字 + 坐标）；
        ///   ② 把「零件框以外」的文字分类返回：表格 / 标题栏区（词的横向中心 X ≥ <paramref name="tableStartX"/>）
        ///      与其它未归属文字，并把表格区文字整理成「表 → 行 → 单元格」结构。
        /// 任何异常都只写日志、不中断分割流程。
        /// </summary>
        /// <param name="tableStartX">表格区起点（零件检测时实际采用的右侧遮蔽线 X）；≤ 0 表示未遮蔽，框外文字全部归入「其它」。</param>
        public static PageTextResult Attach(
            string pdfPath, int pageIndex, int imgWidth, int imgHeight,
            double dpi, int tableStartX, List<PartInfo> parts, Action<string> log)
        {
            Action<string> write = log ?? (_ => { });

            var result = new PageTextResult
            {
                TableStartX = tableStartX,
                ImageWidth  = imgWidth,
                ImageHeight = imgHeight
            };

            if (parts == null || parts.Count == 0)
                return result;

            try
            {
                using (var doc = PdfDocument.Open(pdfPath))
                {
                    var page = doc.GetPage(pageIndex + 1);   // PdfPig 页码从 1 开始

                    double pageW = page.Width;
                    double pageH = page.Height;
                    if (pageW <= 0 || pageH <= 0 || imgWidth <= 0 || imgHeight <= 0)
                    {
                        write("[WARN] 页面尺寸或渲染尺寸无效，跳过文字坐标提取。");
                        return result;
                    }

                    double scaleX = imgWidth  / pageW;
                    double scaleY = imgHeight / pageH;

                    int outside;
                    var words = ReadPageWords(page, pageH, scaleX, scaleY, imgWidth, imgHeight, out outside);

                    write($"[INFO] PDF 文字层：{words.Count} 个词（页面 {pageW:0.#}×{pageH:0.#} pt，"
                          + $"缩放 {scaleX:0.####}×{scaleY:0.####}）");

                    if (words.Count == 0)
                    {
                        write("[INFO] 该页没有文字层（扫描件或文字已轮廓化），跳过文字坐标提取。");
                        return result;
                    }

                    if (outside > words.Count / 2)
                    {
                        write($"[WARN] {outside}/{words.Count} 个词换算后落在画面外，"
                              + "可能是页面旋转或裁剪框偏移，文字坐标仅供参考。");
                    }

                    double mmPerPx = 25.4 / dpi;
                    int    total   = 0;

                    foreach (var part in parts)
                    {
                        part.Texts = CollectForPart(words, part, mmPerPx);
                        total     += part.Texts.Count;

                        if (part.Texts.Count > 0)
                            write($"[TEXT] {part.FileName}  {part.Texts.Count} 个词");
                    }

                    result.HasTextLayer = true;
                    result.WordCount    = words.Count;

                    write($"[INFO] 文字坐标：共 {total} 个词已分配到 {parts.Count} 张零件图。");

                    // ── 零件框以外的文字：表格 / 标题栏区 与 其它未归属 ──────────
                    var tableWords = new List<PartText>();
                    var otherWords = new List<PartText>();

                    foreach (var w in words)
                    {
                        if (IsInsideAnyPart(w, parts))
                            continue;

                        bool inTable = tableStartX > 0 && (w.X + w.W / 2.0) >= tableStartX;
                        PartText t = ToPartText(w, mmPerPx, 0, 0);   // 框外文字无局部坐标，Local = 页面坐标

                        if (inTable) tableWords.Add(t);
                        else         otherWords.Add(t);
                    }

                    tableWords.Sort(ByYThenX);
                    otherWords.Sort(ByYThenX);

                    result.TableWords = tableWords;
                    result.OtherWords = otherWords;
                    result.Tables     = BuildTables(tableWords, tableStartX, imgWidth, mmPerPx);

                    write($"[INFO] 零件框外文字：{result.OutsideWordCount} 个词"
                          + $"（表格区 {tableWords.Count} / 其它 {otherWords.Count}），"
                          + $"表格 {result.Tables.Count} 张 / "
                          + $"行 {result.Tables.Sum(tb => tb.Rows.Count)} / "
                          + $"单元格 {result.Tables.Sum(tb => tb.CellCount)}");

                    foreach (var tb in result.Tables)
                        write($"[TABLE] {tb.Name}  行 {tb.Rows.Count} / 单元格 {tb.CellCount}  "
                              + $"X {tb.PixelX}~{tb.PixelX + tb.PixelW}  Y {tb.PixelY}~{tb.PixelY + tb.PixelH}");
                }
            }
            catch (Exception ex)
            {
                write($"[WARN] 文字坐标提取失败（不影响分割结果）：{ex.Message}");
                foreach (var p in parts)
                    if (p.Texts == null) p.Texts = new List<PartText>();
            }

            return result;
        }

        /// <summary>
        /// 读取一页的全部词并换算成页面像素坐标（原点左上、Y 向下）。
        /// 空白词、退化框（宽或高 ≤ 0）会被丢弃；<paramref name="outside"/> 返回换算后落在画面外的词数。
        /// </summary>
        private static List<WordBox> ReadPageWords(
            UglyToad.PdfPig.Content.Page page, double pageH,
            double scaleX, double scaleY, int imgWidth, int imgHeight, out int outside)
        {
            var words = new List<WordBox>();
            outside = 0;

            foreach (var w in page.GetWords())
            {
                string text = (w.Text ?? string.Empty).Trim();
                if (text.Length == 0)
                    continue;

                var bb = w.BoundingBox;
                double x  = bb.Left * scaleX;
                double y  = (pageH - bb.Top) * scaleY;      // PDF 的 Top 是上边缘（Y 向上）
                double cw = (bb.Right - bb.Left) * scaleX;
                double ch = (bb.Top - bb.Bottom) * scaleY;
                if (cw <= 0 || ch <= 0)
                    continue;

                if (x + cw < 0 || y + ch < 0 || x > imgWidth || y > imgHeight)
                    outside++;

                words.Add(new WordBox { Text = text, X = x, Y = y, W = cw, H = ch });
            }

            return words;
        }

        /// <summary>
        /// 把词级文字按「同一行」合并成可读文本行（`--merge-text-lines` 时的清单粒度）。
        /// 判行为「纵向中心差 ≤ 0.6 倍字高」；同一行内按 X 排序，
        /// 词间水平间隙大于半个字高时补一个空格，否则直接拼接（适应中/日文紧排）。
        /// 词若已被圈号标注标记（<see cref="PartText.CalloutDisplay"/> 非空），
        /// 在它原来的位置上输出「①1」这样的带圈形式，而不是裸数字。
        /// </summary>
        public static List<PartTextLine> BuildLines(List<PartText> texts)
        {
            var result = new List<PartTextLine>();
            if (texts == null || texts.Count == 0)
                return result;

            var groups = GroupIntoLines(texts);

            foreach (var g in groups)
            {
                var ordered = g.OrderBy(t => t.PixelX).ToList();
                var sb = new StringBuilder();
                int prevRight = int.MinValue;

                foreach (var t in ordered)
                {
                    if (sb.Length > 0 && t.PixelX - prevRight > Math.Max(2.0, t.PixelH * 0.5))
                        sb.Append(' ');
                    // 圈号标注的圈内数字：在它原来的位置输出 ①1 这类带圈形式
                    //（CalloutDisplay 由 CalloutExtractor.MarkCalloutWords 按坐标回填，未标记的词为 null）
                    sb.Append(string.IsNullOrEmpty(t.CalloutDisplay) ? t.Text : t.CalloutDisplay);
                    prevRight = t.PixelX + t.PixelW;
                }

                int x = ordered.Min(t => t.PixelX);
                int y = ordered.Min(t => t.PixelY);
                int r = ordered.Max(t => t.PixelX + t.PixelW);
                int b = ordered.Max(t => t.PixelY + t.PixelH);

                // 由词级 LocalX 反推该零件图左上角：partX = PixelX − LocalX
                int partX = ordered[0].PixelX - ordered[0].LocalX;
                int partY = ordered[0].PixelY - ordered[0].LocalY;

                result.Add(new PartTextLine
                {
                    Text      = sb.ToString(),
                    PixelX    = x,
                    PixelY    = y,
                    PixelW    = r - x,
                    PixelH    = b - y,
                    LocalX    = x - partX,
                    LocalY    = y - partY,
                    WordCount = ordered.Count
                });
            }

            result.Sort((a, b) =>
            {
                int dy = a.PixelY.CompareTo(b.PixelY);
                return dy != 0 ? dy : a.PixelX.CompareTo(b.PixelX);
            });

            return result;
        }

        /// <summary>
        /// 把词级文字**逐个转成一行**（供 parts_text.txt 使用，默认粒度）。
        ///
        /// 与 <see cref="BuildLines"/> 的区别：不做「拼成一句」，一个 PDF 词就是一条记录，
        /// 坐标是该词自己的外接框（而不是整行的并集框），文本也只含这一个词。
        /// 这么做的原因：合并后的 `①1 鴨居 G部` 只有一个并集坐标，
        /// 下游按行解析时分不清 `鴨居` 和 `G部` 各自的框在哪。
        ///
        /// 分行规则与 <see cref="BuildLines"/> 完全相同（共用 <see cref="GroupIntoLines"/>），
        /// 所以行序仍是自上而下、行内仍是从左到右 —— 拆成多行后阅读顺序与原图一致，
        /// 相邻的几条就是原本同一行上的词。
        /// 圈号标注回填的 ①1 同样生效（<see cref="PartText.CalloutDisplay"/> 优先于
        /// <see cref="PartText.Text"/>）。
        /// </summary>
        public static List<PartTextLine> BuildWordLines(List<PartText> texts)
        {
            var result = new List<PartTextLine>();
            if (texts == null || texts.Count == 0)
                return result;

            foreach (var g in GroupIntoLines(texts))
            {
                foreach (var t in g.OrderBy(t => t.PixelX))
                {
                    result.Add(new PartTextLine
                    {
                        // 圈号标注的圈内数字：输出 ①1 这类带圈形式（未标记的词为 null，用原文本）
                        Text      = string.IsNullOrEmpty(t.CalloutDisplay) ? t.Text : t.CalloutDisplay,
                        PixelX    = t.PixelX,
                        PixelY    = t.PixelY,
                        PixelW    = t.PixelW,
                        PixelH    = t.PixelH,
                        LocalX    = t.LocalX,
                        LocalY    = t.LocalY,
                        WordCount = 1
                    });
                }
            }

            return result;
        }

        /// <summary>
        /// 把词按「同一行」分组：纵向中心差 ≤ 0.6 倍字高（相对该组首个词）即为同一行。
        /// 组的顺序 = 首次出现顺序（即自上而下）；组内词的顺序未定，调用方按 X 排序。
        /// <see cref="BuildLines"/> 与 <see cref="BuildWordLines"/> 共用这套分行规则。
        /// </summary>
        private static List<List<PartText>> GroupIntoLines(List<PartText> texts)
        {
            var groups = new List<List<PartText>>();
            if (texts == null || texts.Count == 0)
                return groups;

            foreach (var t in texts.OrderBy(t => t.PixelY).ThenBy(t => t.PixelX))
            {
                double cy  = t.PixelY + t.PixelH / 2.0;
                double tol = Math.Max(4.0, t.PixelH * 0.6);
                List<PartText> best = null;
                double bestDist = double.MaxValue;

                foreach (var g in groups)
                {
                    double gy = g[0].PixelY + g[0].PixelH / 2.0;
                    double d  = Math.Abs(gy - cy);
                    if (d <= tol && d < bestDist)
                    {
                        best     = g;
                        bestDist = d;
                    }
                }

                if (best == null) groups.Add(new List<PartText> { t });
                else              best.Add(t);
            }

            return groups;
        }

        /// <summary>取落在某个零件图框内（重叠率 ≥ 阈值）的词，按 Y、X 排序后转成 PartText。</summary>
        private static List<PartText> CollectForPart(List<WordBox> words, PartInfo part, double mmPerPx)
        {
            var list = new List<PartText>();

            foreach (var w in words)
            {
                if (OverlapRatio(w, part) < MinOverlapRatio)
                    continue;

                list.Add(ToPartText(w, mmPerPx, part.PixelX, part.PixelY));
            }

            list.Sort(ByYThenX);

            return list;
        }

        /// <summary>
        /// 词框 → <see cref="PartText"/>。局部坐标以 (<paramref name="originX"/>, <paramref name="originY"/>) 为原点，
        /// 传 (0, 0) 表示「页面坐标即局部坐标」（零件框以外的文字用）。
        /// </summary>
        private static PartText ToPartText(WordBox w, double mmPerPx, int originX, int originY)
        {
            int px = (int)Math.Round(w.X);
            int py = (int)Math.Round(w.Y);
            int pw = Math.Max(1, (int)Math.Round(w.W));
            int ph = Math.Max(1, (int)Math.Round(w.H));

            return new PartText
            {
                Text    = w.Text,
                PixelX  = px,
                PixelY  = py,
                PixelW  = pw,
                PixelH  = ph,
                LocalX  = px - originX,
                LocalY  = py - originY,
                PdfX_mm = px * mmPerPx,
                PdfY_mm = py * mmPerPx,
                PdfW_mm = pw * mmPerPx,
                PdfH_mm = ph * mmPerPx
            };
        }

        /// <summary>该词是否落在任意一张零件图框内（重叠率 ≥ 阈值）。</summary>
        private static bool IsInsideAnyPart(WordBox w, List<PartInfo> parts)
        {
            foreach (var p in parts)
                if (OverlapRatio(w, p) >= MinOverlapRatio)
                    return true;

            return false;
        }

        /// <summary>排序：先 Y 后 X（自上而下、自左而右）。</summary>
        private static int ByYThenX(PartText a, PartText b)
        {
            int dy = a.PixelY.CompareTo(b.PixelY);
            return dy != 0 ? dy : a.PixelX.CompareTo(b.PixelX);
        }

        /// <summary>
        /// 把表格区的词整理成「表 → 行 → 单元格」结构（供 outside_info.json / outside_text.txt 使用）。
        ///
        /// 规则（分行容差沿用 <see cref="BuildLines"/> 的启发式）：
        ///   · 分行：纵向中心差 ≤ 0.6 倍字高；
        ///   · 单元格：行内按 X 排序，与前一个词的水平间隙 &gt; max(24px, 1.0 倍字高) 即断开；
        ///   · 列号：每张表内按单元格「中心 X」聚类（容差 max(16px, 0.6 倍平均字高)）的序号，
        ///           见 <see cref="BuildColumns"/>（推断值，合并单元格会偏）；
        ///   · 分表：见 <see cref="BuildBlockList"/>（垂直间隙或单元格数突变）。
        /// </summary>
        private static List<TableBlock> BuildTables(
            List<PartText> tableWords, int tableStartX, int imgWidth, double mmPerPx)
        {
            var blocks = new List<TableBlock>();
            if (tableWords == null || tableWords.Count == 0)
                return blocks;

            // ①② 分行 + 行内按水平间隙拆单元格（与表格块检测共用同一套启发式）
            List<TableRow> rows = BuildRows(tableWords);

            // ③④⑤ 按行结构变化分表 → 每张表内单独推断列号 → 求外接框
            return BuildBlockList(rows, tableStartX, imgWidth, mmPerPx);
        }

        /// <summary>
        /// 把词整理成「行 → 单元格」（<see cref="BuildTables"/> 与 <see cref="DetectTableAreas"/> 共用）。
        ///
        ///   · 分行：纵向中心差 ≤ 0.6 倍字高（与 <see cref="BuildLines"/> 一致）；
        ///   · 单元格：行内按 X 排序，与前一个词的水平间隙 &gt; max(24px, 1.0 倍字高) 即断开。
        /// </summary>
        private static List<TableRow> BuildRows(List<PartText> words)
        {
            var rows = new List<TableRow>();
            if (words == null || words.Count == 0)
                return rows;

            // ① 分行
            var rowGroups = new List<List<PartText>>();

            foreach (var t in words)
            {
                double cy  = t.PixelY + t.PixelH / 2.0;
                double tol = Math.Max(4.0, t.PixelH * 0.6);
                List<PartText> best = null;
                double bestDist = double.MaxValue;

                foreach (var g in rowGroups)
                {
                    double gy = g[0].PixelY + g[0].PixelH / 2.0;
                    double d  = Math.Abs(gy - cy);
                    if (d <= tol && d < bestDist)
                    {
                        best     = g;
                        bestDist = d;
                    }
                }

                if (best == null) rowGroups.Add(new List<PartText> { t });
                else              best.Add(t);
            }

            // ② 行内按水平间隙拆单元格
            foreach (var g in rowGroups)
            {
                var ordered   = g.OrderBy(t => t.PixelX).ToList();
                var row       = new TableRow();
                int prevRight = int.MinValue;

                foreach (var t in ordered)
                {
                    double gapTol = Math.Max(24.0, t.PixelH * 1.0);

                    if (row.Cells.Count == 0 || t.PixelX - prevRight > gapTol)
                        row.Cells.Add(CellFromText(t));
                    else
                        AppendWord(row.Cells[row.Cells.Count - 1], t, prevRight);

                    prevRight = t.PixelX + t.PixelW;
                }

                row.PixelX = ordered.Min(t => t.PixelX);
                row.PixelY = ordered.Min(t => t.PixelY);
                row.PixelW = ordered.Max(t => t.PixelX + t.PixelW) - row.PixelX;
                row.PixelH = ordered.Max(t => t.PixelY + t.PixelH) - row.PixelY;
                rows.Add(row);
            }

            return rows.OrderBy(r => r.PixelY).ToList();
        }

        // ── 表格块检测（供零件检测阶段做表格遮蔽）────────────────────────────

        /// <summary>表格块至少需要的行数（且其中 ≥ 2 行的单元格数 ≥ 3）。</summary>
        private const int TableAreaMinRows = 3;

        /// <summary>每行单元格数的中位数下限（表格的特征是多列，尺寸标注是单行线性排列）。</summary>
        private const int TableAreaMinCells = 3;

        /// <summary>只有 2 行的表（例如 2 行 7 列的型番表）要求列数 ≥ 该值。</summary>
        private const int TableAreaMinColumnsForShort = 4;

        /// <summary>表格块至少需要的词数。</summary>
        private const int TableAreaMinWords = 8;

        /// <summary>表格块至少需要的字符数（大量合并单元格的表題欄词很少，用字符数兜底）。</summary>
        private const int TableAreaMinChars = 24;

        /// <summary>行间列对齐率下限（真表格每行列位置高度一致，散落的尺寸标注对不齐）。</summary>
        private const double TableAreaMinAlign = 0.60;

        /// <summary>行距规律性下限（相邻行中心距落在中位数 0.5~1.6 倍的比例）；表題欄类表格靠这一条。</summary>
        private const double TableAreaMinPitchRegularity = 0.80;

        /// <summary>
        /// 仅解析文字层，推断「表格 / 表題欄」矩形（页面像素坐标）；不依赖零件检测结果，也不需要遮蔽线。
        ///
        /// 用途：给 <see cref="PartDetector"/> 做「按块遮蔽」，取代「把右侧整列涂黑」。
        /// 整列遮蔽假设表格区与零件区在 X 方向可分；当表格在右上、零件视图在下方中部时，
        /// 密实列带会一路吃进零件区，把中部视图整块抹掉（现象：输出图只剩左侧一半）。
        ///
        /// 判据（见 <see cref="LooksLikeTable"/>）：
        ///   · 行数 ≥ 3 且每行单元格数中位数 ≥ 3（或 2 行 + 列数 ≥ 4）；
        ///   · 列数 ≥ 3、词数 ≥ 8、行间列对齐率 ≥ 0.60；
        ///   · 块宽 ≥ 图宽 4%、块高 ≥ 图高 1.2%、块面积 ≤ 全图 45%。
        /// 无文字层 / 解析失败 / 未检出时返回空列表（调用方自动回退整列遮蔽）。
        /// </summary>
        public static List<PageRect> DetectTableAreas(
            string pdfPath, int pageIndex, int imgWidth, int imgHeight, Action<string> log)
        {
            Action<string> write = log ?? (_ => { });
            var areas = new List<PageRect>();

            if (string.IsNullOrEmpty(pdfPath) || imgWidth <= 0 || imgHeight <= 0)
                return areas;

            try
            {
                using (var doc = PdfDocument.Open(pdfPath))
                {
                    if (pageIndex < 0 || pageIndex >= doc.NumberOfPages)
                        return areas;

                    var page = doc.GetPage(pageIndex + 1);   // PdfPig 页码从 1 开始
                    if (page.Width <= 0 || page.Height <= 0)
                        return areas;

                    double scaleX = imgWidth  / page.Width;
                    double scaleY = imgHeight / page.Height;

                    int outside;
                    var raw = ReadPageWords(page, page.Height, scaleX, scaleY, imgWidth, imgHeight, out outside);
                    if (raw.Count == 0)
                        return areas;

                    var words = new List<PartText>(raw.Count);
                    foreach (var w in raw)
                        words.Add(ToPartText(w, 1.0, 0, 0));   // 此处只用像素坐标，mm 不参与计算

                    var candidateRows = SplitRowsByWideGaps(BuildRows(words));

                    foreach (var block in GroupRowsIntoAreas(candidateRows))
                    {
                        int bx0 = block.Min(r => r.PixelX);
                        int by0 = block.Min(r => r.PixelY);
                        int bx1 = block.Max(r => r.PixelX + r.PixelW);
                        int by1 = block.Max(r => r.PixelY + r.PixelH);

                        int bw = bx1 - bx0;
                        int bh = by1 - by0;

                        bool ok = LooksLikeTable(block, imgWidth, imgHeight);
                        if (!ok)
                            continue;

                        write($"[TABLE-AREA] 表格块 行 {block.Count} / 列 {CountColumns(block)}"
                              + $" / 对齐 {RowAlignment(block):0.00} / 行距 {RowPitchRegularity(block):0.00}"
                              + $"  X {bx0}~{bx1}  Y {by0}~{by1}");

                        areas.Add(new PageRect(bx0, by0, bw, bh));
                    }

                    write($"[INFO] 表格块检测：{areas.Count} 处表格 / 表題欄"
                          + (areas.Count > 0 ? "（按块遮蔽，保留表格以外的零件视图）" : "（改用整列遮蔽）"));
                }
            }
            catch (Exception ex)
            {
                write($"[WARN] 表格块检测失败（退回整列遮蔽）：{ex.Message}");
                areas.Clear();
            }

            return areas;
        }

        /// <summary>
        /// 只解析文字层，返回整页「文字行」的外接框（页面像素坐标）；不做归属、不改 <see cref="PartInfo"/>。
        ///
        /// 行的划分与 <see cref="BuildLines"/> 完全一致（同一套 Y 容差分组），
        /// 所以这里的框就是 <c>parts_text.txt</c> 里那一行文字实际占的矩形。
        ///
        /// 用途：给 <see cref="PartDetector"/> 判「文字面积占比」用 —— 表題欄 / 変更履歴表这类
        /// 表格的每一行都横跨单元格、行行相邻，行框几乎铺满整块；真正的零件视图里文字只是
        /// 零散标注，行框占比很低。据此可在导出 PNG 之前把表格残片筛掉。
        ///
        /// 无文字层 / 解析失败时返回空列表（调用方跳过筛除，按原样输出）。
        /// </summary>
        public static List<PageRect> ReadTextLineBoxes(
            string pdfPath, int pageIndex, int imgWidth, int imgHeight, Action<string> log)
        {
            Action<string> write = log ?? (_ => { });
            var boxes = new List<PageRect>();

            if (string.IsNullOrEmpty(pdfPath) || imgWidth <= 0 || imgHeight <= 0)
                return boxes;

            try
            {
                using (var doc = PdfDocument.Open(pdfPath))
                {
                    if (pageIndex < 0 || pageIndex >= doc.NumberOfPages)
                        return boxes;

                    var page = doc.GetPage(pageIndex + 1);   // PdfPig 页码从 1 开始
                    if (page.Width <= 0 || page.Height <= 0)
                        return boxes;

                    int outside;
                    var raw = ReadPageWords(page, page.Height,
                                            imgWidth / page.Width, imgHeight / page.Height,
                                            imgWidth, imgHeight, out outside);

                    var texts = new List<PartText>(raw.Count);
                    foreach (var w in raw)
                        texts.Add(ToPartText(w, 1.0, 0, 0));   // mm 不参与计算，只取像素坐标

                    foreach (var line in BuildLines(texts))
                        boxes.Add(new PageRect(line.PixelX, line.PixelY, line.PixelW, line.PixelH));
                }
            }
            catch (Exception ex)
            {
                write($"[WARN] 文字行读取失败（跳过「文字占比」筛除）：{ex.Message}");
                boxes.Clear();
            }

            return boxes;
        }

        /// <summary>
        /// 把「一行」按超大水平间隙拆成多段（相邻单元格间隙 &gt; max(120px, 8 倍字高)）。
        ///
        /// 必要性：同一 Y 位置上可能同时存在「左侧零件图的尺寸标注」与「右侧部品表」两处文字。
        /// 行内按普通间隙（1 倍字高）拆格后它们会连成一整行，进而被当成一张跨页宽表 —— 遮蔽时
        /// 连左侧零件图一起涂黑。先按超大间隙拆段再分块，两处各成一块，互不影响。
        ///
        /// 阈值取 8 倍字高：普通表格的列间隙（1~6 倍字高）不会被拆开，
        /// 而不同区域之间通常隔着数百像素的空白。
        /// </summary>
        private static List<TableRow> SplitRowsByWideGaps(List<TableRow> rows)
        {
            var result = new List<TableRow>();
            if (rows == null || rows.Count == 0)
                return result;

            foreach (var row in rows)
            {
                TableRow current = null;
                int prevRight = int.MinValue;

                foreach (var cell in row.Cells)
                {
                    double gapTol = Math.Max(120.0, cell.PixelH * 8.0);

                    if (current == null || cell.PixelX - prevRight > gapTol)
                    {
                        current = new TableRow();
                        result.Add(current);
                    }

                    current.Cells.Add(cell);
                    prevRight = cell.PixelX + cell.PixelW;
                }
            }

            foreach (var r in result)
                RecalcRowBounds(r);

            return result.OrderBy(r => r.PixelY).ThenBy(r => r.PixelX).ToList();
        }

        /// <summary>按单元格重算一行的外接框。</summary>
        private static void RecalcRowBounds(TableRow row)
        {
            if (row == null || row.Cells.Count == 0)
                return;

            row.PixelX = row.Cells.Min(c => c.PixelX);
            row.PixelY = row.Cells.Min(c => c.PixelY);
            row.PixelW = row.Cells.Max(c => c.PixelX + c.PixelW) - row.PixelX;
            row.PixelH = row.Cells.Max(c => c.PixelY + c.PixelH) - row.PixelY;
        }

        /// <summary>
        /// 把「行」聚成候选块：与某个已有块的末行在纵向紧邻（间隙 ≤ max(60px, 1.5 倍行高)）、
        /// 单元格数相差 ≤ 3、且横向范围重叠 ≥ 50% 即并入该块（取纵向距离最近的一块）。
        /// 这里只是「候选」，是否真表格由 <see cref="LooksLikeTable"/> 判定。
        /// </summary>
        private static List<List<TableRow>> GroupRowsIntoAreas(List<TableRow> rows)
        {
            var groups = new List<List<TableRow>>();
            if (rows == null || rows.Count == 0)
                return groups;

            foreach (var r in rows)
            {
                List<TableRow> best    = null;
                double        bestDist = double.MaxValue;

                foreach (var g in groups)
                {
                    TableRow prev   = g[g.Count - 1];
                    double   gap    = r.PixelY - (prev.PixelY + prev.PixelH);
                    double   rowH   = Math.Max(8.0, Math.Max(prev.PixelH, r.PixelH));
                    double   gapTol = Math.Max(60.0, rowH * 1.5);

                    if (Math.Abs(gap) > gapTol) continue;
                    if (Math.Abs(r.Cells.Count - prev.Cells.Count) > 3) continue;
                    if (XRangeOverlap(prev, r) < 0.5) continue;

                    if (Math.Abs(gap) < bestDist)
                    {
                        bestDist = Math.Abs(gap);
                        best     = g;
                    }
                }

                if (best == null) groups.Add(new List<TableRow> { r });
                else              best.Add(r);
            }

            return groups;
        }

        /// <summary>两行横向范围的交集 / 较窄者宽度（0~1）。</summary>
        private static double XRangeOverlap(TableRow a, TableRow b)
        {
            int left   = Math.Max(a.PixelX, b.PixelX);
            int right  = Math.Min(a.PixelX + a.PixelW, b.PixelX + b.PixelW);
            int narrow = Math.Min(a.PixelW, b.PixelW);

            if (right <= left || narrow <= 0)
                return 0.0;

            return (right - left) / (double)narrow;
        }

        /// <summary>
        /// 判定一个候选块是否「表格 / 表題欄」。
        ///
        /// 关键是「多行 × 多列 + 列对齐」：图纸里的尺寸标注是沿引线排开的几个词，
        /// 即使恰好凑成 3 行，各行的横向位置也互不对齐；表格每行的列位置几乎完全重合。
        /// </summary>
        private static bool LooksLikeTable(List<TableRow> rows, int imgWidth, int imgHeight)
        {
            if (rows == null || rows.Count < 2)
                return false;

            int wordCount = 0;
            int charCount = 0;
            foreach (var r in rows)
            {
                wordCount += r.Cells.Count;
                foreach (var c in r.Cells)
                    charCount += (c.Text ?? string.Empty).Trim().Length;
            }

            if (wordCount < TableAreaMinWords && charCount < TableAreaMinChars)
                return false;

            var cellCounts  = rows.Select(r => r.Cells.Count).OrderBy(n => n).ToList();
            int medianCells = cellCounts[cellCounts.Count / 2];
            int multiRows   = rows.Count(r => r.Cells.Count >= 3);
            int columns     = CountColumns(rows);

            if (columns < TableAreaMinCells)
                return false;

            int x0 = rows.Min(r => r.PixelX);
            int y0 = rows.Min(r => r.PixelY);
            int x1 = rows.Max(r => r.PixelX + r.PixelW);
            int y1 = rows.Max(r => r.PixelY + r.PixelH);

            int w = x1 - x0;
            int h = y1 - y0;

            if (w <= 0 || h <= 0)
                return false;

            double align = RowAlignment(rows);
            double pitch = RowPitchRegularity(rows);

            bool byRows  = rows.Count >= TableAreaMinRows
                           && medianCells >= TableAreaMinCells
                           && multiRows >= 2;
            bool shortBy = rows.Count == 2 && columns >= TableAreaMinColumnsForShort;

            // ③ 密集文本块：大量合并单元格的表格 / 标题栏（每行只有 1~2 格，列对不齐），
            //    特征改为「行高等距 + 每行横向铺满 + 文字较多」。
            bool denseBy = rows.Count >= TableAreaMinRows
                           && pitch >= TableAreaMinPitchRegularity
                           && charCount >= TableAreaMinChars
                           && MedianRowWidth(rows) >= w * 0.5;

            if (!byRows && !shortBy && !denseBy)
                return false;

            // 表格的结构证据（满足其一）：行间列对齐 / 行距规律
            if (align < TableAreaMinAlign && pitch < TableAreaMinPitchRegularity)
                return false;

            if (w < imgWidth  * 0.04) return false;
            if (h < imgHeight * 0.012) return false;
            if ((double)w * h > imgWidth * (double)imgHeight * 0.45) return false;

            return true;
        }

        /// <summary>各行宽度的中位数（用于判断「每行都横向铺满整块」的表題欄）。</summary>
        private static double MedianRowWidth(List<TableRow> rows)
        {
            var widths = rows.Select(r => (double)r.PixelW).OrderBy(v => v).ToList();
            return widths[widths.Count / 2];
        }

        /// <summary>
        /// 用「中心 X」聚类估算列数（容差 max(16px, 0.6 倍平均字高)，与 <see cref="BuildColumns"/> 一致，
        /// 但不改写单元格，供表格块判定使用）。
        /// </summary>
        private static int CountColumns(List<TableRow> rows)
        {
            var cells = new List<TableCell>();
            foreach (var r in rows)
                cells.AddRange(r.Cells);

            if (cells.Count == 0)
                return 0;

            double avgH   = cells.Average(c => (double)c.PixelH);
            double colTol = Math.Max(16.0, avgH * 0.6);

            var centers = new List<double>();

            foreach (var c in cells.OrderBy(c => c.PixelX + c.PixelW / 2.0))
            {
                double center = c.PixelX + c.PixelW / 2.0;
                bool found = false;

                foreach (double s in centers)
                {
                    if (Math.Abs(s - center) <= colTol) { found = true; break; }
                }

                if (!found) centers.Add(center);
            }

            return centers.Count;
        }

        /// <summary>
        /// 行距规律性（0~1）：相邻行中心距落在「中位数 0.5~1.6 倍」范围内的比例。
        ///
        /// 表題欄这类大量合并单元格的表格，列位置对不齐（<see cref="RowAlignment"/> 很低），
        /// 但行高等距、行距规整；散落的尺寸标注行距忽大忽小。
        /// </summary>
        private static double RowPitchRegularity(List<TableRow> rows)
        {
            if (rows == null || rows.Count < 3)
                return 0.0;

            var pitches = new List<double>();
            for (int i = 1; i < rows.Count; i++)
            {
                double prevCenter = rows[i - 1].PixelY + rows[i - 1].PixelH / 2.0;
                double curCenter  = rows[i].PixelY     + rows[i].PixelH     / 2.0;
                double pitch      = Math.Abs(curCenter - prevCenter);
                if (pitch > 0) pitches.Add(pitch);
            }

            if (pitches.Count < 2)
                return 0.0;

            var sorted = pitches.OrderBy(p => p).ToList();
            double median = sorted[sorted.Count / 2];
            if (median <= 0) return 0.0;

            int ok = pitches.Count(p => p >= median * 0.5 && p <= median * 1.6);
            return ok / (double)pitches.Count;
        }

        /// <summary>
        /// 相邻行之间的列对齐率（0~1）：每行有多少比例的单元格中心能在上一行找到「对应列」。
        /// 真表格接近 1；散落的尺寸标注很低。
        /// </summary>
        private static double RowAlignment(List<TableRow> rows)
        {
            if (rows.Count < 2)
                return 0.0;

            double total = 0.0;
            int    pairs = 0;

            for (int i = 1; i < rows.Count; i++)
            {
                TableRow prev = rows[i - 1];
                TableRow cur  = rows[i];
                if (prev.Cells.Count == 0 || cur.Cells.Count == 0)
                    continue;

                double avgH = (prev.Cells.Average(c => (double)c.PixelH)
                               + cur.Cells.Average(c => (double)c.PixelH)) / 2.0;
                double tol   = Math.Max(20.0, avgH * 0.8);
                int    match = 0;

                foreach (var c in cur.Cells)
                {
                    double center = c.PixelX + c.PixelW / 2.0;

                    foreach (var p in prev.Cells)
                    {
                        if (Math.Abs(p.PixelX + p.PixelW / 2.0 - center) <= tol)
                        {
                            match++;
                            break;
                        }
                    }
                }

                total += match / (double)cur.Cells.Count;
                pairs++;
            }

            return pairs == 0 ? 0.0 : total / pairs;
        }

        /// <summary>
        /// 按单元格「中心 X」聚类推断列号（容差 max(16px, 0.6 倍平均字高)），并重算毫米坐标。
        ///
        /// 用中心而不是左边缘：表格文字多为居中对齐，长短不同的文字左边缘漂移很大
        /// （例如同一列里「番号」与「鬼目ナット（ブレーキ取付用）」的左边缘可差 200px 以上），
        /// 中心则几乎重合。列号是推断值：跨列合并的单元格会落到它中心所在的那一列。
        /// </summary>
        private static void BuildColumns(List<TableCell> cells, double mmPerPx)
        {
            if (cells == null || cells.Count == 0)
                return;

            double avgH   = cells.Average(c => (double)c.PixelH);
            double colTol = Math.Max(16.0, avgH * 0.6);

            var colCenters = new List<double>();

            foreach (var c in cells.OrderBy(c => c.PixelX + c.PixelW / 2.0))
            {
                double center = c.PixelX + c.PixelW / 2.0;
                bool found = false;

                foreach (double s in colCenters)
                {
                    if (Math.Abs(s - center) <= colTol) { found = true; break; }
                }

                if (!found) colCenters.Add(center);
            }

            foreach (var c in cells)
            {
                double center    = c.PixelX + c.PixelW / 2.0;
                int    bestIndex = 0;
                double bestDist  = double.MaxValue;

                for (int i = 0; i < colCenters.Count; i++)
                {
                    double d = Math.Abs(colCenters[i] - center);
                    if (d < bestDist) { bestDist = d; bestIndex = i; }
                }

                c.Column = bestIndex + 1;
                SyncCellMm(c, mmPerPx);
            }
        }

        /// <summary>
        /// 按「行结构变化」把行分组为多张表，并为每张表求并集外接框、在块内重排行号（从 1 开始）。
        ///
        /// 触发新表的条件（满足任一）：
        ///   · 相邻行的垂直间隙 &gt; max(80px, 3 倍平均行高)；
        ///   · 相邻行的单元格数突变超过 <see cref="CellCountJumpTol"/> 个
        ///     ── 图纸里上下紧邻、但列数完全不同的表（例如 7 列部品表紧接 15 列型枠表）靠这一条切开。
        /// </summary>
        private static List<TableBlock> BuildBlockList(List<TableRow> rows, int tableStartX, int imgWidth, double mmPerPx)
        {
            var blocks = new List<TableBlock>();
            if (rows == null || rows.Count == 0)
                return blocks;

            double avgRowH  = rows.Average(r => (double)Math.Max(1, r.PixelH));
            double splitGap = Math.Max(80.0, avgRowH * 3.0);

            var groups = new List<List<TableRow>>();
            foreach (var r in rows)
            {
                List<TableRow> last = groups.Count > 0 ? groups[groups.Count - 1] : null;
                if (last == null)
                {
                    groups.Add(new List<TableRow> { r });
                    continue;
                }

                TableRow prev      = last[last.Count - 1];
                bool     wideGap   = r.PixelY - (prev.PixelY + prev.PixelH) > splitGap;
                bool     jumpCells = Math.Abs(r.Cells.Count - prev.Cells.Count) > CellCountJumpTol;

                if (wideGap || jumpCells)
                    groups.Add(new List<TableRow> { r });
                else
                    last.Add(r);
            }

            int index = 1;
            foreach (var g in groups)
            {
                var block = new TableBlock { Name = "table_" + index++ };

                int left = int.MaxValue, top = int.MaxValue;
                int right = int.MinValue, bottom = int.MinValue;
                int rowNo = 1;

                // 列号在本表内单独聚类（不同表列数不同，共享列号会串）
                var blockCells = new List<TableCell>();

                foreach (var r in g)
                {
                    r.Index = rowNo++;

                    foreach (var c in r.Cells)
                    {
                        blockCells.Add(c);

                        if (c.PixelX < left)              left   = c.PixelX;
                        if (c.PixelY < top)               top    = c.PixelY;
                        if (c.PixelX + c.PixelW > right)  right  = c.PixelX + c.PixelW;
                        if (c.PixelY + c.PixelH > bottom) bottom = c.PixelY + c.PixelH;
                    }
                }

                BuildColumns(blockCells, mmPerPx);

                // 左边界对齐到表格区起点，便于与零件区一眼分开
                if (tableStartX > 0 && tableStartX < left) left = tableStartX;
                if (imgWidth   > 0 && left > imgWidth)     left = imgWidth;

                block.PixelX = left;
                block.PixelY = top;
                block.PixelW = right  - left;
                block.PixelH = bottom - top;
                block.Rows   = g;

                blocks.Add(block);
            }

            return blocks;
        }

        /// <summary>词 → 新单元格。</summary>
        private static TableCell CellFromText(PartText t)
        {
            return new TableCell
            {
                Text    = t.Text,
                PixelX  = t.PixelX,
                PixelY  = t.PixelY,
                PixelW  = t.PixelW,
                PixelH  = t.PixelH,
                PdfX_mm = t.PdfX_mm,
                PdfY_mm = t.PdfY_mm,
                PdfW_mm = t.PdfW_mm,
                PdfH_mm = t.PdfH_mm
            };
        }

        /// <summary>把同一单元格内的下一个词并入：必要时补空格，并扩展单元格外接框。</summary>
        private static void AppendWord(TableCell cell, PartText t, int prevRight)
        {
            bool spaced = t.PixelX - prevRight > Math.Max(2.0, t.PixelH * 0.5);
            cell.Text  += (spaced ? " " : string.Empty) + t.Text;

            int left   = Math.Min(cell.PixelX, t.PixelX);
            int top    = Math.Min(cell.PixelY, t.PixelY);
            int right  = Math.Max(cell.PixelX + cell.PixelW, t.PixelX + t.PixelW);
            int bottom = Math.Max(cell.PixelY + cell.PixelH, t.PixelY + t.PixelH);

            cell.PixelX = left;
            cell.PixelY = top;
            cell.PixelW = right  - left;
            cell.PixelH = bottom - top;
        }

        /// <summary>按像素外接框重算单元格的毫米坐标。</summary>
        private static void SyncCellMm(TableCell c, double mmPerPx)
        {
            c.PdfX_mm = c.PixelX * mmPerPx;
            c.PdfY_mm = c.PixelY * mmPerPx;
            c.PdfW_mm = c.PixelW * mmPerPx;
            c.PdfH_mm = c.PixelH * mmPerPx;
        }

        /// <summary>词框与零件图框的交集面积 / 词框面积。</summary>
        private static double OverlapRatio(WordBox w, PartInfo part)
        {
            double left   = Math.Max(w.X, part.PixelX);
            double top    = Math.Max(w.Y, part.PixelY);
            double right  = Math.Min(w.X + w.W, part.PixelX + part.PixelW);
            double bottom = Math.Min(w.Y + w.H, part.PixelY + part.PixelH);

            if (right <= left || bottom <= top)
                return 0.0;

            return (right - left) * (bottom - top) / (w.W * w.H);
        }

        /// <summary>页面像素坐标系下的词框（中间结果）。</summary>
        private class WordBox
        {
            public string Text { get; set; }
            public double X    { get; set; }
            public double Y    { get; set; }
            public double W    { get; set; }
            public double H    { get; set; }
        }
    }
}
