namespace CadPartSplitter
{
    using System.Collections.Generic;

    /// <summary>
    /// 单个零件的检测结果：渲染图像中的像素坐标 + 对应 PDF 页面坐标（mm，从页面左上角计），
    /// 以及该零件图范围内的文字（PDF 文字层，词级坐标）。
    /// </summary>
    public class PartInfo
    {
        public int    Index   { get; set; }
        public string FileName { get; set; }

        // ── 渲染图像坐标（像素，左上角原点）────────────────────────────────
        public int PixelX { get; set; }
        public int PixelY { get; set; }
        public int PixelW { get; set; }
        public int PixelH { get; set; }

        // ── PDF 页面坐标（mm，左上角原点，与图像坐标轴方向一致）────────────
        public double PdfX_mm { get; set; }
        public double PdfY_mm { get; set; }
        public double PdfW_mm { get; set; }
        public double PdfH_mm { get; set; }

        // ── 该零件图上的文字（位图输入无文字层时为空列表）──────────────────
        public List<PartText> Texts { get; set; } = new List<PartText>();
    }
}
