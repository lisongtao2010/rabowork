using System;

namespace CadPartSplitter
{
    /// <summary>
    /// 图纸上的「圈号标注」：一个矢量圆 + 圆内一个数字组成的引出编号（①1、②2 …）。
    ///
    /// 为什么需要它：圆圈是 PDF 里的图形（矢量路径），不属于文字层，
    /// 单独复制文字时圈会丢失，只剩一个孤立数字（还会和其它数字混淆）。
    /// 本类把「圆 + 圆内数字」配成一条记录，输出 ①、② 这类可直接使用的编号，
    /// 并给出圆心 / 半径 / 坐标，便于把编号贴回原图位置。
    ///
    /// 坐标口径与 <see cref="PartText"/> 一致：页面绝对像素（左上原点、Y 向下）
    /// + 页面毫米 + 相对所属零件图的局部像素。
    /// </summary>
    public class Callout
    {
        /// <summary>圈内文字（PDF 文字层的原始词，如 "1" / "10"）。</summary>
        public string Text { get; set; }

        /// <summary>圈内数字；无法解析为整数时为 0。</summary>
        public int Number { get; set; }

        /// <summary>圈 + 圈内数字的组合显示形式（如 "①1"）；号码无法识别时为 "(文本)"。</summary>
        public string Display { get; set; }

        // ── 圆（页面绝对像素）────────────────────────────────────────────────
        public double CenterX { get; set; }
        public double CenterY { get; set; }
        public double Radius { get; set; }

        public int PixelX { get; set; }
        public int PixelY { get; set; }
        public int PixelW { get; set; }
        public int PixelH { get; set; }

        // ── 页面毫米坐标 ─────────────────────────────────────────────────────
        public double PdfX_mm { get; set; }
        public double PdfY_mm { get; set; }
        public double PdfRadius_mm { get; set; }

        // ── 归属零件图 ───────────────────────────────────────────────────────
        /// <summary>所属零件序号（= <see cref="PartInfo.Index"/>）；0 表示不在任何零件图范围内。</summary>
        public int PartIndex { get; set; }

        /// <summary>所属零件图文件名（如 Part_01.png）；未归属时为 null。</summary>
        public string PartFileName { get; set; }

        /// <summary>圆心相对所属零件图左上角的局部像素坐标。</summary>
        public int LocalX { get; set; }
        public int LocalY { get; set; }

        /// <summary>
        /// 把数字转成带圈字符：1~20 → ①…⑳，21~35 → ㉑…㉟，36~50 → ㊱…㊿，
        /// 其它（含 0）→ "(n)"。用于输出「圈 + 数字」而不是裸数字。
        /// </summary>
        public static string RingOf(int n)
        {
            if (n >= 1 && n <= 20)  return ((char)(0x2460 + n - 1)).ToString();
            if (n >= 21 && n <= 35) return ((char)(0x3251 + n - 21)).ToString();
            if (n >= 36 && n <= 50) return ((char)(0x32B1 + n - 36)).ToString();
            return "(" + n + ")";
        }

        /// <summary>「圈 + 圈内文字」的组合显示形式（①1 → "①1"，字母/异常时为 "(A)"）。</summary>
        public static string Compose(int number, string text)
        {
            if (number > 0)
                return RingOf(number) + text;

            string t = (text ?? string.Empty).Trim();
            return t.Length == 0 ? "○" : "(" + t + ")";
        }
    }
}
