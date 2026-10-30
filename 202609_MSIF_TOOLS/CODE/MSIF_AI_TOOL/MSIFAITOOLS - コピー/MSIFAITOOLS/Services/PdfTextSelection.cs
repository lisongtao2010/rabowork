using System;
using System.Collections.Generic;
using System.Text;
using UglyToad.PdfPig.Content;

namespace MSIFAITOOLS.Services
{
    /// <summary>
    /// 一段可选择的字符范围（索引均含端点，索引基于 <see cref="PdfTextSelectionModel.Chars"/>）。
    /// </summary>
    internal struct PdfTextRange
    {
        /// <summary>空范围（没有选择）。</summary>
        public static readonly PdfTextRange Empty = new PdfTextRange(-1, -1);

        public PdfTextRange(int start, int end)
        {
            Start = start;
            End = end;
        }

        /// <summary>起始字符索引（含）。</summary>
        public int Start { get; }

        /// <summary>结束字符索引（含）。</summary>
        public int End { get; }

        /// <summary>是否为空范围。</summary>
        public bool IsEmpty
        {
            get { return Start < 0 || End < Start; }
        }

        /// <summary>范围内的字符个数。</summary>
        public int Length
        {
            get { return IsEmpty ? 0 : End - Start + 1; }
        }
    }

    /// <summary>
    /// 页面上的一个可选字符。坐标已换算为左上角原点、Y 轴向下的阅读器坐标系，单位 PDF 点，
    /// 与页面渲染位图的像素坐标只差一个缩放比例。
    /// </summary>
    internal sealed class PdfSelectableChar
    {
        public PdfSelectableChar(string text, double left, double top, double right, double bottom,
            double fontSize, int lineIndex)
        {
            Text = text;
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
            FontSize = fontSize;
            LineIndex = lineIndex;
        }

        /// <summary>字符内容（PdfPig 的一个字形，通常是 1 个字符，个别连字可能更长）。</summary>
        public string Text { get; }

        /// <summary>左边界（X）。</summary>
        public double Left { get; }

        /// <summary>上边界（Y，越小越靠上）。</summary>
        public double Top { get; }

        /// <summary>右边界（X）。</summary>
        public double Right { get; }

        /// <summary>下边界（Y，越大越靠下）。</summary>
        public double Bottom { get; }

        /// <summary>字号（用于字间距判断）。</summary>
        public double FontSize { get; }

        /// <summary>所属行的索引（对应 <see cref="PdfTextSelectionModel.Lines"/>）。</summary>
        public int LineIndex { get; set; }

        /// <summary>
        /// 文字流方向是否与坐标轴正方向一致：
        /// 横排时 true 表示从左到右，竖排时 true 表示（屏幕上）从上到下。
        /// /Rotate 180（横排从右到左）、/Rotate 270（竖排从下到上）时为 false。
        /// </summary>
        public bool FlowAscending { get; set; } = true;

        /// <summary>字符宽度。</summary>
        public double Width
        {
            get { return Right - Left; }
        }

        /// <summary>字符高度。</summary>
        public double Height
        {
            get { return Bottom - Top; }
        }

        /// <summary>垂直中心。</summary>
        public double CenterY
        {
            get { return (Top + Bottom) / 2.0; }
        }

        /// <summary>是否只由空白字符组成。</summary>
        public bool IsWhiteSpace
        {
            get
            {
                if (string.IsNullOrEmpty(Text))
                {
                    return true;
                }

                for (int index = 0; index < Text.Length; index++)
                {
                    if (!char.IsWhiteSpace(Text[index]))
                    {
                        return false;
                    }
                }

                return true;
            }
        }
    }

    /// <summary>一行文字：行内字符在 <see cref="PdfTextSelectionModel.Chars"/> 中的索引是连续的。</summary>
    internal sealed class PdfSelectableLine
    {
        public PdfSelectableLine(int index, int firstCharIndex, bool isVertical)
            : this(index, firstCharIndex, isVertical, true)
        {
        }

