using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace MSIFAITOOLS.Services
{
    /// <summary>
    /// 版面分析器：把 PdfPig 提取到的文字按“行 → 列”重组，
    /// 用于生成 pdfplumber 风格的三种输出（纯文本、版面文本、表格）。
    /// 全部为纯 .NET 计算，不依赖 Python / pdfplumber。
    /// </summary>
    internal static class PdfLayoutAnalyzer
    {
        /// <summary>一个词（坐标已换算为左上角原点、Y 轴向下的阅读器坐标系）。</summary>
        internal sealed class LayoutWord
        {
            public LayoutWord(string text, double left, double right, double top, double bottom)
            {
                Text = text;
                Left = left;
                Right = right;
                Top = top;
                Bottom = bottom;
            }

            /// <summary>词内容。</summary>
            public string Text { get; }

            /// <summary>左边界（X）。</summary>
            public double Left { get; }

            /// <summary>右边界（X）。</summary>
            public double Right { get; }

            /// <summary>上边界（Y，越小越靠上）。</summary>
            public double Top { get; }

            /// <summary>下边界（Y，越大越靠下）。</summary>
            public double Bottom { get; }

            /// <summary>垂直中心。</summary>
            public double CenterY
            {
                get { return (Top + Bottom) / 2.0; }
            }

            /// <summary>词高度。</summary>
            public double Height
            {
                get { return Bottom - Top; }
            }
        }

        /// <summary>同一水平行上的若干词。</summary>
        internal sealed class LayoutLine
        {
            public LayoutLine(LayoutWord first)
            {
                Words = new List<LayoutWord>();
                Top = first.Top;
                Bottom = first.Bottom;
                Words.Add(first);
            }

            /// <summary>该行包含的词（按加入顺序，最终按 Left 排序后使用）。</summary>
            public List<LayoutWord> Words { get; }

            /// <summary>行的上边界。</summary>
            public double Top { get; private set; }

            /// <summary>行的下边界。</summary>
            public double Bottom { get; private set; }

            /// <summary>行高度。</summary>
            public double Height
            {
                get { return Bottom - Top; }
            }

            /// <summary>把词并入该行，同时扩展行的上下边界。</summary>
            public void Add(LayoutWord word)
            {
                Words.Add(word);
                Top = Math.Min(Top, word.Top);
                Bottom = Math.Max(Bottom, word.Bottom);
            }

            /// <summary>行内文字（按 X 从左到右，词间以一个空格分隔）。</summary>
            public string ToText()
            {
                StringBuilder sb = new StringBuilder();

                foreach (LayoutWord word in GetSortedWords())
                {
                    if (sb.Length > 0)
                    {
                        sb.Append(' ');
                    }

                    sb.Append(word.Text);
                }

                return sb.ToString();
            }

            /// <summary>按 X 排序后的词列表。</summary>
            public List<LayoutWord> GetSortedWords()
            {
                List<LayoutWord> sorted = new List<LayoutWord>(Words);
                sorted.Sort(delegate(LayoutWord a, LayoutWord b)
                {
                    int compare = a.Left.CompareTo(b.Left);
                    return compare != 0 ? compare : a.Top.CompareTo(b.Top);
                });

                return sorted;
            }
        }

        /// <summary>
        /// 一个图片（image XObject）在页面中的区域，坐标已换算为左上角原点、Y 向下。
        /// 用于把“落在图片范围内的文字”判定为图上文字（坐标比对）。
        /// </summary>
        internal sealed class LayoutImage
        {
            public LayoutImage(int index, double left, double right, double top, double bottom)
            {
                Index = index;
                Left = Math.Min(left, right);
                Right = Math.Max(left, right);
                Top = Math.Min(top, bottom);
                Bottom = Math.Max(top, bottom);
            }

            /// <summary>图片序号（从 1 开始，按内容流中出现的顺序）。</summary>
            public int Index { get; }

            /// <summary>左边界（X）。</summary>
            public double Left { get; }

            /// <summary>右边界（X）。</summary>
            public double Right { get; }

            /// <summary>上边界（Y，越小越靠上）。</summary>
            public double Top { get; }

            /// <summary>下边界（Y，越大越靠下）。</summary>
            public double Bottom { get; }

            /// <summary>宽度。</summary>
            public double Width
            {
                get { return Right - Left; }
            }

            /// <summary>高度。</summary>
            public double Height
            {
                get { return Bottom - Top; }
            }

            /// <summary>面积（重叠图片中取面积最小者，即最具体的区域）。</summary>
            public double Area
            {
                get { return Width * Height; }
            }

            /// <summary>词的包围盒中心是否落在本区域内。</summary>
            public bool Contains(LayoutWord word, double tolerance)
            {
                if (word == null)
                {
                    return false;
                }

                double centerX = (word.Left + word.Right) / 2.0;
                double centerY = word.CenterY;

                return centerX >= Left - tolerance && centerX <= Right + tolerance &&
                       centerY >= Top - tolerance && centerY <= Bottom + tolerance;
            }
        }

        /// <summary>
        /// 一个表格块：连续若干行 + 外接矩形 + 列锚点，
        /// 用于把表格文字（已输出到 _plu_table.txt）与图上文字区分开，避免重复。
        /// 判定表格文字时用的是“有效 X 范围”（过滤噪声列后的横向范围），而不是单纯的外接矩形。
        /// </summary>
        internal sealed class LayoutBlock
        {
            /// <summary>有效 X 范围是否已计算（懒计算，避免逐词重复统计）。</summary>
            private bool _effectiveRangeReady;

            /// <summary>有效 X 范围左边界。</summary>
            private double _effectiveLeft;

            /// <summary>有效 X 范围右边界。</summary>
            private double _effectiveRight;

            public LayoutBlock()
            {
                Lines = new List<LayoutLine>();
                Columns = new List<double>();
                Left = double.MaxValue;
                Top = double.MaxValue;
                Right = double.MinValue;
                Bottom = double.MinValue;
            }

            /// <summary>块内的行（按 Top 升序）。</summary>
            public List<LayoutLine> Lines { get; }

            /// <summary>列锚点（词左边界的聚类结果，升序）；由 FindTableBlocks 填充。</summary>
            public List<double> Columns { get; set; }

            /// <summary>左边界（块内所有词的最小 Left）。</summary>
            public double Left { get; private set; }

            /// <summary>上边界（块内所有词的最小 Top）。</summary>
            public double Top { get; private set; }

            /// <summary>右边界（块内所有词的最大 Right）。</summary>
            public double Right { get; private set; }

            /// <summary>下边界（块内所有词的最大 Bottom）。</summary>
            public double Bottom { get; private set; }

            /// <summary>把行并入块，同时扩展外接矩形。</summary>
            public void Add(LayoutLine line)
            {
                Lines.Add(line);

                foreach (LayoutWord word in line.Words)
                {
                    Left = Math.Min(Left, word.Left);
                    Right = Math.Max(Right, word.Right);
                    Top = Math.Min(Top, word.Top);
                    Bottom = Math.Max(Bottom, word.Bottom);
                }
            }

            /// <summary>词的包围盒中心是否落在外接矩形内。</summary>
            public bool Contains(LayoutWord word, double tolerance)
            {
                if (word == null)
                {
                    return false;
                }

                double centerX = (word.Left + word.Right) / 2.0;
                double centerY = word.CenterY;

                return centerX >= Left - tolerance && centerX <= Right + tolerance &&
                       centerY >= Top - tolerance && centerY <= Bottom + tolerance;
            }

            /// <summary>表格有效左边界（已排除噪声列，并向左留出余量）。</summary>
            public double EffectiveLeft
            {
                get
                {
                    EnsureEffectiveRange();
                    return _effectiveLeft;
                }
            }

            /// <summary>表格有效右边界（已排除噪声列，并向右留出余量）。</summary>
            public double EffectiveRight
            {
                get
                {
                    EnsureEffectiveRange();
                    return _effectiveRight;
                }
            }

            /// <summary>
            /// 词是否属于该表格块的文字：中心点落在外接矩形内，并且左右边界与“有效 X 范围”有重叠。
            /// 有效 X 范围会忽略非空占比过低的噪声列，因此排在表格左右两侧的图纸标注
            /// （尺寸数字、部位记号等）不会被误判成表格文字，仍可作为图上文字输出。
            /// </summary>
            public bool ContainsTableText(LayoutWord word, double tolerance)
            {
                if (!Contains(word, tolerance))
                {
                    return false;
                }

                EnsureEffectiveRange();

                // 用区间重叠（而非中心点）判定，避免品名、図番这类宽词因中心点越界被误判
                return word.Right > _effectiveLeft && word.Left < _effectiveRight;
            }

            /// <summary>
            /// 计算表格的有效 X 范围：
            /// 先统计每个列锚点被多少行使用，非空占比低于 <see cref="MinColumnFillRatio"/> 的列视为噪声列并丢弃，
            /// 再用“有效列上的实际文字”左右边界各放宽 <see cref="EffectiveRangePadding"/> 作为表格实际的横向覆盖范围。
            /// 噪声列多是“图纸标注偶然与表格同高”造成的，因此有效范围通常比外接矩形窄。
            /// </summary>
            private void EnsureEffectiveRange()
            {
                if (_effectiveRangeReady)
                {
                    return;
                }

                // 先退化为外接矩形，列信息不足时直接使用
                _effectiveRangeReady = true;
                _effectiveLeft = Left;
                _effectiveRight = Right;

                List<double> columns = Columns;

                if (columns == null || columns.Count == 0 || Lines.Count == 0)
                {
                    return;
                }

                // 每个列锚点被多少行使用（取列方式与 RenderTables 保持一致）
                int[] rowHits = new int[columns.Count];

                foreach (LayoutLine line in Lines)
                {
                    bool[] used = new bool[columns.Count];

                    foreach (LayoutWord word in line.Words)
                    {
                        used[FindColumn(columns, word)] = true;
                    }

                    for (int i = 0; i < used.Length; i++)
                    {
                        if (used[i])
                        {
                            rowHits[i]++;
                        }
                    }
                }

                double minFill = Math.Max(1.0, Lines.Count * MinColumnFillRatio);
                bool[] keep = new bool[columns.Count];
                bool anyKeep = false;

                for (int i = 0; i < columns.Count; i++)
                {
                    if (rowHits[i] >= minFill)
                    {
                        keep[i] = true;
                        anyKeep = true;
                    }
                }

                if (!anyKeep)
                {
                    return;
                }

                // 取“有效列上的实际文字范围”而非列锚点本身，
                // 这样居中对齐的表头（如居中的「図番」）也能落在有效范围内。
                double textLeft = double.MaxValue;
                double textRight = double.MinValue;

                foreach (LayoutLine line in Lines)
                {
                    foreach (LayoutWord word in line.Words)
                    {
                        if (!keep[FindColumn(columns, word)])
                        {
                            continue;
                        }

                        textLeft = Math.Min(textLeft, word.Left);
                        textRight = Math.Max(textRight, word.Right);
                    }
                }

                if (textLeft > textRight)
                {
                    return;
                }

                _effectiveLeft = textLeft - EffectiveRangePadding;
                _effectiveRight = textRight + EffectiveRangePadding;
            }
        }

        /// <summary>行聚类时回看的历史行数。</summary>
        private const int MaxLineLookBack = 12;

        /// <summary>判定“同一行”的垂直重叠比例阈值。</summary>
        private const double LineOverlapRatio = 0.5;

        /// <summary>列聚类容差 = 平均字宽 × 该系数。</summary>
        private const double ColumnToleranceRatio = 2.0;

        /// <summary>表格分行时允许的最大行间距（相对行高）。</summary>
        private const double TableRowGapRatio = 1.6;

        /// <summary>无法估算时的默认字宽。</summary>
        private const double DefaultCharWidth = 6.0;

        /// <summary>无法估算时的默认行高。</summary>
        private const double DefaultLineHeight = 12.0;

        /// <summary>小于该尺寸的图片视为图标/装饰，不作为“图”区域参与文字判定。</summary>
        private const double MinImageSize = 4.0;

        /// <summary>坐标比对容差（PDF 点），用于吸收文字与图片边界之间的细微误差。</summary>
        private const double HitTolerance = 1.0;

        /// <summary>列的非空占比低于该值时视为噪声列（多为图纸标注偶然落在表格的 Y 范围内）。</summary>
        private const double MinColumnFillRatio = 0.15;

        /// <summary>计算表格有效 X 范围时向左右两侧放宽的余量（PDF 点）。</summary>
        private const double EffectiveRangePadding = 15.0;

        /// <summary>区域差小于该值（PDF 点）的图片视为同一张，避免重复输出。</summary>
        private const double DuplicateImageTolerance = 0.5;

        /// <summary>提取一页中所有非空白词，并把坐标换算为左上角原点、Y 向下。</summary>
        /// <param name="page">PdfPig 的页面对象。</param>
        /// <returns>按内容流顺序排列的词集合（后续会再排序）。</returns>
        public static List<LayoutWord> GetWords(Page page)
        {
            if (page == null)
            {
                throw new ArgumentNullException(nameof(page));
            }

            double pageHeight = page.Height;
            List<LayoutWord> words = new List<LayoutWord>();

            foreach (Word word in page.GetWords(NearestNeighbourWordExtractor.Instance))
            {
                // 过滤纯空白“词”，与 pdfplumber / PyMuPDF 的行为保持一致
                if (string.IsNullOrEmpty(word.Text) || word.Text.Trim().Length == 0)
                {
                    continue;
                }

                // 用 var 推断，避免不同 PdfPig 版本间 PdfRectangle 命名空间( Core / Geometry )差异
                var box = word.BoundingBox;

                words.Add(new LayoutWord(
                    word.Text,
                    box.Left,
                    box.Right,
                    pageHeight - box.Top,
                    pageHeight - box.Bottom));
            }

            return words;
        }

        /// <summary>
        /// 提取一页中所有图片（image XObject）的区域，并把坐标换算为左上角原点、Y 向下，
        /// 以便与 <see cref="GetWords"/> 的词坐标直接比对。
        /// </summary>
        /// <param name="page">PdfPig 的页面对象。</param>
        /// <returns>按内容流顺序排列的图片区域；小于 <see cref="MinImageSize"/> 的图标会被忽略。</returns>
        public static List<LayoutImage> GetImages(Page page)
        {
            if (page == null)
            {
                throw new ArgumentNullException(nameof(page));
            }

            double pageHeight = page.Height;
            List<LayoutImage> images = new List<LayoutImage>();

            foreach (IPdfImage image in page.GetImages())
            {
                // 用 var 推断，避免不同 PdfPig 版本间 PdfRectangle 命名空间( Core / Geometry )差异
                // BoundingBox 替代已过时的 Bounds，含义相同：图片在页面上的实际绘制矩形
                var box = image.BoundingBox;
                double width = box.Right - box.Left;
                double height = box.Top - box.Bottom;

                if (width < MinImageSize || height < MinImageSize)
                {
                    continue;
                }

                LayoutImage candidate = new LayoutImage(images.Count + 1, box.Left, box.Right,
                    pageHeight - box.Top, pageHeight - box.Bottom);

                // 同一区域被重复绘制时只保留一份，避免输出多个空图块
                if (ContainsSameArea(images, candidate))
                {
                    continue;
                }

                images.Add(candidate);
            }

            return images;
        }

        /// <summary>判断集合中是否已有与候选区域相同的图片。</summary>
        private static bool ContainsSameArea(IList<LayoutImage> images, LayoutImage candidate)
        {
            foreach (LayoutImage image in images)
            {
                if (Math.Abs(image.Left - candidate.Left) <= DuplicateImageTolerance &&
                    Math.Abs(image.Top - candidate.Top) <= DuplicateImageTolerance &&
                    Math.Abs(image.Right - candidate.Right) <= DuplicateImageTolerance &&
                    Math.Abs(image.Bottom - candidate.Bottom) <= DuplicateImageTolerance)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>把词按垂直位置聚类成行（结果按自上而下排序）。</summary>
        /// <param name="words">同一页的所有词。</param>
        /// <returns>按 Top 升序排列的行集合。</returns>
        public static List<LayoutLine> GetLines(IList<LayoutWord> words)
        {
            List<LayoutLine> lines = new List<LayoutLine>();

            if (words == null || words.Count == 0)
            {
                return lines;
            }

            List<LayoutWord> sorted = new List<LayoutWord>(words);
            sorted.Sort(delegate(LayoutWord a, LayoutWord b)
            {
                int compare = a.Top.CompareTo(b.Top);
                return compare != 0 ? compare : a.Left.CompareTo(b.Left);
            });

            foreach (LayoutWord word in sorted)
            {
                LayoutLine target = null;

                // 只回看最近若干行，避免整表扫描
                for (int i = lines.Count - 1; i >= 0 && i >= lines.Count - MaxLineLookBack; i--)
                {
                    LayoutLine line = lines[i];
                    double overlap = Math.Min(line.Bottom, word.Bottom) - Math.Max(line.Top, word.Top);
                    double minHeight = Math.Min(line.Height, word.Height);

                    if (minHeight > 0 && overlap > minHeight * LineOverlapRatio)
                    {
                        target = line;
                        break;
                    }
                }

                if (target == null)
                {
                    lines.Add(new LayoutLine(word));
                }
                else
                {
                    target.Add(word);
                }
            }

            lines.Sort(delegate(LayoutLine a, LayoutLine b)
            {
                return a.Top.CompareTo(b.Top);
            });

            return lines;
        }

        /// <summary>
        /// 生成 pdfplumber <c>extract_text()</c> 风格的纯文本：
        /// 每行一段，行内词按 X 从左到右、以单个空格分隔。
        /// </summary>
        /// <param name="lines">该页的行集合。</param>
        /// <returns>以 CRLF 分隔的多行文本（末尾不带换行）。</returns>
        public static string RenderPlain(IList<LayoutLine> lines)
        {
            if (lines == null || lines.Count == 0)
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder();

            foreach (LayoutLine line in lines)
            {
                string text = line.ToText();

                if (text.Length == 0)
                {
                    continue;
                }

                sb.AppendLine(text);
            }

            return sb.ToString().TrimEnd('\r', '\n');
        }

        /// <summary>
        /// 生成 pdfplumber <c>extract_text(layout=True)</c> 风格的版面文本：
        /// 按估算字宽把词换算成列号，再用空格补齐，尽量还原原始横向位置。
        /// </summary>
        /// <param name="lines">该页的行集合。</param>
        /// <returns>以 CRLF 分隔的多行文本（末尾不带换行）。</returns>
        public static string RenderLayout(IList<LayoutLine> lines)
        {
            if (lines == null || lines.Count == 0)
            {
                return string.Empty;
            }

            double charWidth = EstimateCharWidth(lines);
            double pageLeft = double.MaxValue;

            foreach (LayoutLine line in lines)
            {
                foreach (LayoutWord word in line.Words)
                {
                    if (word.Left < pageLeft)
                    {
                        pageLeft = word.Left;
                    }
                }
            }

            if (pageLeft == double.MaxValue)
            {
                pageLeft = 0;
            }

            StringBuilder sb = new StringBuilder();

            foreach (LayoutLine line in lines)
            {
                List<LayoutWord> words = line.GetSortedWords();

                if (words.Count == 0)
                {
                    continue;
                }

                StringBuilder lineBuilder = new StringBuilder();

                // 行首缩进：按词左边界与全页最左端的距离换算空格
                int indent = (int)Math.Round((words[0].Left - pageLeft) / charWidth);

                if (indent > 0)
                {
                    lineBuilder.Append(' ', indent);
                }

                lineBuilder.Append(words[0].Text);

                double previousRight = words[0].Right;

                for (int i = 1; i < words.Count; i++)
                {
                    LayoutWord word = words[i];

                    // 词间空格数 = 实际间隙 / 估算字宽，至少保留一个空格避免粘连
                    int spaces = (int)Math.Round((word.Left - previousRight) / charWidth);

                    if (spaces < 1)
                    {
                        spaces = 1;
                    }

                    lineBuilder.Append(' ', spaces);
                    lineBuilder.Append(word.Text);

                    previousRight = word.Right;
                }

                sb.AppendLine(lineBuilder.ToString().TrimEnd());
            }

            return sb.ToString().TrimEnd('\r', '\n');
        }

        /// <summary>
        /// 找出该页的表格块（供坐标比对使用）：
        /// 先按行间距切块，再按列锚点判定，行数 ≥ 2 且列数 ≥ 2 才视为表格。
        /// </summary>
        /// <param name="lines">该页的行集合。</param>
        /// <returns>表格块集合（含外接矩形与列锚点）；未识别到表格时返回空集合。</returns>
        public static List<LayoutBlock> FindTableBlocks(IList<LayoutLine> lines)
        {
            List<LayoutBlock> tables = new List<LayoutBlock>();

            if (lines == null || lines.Count == 0)
            {
                return tables;
            }

            double charWidth = EstimateCharWidth(lines);
            double lineHeight = EstimateLineHeight(lines);

            // 1) 按行间距把行切成若干“块”
            List<LayoutBlock> blocks = new List<LayoutBlock>();
            LayoutBlock currentBlock = null;
            LayoutLine previousLine = null;

            foreach (LayoutLine line in lines)
            {
                if (line.Words.Count == 0)
                {
                    continue;
                }

                if (previousLine != null && line.Top - previousLine.Bottom > lineHeight * TableRowGapRatio)
                {
                    currentBlock = null;
                }

                if (currentBlock == null)
                {
                    currentBlock = new LayoutBlock();
                    blocks.Add(currentBlock);
                }

                currentBlock.Add(line);
                previousLine = line;
            }

            // 2) 每个块识别列；行数 ≥ 2 且列数 ≥ 2 才视为表格
            foreach (LayoutBlock block in blocks)
            {
                List<double> columns = DetectColumns(block.Lines, charWidth);

                if (block.Lines.Count < 2 || columns.Count < 2)
                {
                    continue;
                }

                block.Columns = columns;
                tables.Add(block);
            }

            return tables;
        }

        /// <summary>
        /// 生成 pdfplumber <c>extract_tables()</c> 风格的表格文本：
        /// 先按行间距切块，再把块内文字按列锚点聚类成单元格，输出 <c>| a | b |</c> 形式。
        /// </summary>
        /// <param name="lines">该页的行集合。</param>
        /// <returns>表格文本行（表格之间以空行分隔）；未识别到表格时返回空集合。</returns>
        public static List<string> RenderTables(IList<LayoutLine> lines)
        {
            return RenderTables(FindTableBlocks(lines));
        }

        /// <summary>用已识别好的表格块生成 pdfplumber 风格的表格文本。</summary>
        /// <param name="tables">表格块集合。</param>
        /// <returns>表格文本行（表格之间以空行分隔）；未识别到表格时返回空集合。</returns>
        public static List<string> RenderTables(IList<LayoutBlock> tables)
        {
            List<string> output = new List<string>();

            if (tables == null || tables.Count == 0)
            {
                return output;
            }

            int tableIndex = 0;

            foreach (LayoutBlock block in tables)
            {
                List<double> columns = block.Columns ?? new List<double>();

                if (columns.Count == 0)
                {
                    continue;
                }

                tableIndex++;

                if (output.Count > 0)
                {
                    output.Add(string.Empty);
                }

                output.Add("[表格 " + tableIndex + "]");

                foreach (LayoutLine line in block.Lines)
                {
                    string[] cells = new string[columns.Count];

                    for (int i = 0; i < cells.Length; i++)
                    {
                        cells[i] = string.Empty;
                    }

                    foreach (LayoutWord word in line.GetSortedWords())
                    {
                        int columnIndex = FindColumn(columns, word);

                        cells[columnIndex] = cells[columnIndex].Length == 0
                            ? word.Text
                            : cells[columnIndex] + " " + word.Text;
                    }

                    StringBuilder sb = new StringBuilder("|");

                    for (int i = 0; i < cells.Length; i++)
                    {
                        sb.Append(' ');
                        sb.Append(cells[i]);
                        sb.Append(" |");
                    }

                    output.Add(sb.ToString());
                }
            }

            return output;
        }

        /// <summary>
        /// 生成“图上文字”结果（坐标比对判定）：
        /// 1) 页面有图片对象时：把词按“中心点落在哪个图片区域内”分组，逐图输出图内文字行，
        ///    已被判定为表格文字的行会跳过（表格文字见 _plu_table.txt，避免重复）；
        /// 2) 页面没有图片对象时：退化为“表格之外的文字”，保证矢量图页面也能得到有用结果。
        /// 表格文字按“表格块外接矩形 + 有效 X 范围”判定，与表格左右并排的图纸标注仍算图上文字。
        /// </summary>
        /// <param name="words">该页的所有词。</param>
        /// <param name="images">该页的图片区域（可为空集合）。</param>
        /// <param name="tableBlocks">该页已识别的表格块（用于排除表格文字，可为空集合）。</param>
        /// <returns>图上文字的输出行（含 [图 n] 区域信息与缩进两格的文字行）。</returns>
        public static List<string> RenderImageTexts(IList<LayoutWord> words, IList<LayoutImage> images,
            IList<LayoutBlock> tableBlocks)
        {
            List<string> output = new List<string>();

            if (words == null || words.Count == 0)
            {
                return output;
            }

            if (images == null || images.Count == 0)
            {
                AppendFallbackImageTexts(output, words, tableBlocks);
                return output;
            }

            // 每个词归属到“包含它的面积最小的图片”，重叠图片时取最具体的那张
            List<List<LayoutWord>> groups = new List<List<LayoutWord>>();

            for (int i = 0; i < images.Count; i++)
            {
                groups.Add(new List<LayoutWord>());
            }

            foreach (LayoutWord word in words)
            {
                int bestIndex = -1;
                double bestArea = double.MaxValue;

                for (int i = 0; i < images.Count; i++)
                {
                    LayoutImage image = images[i];

                    if (!image.Contains(word, HitTolerance))
                    {
                        continue;
                    }

                    if (image.Area < bestArea)
                    {
                        bestArea = image.Area;
                        bestIndex = i;
                    }
                }

                if (bestIndex >= 0)
                {
                    groups[bestIndex].Add(word);
                }
            }

            for (int i = 0; i < images.Count; i++)
            {
                LayoutImage image = images[i];

                if (output.Count > 0)
                {
                    output.Add(string.Empty);
                }

                output.Add(string.Format(CultureInfo.InvariantCulture,
                    "[图 {0}] 区域: x0={1:F2}, y0={2:F2}, x1={3:F2}, y1={4:F2} | 尺寸: {5:F2} x {6:F2} | 词数: {7}",
                    image.Index, image.Left, image.Top, image.Right, image.Bottom,
                    image.Width, image.Height, groups[i].Count));

                List<LayoutLine> imageLines = GetLines(groups[i]);
                int written = 0;

                foreach (LayoutLine line in imageLines)
                {
                    if (IsInsideTableBlock(line, tableBlocks))
                    {
                        continue;
                    }

                    string text = line.ToText();

                    if (text.Length == 0)
                    {
                        continue;
                    }

                    output.Add("  " + text);
                    written++;
                }

                if (written == 0)
                {
                    output.Add(groups[i].Count == 0
                        ? "  （图内没有文字）"
                        : "  （图内文字均属于表格，见 _plu_table.txt）");
                }
            }

            if (output.Count == 0)
            {
                output.Add("（未识别到图上文字）");
            }

            return output;
        }

        /// <summary>
        /// 页面没有图片对象时的退化输出：表格文字之外的文字行。
        /// 表格文字按“表格块外接矩形 + 有效 X 范围”逐词排除，因此与表格同高但排在表格外侧的
        /// 图纸标注（尺寸数字、部位记号等）不会被吞掉。
        /// </summary>
        private static void AppendFallbackImageTexts(List<string> output, IList<LayoutWord> words,
            IList<LayoutBlock> tableBlocks)
        {
            List<LayoutWord> outside = new List<LayoutWord>();

            foreach (LayoutWord word in words)
            {
                if (!IsInsideTableBlock(word, tableBlocks))
                {
                    outside.Add(word);
                }
            }

            List<LayoutLine> lines = GetLines(outside);

            if (lines.Count == 0)
            {
                output.Add("（本页没有图片对象，也没有表格之外的文字）");
                return;
            }

            output.Add(string.Format(CultureInfo.InvariantCulture,
                "（本页没有图片对象：按“表格之外的文字”输出，共 {0} 行 / {1} 词）", lines.Count, outside.Count));

            foreach (LayoutLine line in lines)
            {
                string text = line.ToText();

                if (text.Length > 0)
                {
                    output.Add("  " + text);
                }
            }
        }

        /// <summary>词是否属于某个表格块的文字（外接矩形 + 有效 X 范围判定）。</summary>
        private static bool IsInsideTableBlock(LayoutWord word, IList<LayoutBlock> tableBlocks)
        {
            if (tableBlocks == null || tableBlocks.Count == 0)
            {
                return false;
            }

            foreach (LayoutBlock block in tableBlocks)
            {
                if (block.ContainsTableText(word, HitTolerance))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>整行的词是否都属于同一个表格块（表格行不重复计入图上文字）。</summary>
        private static bool IsInsideTableBlock(LayoutLine line, IList<LayoutBlock> tableBlocks)
        {
            if (line == null || line.Words.Count == 0 || tableBlocks == null || tableBlocks.Count == 0)
            {
                return false;
            }

            foreach (LayoutBlock block in tableBlocks)
            {
                bool allInside = true;

                foreach (LayoutWord word in line.Words)
                {
                    if (!block.ContainsTableText(word, HitTolerance))
                    {
                        allInside = false;
                        break;
                    }
                }

                if (allInside)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>估算全页平均字宽（取“词宽 / 字符数”的中位数），用于版面空格对齐。</summary>
        private static double EstimateCharWidth(IList<LayoutLine> lines)
        {
            List<double> widths = new List<double>();

            foreach (LayoutLine line in lines)
            {
                foreach (LayoutWord word in line.Words)
                {
                    if (word.Text.Length <= 0)
                    {
                        continue;
                    }

                    double width = (word.Right - word.Left) / word.Text.Length;

                    if (width > 0)
                    {
                        widths.Add(width);
                    }
                }
            }

            if (widths.Count == 0)
            {
                return DefaultCharWidth;
            }

            widths.Sort();

            double median = widths[widths.Count / 2];

            return median > 0 ? median : DefaultCharWidth;
        }

        /// <summary>估算全页平均行高（中位数），用于表格分块。</summary>
        private static double EstimateLineHeight(IList<LayoutLine> lines)
        {
            List<double> heights = new List<double>();

            foreach (LayoutLine line in lines)
            {
                if (line.Height > 0)
                {
                    heights.Add(line.Height);
                }
            }

            if (heights.Count == 0)
            {
                return DefaultLineHeight;
            }

            heights.Sort();

            double median = heights[heights.Count / 2];

            return median > 0 ? median : DefaultLineHeight;
        }

        /// <summary>把一个块内所有词的左边界聚类成列锚点。</summary>
        private static List<double> DetectColumns(IList<LayoutLine> block, double charWidth)
        {
            double tolerance = charWidth * ColumnToleranceRatio;
            List<double> lefts = new List<double>();

            foreach (LayoutLine line in block)
            {
                foreach (LayoutWord word in line.GetSortedWords())
                {
                    lefts.Add(word.Left);
                }
            }

            lefts.Sort();

            List<double> columns = new List<double>();

            foreach (double left in lefts)
            {
                if (columns.Count == 0 || left - columns[columns.Count - 1] > tolerance)
                {
                    columns.Add(left);
                }
            }

            return columns;
        }

        /// <summary>找到词所属的列（与列锚点左边界距离最小者）。</summary>
        private static int FindColumn(IList<double> columns, LayoutWord word)
        {
            int bestIndex = 0;
            double bestDistance = double.MaxValue;

            for (int i = 0; i < columns.Count; i++)
            {
                double distance = Math.Abs(word.Left - columns[i]);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }
    }
}
