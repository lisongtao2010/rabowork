using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCvSharp;

namespace CadPartSplitter
{
    /// <summary>
    /// 核心零件检测与分割器。
    /// Pipeline：
    ///   1. 灰度化 + 自适应反向二值化（线条 = 白色前景）
    ///   2. 遮蔽右侧 BOM / 说明区域（可选，比例可调）
    ///   3. 形态学闭运算 + 膨胀（聚合同一零件的碎散图元）
    ///   4. Tree 轮廓提取 + 几何筛选 + 重叠合并
    ///   5. 按 Y 坐标排序，裁切导出 PNG，同时记录像素与 PDF 坐标
    /// </summary>
    public class PartDetector
    {
        private readonly DetectorOptions _opts;

        /// <summary>本次运行实际采用的右侧遮蔽起始列（像素）。由 <see cref="MaskRight"/> 写入。</summary>
        private int _maskStartX = -1;

        /// <summary>
        /// 本次运行实际采用的右侧遮蔽起始列（像素，-1 表示尚未计算）。
        /// 该列右侧即「表格 / 标题栏区」，文字记录模块据此把零件框以外的文字
        /// 分成「表格区」与「其它未归属」两类。
        /// </summary>
        public int MaskStartX
        {
            get { return _maskStartX; }
        }

        public PartDetector(DetectorOptions opts)
        {
            _opts = opts ?? throw new ArgumentNullException(nameof(opts));
        }

        /// <summary>
        /// 对输入图像执行完整的零件检测与分割，将结果写入输出目录。
        /// </summary>
        /// <param name="original">原始 BGR 图像（来自 PDF 渲染或直接读取），不会被修改。</param>
        /// <returns>检测并导出的零件信息列表（含像素坐标与 PDF 坐标）。</returns>
        public List<PartInfo> Detect(Mat original)
        {
            if (original == null || original.Empty())
                throw new ArgumentException("输入图像为空。");

            Directory.CreateDirectory(_opts.OutputDirectory);

            int imgWidth  = original.Cols;
            int imgHeight = original.Rows;
            _opts.Log($"[INFO] 图像尺寸：{imgWidth} × {imgHeight} px");

            SaveDebug(original, "debug_00_original.png");

            using (Mat gray   = ToGray(original))
            using (Mat binary = AdaptiveBinarize(gray))
            {
                SaveDebug(binary, "debug_01_binary.png");

                using (Mat masked = MaskRight(binary, _opts.RightMaskRatio))
                {
                    SaveDebug(masked, "debug_02_masked.png");

                    using (Mat dilated = DilateForGrouping(masked))
                    {
                        SaveDebug(dilated, "debug_03_dilated.png");

                        List<Rect> partRects = ExtractPartRects(dilated, imgWidth, imgHeight);

                        _opts.Log($"[INFO] 有效零件区域：{partRects.Count} 个");

                        if (partRects.Count == 0)
                        {
                            PrintNoResultHint();
                            return new List<PartInfo>();
                        }

                        return ExportParts(original, partRects, imgWidth, imgHeight);
                    }
                }
            }
        }

        // ── 私有：各流水线步骤 ────────────────────────────────────────────────

        private static Mat ToGray(Mat src)
        {
            if (src.Channels() == 1)
                return src.Clone();

            Mat gray = new Mat();
            Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
            return gray;
        }

        private Mat AdaptiveBinarize(Mat gray)
        {
            int blockSize = _opts.AdaptiveBlockSize;
            if (blockSize < 3)      blockSize = 3;
            if (blockSize % 2 == 0) blockSize++;

            Mat binary = new Mat();
            Cv2.AdaptiveThreshold(
                gray, binary,
                maxValue:       255,
                adaptiveMethod: AdaptiveThresholdTypes.GaussianC,
                thresholdType:  ThresholdTypes.BinaryInv,
                blockSize:      blockSize,
                c:              _opts.AdaptiveC);
            return binary;
        }