        public PdfSelectableLine(int index, int firstCharIndex, bool isVertical, bool isAscending)
        {
            Index = index;
            FirstCharIndex = firstCharIndex;
            IsVertical = isVertical;
            IsAscending = isAscending;
            Left = double.MaxValue;
            Top = double.MaxValue;
            Right = double.MinValue;
            Bottom = double.MinValue;
        }

        /// <summary>行的索引。</summary>
        public int Index { get; }

        /// <summary>该行第一个字符在字符集合中的索引。</summary>
        public int FirstCharIndex { get; }

        /// <summary>
        /// 是否为竖排（自上而下）的一列文字。竖排时“行”表示一列，字符按 Y 从上到下排列；
        /// 旋转页面（/Rotate 90 / 270）上的横排文字在显示坐标中就是竖排。
        /// </summary>
        public bool IsVertical { get; }

        /// <summary>行内字符的排列方向是否与坐标轴正方向一致（false 表示倒排，如 /Rotate 180 / 270）。</summary>
        public bool IsAscending { get; }

        /// <summary>该行的字符个数。</summary>
        public int Count { get; private set; }

        /// <summary>行左边界。</summary>
        public double Left { get; private set; }

        /// <summary>行上边界。</summary>
        public double Top { get; private set; }

        /// <summary>行右边界。</summary>
        public double Right { get; private set; }

        /// <summary>行下边界。</summary>
        public double Bottom { get; private set; }

        /// <summary>行宽。</summary>
        public double Width
        {
            get { return Right - Left; }
        }

        /// <summary>行高。</summary>
        public double Height
        {
            get { return Bottom - Top; }
        }

        /// <summary>把字符并入该行并扩展行的外接矩形。</summary>
        public void Extend(PdfSelectableChar item)
        {
            if (Count == 0)
            {
                Left = item.Left;
                Top = item.Top;
                Right = item.Right;
                Bottom = item.Bottom;
            }
            else if (IsVertical)
            {
                Top = Math.Min(Top, item.Top);
                Bottom = Math.Max(Bottom, item.Bottom);

                // 空格的字形框通常按字号撑满，用它参与横向边界会把列宽撑大
                if (!item.IsWhiteSpace)
                {
                    Left = Math.Min(Left, item.Left);
                    Right = Math.Max(Right, item.Right);
                }
            }
            else
            {
                Left = Math.Min(Left, item.Left);
                Right = Math.Max(Right, item.Right);

                // 空格的字形框通常按字号撑满，用它参与上下边界会把行高撑大
                if (!item.IsWhiteSpace)
                {
                    Top = Math.Min(Top, item.Top);
                    Bottom = Math.Max(Bottom, item.Bottom);
                }
            }

            Count++;
        }

        /// <summary>判断字符是否与该行在垂直方向上明显重叠（用于合并同一视觉行）。</summary>
        public bool OverlapsVertically(PdfSelectableChar item)
        {
            double overlap = Math.Min(Bottom, item.Bottom) - Math.Max(Top, item.Top);
            double minHeight = Math.Min(Height, item.Height);

            if (minHeight <= 0.0)
            {
                return overlap >= 0.0;
            }

            return overlap > minHeight * 0.5;
        }

        /// <summary>判断字符是否与该列在水平方向上明显重叠（用于把竖排文字合并成一列）。</summary>
        public bool OverlapsHorizontally(PdfSelectableChar item)
        {
            double overlap = Math.Min(Right, item.Right) - Math.Max(Left, item.Left);
            double minWidth = Math.Min(Width, item.Width);

            if (minWidth <= 0.0)
            {
                return overlap >= 0.0;
            }

            return overlap > minWidth * 0.5;
        }
    }

    /// <summary>选择高亮的矩形（左上角原点、Y 向下、单位 PDF 点）。</summary>
    internal struct PdfSelectionRectangle
    {
        public PdfSelectionRectangle(double left, double top, double right, double bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public double Left { get; }

        public double Top { get; }

        public double Right { get; }

        public double Bottom { get; }

        public double Width
        {
            get { return Right - Left; }
        }

        public double Height
        {
            get { return Bottom - Top; }
        }
    }

