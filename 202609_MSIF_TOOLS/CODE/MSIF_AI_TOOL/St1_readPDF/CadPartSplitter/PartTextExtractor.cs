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

                    var words   = new List<WordBox>();
                    int outside = 0;

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
        /// 把词级文字按「同一行」合并成可读文本行（供 parts_text.txt 使用）。
        /// 判行为「纵向中心差 ≤ 0.6 倍字高」；同一行内按 X 排序，
        /// 词间水平间隙大于半个字高时补一个空格，否则直接拼接（适应中/日文紧排）。
        /// </summary>
        public static List<PartTextLine> BuildLines(List<PartText> texts)
        {
            var result = new List<PartTextLine>();
            if (texts == null || texts.Count == 0)
                return result;

            var groups = new List<List<PartText>>();

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

            foreach (var g in groups)
            {
                var ordered = g.OrderBy(t => t.PixelX).ToList();
                var sb = new StringBuilder();
                int prevRight = int.MinValue;

                foreach (var t in ordered)
                {
                    if (sb.Length > 0 && t.PixelX - prevRight > Math.Max(2.0, t.PixelH * 0.5))
                        sb.Append(' ');
                    sb.Append(t.Text);
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

            // ① 分行
            var rowGroups = new List<List<PartText>>();

            foreach (var t in tableWords)
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
            var rows = new List<TableRow>();

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

            rows = rows.OrderBy(r => r.PixelY).ToList();

            // ③④⑤ 按行结构变化分表 → 每张表内单独推断列号 → 求外接框
            return BuildBlockList(rows, tableStartX, imgWidth, mmPerPx);
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