        /// <summary>
        /// 遮蔽右侧 BOM 表 / 标题栏区域。
        ///
        /// 优先使用「列墨迹」自动定位表格区左边界（<see cref="DetectorOptions.AutoMaskRightColumn"/>），
        /// 自动定位不可用或结果异常时退回 <paramref name="ratio"/> 固定比例。
        /// 实际采用的边界写入 <see cref="_maskStartX"/>，供轮廓筛选复用同一条边界。
        /// </summary>
        private Mat MaskRight(Mat binary, double ratio)
        {
            int maskStartX = DetermineMaskStartX(binary, ratio);
            _maskStartX = maskStartX;

            Mat result = binary.Clone();
            if (maskStartX < result.Cols)
            {
                Rect maskRect = new Rect(maskStartX, 0, result.Cols - maskStartX, result.Rows);
                result[maskRect].SetTo(Scalar.Black);
            }
            return result;
        }

        /// <summary>
        /// 计算右侧遮蔽的起始列：自动检测优先，失败时用固定比例兜底。
        /// </summary>
        private int DetermineMaskStartX(Mat binary, double ratio)
        {
            int width = binary.Cols;
            int fallbackX = ClampColumn((int)Math.Round(width * (1.0 - ratio)), width);

            if (!_opts.AutoMaskRightColumn)
            {
                _opts.Log($"[INFO] 右侧遮蔽：固定比例 {ratio:P0} → X={fallbackX}（自动检测已关闭）");
                return fallbackX;
            }

            int autoX = DetectTableColumnStart(binary);
            if (autoX < 0)
            {
                _opts.Log($"[INFO] 右侧遮蔽：未检出表格/标题栏列带，改用固定比例 {ratio:P0} → X={fallbackX}");
                return fallbackX;
            }

            double maskedRatio = (width - autoX) / (double)width;
            if (maskedRatio < _opts.AutoMaskMinMaskedRatio || maskedRatio > _opts.AutoMaskMaxMaskedRatio)
            {
                _opts.Log($"[INFO] 右侧遮蔽：自动检出 X={autoX}（占宽 {maskedRatio:P0}）超出允许范围 "
                          + $"{_opts.AutoMaskMinMaskedRatio:P0}~{_opts.AutoMaskMaxMaskedRatio:P0}，"
                          + $"改用固定比例 {ratio:P0} → X={fallbackX}");
                return fallbackX;
            }

            _opts.Log($"[INFO] 右侧遮蔽：自动检出表格/标题栏区 X={autoX}（遮蔽右侧 {maskedRatio:P0}，"
                      + $"较固定比例 {ratio:P0} 保留更多图纸内容）");
            return autoX;
        }

        /// <summary>
        /// 检测右侧表格 / 标题栏区的起始列（像素 X）；未检出返回 -1。
        ///
        /// 原理：
        ///   ① 统计每一列的前景像素数（列墨迹）；
        ///   ② 从最右侧有墨迹的列开始向左延伸，只要该列墨迹 ≥ 图高 × AutoMaskColumnInkRatio 就继续；
        ///   ③ 延伸中断处即「密实文字带」（表格线 + 文字）的左边界。
        /// 图纸中的零件即使紧挨着表格，两者之间也必然存在至少一列几乎空白的列（引线/尺寸线不会整列贯穿），
        /// 因此该边界不会吃进零件区。
        /// </summary>
        private int DetectTableColumnStart(Mat binary)
        {
            int width  = binary.Cols;
            int height = binary.Rows;
            if (width < 16 || height < 16) return -1;

            int[] ink = new int[width];
            for (int x = 0; x < width; x++)
            {
                using (Mat column = binary.Col(x))
                    ink[x] = Cv2.CountNonZero(column);
            }

            int lastInk = width - 1;
            while (lastInk > 0 && ink[lastInk] == 0) lastInk--;
            if (lastInk <= 0) return -1;

            int threshold = (int)Math.Round(height * _opts.AutoMaskColumnInkRatio);
            if (threshold < 8) threshold = 8;

            int x2 = lastInk;
            while (x2 > 0 && ink[x2] >= threshold) x2--;

            int start = x2 + 1;
            if (start >= lastInk) return -1;   // 完全没有密实列

            return start;
        }