    /// <summary>
    /// 页面文字选择模型：把 PdfPig 提取到的字形按“行 → 字符”整理成阅读顺序，
    /// 并提供命中测试、选词 / 选行、复制文本与高亮矩形计算。全部为纯计算，不依赖界面。
    /// </summary>
    internal sealed class PdfTextSelectionModel
    {
        /// <summary>所有可选择字符（按阅读顺序：先按行自上而下，行内自左向右）。</summary>
        private readonly List<PdfSelectableChar> _chars = new List<PdfSelectableChar>();

        /// <summary>所有行（索引与 <see cref="PdfSelectableChar.LineIndex"/> 对应）。</summary>
        private readonly List<PdfSelectableLine> _lines = new List<PdfSelectableLine>();

        private PdfTextSelectionModel()
        {
        }

        /// <summary>字符集合（只读）。</summary>
        public IList<PdfSelectableChar> Chars
        {
            get { return _chars; }
        }

        /// <summary>行集合（只读）。</summary>
        public IList<PdfSelectableLine> Lines
        {
            get { return _lines; }
        }

        /// <summary>页面宽度（PDF 点）。</summary>
        public double PageWidth { get; private set; }

        /// <summary>页面高度（PDF 点）。</summary>
        public double PageHeight { get; private set; }

        /// <summary>
        /// 从 PdfPig 的一页内容创建选择模型。
        /// PdfPig 的字形坐标已是「CropBox 左下角原点、Y 向上」（与 pdfium 渲染区域一致），
        /// 这里统一换算成左上角原点、Y 向下，便于直接乘以缩放比例得到像素坐标。
        /// </summary>
        public static PdfTextSelectionModel Create(Page page)
        {
            if (page == null)
            {
                throw new ArgumentNullException(nameof(page));
            }

            PdfTextSelectionModel model = new PdfTextSelectionModel();

            model.PageWidth = page.Width;
            model.PageHeight = page.Height;

            List<PdfSelectableChar> horizontal = new List<PdfSelectableChar>();
            List<PdfSelectableChar> vertical = new List<PdfSelectableChar>();

            foreach (Letter letter in page.Letters)
            {
                string text = letter.Value;

                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                UglyToad.PdfPig.Core.PdfRectangle box = letter.BoundingBox;

                double left = Math.Min(box.Left, box.Right);
                double right = Math.Max(box.Left, box.Right);
                double top = page.Height - Math.Max(box.Top, box.Bottom);
                double bottom = page.Height - Math.Min(box.Top, box.Bottom);
                double fontSize = letter.PointSize > 0 ? letter.PointSize : bottom - top;

                if (fontSize <= 0.0)
                {
                    fontSize = 1.0;
                }

                // 个别字形（如空格）宽度为 0，补一个最小宽度，避免无法命中
                if (right - left <= 0.0)
                {
                    right = left + fontSize * 0.3;
                }

                if (bottom - top <= 0.0)
                {
                    bottom = top + fontSize * 0.75;
                }

                PdfSelectableChar item = new PdfSelectableChar(text, left, top, right, bottom, fontSize, -1);

                // 用基线方向判断“显示坐标”中文字是横排还是竖排：
                // 旋转页面（/Rotate 90 / 270）上原本横排的文字，PdfPig 会把 TextOrientation
                // 报告成 Rotate90 / Rotate270，但坐标已随页面旋转，此时文字在屏幕上是竖排的。
                double baselineDx = letter.EndBaseLine.X - letter.StartBaseLine.X;
                double baselineDy = letter.EndBaseLine.Y - letter.StartBaseLine.Y;
                bool isHorizontal;

                if (Math.Abs(baselineDx) < 0.0001 && Math.Abs(baselineDy) < 0.0001)
                {
                    isHorizontal = right - left >= bottom - top;
                }
                else
                {
                    isHorizontal = Math.Abs(baselineDx) >= Math.Abs(baselineDy);
                }

                // 文字流方向：横排按基线 X 方向（180° 倒排时为负），竖排按基线 Y 方向
                item.FlowAscending = isHorizontal ? baselineDx >= 0.0 : baselineDy <= 0.0;

                if (isHorizontal)
                {
                    horizontal.Add(item);
                }
                else
                {
                    vertical.Add(item);
                }
            }

            model.BuildLines(horizontal, vertical);

            return model;
        }

