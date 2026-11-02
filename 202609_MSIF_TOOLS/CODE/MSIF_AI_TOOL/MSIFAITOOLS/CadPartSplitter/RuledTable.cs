using System.Collections.Generic;

namespace CadPartSplitter
{
    /// <summary>
    /// 有线表格（单元格四面都有框线）里的一个单元格。
    ///
    /// 坐标三套口径与项目其它文件一致，均为「左上原点、Y 向下」：
    ///   · Pixel*  ── 页面绝对像素坐标（与 <see cref="PartInfo.Pixel*"/>、<see cref="TableBlock"/> 同一坐标系）
    ///   · Pdf*_mm ── 页面毫米坐标（像素 × 25.4 / DPI）
    ///
    /// <see cref="Row"/> / <see cref="Column"/> 由框线本身决定（不是按文字猜出来的列号），
    /// 所以合并单元格也能给出正确的跨度：<see cref="RowSpan"/> / <see cref="ColumnSpan"/>。
    /// </summary>
    public class RuledTableCell
    {
        /// <summary>行号（从 1 开始，自上而下）。</summary>
        public int Row { get; set; }

        /// <summary>列号（从 1 开始，自左而右）。</summary>
        public int Column { get; set; }

        /// <summary>纵向跨几行（合并单元格 &gt; 1）。</summary>
        public int RowSpan { get; set; } = 1;

        /// <summary>横向跨几列（合并单元格 &gt; 1）。</summary>
        public int ColumnSpan { get; set; } = 1;

        /// <summary>单元格内的文字（多个词按阅读顺序用空格连接；空单元格为空串）。</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>组成该单元格文字的词数（0 表示空单元格）。</summary>
        public int WordCount { get; set; }

        // ── 页面绝对像素坐标 ────────────────────────────────────────────────
        public int PixelX { get; set; }
        public int PixelY { get; set; }
        public int PixelW { get; set; }
        public int PixelH { get; set; }

        // ── 页面毫米坐标 ────────────────────────────────────────────────────
        public double PdfX_mm { get; set; }
        public double PdfY_mm { get; set; }
        public double PdfW_mm { get; set; }
        public double PdfH_mm { get; set; }
    }

    /// <summary>有线表格的一行：同一行边界带内的所有单元格（按列号排序）。</summary>
    public class RuledTableRow
    {
        /// <summary>行号（从 1 开始，自上而下）。</summary>
        public int Index { get; set; }

        // 该行所有单元格的并集外接框（页面绝对像素）
        public int PixelX { get; set; }
        public int PixelY { get; set; }
        public int PixelW { get; set; }
        public int PixelH { get; set; }

        public List<RuledTableCell> Cells { get; set; } = new List<RuledTableCell>();
    }

    /// <summary>
    /// 一张「有线表格」：由 PDF 矢量路径里的横 / 竖框线围出来的连续单元格集合
    /// （<see cref="RuledTableExtractor"/> 产出）。
    ///
    /// 与 <see cref="TableBlock"/> 的区别：<see cref="TableBlock"/> 是按文字间距**推断**的行列，
    /// 只用于记录「表格区里有哪些词」；本类则完全由框线决定行列，
    /// 单元格文字按「词中心落在哪个格子」填进去，因此合并单元格、空单元格、
    /// 甚至只有框线没有文字的表题欄都能如实还原。
    /// </summary>
    public class RuledTable
    {
        /// <summary>表名（<c>table_1</c>、<c>table_2</c> …，按位置自上而下、自左而右编号）。</summary>
        public string Name { get; set; }

        // ── 该表所有单元格的并集外接框（页面绝对像素 / 毫米）────────────────
        public int PixelX { get; set; }
        public int PixelY { get; set; }
        public int PixelW { get; set; }
        public int PixelH { get; set; }

        public double PdfX_mm { get; set; }
        public double PdfY_mm { get; set; }
        public double PdfW_mm { get; set; }
        public double PdfH_mm { get; set; }

        /// <summary>行数（由横向框线的边界数决定）。</summary>
        public int RowCount { get; set; }

        /// <summary>列数（由纵向框线的边界数决定）。</summary>
        public int ColumnCount { get; set; }

        /// <summary>按行组织的单元格（每行内按列号升序）。</summary>
        public List<RuledTableRow> Rows { get; set; } = new List<RuledTableRow>();

        /// <summary>单元格总数（含合并单元格，一个合并单元格算一个）。</summary>
        public int CellCount
        {
            get
            {
                int n = 0;
                foreach (var r in Rows) n += r.Cells.Count;
                return n;
            }
        }

        /// <summary>有文字的单元格数。</summary>
        public int TextCellCount
        {
            get
            {
                int n = 0;
                foreach (var r in Rows)
                    foreach (var c in r.Cells)
                        if (!string.IsNullOrEmpty(c.Text)) n++;
                return n;
            }
        }

        /// <summary>
        /// 把表展开成二维文本网格（<c>[行][列]</c>，均从 0 开始，与 <see cref="Rows"/> 顺序一致）。
        /// 合并单元格只在左上角写文字，其余被覆盖的格子留空 —— CSV / txt 输出都是这个形态。
        /// </summary>
        public string[][] ToGrid()
        {
            var grid = new string[RowCount][];
            for (int r = 0; r < RowCount; r++)
            {
                grid[r] = new string[ColumnCount];
                for (int c = 0; c < ColumnCount; c++)
                    grid[r][c] = string.Empty;
            }

            foreach (var row in Rows)
            {
                foreach (var cell in row.Cells)
                {
                    int r = cell.Row - 1;
                    int c = cell.Column - 1;
                    if (r < 0 || r >= RowCount || c < 0 || c >= ColumnCount)
                        continue;

                    grid[r][c] = cell.Text ?? string.Empty;
                }
            }

            return grid;
        }
    }
}
