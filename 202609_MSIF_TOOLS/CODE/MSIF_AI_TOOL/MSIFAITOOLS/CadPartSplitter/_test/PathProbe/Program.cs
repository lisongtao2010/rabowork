using System;
using System.Collections.Generic;
using System.Linq;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;

// 探针：分析 PDF 里「圈」的绘制方式（矢量路径 / 绘制指令），为圈号标注识别做可行性验证
// 用法： PathProbe <pdf> <page> <imgW> <imgH> [ops cxPx cyPx rPx | hist]
class Program
{
    static void Main(string[] args)
    {
        string pdf = args.Length > 0 ? args[0] : @"..\..\H310A20009.pdf";
        int pageNo = args.Length > 1 ? int.Parse(args[1]) : 1;
        int imgW = args.Length > 2 ? int.Parse(args[2]) : 3508;
        int imgH = args.Length > 3 ? int.Parse(args[3]) : 2479;
        string mode = args.Length > 4 ? args[4] : "circles";

        using var doc = PdfDocument.Open(pdf);
        var page = doc.GetPage(pageNo);
        double sx = imgW / page.Width, sy = imgH / page.Height;
        var words = page.GetWords().ToList();
        Console.WriteLine($"page {page.Width:0.##}x{page.Height:0.##} pt -> {imgW}x{imgH} px, scale {sx:0.####}x{sy:0.####}");

        if (mode == "ops")
        {
            double cxPx = args.Length > 5 ? double.Parse(args[5]) : 953;
            double cyPx = args.Length > 6 ? double.Parse(args[6]) : 205;
            double rPx  = args.Length > 7 ? double.Parse(args[7]) : 40;
            double cxPt = cxPx / sx, cyPt = page.Height - cyPx / sy, rPt = rPx / sx;
            Console.WriteLine($"query pt center=({cxPt:0.##},{cyPt:0.##}) r={rPt:0.##}  ops total={page.Operations.Count}");
            int n = 0;
            foreach (var op in page.Operations)
            {
                string s = op.ToString();
                var nums = Num(s);
                bool near = false;
                for (int i = 0; i + 1 < nums.Count; i += 2)
                    if (Math.Abs(nums[i] - cxPt) <= rPt && Math.Abs(nums[i + 1] - cyPt) <= rPt) { near = true; break; }
                if (!near) continue;
                n++;
                if (n <= 60) Console.WriteLine($"  {op.GetType().Name}: {s}");
            }
            Console.WriteLine($"ops near = {n}");
            return;
        }

        if (mode == "raw")
        {
            Console.WriteLine("-- first 25 ops --");
            foreach (var op in page.Operations.Take(25)) Console.WriteLine($"  {op.GetType().Name}: {op}");
            Console.WriteLine("-- first 3 paths --");
            foreach (var path in page.ExperimentalAccess.Paths.Take(3))
            {
                Console.WriteLine($"  path: subpaths={path.Count} stroked={path.IsStroked} filled={path.IsFilled} lw={path.LineWidth}");
                foreach (var sub in path.Take(3))
                {
                    Console.WriteLine($"    sub: cmds={sub.Commands.Count} closed={sub.Commands.Any(c => c is PdfSubpath.Close)}");
                    foreach (var c in sub.Commands.Take(6)) Console.WriteLine($"      {c.GetType().Name}: {c}");
                }
            }
            return;
        }

        // circles：把每条子路径扁平化成折线，判定是否为「整圆」，并列出圆内的文字
        var circles = new List<(double cx, double cy, double r, double ratio, int oct, int npts)>();
        var cmdKinds = new Dictionary<string, int>();
        int subTotal = 0;
        foreach (var path in page.ExperimentalAccess.Paths)
            foreach (var sub in path)
            {
                subTotal++;
                foreach (var c in sub.Commands)
                    cmdKinds[c.GetType().Name] = cmdKinds.TryGetValue(c.GetType().Name, out int v0) ? v0 + 1 : 1;

                var pts = Flatten(sub, 8);
                if (pts.Count < 6) continue;

                double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
                double minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
                double w = maxX - minX, h = maxY - minY;
                if (w <= 0 || h <= 0) continue;
                if (Math.Min(w, h) / Math.Max(w, h) < 0.90) continue;

                double cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
                double rMin = double.MaxValue, rMax = 0;
                foreach (var p in pts)
                {
                    double d = Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy));
                    rMin = Math.Min(rMin, d); rMax = Math.Max(rMax, d);
                }
                double ratio = rMin > 0 ? rMax / rMin : 999;
                var oct = new bool[8];
                foreach (var p in pts)
                {
                    double a = Math.Atan2(p.Y - cy, p.X - cx);
                    int i = (int)Math.Floor(((a + Math.PI) / (2 * Math.PI)) * 8) % 8;
                    if (i < 0) i = 0;
                    oct[i] = true;
                }
                int hit = oct.Count(b => b);
                double rpx = rMax * sx;
                if (!(ratio <= 1.15 && hit >= 7 && rpx >= 6 && rpx <= 60)) continue;
                circles.Add((cx * sx, (page.Height - cy) * sy, rpx, ratio, hit, pts.Count));
            }

        Console.WriteLine($"paths={page.ExperimentalAccess.Paths.Count} subpaths={subTotal} circles={circles.Count}");
        Console.WriteLine("-- command kinds --");
        foreach (var kv in cmdKinds.OrderByDescending(k => k.Value)) Console.WriteLine($"  {kv.Key}={kv.Value}");
        foreach (var c in circles.OrderBy(c => c.cy).ThenBy(c => c.cx))
        {
            var inside = new List<string>();
            foreach (var wr in words)
            {
                var bb = wr.BoundingBox;
                double wx = bb.Left * sx, wy = (page.Height - bb.Top) * sy;
                double cw = (bb.Right - bb.Left) * sx, ch = (bb.Top - bb.Bottom) * sy;
                double dx = (wx + cw / 2) - c.cx, dy = (wy + ch / 2) - c.cy;
                if (Math.Sqrt(dx * dx + dy * dy) <= c.r * 0.95) inside.Add(wr.Text);
            }
            Console.WriteLine($"  cx={c.cx:0} cy={c.cy:0} r={c.r:0}px ratio={c.ratio:0.00} oct={c.oct}/8 npts={c.npts}  words=[{string.Join("|", inside)}]");
        }
    }

    /// <summary>把子路径扁平化成折线点（贝塞尔按 steps 等分采样）。</summary>
    static List<PdfPoint> Flatten(PdfSubpath sub, int steps)
    {
        var pts = new List<PdfPoint>();
        foreach (var c in sub.Commands)
            switch (c)
            {
                case PdfSubpath.Move m: pts.Add(m.Location); break;
                case PdfSubpath.Line l: pts.Add(l.From); pts.Add(l.To); break;
                case PdfSubpath.CubicBezierCurve cb:
                    for (int i = 1; i <= steps; i++)
                        pts.Add(Cubic(cb.StartPoint, cb.FirstControlPoint, cb.SecondControlPoint, cb.EndPoint, i / (double)steps));
                    break;
                case PdfSubpath.QuadraticBezierCurve qb:
                    for (int i = 1; i <= steps; i++)
                        pts.Add(Quad(qb.StartPoint, qb.ControlPoint, qb.EndPoint, i / (double)steps));
                    break;
                case PdfSubpath.BezierCurve b: pts.Add(b.StartPoint); pts.Add(b.EndPoint); break;
            }
        return pts;
    }

    static PdfPoint Cubic(PdfPoint p0, PdfPoint p1, PdfPoint p2, PdfPoint p3, double t)
    {
        double u = 1 - t;
        double x = u * u * u * p0.X + 3 * u * u * t * p1.X + 3 * u * t * t * p2.X + t * t * t * p3.X;
        double y = u * u * u * p0.Y + 3 * u * u * t * p1.Y + 3 * u * t * t * p2.Y + t * t * t * p3.Y;
        return new PdfPoint(x, y);
    }

    static PdfPoint Quad(PdfPoint p0, PdfPoint p1, PdfPoint p2, double t)
    {
        double u = 1 - t;
        return new PdfPoint(u * u * p0.X + 2 * u * t * p1.X + t * t * p2.X,
                            u * u * p0.Y + 2 * u * t * p1.Y + t * t * p2.Y);
    }

    static List<double> Num(string s)
    {
        var list = new List<double>();
        int i = 0;
        while (i < s.Length)
        {
            if (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+')
            {
                int j = i + 1;
                while (j < s.Length && (char.IsDigit(s[j]) || s[j] == '.')) j++;
                if (double.TryParse(s.Substring(i, j - i), System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out double d)) list.Add(d);
                i = j;
            }
            else i++;
        }
        return list;
    }
}