        /// <summary>把字符聚成行（横排）或列（竖排），并按阅读顺序写入字符集合。</summary>
        private void BuildLines(List<PdfSelectableChar> horizontal, List<PdfSelectableChar> vertical)
        {
            horizontal.Sort(delegate(PdfSelectableChar a, PdfSelectableChar b)
            {
                int compare = a.CenterY.CompareTo(b.CenterY);

                return compare != 0 ? compare : a.Left.CompareTo(b.Left);
            });

            List<List<PdfSelectableChar>> rows = new List<List<PdfSelectableChar>>();
            List<PdfSelectableLine> rowBounds = new List<PdfSelectableLine>();

            foreach (PdfSelectableChar item in horizontal)
            {
                int last = rows.Count - 1;

                if (last >= 0 && rowBounds[last].OverlapsVertically(item))
                {
                    rows[last].Add(item);
                    rowBounds[last].Extend(item);
                }
                else
                {
                    rows.Add(new List<PdfSelectableChar> { item });

                    PdfSelectableLine bounds = new PdfSelectableLine(rows.Count - 1, 0, false);
                    bounds.Extend(item);
                    rowBounds.Add(bounds);
                }
            }

            foreach (List<PdfSelectableChar> row in rows)
            {
                row.Sort(delegate(PdfSelectableChar a, PdfSelectableChar b)
                {
                    return a.Left.CompareTo(b.Left);
                });

                if (!row[0].FlowAscending)
                {
                    // /Rotate 180 等倒排文字：按 X 降序才是阅读顺序
                    row.Reverse();
                }

                AppendLine(row, false);
            }

            // 竖排文字（含 /Rotate 90 / 270 页面上的横排文字）：按 X 聚成列，列内由上到下
            vertical.Sort(delegate(PdfSelectableChar a, PdfSelectableChar b)
            {
                int compare = a.Left.CompareTo(b.Left);

                return compare != 0 ? compare : a.Top.CompareTo(b.Top);
            });

            List<List<PdfSelectableChar>> columns = new List<List<PdfSelectableChar>>();
            List<PdfSelectableLine> columnBounds = new List<PdfSelectableLine>();

            foreach (PdfSelectableChar item in vertical)
            {
                int last = columns.Count - 1;

                if (last >= 0 && columnBounds[last].OverlapsHorizontally(item))
                {
                    columns[last].Add(item);
                    columnBounds[last].Extend(item);
                }
                else
                {
                    columns.Add(new List<PdfSelectableChar> { item });

                    PdfSelectableLine bounds = new PdfSelectableLine(columns.Count - 1, 0, true);
                    bounds.Extend(item);
                    columnBounds.Add(bounds);
                }
            }

            foreach (List<PdfSelectableChar> column in columns)
            {
                column.Sort(delegate(PdfSelectableChar a, PdfSelectableChar b)
                {
                    int compare = a.Top.CompareTo(b.Top);

                    return compare != 0 ? compare : a.Left.CompareTo(b.Left);
                });

                if (!column[0].FlowAscending)
                {
                    // /Rotate 270 等竖排倒排文字：按 Y 降序（从下往上）才是阅读顺序
                    column.Reverse();
                }

                AppendLine(column, true);
            }
        }

