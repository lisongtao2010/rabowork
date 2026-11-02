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
                DumpLines(page, pageH, scale, wx1, wy1, wx2, wy2);
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

        private static void DumpLines(
            Page page, double pageH, double scale, double wx1, double wy1, double wx2, double wy2)
        {
            double tol = TolMm * PtPerMm;
            double minLine = MinLineMm * PtPerMm;

            var hor = new List<string>();
            var ver = new List<string>();

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