        private static int ClampColumn(int x, int width)
        {
            if (x < 0) return 0;
            if (x > width) return width;
            return x;
        }

        private Mat DilateForGrouping(Mat binary)
        {
            Size kernelSize = new Size(_opts.DilateKernelWidth, _opts.DilateKernelHeight);
            using (Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, kernelSize))
            {
                Mat result = new Mat();
                Cv2.MorphologyEx(binary, result, MorphTypes.Close, kernel,
                                 iterations: _opts.DilateIterations);
                Cv2.Dilate(result, result, kernel, iterations: 1);
                return result;
            }
        }

        /// <summary>
        /// 提取轮廓（Tree 模式避免图纸外框吞噬内部零件），过滤后返回有效矩形列表。
        ///
        /// 过滤规则：
        ///   ① 轮廓面积 ≥ MinContourArea（去除孤立文字/噪点）
        ///   ② 外接框宽高 ≥ 最小宽高（去除细线）
        ///   ③ 外接框面积 / 全图面积 ≤ MaxBboxFillRatio（排除图纸外边框矩形）
        ///   ④ 外接框左边沿 < 遮蔽边界（排除完全落在 BOM 区的轮廓）
        ///      注意：用左边沿而非中心 X，允许跨越边界的宽零件被完整检测到；
        ///            边界取自 <see cref="MaskRight"/> 实际采用的 X（自动检测或固定比例）
        /// </summary>
        private List<Rect> ExtractPartRects(Mat dilated, int imgWidth, int imgHeight)
        {
            Point[][] contours;
            HierarchyIndex[] hierarchy;

            Cv2.FindContours(
                dilated, out contours, out hierarchy,
                RetrievalModes.Tree,
                ContourApproximationModes.ApproxSimple);

            // 与 MaskRight 使用完全相同的边界，避免自动遮蔽后仍按固定比例筛选
            int    rightBoundary = _maskStartX >= 0
                ? _maskStartX
                : (int)(imgWidth * (1.0 - _opts.RightMaskRatio));
            double fullArea      = (double)imgWidth * imgHeight;

            var  candidates        = new List<Rect>();
            bool warnedTruncation  = false;

            foreach (Point[] contour in contours)
            {
                double area = Cv2.ContourArea(contour);
                if (area < _opts.MinContourArea) continue;

                Rect bbox = Cv2.BoundingRect(contour);

                if (bbox.Width  < _opts.MinBoundingWidth)  continue;
                if (bbox.Height < _opts.MinBoundingHeight) continue;

                // 排除覆盖面积过大的图纸外框
                double bboxArea = bbox.Width * (double)bbox.Height;
                if (bboxArea / fullArea > _opts.MaxBboxFillRatio) continue;

                // 排除完全落在遮蔽区右侧的轮廓（用左边沿判断，允许跨边界的零件）
                if (bbox.Left >= rightBoundary) continue;

                Rect paddedRect = AddPadding(bbox, _opts.Padding, imgWidth, imgHeight);
                candidates.Add(paddedRect);

                if (!warnedTruncation && bbox.Right + _opts.Padding >= rightBoundary)
                {
                    warnedTruncation = true;
                    _opts.Log($"[WARN] 有零件延伸至遮蔽边界 X={rightBoundary}，右侧可能被裁断；" +
                              "可减小「右侧遮蔽」比例后重试。");
                }
            }

            _opts.Log($"[INFO] 轮廓筛选后候选区域：{candidates.Count} 个（合并前）");

            return MergeOverlappingRects(candidates, _opts.MergeOverlapThreshold);
        }

        private static Rect AddPadding(Rect r, int padding, int imgW, int imgH)
        {
            int x      = Math.Max(0,    r.X      - padding);
            int y      = Math.Max(0,    r.Y      - padding);
            int right  = Math.Min(imgW, r.Right  + padding);
            int bottom = Math.Min(imgH, r.Bottom + padding);
            return new Rect(x, y, right - x, bottom - y);
        }