        /// <summary>把排好序的字符写入字符集合，并生成对应的行（或列）。</summary>
        private void AppendLine(List<PdfSelectableChar> items, bool isVertical)
        {
            if (items.Count == 0)
            {
                return;
            }

            PdfSelectableLine line = new PdfSelectableLine(_lines.Count, _chars.Count, isVertical, items[0].FlowAscending);

            foreach (PdfSelectableChar item in items)
            {
                item.LineIndex = line.Index;
                line.Extend(item);
                _chars.Add(item);
            }

            _lines.Add(line);
        }

        /// <summary>
        /// 命中测试：把页面坐标（左上角原点、单位 PDF 点）换算成插入符位置（0 ~ 字符总数）。
        /// 返回 -1 表示没有落在任何文字附近（例如点在页面空白处）。
        /// </summary>
        public int HitTestCaret(double x, double y)
        {
            if (_chars.Count == 0)
            {
                return -1;
            }

            PdfSelectableLine bestLine = null;
            double bestDistance = double.MaxValue;

            foreach (PdfSelectableLine line in _lines)
            {
                double distance = 0.0;

                if (line.IsVertical)
                {
                    // 竖排（一列）文字：按横向距离判断光标落在哪一列
                    if (x < line.Left)
                    {
                        distance = line.Left - x;
                    }
                    else if (x > line.Right)
                    {
                        distance = x - line.Right;
                    }
                }
                else
                {
                    if (y < line.Top)
                    {
                        distance = line.Top - y;
                    }
                    else if (y > line.Bottom)
                    {
                        distance = y - line.Bottom;
                    }
                }

                if (distance <= 0.0)
                {
                    bestLine = line;
                    bestDistance = 0.0;
                    break;
                }

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestLine = line;
                }
            }

            if (bestLine == null)
            {
                return -1;
            }

            // 允许一定的吸附范围（横排按行高、竖排按列宽），避免点在文字空隙时选择被清空
            double snap = bestLine.IsVertical
                ? Math.Max(3.0, bestLine.Width)
                : Math.Max(3.0, bestLine.Height);

            if (bestDistance > snap)
            {
                return -1;
            }

            int first = bestLine.FirstCharIndex;
            int last = first + bestLine.Count - 1;

            if (bestLine.IsVertical)
            {
                for (int index = first; index <= last; index++)
                {
                    PdfSelectableChar item = _chars[index];

                    if (bestLine.IsAscending)
                    {
                        if (y < item.Top)
                        {
                            return index;
                        }

                        if (y <= item.Bottom)
                        {
                            return y < (item.Top + item.Bottom) / 2.0 ? index : index + 1;
                        }
                    }
                    else
                    {
                        // 倒排（从下往上）：越往下索引越小
                        if (y > item.Bottom)
                        {
                            return index;
                        }

                        if (y >= item.Top)
                        {
                            return y > (item.Top + item.Bottom) / 2.0 ? index : index + 1;
                        }
                    }
                }

                return last + 1;
            }

            for (int index = first; index <= last; index++)
            {
                PdfSelectableChar item = _chars[index];

                if (bestLine.IsAscending)
                {
                    if (x < item.Left)
                    {
                        return index;
                    }

                    if (x <= item.Right)
                    {
                        // 点在字符左半边 -> 插入符在字符之前；右半边 -> 在字符之后
                        return x < (item.Left + item.Right) / 2.0 ? index : index + 1;
                    }
                }
                else
                {
                    // 倒排（从右往左）：越往右索引越小
                    if (x > item.Right)
                    {
                        return index;
                    }

                    if (x >= item.Left)
                    {
                        return x > (item.Left + item.Right) / 2.0 ? index : index + 1;
                    }
                }
            }

            return last + 1;
        }

