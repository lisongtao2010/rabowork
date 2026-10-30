using System.Collections.Generic;

namespace CadPartSplitter
{
    /// <summary>
    /// 单个零件图上的一段文字（PDF 文字层的一个词）及其坐标。
    ///
    /// 坐标有三套，均为「左上角原点、Y 向下」：
    ///   · Pixel*  ── 页面绝对像素坐标（与 PartInfo.Pixel* 同一坐标系，便于叠回整页）
    ///   · Local*  ── 相对所属零件图左上角的局部像素坐标（便于在裁切图上定位/画框）
    ///   · Pdf*_mm ── 页面毫米坐标（由像素 × 25.4 / DPI 换算）
    /// </summary>
    public class PartText
    {
        /// <summary>文字内容（按 PDF 词边界切分，已 Trim）。</summary>
        public string Text { get; set; }

        // ── 页面绝对像素坐标 ────────────────────────────────────────────────
        public int PixelX { get; set; }
        public int PixelY { get; set; }
        public int PixelW { get; set; }
        public int PixelH { get; set; }

        // ── 相对所属零件图的局部像素坐标 ────────────────────────────────────
        public int LocalX { get; set; }
        public int LocalY { get; set; }

        // ── 页面毫米坐标 ────────────────────────────────────────────────────
        public double PdfX_mm { get; set; }
        public double PdfY_mm { get; set; }
        public double PdfW_mm { get; set; }
        public double PdfH_mm { get; set; }
    }

    /// <summary>
    /// 由若干 <see cref="PartText"/> 按「同一行」合并而成的可读文本行，
    /// 只用于生成人眼可读的 <c>parts_text.txt</c>；JSON 里保存的仍是词级原始坐标。
    /// </summary>
    public class PartTextLine
    {
        /// <summary>整行文字（同一行内的词按 X 排序、按间距决定是否补空格）。</summary>
        public string Text { get; set; }

        // ── 该行所有词的并集外接框（页面绝对像素）──────────────────────────
        public int PixelX { get; set; }
        public int PixelY { get; set; }
        public int PixelW { get; set; }
        public int PixelH { get; set; }

        // ── 相对零件图左上角的局部像素坐标 ──────────────────────────────────
        public int LocalX { get; set; }
        public int LocalY { get; set; }

        /// <summary>该行由多少个词合并而来。</summary>
        public int WordCount { get; set; }
    }

    /// <summary>
    /// 表格中的一个单元格（由同一行内水平相邻的词合并而来），用于记录
    /// 「被分割部分以外」的表格文字。坐标同样是页面绝对像素 + 页面毫米。
    /// </summary>
    public class TableCell
    {
        /// <summary>推断列号（从 1 开始，按单元格左边缘聚类得到）。</summary>
        public int Column { get; set; }

        public string Text { get; set; }

        public int PixelX { get; set; }
        public int PixelY { get; set; }
        public int PixelW { get; set; }
        public int PixelH { get; set; }

        public double PdfX_mm { get; set; }
        public double PdfY_mm { get; set; }
        public double PdfW_mm { get; set; }
        public double PdfH_mm { get; set; }
    }

    /// <summary>表格的一行：同一水平线上的若干单元格。</summary>
    public class TableRow
    {
        /// <summary>行号（从 1 开始，按 Y 升序）。</summary>
        public int Index { get; set; }

        // 该行所有单元格的并集外接框（页面绝对像素）
        public int PixelX { get; set; }
        public int PixelY { get; set; }
        public int PixelW { get; set; }
        public int PixelH { get; set; }

        public List<TableCell> Cells { get; set; } = new List<TableCell>();
    }

    /// <summary>
    /// 一张表格 / 标题栏（按行间大垂直间隙切分）。只记录文字与坐标，
    /// 不做合并单元格、跨页等表格语义推断。
    /// </summary>
    public class TableBlock
    {
        public string Name { get; set; }

        // 该表所有单元格的并集外接框（页面绝对像素）
        public int PixelX { get; set; }
        public int PixelY { get; set; }
        public int PixelW { get; set; }
        public int PixelH { get; set; }

        public List<TableRow> Rows { get; set; } = new List<TableRow>();

        /// <summary>单元格总数。</summary>
        public int CellCount
        {
            get
            {
                int n = 0;
                foreach (var r in Rows) n += r.Cells.Count;
                return n;
            }
        }
    }

    /// <summary>
    /// 一次「PDF 文字层」读取的完整结果：零件框内的词已写回 <see cref="PartInfo.Texts"/>，
    /// 零件框以外的词分为「表格 / 标题栏区」与「其它未归属」两组，表格部分另给出行列结构。
    /// </summary>
    public class PageTextResult
    {
        /// <summary>该页是否有文字层（位图输入恒为 false）。</summary>
        public bool HasTextLayer { get; set; }

        /// <summary>全页词数。</summary>
        public int WordCount { get; set; }

        /// <summary>表格区起点（= 零件检测时的右侧遮蔽线 X，-1 表示未遮蔽）。</summary>
        public int TableStartX { get; set; }

        public int ImageWidth  { get; set; }
        public int ImageHeight { get; set; }

        /// <summary>落在表格 / 标题栏区（X ≥ TableStartX）的词。</summary>
        public List<PartText> TableWords { get; set; } = new List<PartText>();

        /// <summary>零件框以外、且不在表格区的词（页边注记等）。</summary>
        public List<PartText> OtherWords { get; set; } = new List<PartText>();

        /// <summary>表格区按行 / 单元格组织后的结构。</summary>
        public List<TableBlock> Tables { get; set; } = new List<TableBlock>();

        /// <summary>零件框以外的词总数。</summary>
        public int OutsideWordCount
        {
            get { return TableWords.Count + OtherWords.Count; }
        }
    }
}
