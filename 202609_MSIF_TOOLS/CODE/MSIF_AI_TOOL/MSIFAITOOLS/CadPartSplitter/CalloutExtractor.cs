using System;
using System.Collections.Generic;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace CadPartSplitter
{
    /// <summary>
    /// 识别图纸上的「圈号标注」（①1、②2 …）。
    ///
    /// 难点：圆圈是 PDF 里的**图形**（矢量路径），不在文字层里，所以复制文字时只剩孤立数字。
    /// 解决思路（不依赖 OCR、不需要调像素阈值）：
    ///   ① 用 PdfPig 取该页的全部矢量路径（<c>page.ExperimentalAccess.Paths</c>，坐标已应用 CTM 变换，
    ///      与文字层的页面坐标同一口径）；
    ///   ② 把每条子路径扁平化成折线（四段贝塞尔圆、CAD 折线圆都能统一处理），
    ///      再判定是否为「整圆」：包围盒近正方 + 各点到外接中心的半径比接近 1 + 八个方位都被覆盖；
    ///   ③ 用文字层定位圆内的数字词，配成「圈 + 数字」；
    ///   ④ 按圆心落在哪张零件图内，给出绝对 / 局部 / 毫米坐标。
    ///
    /// 结果既写回 <see cref="PartInfo.Callouts"/>，也作为返回值供全局清单使用。
    /// 任何异常都只写日志、不中断分割流程。
    /// </summary>
    public static class CalloutExtractor
    {
        /// <summary>贝塞尔曲线扁平化时每段的等分采样数（8 段足够让半径比误差小于 1%）。</summary>
        private const int BezierSamples = 8;

        /// <summary>
        /// 识别一页上的全部圈号标注。
        /// </summary>
        /// <param name="pdfPath">PDF 路径。</param>
        /// <param name="pageIndex">页码（从 0 开始）。</param>
        /// <param name="imgWidth">渲染图像宽（像素，用于 PDF 坐标 → 像素换算）。</param>
        /// <param name="imgHeight">渲染图像高。</param>
        /// <param name="dpi">渲染 DPI（毫米换算与半径阈值用）。</param>
        /// <param name="parts">零件检测结果；圈号会按圆心归属到对应零件图。</param>
        /// <param name="opts">参数（开关、半径范围、日志回调）。</param>
        public static List<Callout> Extract(
            string pdfPath, int pageIndex, int imgWidth, int imgHeight,
            double dpi, List<PartInfo> parts, DetectorOptions opts)
        {
            var result = new List<Callout>();
            DetectorOptions o = opts ?? new DetectorOptions();
            Action<string> write = o.Log;

            if (!o.DetectCallouts)
            {
                write("[INFO] 圈号标注识别已关闭（DetectCallouts = false）。");
                return result;
            }

            List<PartInfo> partList = parts ?? new List<PartInfo>();

            try
            {
                using (var doc = PdfDocument.Open(pdfPath))
                {
                    Page page = doc.GetPage(pageIndex + 1);   // PdfPig 页码从 1 开始
                    double pageW = page.Width, pageH = page.Height;
                    if (pageW <= 0 || pageH <= 0 || imgWidth <= 0 || imgHeight <= 0)
                    {
                        write("[WARN] 页面尺寸或渲染尺寸无效，跳过圈号标注识别。");
                        return result;
                    }

                    double scaleX = imgWidth  / pageW;
                    double scaleY = imgHeight / pageH;
                    double mmPerPx = 25.4 / Math.Max(1.0, dpi);

                    List<double[]> circles;
                    try
                    {
                        circles = FindCircles(page, scaleX, scaleY, pageH, o);
                    }
                    catch (Exception ex)
                    {
                        write("[WARN] 读取 PDF 矢量路径失败，跳过圈号标注识别：" + ex.Message);
                        return result;
                    }

                    if (circles.Count == 0)
                    {
                        write("[INFO] 圈号标注：页面未发现圆形图形。");
                        return result;
                    }

                    // 圆内的文字（只取 1~3 位纯数字：日文图纸的圈号基本都符合）
                    List<NumberWord> numberWords = ReadNumberWords(page, pageH, scaleX, scaleY);
                    var used = new bool[numberWords.Count];
                    int matched = 0, skipped = 0;

                    foreach (double[] circle in circles)
                    {
                        int best = -1;
                        double bestDist = double.MaxValue;
                        for (int i = 0; i < numberWords.Count; i++)
                        {
                            if (used[i]) continue;

                            // 数字必须装得进圈：圆是圈号时，圈内数字高度约为半径的 0.7~1.0 倍；
                            // 大圆（孔、放大视图）里若恰好有数字，尺寸比例也对不上，可借此排除。
                            if (numberWords[i].H > circle[2] * 1.3) continue;
                            if (numberWords[i].W > circle[2] * 1.6) continue;

                            double dx = numberWords[i].Cx - circle[0];
                            double dy = numberWords[i].Cy - circle[1];
                            double d  = Math.Sqrt(dx * dx + dy * dy);
                            if (d > circle[2] * 0.95) continue;      // 词中心必须在圆内（留一点容差）
                            if (d < bestDist) { bestDist = d; best = i; }
                        }

                        if (best < 0) { skipped++; continue; }
                        used[best] = true;
                        matched++;

                        double cx = circle[0], cy = circle[1], r = circle[2];
                        var c = new Callout
                        {
                            Text    = numberWords[best].Text,
                            Number  = ParseNumber(numberWords[best].Text),
                            CenterX = Math.Round(cx, 1),
                            CenterY = Math.Round(cy, 1),
                            Radius  = Math.Round(r, 1),
                            PixelX  = (int)Math.Round(cx - r),
                            PixelY  = (int)Math.Round(cy - r),
                            PixelW  = (int)Math.Round(r * 2),
                            PixelH  = (int)Math.Round(r * 2),
                            PdfX_mm      = Math.Round(cx * mmPerPx, 2),
                            PdfY_mm      = Math.Round(cy * mmPerPx, 2),
                            PdfRadius_mm = Math.Round(r * mmPerPx, 2)
                        };
                        c.Display = Callout.Compose(c.Number, c.Text);

                        // 归属零件图：圆心落在哪个零件框内
                        foreach (var p in partList)
                        {
                            if (cx < p.PixelX || cx > p.PixelX + p.PixelW) continue;
                            if (cy < p.PixelY || cy > p.PixelY + p.PixelH) continue;
                            c.PartIndex    = p.Index;
                            c.PartFileName = p.FileName;
                            c.LocalX       = (int)Math.Round(cx - p.PixelX);
                            c.LocalY       = (int)Math.Round(cy - p.PixelY);
                            break;
                        }

                        result.Add(c);
                    }

                    // 按「零件 → 上→下、左→右」排序，便于人眼核对
                    result.Sort(ByPartThenPosition);

                    foreach (var c in result)
                    {
                        c.Display = Callout.Compose(c.Number, c.Text);
                        string where = string.IsNullOrEmpty(c.PartFileName) ? "（零件图外）" : c.PartFileName;
                        write("[CALLOUT] " + c.Display + "  " + where
                              + "  圆心(" + c.CenterX.ToString("0") + "," + c.CenterY.ToString("0") + ")"
                              + " r=" + c.Radius.ToString("0") + "px=" + c.PdfRadius_mm.ToString("0.##") + "mm"
                              + (c.PartIndex > 0 ? "  局部(" + c.LocalX + "," + c.LocalY + ")" : string.Empty));
                    }

                    int inParts = 0;
                    foreach (var c in result) if (c.PartIndex > 0) inParts++;

                    write("[INFO] 圈号标注：" + circles.Count + " 个圆 / 圈内数字 " + matched + " 个"
                          + (skipped > 0 ? "（" + skipped + " 个圆内无数字，已忽略）" : string.Empty)
                          + "，归属零件图 " + inParts + " 个"
                          + (result.Count > inParts ? "，零件图外 " + (result.Count - inParts) + " 个" : string.Empty));

                    foreach (var p in partList)
                    {
                        p.Callouts = new List<Callout>();
                        foreach (var c in result)
                            if (c.PartIndex == p.Index)
                                p.Callouts.Add(c);
                    }
                }
            }
            catch (Exception ex)
            {
                write("[WARN] 圈号标注识别失败（不影响分割结果）：" + ex.Message);
            }

            return result;
        }

        // ── 圈号 → 文字行回填 ─────────────────────────────────────────────────

        /// <summary>
        /// 把圈号标注回填到词级文字上：对每个圈，找出「中心落在圈内、文本与圈内数字相同」的那个词，
        /// 把它的 <see cref="PartText.CalloutDisplay"/> 设为 ①1 这类带圈形式，
        /// 之后 <see cref="PartTextExtractor.BuildLines"/> 拼行时就会在**原位置**输出带圈编号。
        ///
        /// 为什么按坐标找词、而不是做字符串替换：同一行里往往还有尺寸数字（如 2、10、30），
        /// 全局替换会把它们一起改掉；只有「词中心到圆心的距离 ≤ 0.95r 且文本相同」才能命中
        /// 被圈住的那一个（圈内数字中心与圆心偏差实测只有 1~2 px）。
        ///
        /// 尺寸约束与配对时一致（高 ≤ 1.3r、宽 ≤ 1.6r），避免把圈外的大号数字误当圈内数字。
        /// </summary>
        /// <param name="callouts">该零件图的圈号标注（<see cref="PartInfo.Callouts"/>）。</param>
        /// <param name="texts">对应的词级文字（<see cref="PartInfo.Texts"/>）。</param>
        /// <returns>成功标记的词数（正常应等于圈号数）。</returns>
        public static int MarkCalloutWords(List<Callout> callouts, List<PartText> texts)
        {
            if (callouts == null || callouts.Count == 0 || texts == null || texts.Count == 0)
                return 0;

            int marked = 0;

            foreach (var c in callouts)
            {
                if (c == null || string.IsNullOrEmpty(c.Text)) continue;

                double r = c.Radius;
                PartText best = null;
                double bestDist = double.MaxValue;

                foreach (var t in texts)
                {
                    if (t == null || string.IsNullOrEmpty(t.Text)) continue;
                    if (!string.Equals(t.Text.Trim(), c.Text.Trim(), StringComparison.Ordinal)) continue;

                    // 与「圈 + 数字」配对时的约束保持一致：数字必须装得进圈
                    if (t.PixelH > r * 1.3) continue;
                    if (t.PixelW > r * 1.6) continue;

                    double cx = t.PixelX + t.PixelW / 2.0;
                    double cy = t.PixelY + t.PixelH / 2.0;
                    double dx = cx - c.CenterX;
                    double dy = cy - c.CenterY;
                    double d  = Math.Sqrt(dx * dx + dy * dy);
                    if (d > r * 0.95) continue;
                    if (d < bestDist) { bestDist = d; best = t; }
                }

                if (best == null) continue;

                best.CalloutDisplay = string.IsNullOrEmpty(c.Display)
                    ? Callout.Compose(c.Number, c.Text)
                    : c.Display;
                marked++;
            }

            return marked;
        }

        // ── 圆检测 ────────────────────────────────────────────────────────────

        /// <summary>
        /// 从矢量路径中找出所有「整圆」，返回 {cx, cy, r}（页面像素，原点左上、Y 向下）。
        ///
        /// 判定条件（宁可漏检，也不要把圆弧 / 圆角 / 螺纹牙型当成圈号）：
        ///   · 扁平化后至少 6 个点（四段贝塞尔圆、CAD 折线圆都满足）；
        ///   · 包围盒近正方（宽高差 ≤ 10%）；
        ///   · 半径比 rMax / rMin ≤ 1 + <see cref="DetectorOptions.CalloutRadiusTolerance"/>；
        ///   · 八个方位都有点（排除圆弧、半圆、圆角）；
        ///   · 半径落在 CalloutMinRadiusMm ~ CalloutMaxRadiusMm（默认 0.8~12mm）。
        ///
        /// 同心的内外两圈（粗线圈会画两条路径）会被合并成一条。
        /// </summary>
        private static List<double[]> FindCircles(
            Page page, double scaleX, double scaleY, double pageH, DetectorOptions o)
        {
            var found = new List<double[]>();
            if (page.ExperimentalAccess == null)
                return found;

            double dpi  = Math.Max(1.0, o.RenderDpi);
            double minR = Math.Max(2.0, o.CalloutMinRadiusMm * dpi / 25.4);
            double maxR = Math.Max(minR + 1.0, o.CalloutMaxRadiusMm * dpi / 25.4);
            double tol  = 1.0 + Math.Max(0.02, o.CalloutRadiusTolerance);

            foreach (var path in page.ExperimentalAccess.Paths)
            {
                foreach (var sub in path)
                {
                    List<PdfPoint> pts = Flatten(sub, BezierSamples);
                    if (pts.Count < 6) continue;

                    double minX = double.MaxValue, maxX = -double.MaxValue;
                    double minY = double.MaxValue, maxY = -double.MaxValue;
                    foreach (var p in pts)
                    {
                        if (p.X < minX) minX = p.X;
                        if (p.X > maxX) maxX = p.X;
                        if (p.Y < minY) minY = p.Y;
                        if (p.Y > maxY) maxY = p.Y;
                    }

                    double w = maxX - minX, h = maxY - minY;
                    if (w <= 0 || h <= 0) continue;
                    if (Math.Min(w, h) / Math.Max(w, h) < 0.90) continue;   // 近正方

                    double cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
                    double rMin = double.MaxValue, rMax = 0;
                    var hit = new bool[8];

                    foreach (var p in pts)
                    {
                        double d = Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy));
                        if (d < rMin) rMin = d;
                        if (d > rMax) rMax = d;

                        int oct = (int)Math.Floor(((Math.Atan2(p.Y - cy, p.X - cx) + Math.PI) / (2 * Math.PI)) * 8);
                        if (oct < 0) oct = 0;
                        if (oct > 7) oct = 7;
                        hit[oct] = true;
                    }

                    if (rMin <= 0 || rMax / rMin > tol) continue;

                    int covered = 0;
                    foreach (bool b in hit) if (b) covered++;
                    if (covered < 7) continue;                              // 必须覆盖整圈

                    double rpx = rMax * scaleX;
                    if (rpx < minR || rpx > maxR) continue;

                    // PDF 用户坐标（原点左下、Y 向上）→ 页面像素（原点左上、Y 向下）
                    double px = cx * scaleX;
                    double py = (pageH - cy) * scaleY;
                    found.Add(new[] { px, py, rpx });
                }
            }

            // 去重：圆心距离小于较小半径的一半时视为同一个圈（保留半径较大的那个）
            var merged = new List<double[]>();
            foreach (var c in found)
            {
                int same = -1;
                for (int i = 0; i < merged.Count; i++)
                {
                    double dx = merged[i][0] - c[0], dy = merged[i][1] - c[1];
                    if (Math.Sqrt(dx * dx + dy * dy) < Math.Min(merged[i][2], c[2]) * 0.5) { same = i; break; }
                }
                if (same < 0) merged.Add(c);
                else if (c[2] > merged[same][2]) merged[same] = c;
            }

            return merged;
        }

        // ── 路径扁平化 ────────────────────────────────────────────────────────

        /// <summary>
        /// 把一条子路径扁平化成折线点列：直线取端点，贝塞尔曲线按 <paramref name="steps"/> 等分采样。
        /// CAD 导出的圆有两种写法——「四段三次贝塞尔」与「大量短直线折线」，扁平化后判定逻辑统一。
        /// </summary>
        private static List<PdfPoint> Flatten(PdfSubpath sub, int steps)
        {
            var pts = new List<PdfPoint>();
            if (sub == null) return pts;

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
                    for (int i = 1; i <= steps; i++)
                        pts.Add(CubicAt(cb.StartPoint, cb.FirstControlPoint, cb.SecondControlPoint,
                                        cb.EndPoint, i / (double)steps));
                }
                else if (c is PdfSubpath.QuadraticBezierCurve)
                {
                    var qb = (PdfSubpath.QuadraticBezierCurve)c;
                    for (int i = 1; i <= steps; i++)
                        pts.Add(QuadAt(qb.StartPoint, qb.ControlPoint, qb.EndPoint, i / (double)steps));
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

        /// <summary>三次贝塞尔曲线上 t 处的点。</summary>
        private static PdfPoint CubicAt(PdfPoint p0, PdfPoint p1, PdfPoint p2, PdfPoint p3, double t)
        {
            double u = 1 - t;
            double x = u * u * u * p0.X + 3 * u * u * t * p1.X + 3 * u * t * t * p2.X + t * t * t * p3.X;
            double y = u * u * u * p0.Y + 3 * u * u * t * p1.Y + 3 * u * t * t * p2.Y + t * t * t * p3.Y;
            return new PdfPoint(x, y);
        }

        /// <summary>二次贝塞尔曲线上 t 处的点。</summary>
        private static PdfPoint QuadAt(PdfPoint p0, PdfPoint p1, PdfPoint p2, double t)
        {
            double u = 1 - t;
            return new PdfPoint(u * u * p0.X + 2 * u * t * p1.X + t * t * p2.X,
                                u * u * p0.Y + 2 * u * t * p1.Y + t * t * p2.Y);
        }

        // ── 圈内数字词 ────────────────────────────────────────────────────────

        /// <summary>文字层里的一个「纯数字」词及其中心点（页面像素）。</summary>
        private class NumberWord
        {
            public string Text;
            public double Cx;
            public double Cy;
            public double W;
            public double H;
        }

        /// <summary>
        /// 取该页文字层中「1~3 位纯数字」的词，换算成页面像素中心坐标。
        /// 只保留数字：圈号是 ①②…，日文图纸里偶尔出现的字母标注不属于本功能范围。
        /// 同时返回词框宽高，供「数字必须装得进圈」的尺寸校验使用。
        /// </summary>
        private static List<NumberWord> ReadNumberWords(
            Page page, double pageH, double scaleX, double scaleY)
        {
            var list = new List<NumberWord>();

            foreach (var w in page.GetWords())
            {
                string text = (w.Text ?? string.Empty).Trim();
                if (!IsNumber(text)) continue;

                var bb = w.BoundingBox;
                double x  = bb.Left * scaleX;
                double y  = (pageH - bb.Top) * scaleY;
                double cw = (bb.Right - bb.Left) * scaleX;
                double ch = (bb.Top - bb.Bottom) * scaleY;
                if (cw <= 0 || ch <= 0) continue;

                list.Add(new NumberWord { Text = text, Cx = x + cw / 2, Cy = y + ch / 2, W = cw, H = ch });
            }

            return list;
        }

        /// <summary>是否为 1~3 位纯数字（圈内编号的典型形式）。</summary>
        private static bool IsNumber(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length > 3) return false;
            foreach (char ch in s)
                if (ch < '0' || ch > '9') return false;
            return true;
        }

        /// <summary>字符串转整数，失败返回 0。</summary>
        private static int ParseNumber(string s)
        {
            int v;
            return int.TryParse(s, out v) ? v : 0;
        }

        /// <summary>排序：先按所属零件（零件图外的排在最后），再按 Y、X（上→下、左→右）。</summary>
        private static int ByPartThenPosition(Callout a, Callout b)
        {
            int pa = a.PartIndex > 0 ? a.PartIndex : int.MaxValue;
            int pb = b.PartIndex > 0 ? b.PartIndex : int.MaxValue;
            if (pa != pb) return pa.CompareTo(pb);

            int dy = a.CenterY.CompareTo(b.CenterY);
            if (Math.Abs(a.CenterY - b.CenterY) > 8) return dy;
            return a.CenterX.CompareTo(b.CenterX);
        }
    }
}