        /// <summary>双击选词：以空白 / 明显字间距为词边界，向左右扩展。</summary>
        public PdfTextRange SelectWord(int caret)
        {
            if (_chars.Count == 0)
            {
                return PdfTextRange.Empty;
            }

            int index = Math.Max(0, Math.Min(_chars.Count - 1, caret));
            PdfSelectableLine line = _lines[_chars[index].LineIndex];
            int first = line.FirstCharIndex;
            int last = first + line.Count - 1;

            // 点在空白上时，优先向左找最近的可见字符
            if (_chars[index].IsWhiteSpace && index > first)
            {
                index--;
            }

            if (_chars[index].IsWhiteSpace)
            {
                index = first;
            }

            int start = index;
            int end = index;

            while (start > first && !IsWordBreak(start - 1))
            {
                start--;
            }

            while (end < last && !IsWordBreak(end))
            {
                end++;
            }

            while (start <= end && _chars[start].IsWhiteSpace)
            {
                start++;
            }

            while (end >= start && _chars[end].IsWhiteSpace)
            {
                end--;
            }

            return start <= end ? new PdfTextRange(start, end) : PdfTextRange.Empty;
        }

        /// <summary>三击 / 选中整行（自动去掉行首行尾的空白）。</summary>
        public PdfTextRange SelectLine(int caret)
        {
            if (_chars.Count == 0)
            {
                return PdfTextRange.Empty;
            }

            int index = Math.Max(0, Math.Min(_chars.Count - 1, caret));
            PdfSelectableLine line = _lines[_chars[index].LineIndex];
            int start = line.FirstCharIndex;
            int end = start + line.Count - 1;

            while (start <= end && _chars[start].IsWhiteSpace)
            {
                start++;
            }

            while (end >= start && _chars[end].IsWhiteSpace)
            {
                end--;
            }

            return start <= end ? new PdfTextRange(start, end) : PdfTextRange.Empty;
        }

        /// <summary>全选本页文字（自动去掉首尾空白）。</summary>
        public PdfTextRange SelectAll()
        {
            if (_chars.Count == 0)
            {
                return PdfTextRange.Empty;
            }

            int start = 0;
            int end = _chars.Count - 1;

            while (start <= end && _chars[start].IsWhiteSpace)
            {
                start++;
            }

            while (end >= start && _chars[end].IsWhiteSpace)
            {
                end--;
            }

            return start <= end ? new PdfTextRange(start, end) : PdfTextRange.Empty;
        }

        /// <summary>两个相邻字符之间是否断词。</summary>
        private bool IsWordBreak(int leftIndex)
        {
            PdfSelectableChar left = _chars[leftIndex];
            PdfSelectableChar right = _chars[leftIndex + 1];

            if (left.IsWhiteSpace || right.IsWhiteSpace)
            {
                return true;
            }

            return NeedsSpace(left, right);
        }

        /// <summary>两个相邻字符之间是否需要补一个空格（PDF 里没有空格字符、只靠间距排版的情况）。</summary>
        private static bool NeedsSpace(PdfSelectableChar previous, PdfSelectableChar current)
        {
            if (previous.IsWhiteSpace || current.IsWhiteSpace)
            {
                return false;
            }

            // 阈值按字号取（约 0.3 个字高）：正常字距（12pt 字体约 0.6~2.8pt）不会误判成空格，
            // 而真正的空格（约 0.28 个字宽 ≈ 3.3pt 以上）能识别出来。
            double fontSize = Math.Max(previous.FontSize, current.FontSize);
            double threshold = Math.Max(2.5, fontSize * 0.3);

            return current.Left - previous.Right > threshold;
        }

        /// <summary>取范围内的文字：跨行时换行，词间距过大时补一个空格。</summary>
        public string GetText(PdfTextRange range)
        {
            if (range.IsEmpty)
            {
                return string.Empty;
            }

            int start = Math.Max(0, range.Start);
            int end = Math.Min(_chars.Count - 1, range.End);
            StringBuilder builder = new StringBuilder();

            for (int index = start; index <= end; index++)
            {
                PdfSelectableChar item = _chars[index];

                if (index > start)
                {
                    PdfSelectableChar previous = _chars[index - 1];

                    if (item.LineIndex != previous.LineIndex)
                    {
                        TrimEnd(builder);
                        builder.Append("\r\n");
                    }
                    else if (NeedsSpace(previous, item))
                    {
                        builder.Append(' ');
                    }
                }

                builder.Append(item.Text);
            }

            return builder.ToString();
        }