        private static List<Rect> MergeOverlappingRects(List<Rect> rects, double overlapThreshold)
        {
            if (rects.Count == 0) return rects;

            var merged = rects.OrderBy(r => r.Y).ThenBy(r => r.X).ToList();

            bool anyMerge = true;
            while (anyMerge)
            {
                anyMerge = false;
                for (int i = 0; i < merged.Count; i++)
                {
                    for (int j = i + 1; j < merged.Count; j++)
                    {
                        Rect inter = Rect.Intersect(merged[i], merged[j]);
                        if (inter.Width <= 0 || inter.Height <= 0) continue;

                        double interArea = inter.Width  * (double)inter.Height;
                        double areaI     = merged[i].Width * (double)merged[i].Height;
                        double areaJ     = merged[j].Width * (double)merged[j].Height;

                        if (interArea / Math.Min(areaI, areaJ) > overlapThreshold)
                        {
                            merged[i] = UnionRect(merged[i], merged[j]);
                            merged.RemoveAt(j);
                            j--;
                            anyMerge = true;
                        }
                    }
                }
            }

            return merged;
        }

        private static Rect UnionRect(Rect a, Rect b)
        {
            int x      = Math.Min(a.X,      b.X);
            int y      = Math.Min(a.Y,      b.Y);
            int right  = Math.Max(a.Right,  b.Right);
            int bottom = Math.Max(a.Bottom, b.Bottom);
            return new Rect(x, y, right - x, bottom - y);
        }

        /// <summary>
        /// 按 Y 坐标排序，裁切每个零件并保存 PNG；同时计算 PDF 坐标（mm）。
        /// PDF 坐标以页面左上角为原点，与图像坐标轴方向一致。
        /// </summary>
        private List<PartInfo> ExportParts(Mat original, List<Rect> partRects,
                                           int imgWidth, int imgHeight)
        {
            double mmPerPx = 25.4 / _opts.RenderDpi;
            var result     = new List<PartInfo>();
            var sorted     = partRects.OrderBy(r => r.Y).ToList();

            for (int i = 0; i < sorted.Count; i++)
            {
                Rect   roi  = sorted[i];
                string name = $"Part_{i + 1:D2}.png";
                string path = Path.Combine(_opts.OutputDirectory, name);

                using (Mat crop = new Mat(original, roi))
                {
                    bool ok = crop.SaveImage(path);
                    if (!ok)
                    {
                        _opts.Log($"[WARN] 保存失败：{path}");
                        continue;
                    }
                    _opts.Log($"[SAVE] {name}  ({roi.Width}×{roi.Height} px  Y={roi.Y})");
                }

                result.Add(new PartInfo
                {
                    Index    = i + 1,
                    FileName = name,
                    PixelX   = roi.X,
                    PixelY   = roi.Y,
                    PixelW   = roi.Width,
                    PixelH   = roi.Height,
                    PdfX_mm  = roi.X      * mmPerPx,
                    PdfY_mm  = roi.Y      * mmPerPx,
                    PdfW_mm  = roi.Width  * mmPerPx,
                    PdfH_mm  = roi.Height * mmPerPx
                });
            }

            return result;
        }

        private void SaveDebug(Mat img, string filename)
        {
            if (!_opts.SaveDebugImages) return;
            img.SaveImage(Path.Combine(_opts.OutputDirectory, filename));
        }

        private void PrintNoResultHint()
        {
            _opts.Log("[WARN] 未检测到任何有效零件。请逐项检查：");
            _opts.Log("       ① 保存调试图后查看 debug_02_masked.png：右侧表格区遮蔽线是否盖住了零件" +
                      "（关闭「自动遮蔽」并减小 RightMaskRatio 到 0.40 试试）");
            _opts.Log("       ② MinContourArea 是否设置过高（尝试降至 10000）");
            _opts.Log("       ③ 膨胀核是否过大导致整页粘连成一块（减小 DilateKernelWidth/Height）");
            _opts.Log("       ④ 开启 SaveDebugImages=true 观察中间处理结果");
        }
    }
}
