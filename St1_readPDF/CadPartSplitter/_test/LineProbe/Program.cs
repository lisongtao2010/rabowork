using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace CadPartSplitter
{
    /// <summary>
    /// 框线诊断探针：把「某个像素窗口里到底有哪些矢量路径、哪些横线 / 竖线」原样打出来。
    /// 用来查「表格多出一行 / 文字跑到别的行」这类几何问题 —— 先看清 PDF 里那几条线是
    /// 描边线、填充细矩形还是一整块填充面，再决定怎么改算法。
    ///
    /// 用法：
    ///   dotnet run --project _test\LineProbe -- &lt;pdf&gt; [页码(1起)] [DPI] [x1 y1 x2 y2（像素窗口）]
    ///
    /// 例（表題欄 table_05 的第 4~6 行那一带）：
    ///   dotnet run --project _test\LineProbe -- H310U10004.pdf 1 300 7600 5500 9200 5650
    /// </summary>
    internal static class Program
    {
        private const double PtPerMm = 72.0 / 25.4;
        private const double TolMm = 0.6;
        private const double MinLineMm = 2.0;

        /// <summary>输出过滤：包围盒长短边都小于这个像素数的路径（= 文字笔画）不打印。</summary>
        private const double MinSpanPx = 60;

        private static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("用法: LineProbe <pdf> [页码(1起)] [DPI] [x1 y1 x2 y2（像素窗口）]");
                return 1;
            }

            string pdf = System.IO.Path.GetFullPath(args[0]);
            if (!System.IO.File.Exists(pdf))
            {
                Console.WriteLine("[ERROR] 文件不存在: " + pdf);
                return 1;
            }

            int pageNo = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 1;
            double dpi = args.Length > 2 ? double.Parse(args[2], CultureInfo.InvariantCulture) : 300.0;

            double wx1 = args.Length > 6 ? double.Parse(args[3], CultureInfo.InvariantCulture) : 0;
            double wy1 = args.Length > 6 ? double.Parse(args[4], CultureInfo.InvariantCulture) : 0;
            double wx2 = args.Length > 6 ? double.Parse(args[5], CultureInfo.InvariantCulture) : double.MaxValue;
            double wy2 = args.Length > 6 ? double.Parse(args[6], CultureInfo.InvariantCulture) : double.MaxValue;
            double minSpan = args.Length > 7 ? double.Parse(args[7], CultureInfo.InvariantCulture) : MinSpanPx;

            using (var doc = PdfDocument.Open(pdf))
            {
                Page page = doc.GetPage(pageNo);

                double pageH = page.Height;
                double scale = dpi / 72.0;

                Console.WriteLine("[页面] " + page.Width.ToString("F2") + " x " + page.Height.ToString("F2")
                                  + " pt  →  px @" + dpi.ToString("0") + " DPI（1pt = "
                                  + scale.ToString("0.####") + "px）");
                Console.WriteLine("[窗口] px x∈[" + wx1 + "," + wx2 + "] y∈[" + wy1 + "," + wy2 + "]");

                DumpPaths(page, pageH, scale, wx1, wy1, wx2, wy2, minSpan);
                List<HLine> hor = DumpLines(page, pageH, scale, wx1, wy1, wx2, wy2);
                DumpWords(page, pageH, scale, wx1, wy1, wx2, wy2, hor);
            }

            return 0;
        }

        // ── 原始路径 ─────────────────────────────────────────────────────────

        private static void DumpPaths(
            Page page, double pageH, double scale, double wx1, double wy1, double wx2, double wy2,
            double minSpan)
        {
            Console.WriteLine();
            Console.WriteLine("== 落在窗口里的矢量路径 ==");

            int index = 0;

            foreach (var path in page.ExperimentalAccess.Paths)
            {
                index++;

                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;
                var subs = new List<string>();

                foreach (var sub in path)
                {
                    List<PdfPoint> pts = Flatten(sub);

                    foreach (PdfPoint p in pts)
                    {
                        double px = p.X * scale;
                        double py = (pageH - p.Y) * scale;

                        if (px < minX) minX = px;
                        if (py < minY) minY = py;
                        if (px > maxX) maxX = px;
                        if (py > maxY) maxY = py;
                    }

                    subs.Add(DescribePoints(pts, pageH, scale));
                }

                if (minX > maxX) continue;

                // 文字轮廓（本图把文字也导出成了填充路径）会淹没输出：只留够长的路径
                if (maxX - minX < minSpan && maxY - minY < minSpan) continue;

                bool hit = maxX >= wx1 && minX <= wx2 && maxY >= wy1 && minY <= wy2;
                if (!hit) continue;

                Console.WriteLine();
                Console.WriteLine("#path " + index
                                  + "  filled=" + path.IsFilled
                                  + " stroked=" + path.IsStroked
                                  + " lineWidth=" + path.LineWidth.ToString("0.###")
                                  + "  stroke=" + ColorText(path.StrokeColor)
                                  + " fill=" + ColorText(path.FillColor)
                                  + "  subpaths=" + subs.Count
                                  + "  bboxPx=(" + minX.ToString("0.#") + "," + minY.ToString("0.#")
                                  + ")-(" + maxX.ToString("0.#") + "," + maxY.ToString("0.#") + ")"
                                  + "  w=" + (maxX - minX).ToString("0.#")
                                  + " h=" + (maxY - minY).ToString("0.#"));

                for (int i = 0; i < subs.Count; i++)
                    Console.WriteLine("    sub[" + i + "] " + subs[i]);
            }
        }

        // ── 横线 / 竖线（判定与 RuledTableExtractor 完全一致） ────────────────

        /// <summary>一条水平线（页面像素坐标：y 自上而下算）。</summary>
        private class HLine
        {
            public double Y;
            public double X1;
            public double X2;
        }

        /// <summary>打印窗口里的横线 / 竖线，并把全部横线返回给「词 ↔ 线」对照用。</summary>
        private static List<HLine> DumpLines(
            Page page, double pageH, double scale, double wx1, double wy1, double wx2, double wy2)
        {
            double tol = TolMm * PtPerMm;
            double minLine = MinLineMm * PtPerMm;

            var hor = new List<string>();
            var ver = new List<string>();
            var allHor = new List<HLine>();

            foreach (var path in page.ExperimentalAccess.Paths)
            {
                foreach (var sub in path)
                {
                    List<PdfPoint> pts = Flatten(sub);

                    for (int i = 1; i < pts.Count; i++)
                    {
                        PdfPoint p = pts[i - 1], q = pts[i];
                        double dx = Math.Abs(q.X - p.X);
                        double dy = Math.Abs(q.Y - p.Y);

                        if (dy <= tol && dx >= minLine)
                        {
                            double y = (pageH - (p.Y + q.Y) / 2.0) * scale;
                            double xa = Math.Min(p.X, q.X) * scale;
                            double xb = Math.Max(p.X, q.X) * scale;

                            allHor.Add(new HLine { Y = y, X1 = xa, X2 = xb });

                            if (y < wy1 || y > wy2 || xb < wx1 || xa > wx2) continue;

                            hor.Add("y=" + y.ToString("0.#") + "  x=" + xa.ToString("0.#") + "~" + xb.ToString("0.#")
                                    + "  len=" + (xb - xa).ToString("0.#") + "px ("
                                    + ((xb - xa) * 25.4 / (scale * 72.0)).ToString("0.##") + "mm)"
                                    + (path.IsFilled ? "  [filled]" : string.Empty)
                                    + (path.IsStroked ? "  [stroked]" : string.Empty));
                        }
                        else if (dx <= tol && dy >= minLine)
                        {
                            double x = p.X * scale;
                            double ya = (pageH - Math.Max(p.Y, q.Y)) * scale;
                            double yb = (pageH - Math.Min(p.Y, q.Y)) * scale;

                            if (x < wx1 || x > wx2 || yb < wy1 || ya > wy2) continue;

                            ver.Add("x=" + x.ToString("0.#") + "  y=" + ya.ToString("0.#") + "~" + yb.ToString("0.#")
                                    + "  len=" + (yb - ya).ToString("0.#") + "px"
                                    + (path.IsFilled ? "  [filled]" : string.Empty)
                                    + (path.IsStroked ? "  [stroked]" : string.Empty));
                        }
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine("== 窗口里的横线（" + hor.Count + " 条） ==");
            foreach (string s in hor) Console.WriteLine("  " + s);

            Console.WriteLine();
            Console.WriteLine("== 窗口里的竖线（" + ver.Count + " 条） ==");
            foreach (string s in ver) Console.WriteLine("  " + s);

            return allHor;
        }

        // ── 词 ↔ 横线 对照 ──────────────────────────────────────────────────

        /// <summary>
        /// 打印窗口里的词，并列出「y 落在该词字身高度之内、水平方向与词有重叠」的横线。
        ///
        /// 这类线就是 取り消し線（划掉整行的删除线）：`中` 表示落点在该词字身的中间一半
        /// （上下各让出 1/4 高度）—— 写字时下划线只会在字身底下、上划线只在字身顶上，
        /// 只有「划掉」才会从中间穿过。
        /// </summary>
        private static void DumpWords(
            Page page, double pageH, double scale, double wx1, double wy1, double wx2, double wy2,
            List<HLine> hor)
        {
            Console.WriteLine();
            Console.WriteLine("== 窗口里的词（← 后面是穿过它的横线）==");

            int count = 0;

            foreach (Word w in page.GetWords())
            {
                var bb = w.BoundingBox;

                double x1 = bb.Left * scale;
                double x2 = bb.Right * scale;
                double yTop = (pageH - bb.Top) * scale;      // 上边（像素 y 小）
                double yBot = (pageH - bb.Bottom) * scale;   // 下边（像素 y 大）

                if (x2 < wx1 || x1 > wx2 || yBot < wy1 || yTop > wy2) continue;
                if (x2 - x1 <= 0 || yBot - yTop <= 0) continue;

                count++;

                double h = yBot - yTop;
                double midA = yTop + h * 0.25;
                double midB = yBot - h * 0.25;

                var hits = new List<string>();

                foreach (var l in hor)
                {
                    if (l.Y <= yTop || l.Y >= yBot) continue;          // 不在字身高度之内
                    if (l.X2 < x1 || l.X1 > x2) continue;              // 水平方向不重叠

                    double cover = Math.Min(l.X2, x2) - Math.Max(l.X1, x1);

                    hits.Add("y=" + l.Y.ToString("0.#")
                             + ((l.Y >= midA && l.Y <= midB) ? "(中)" : "(边)")
                             + " x=" + l.X1.ToString("0") + "~" + l.X2.ToString("0")
                             + " 覆盖率=" + (cover / (x2 - x1)).ToString("0%"));
                }

                Console.WriteLine("  \"" + (w.Text ?? string.Empty) + "\""
                                  + "  x=" + x1.ToString("0.#") + "~" + x2.ToString("0.#")
                                  + "  y=" + yTop.ToString("0.#") + "~" + yBot.ToString("0.#")
                                  + "  h=" + h.ToString("0.#")
                                  + (hits.Count == 0
                                      ? string.Empty
                                      : "  ← " + hits.Count + " 条: " + string.Join(" / ", hits)));
            }

            Console.WriteLine("  （共 " + count + " 个词）");
        }

        // ── 工具 ─────────────────────────────────────────────────────────────

        private static string ColorText(object color)
        {
            if (color == null) return "-";

            try
            {
                // PdfPig 的颜色对象：能取到 RGB 就打 RGB，取不到就打类型名
                var m = color.GetType().GetMethod("ToRGBValues");
                if (m != null && m.GetParameters().Length == 0)
                {
                    object rgb = m.Invoke(color, null);
                    if (rgb != null) return rgb.ToString();
                }
            }
            catch
            {
                // 忽略，退回类型名
            }

            return color.GetType().Name;
        }

        private static List<PdfPoint> Flatten(PdfSubpath sub)
        {
            var pts = new List<PdfPoint>();
            if (sub == null) return pts;

            const int steps = 8;

            foreach (var c in sub.Commands)
            {
                if (c is PdfSubpath.Move)
                {
                    pts.Add(((PdfSubpath.Move)c).Location);
                }
                else if (c is PdfSubpath.Line)
                {
                    var line = (PdfSubpath.Line)c;
                    pts.Add(line.From);
                    pts.Add(line.To);
                }
                else if (c is PdfSubpath.CubicBezierCurve)
                {
                    var cb = (PdfSubpath.CubicBezierCurve)c;
                    for (int i = 1; i <= steps; i++) pts.Add(CubicAt(cb, i / (double)steps));
                }
                else if (c is PdfSubpath.QuadraticBezierCurve)
                {
                    var qb = (PdfSubpath.QuadraticBezierCurve)c;
                    for (int i = 1; i <= steps; i++) pts.Add(QuadAt(qb, i / (double)steps));
                }
                else if (c is PdfSubpath.BezierCurve)
                {
                    var b = (PdfSubpath.BezierCurve)c;
                    pts.Add(b.StartPoint);
                    pts.Add(b.EndPoint);
                }
            }

            return pts;
        }

        private static PdfPoint CubicAt(PdfSubpath.CubicBezierCurve cb, double t)
        {
            double u = 1 - t;
            PdfPoint p0 = cb.StartPoint, p1 = cb.FirstControlPoint, p2 = cb.SecondControlPoint, p3 = cb.EndPoint;

            return new PdfPoint(
                u * u * u * p0.X + 3 * u * u * t * p1.X + 3 * u * t * t * p2.X + t * t * t * p3.X,
                u * u * u * p0.Y + 3 * u * u * t * p1.Y + 3 * u * t * t * p2.Y + t * t * t * p3.Y);
        }

        private static PdfPoint QuadAt(PdfSubpath.QuadraticBezierCurve qb, double t)
        {
            double u = 1 - t;
            PdfPoint p0 = qb.StartPoint, p1 = qb.ControlPoint, p2 = qb.EndPoint;

            return new PdfPoint(
                u * u * p0.X + 2 * u * t * p1.X + t * t * p2.X,
                u * u * p0.Y + 2 * u * t * p1.Y + t * t * p2.Y);
        }

        private static string DescribePoints(List<PdfPoint> pts, double pageH, double scale)
        {
            if (pts.Count == 0) return "(空)";

            var sb = new StringBuilder();
            sb.Append("pts=").Append(pts.Count).Append("  ");

            for (int i = 0; i < pts.Count && i < 12; i++)
            {
                double px = pts[i].X * scale;
                double py = (pageH - pts[i].Y) * scale;

                if (i > 0) sb.Append(" → ");
                sb.Append("(").Append(px.ToString("0.#")).Append(",").Append(py.ToString("0.#")).Append(")");
            }

            if (pts.Count > 12) sb.Append(" …");

            return sb.ToString();
        }
    }
}