        /// <summary>去掉 StringBuilder 末尾的空白字符。</summary>
        private static void TrimEnd(StringBuilder builder)
        {
            while (builder.Length > 0 && char.IsWhiteSpace(builder[builder.Length - 1]))
            {
                builder.Length--;
            }
        }

        /// <summary>
        /// 计算选择高亮矩形（每行一个矩形，行高使用整行高度，避免句点、连字符等扁平字形导致高亮过细）。
        /// </summary>
        public IList<PdfSelectionRectangle> GetRectangles(PdfTextRange range)
        {
            List<PdfSelectionRectangle> rectangles = new List<PdfSelectionRectangle>();

            if (range.IsEmpty || _chars.Count == 0)
            {
                return rectangles;
            }

            int start = Math.Max(0, range.Start);
            int end = Math.Min(_chars.Count - 1, range.End);

            if (start > end)
            {
                return rectangles;
            }

            int currentLine = -1;
            double left = 0.0;
            double top = 0.0;
            double right = 0.0;
            double bottom = 0.0;

            for (int index = start; index <= end; index++)
            {
                PdfSelectableChar item = _chars[index];

                if (item.LineIndex != currentLine)
                {
                    if (currentLine >= 0)
                    {
                        AddRectangle(rectangles, _lines[currentLine], left, top, right, bottom);
                    }

                    currentLine = item.LineIndex;
                    left = item.Left;
                    top = item.Top;
                    right = item.Right;
                    bottom = item.Bottom;
                }
                else
                {
                    left = Math.Min(left, item.Left);
                    top = Math.Min(top, item.Top);
                    right = Math.Max(right, item.Right);
                    bottom = Math.Max(bottom, item.Bottom);
                }
            }

            if (currentLine >= 0)
            {
                AddRectangle(rectangles, _lines[currentLine], left, top, right, bottom);
            }

            return rectangles;
        }

        /// <summary>
        /// 把一行（或一列）内选中的字符范围转成高亮矩形：
        /// 横排用整行高度，竖排用整列宽度，避免句点、连字符等扁平字形导致高亮过细。
        /// </summary>
        private static void AddRectangle(List<PdfSelectionRectangle> rectangles, PdfSelectableLine line,
            double left, double top, double right, double bottom)
        {
            if (line.IsVertical)
            {
                rectangles.Add(new PdfSelectionRectangle(line.Left, top, line.Right, bottom));
            }
            else
            {
                rectangles.Add(new PdfSelectionRectangle(left, line.Top, right, line.Bottom));
            }
        }

        /// <summary>返回插入符（字符之间）位置的竖线矩形，宽度为 0，用于拖动时提示光标位置。</summary>
        public PdfSelectionRectangle GetCaretBar(int caret)
        {
            if (_chars.Count == 0)
            {
                return new PdfSelectionRectangle(0.0, 0.0, 0.0, 0.0);
            }

            int index = Math.Max(0, Math.Min(_chars.Count, caret));

            if (index < _chars.Count)
            {
                PdfSelectableChar item = _chars[index];
                PdfSelectableLine line = _lines[item.LineIndex];

                return line.IsVertical
                    ? new PdfSelectionRectangle(line.Left, item.Top, line.Right, item.Top)
                    : new PdfSelectionRectangle(item.Left, line.Top, item.Left, line.Bottom);
            }

            PdfSelectableChar last = _chars[_chars.Count - 1];
            PdfSelectableLine lastLine = _lines[last.LineIndex];

            return lastLine.IsVertical
                ? new PdfSelectionRectangle(lastLine.Left, last.Bottom, lastLine.Right, last.Bottom)
                : new PdfSelectionRectangle(last.Right, lastLine.Top, last.Right, lastLine.Bottom);
        }
    }
}
