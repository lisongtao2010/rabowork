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
    ///   2. 擦除四周图框（贯穿页边的长横线 / 长竖线），避免图框把内部视图粘成一块
    ///   3. 遮蔽表格区域：优先按「文字层推断出的表格块」精确遮蔽，否则整列遮蔽（BOM / 标题栏）
    ///   4. 形态学闭运算 + 膨胀（聚合同一零件的碎散图元）
    ///   5. Tree 轮廓提取 + 几何筛选 + 重叠合并
    ///   6. 按 Y 坐标排序，裁切导出 PNG，同时记录像素与 PDF 坐标
    /// </summary>
    public class PartDetector
    {
        private readonly DetectorOptions _opts;

        /// <summary>本次运行实际采用的右侧遮蔽起始列（像素）。由 <see cref="MaskRight"/> / <see cref="MaskTableAreas"/> 写入。</summary>
        private int _maskStartX = -1;

        /// <summary>true = 本次采用「表格块」精确遮蔽；false = 整列遮蔽。</summary>
        private bool _maskByBlocks;

        /// <summary>本次擦除的图框横线行号（供导出时把同样的线从原图上擦掉）。</summary>
        private readonly List<int> _frameRows = new List<int>();

        /// <summary>本次擦除的图框竖线列号（供导出时把同样的线从原图上擦掉）。</summary>
        private readonly List<int> _frameCols = new List<int>();

        /// <summary>
        /// 表格列带起点（「从右向左的密实列带」左边界）。块遮蔽时用它剔除
        /// 「整块都落在表格带里」的表格残片（块遮蔽只涂黑检出的表格矩形，
        /// 表格之间的连接线 / 空白行残留仍可能连成伪零件）。
        /// -1 表示未计算。
        /// </summary>
        private int _tableBandStartX = -1;

        /// <summary>
        /// 本次运行实际采用的右侧遮蔽起始列（像素，-1 表示尚未计算）。
        /// 该列右侧即「表格 / 标题栏区」，文字记录模块据此把零件框以外的文字
        /// 分成「表格区」与「其它未归属」两类。
        /// 块遮蔽时取「中心落在右半页的表格块」的最小左边界；
        /// 整列遮蔽时取遮蔽线本身（自动检测或固定比例）。
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
            return Detect(original, null);
        }

        /// <summary>
        /// 对输入图像执行完整的零件检测与分割，将结果写入输出目录。
        /// </summary>
        /// <param name="original">原始 BGR 图像（来自 PDF 渲染或直接读取），不会被修改。</param>
        /// <param name="tableAreas">
        /// 由文字层推断出的表格 / 标题栏矩形（页面像素）。非空时只遮蔽这些矩形，
        /// 其余像素原样保留；为 null 或空则退回「右侧整列遮蔽」。
        /// </param>
        /// <returns>检测并导出的零件信息列表（含像素坐标与 PDF 坐标）。</returns>
        public List<PartInfo> Detect(Mat original, IList<PageRect> tableAreas)
        {
            return Detect(original, tableAreas, null);
        }

        /// <summary>
        /// 对输入图像执行完整的零件检测与分割，将结果写入输出目录。
        /// </summary>
        /// <param name="original">原始 BGR 图像（来自 PDF 渲染或直接读取），不会被修改。</param>
        /// <param name="tableAreas">
        /// 由文字层推断出的表格 / 标题栏矩形（页面像素）。非空时只遮蔽这些矩形，
        /// 其余像素原样保留；为 null 或空则退回「右侧整列遮蔽」。
        /// </param>
        /// <param name="textBoxes">
        /// 整页「文字行」外接框（页面像素）。非空且开启 <see cref="DetectorOptions.DropTextHeavyParts"/> 时，
        /// 会丢弃「文字行合计面积占自身外框 ≥ <see cref="DetectorOptions.TextHeavyRatio"/>」的块
        /// （表題欄 / 変更履歴表等表格残片，不是零件图）。传入 null 表示不筛除。
        /// </param>
        /// <returns>检测并导出的零件信息列表（含像素坐标与 PDF 坐标）。</returns>
        public List<PartInfo> Detect(Mat original, IList<PageRect> tableAreas, IList<PageRect> textBoxes)
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
            using (Mat clean  = RemovePageFrameIfEnabled(binary))
            {
                SaveDebug(clean, "debug_01_binary.png");

                List<Rect> partRects = null;

                // ── 方式 A：按文字层推断出的表格块精确遮蔽 ──────────────────
                if (_opts.MaskTableBlocks && tableAreas != null && tableAreas.Count > 0)
                {
                    // 顺便算出「表格列带」左边界（只为剔除块遮蔽漏掉的表格残片）
                    _tableBandStartX = DetectTableColumnStart(clean);

                    using (Mat masked = MaskTableAreas(clean, tableAreas))
                    {
                        SaveDebug(masked, "debug_02_masked.png");
                        partRects = DilateAndExtract(masked, imgWidth, imgHeight);
                    }

                    if (partRects.Count == 0)
                    {
                        _opts.Log("[WARN] 按表格块遮蔽后未检出零件，回退为整列遮蔽重试。");
                        partRects     = null;
                        _maskStartX   = -1;
                        _maskByBlocks = false;
                    }
                }

                // ── 方式 B：右侧整列遮蔽（也是方式 A 的兜底）──────────────────
                if (partRects == null)
                {
                    using (Mat masked = MaskRight(clean, _opts.RightMaskRatio))
                    {
                        SaveDebug(masked, "debug_02_masked.png");
                        partRects = DilateAndExtract(masked, imgWidth, imgHeight);
                    }
                }

                _opts.Log($"[INFO] 有效零件区域：{partRects.Count} 个");

                // 表格残片（表題欄 / 変更履歴表）文字密、占比高：在导出 PNG 之前筛掉，
                // 这样输出文件编号仍然连续，也不会留下「只有一张表」的伪零件图。
                if (partRects.Count > 0 && _opts.DropTextHeavyParts && textBoxes != null && textBoxes.Count > 0)
                    partRects = DropTextHeavyRects(partRects, textBoxes);

                if (partRects.Count == 0)
                {
                    PrintNoResultHint();
                    return new List<PartInfo>();
                }

                // 导出裁切用的是原始彩色图：把图框线一并擦掉。
                // 否则输出图会带着图纸的左 / 上 / 下边框（右边框落在零件框外），
                // 看上去就像「整页图纸被竖着切了一刀、右边的部分没了」。
                using (Mat exportImage = ErasePageFrameOn(original))
                {
                    return ExportParts(exportImage, partRects, imgWidth, imgHeight);
                }
            }
        }

        /// <summary>膨胀聚合 → 轮廓提取 → 几何筛选 + 重叠合并。</summary>
        private List<Rect> DilateAndExtract(Mat masked, int imgWidth, int imgHeight)
        {
            using (Mat dilated = DilateForGrouping(masked))
            {
                SaveDebug(dilated, "debug_03_dilated.png");
                return ExtractPartRects(dilated, imgWidth, imgHeight);
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
            _maskStartX    = maskStartX;
            _maskByBlocks  = false;

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

        // ── 图框擦除 ──────────────────────────────────────────────────────────

        /// <summary>
        /// 擦除图纸四周的图框（<see cref="DetectorOptions.RemovePageFrame"/>）。
        ///
        /// 判据：一整行（或一整列）有墨迹的像素数 ≥ 幅面 × <see cref="DetectorOptions.PageFrameSpanRatio"/>，
        /// 且该行 / 列位于页边 <see cref="DetectorOptions.PageFrameEdgeRatio"/> 带内。
        /// 擦除时上下（左右）各多擦 2px，避免抗锯齿残留把形态学重新连起来。
        /// </summary>
        private Mat RemovePageFrameIfEnabled(Mat binary)
        {
            Mat result = binary.Clone();
            _frameRows.Clear();
            _frameCols.Clear();

            if (!_opts.RemovePageFrame)
                return result;

            int w = result.Cols;
            int h = result.Rows;
            if (w < 64 || h < 64)
                return result;

            int spanW = (int)Math.Round(w * _opts.PageFrameSpanRatio);
            int spanH = (int)Math.Round(h * _opts.PageFrameSpanRatio);
            int edgeW = (int)Math.Round(w * _opts.PageFrameEdgeRatio);
            int edgeH = (int)Math.Round(h * _opts.PageFrameEdgeRatio);

            int rows = 0, cols = 0;

            // ① 顶部 / 底部的贯穿横线
            for (int y = 0; y < h; y++)
            {
                if (y >= edgeH && y < h - edgeH) continue;

                int ink;
                using (Mat row = result.Row(y))
                    ink = Cv2.CountNonZero(row);

                if (ink < spanW) continue;
                WipeBand(result, y - 2, y + 2, vertical: false);
                _frameRows.Add(y);
                rows++;
            }

            // ② 左侧 / 右侧的贯穿竖线
            for (int x = 0; x < w; x++)
            {
                if (x >= edgeW && x < w - edgeW) continue;

                int ink;
                using (Mat col = result.Col(x))
                    ink = Cv2.CountNonZero(col);

                if (ink < spanH) continue;
                WipeBand(result, x - 2, x + 2, vertical: true);
                _frameCols.Add(x);
                cols++;
            }

            if (rows > 0 || cols > 0)
                _opts.Log($"[INFO] 图框擦除：横线 {rows} 条 / 竖线 {cols} 条（避免图框把内部视图粘成一块）");

            return result;
        }

        /// <summary>
        /// 把已检出的图框线从「导出用图像」里一并擦掉（涂白）。
        ///
        /// 检测阶段擦的是二值图，导出裁切用的却是原始彩色图；若不擦掉，图框会跟着
        /// 零件一起被裁进输出图里，看起来就像「整页图纸被竖着切了一刀」——
        /// 左 / 上 / 下边框都在，右边框却没有（右边框落在零件框之外）。
        /// 擦除范围与检测阶段完全一致（±2px），不影响任何图元。
        /// </summary>
        private Mat ErasePageFrameOn(Mat image)
        {
            Mat result = image.Clone();
            if (!_opts.RemovePageFrame)
                return result;
            if (_frameRows.Count == 0 && _frameCols.Count == 0)
                return result;

            foreach (int y in _frameRows)
                WipeBandWhite(result, y - 2, y + 2, vertical: false);

            foreach (int x in _frameCols)
                WipeBandWhite(result, x - 2, x + 2, vertical: true);

            _opts.Log($"[INFO] 导出图已同步擦除图框：横线 {_frameRows.Count} 条 / 竖线 {_frameCols.Count} 条");

            return result;
        }

        /// <summary>把 [from, to] 号行（vertical=false）或列（vertical=true）涂白，越界自动裁剪。</summary>
        private static void WipeBandWhite(Mat img, int from, int to, bool vertical)
        {
            int max = (vertical ? img.Cols : img.Rows) - 1;
            int a = Math.Max(0, from);
            int b = Math.Min(max, to);
            if (b < a) return;

            if (vertical)
            {
                using (Mat band = img.ColRange(a, b + 1))
                    band.SetTo(Scalar.All(255));
            }
            else
            {
                using (Mat band = img.RowRange(a, b + 1))
                    band.SetTo(Scalar.All(255));
            }
        }


        /// <summary>把 [from, to] 号行（vertical=false）或列（vertical=true）涂黑，越界自动裁剪。</summary>
        private static void WipeBand(Mat img, int from, int to, bool vertical)
        {
            int max = (vertical ? img.Cols : img.Rows) - 1;
            int a = Math.Max(0, from);
            int b = Math.Min(max, to);
            if (b < a) return;

            if (vertical)
            {
                using (Mat band = img.ColRange(a, b + 1))   // ColRange(start, end)：end 不含
                    band.SetTo(Scalar.Black);
            }
            else
            {
                using (Mat band = img.RowRange(a, b + 1))
                    band.SetTo(Scalar.Black);
            }
        }

        // ── 表格块遮蔽 ────────────────────────────────────────────────────────

        /// <summary>
        /// 只涂黑文字层推断出的表格 / 标题栏矩形（外扩 <see cref="DetectorOptions.TableMaskPadding"/>）。
        ///
        /// 与整列遮蔽的区别：整列遮蔽假设「表格区与零件区在 X 方向可分」，一旦表格在右上、
        /// 零件视图在中部（X 投影重叠），密实列带会吃进零件区；块遮蔽只擦表格本身，
        /// 表格左右 / 下方的零件视图完整保留。
        ///
        /// <see cref="MaskStartX"/> 取「中心落在右半页的表格块」的最小左边界，
        /// 供文字模块把框外文字分成「表格区 / 其它」（左下角表題欄不参与该判定）。
        /// </summary>
        private Mat MaskTableAreas(Mat binary, IList<PageRect> areas)
        {
            Mat result = binary.Clone();
            int pad    = Math.Max(0, _opts.TableMaskPadding);
            int minX   = int.MaxValue;
            int masked = 0;

            foreach (var a in areas)
            {
                Rect r = IntersectPage(
                    new Rect(a.X - pad, a.Y - pad, a.W + pad * 2, a.H + pad * 2),
                    result.Cols, result.Rows);

                if (r.Width > 0 && r.Height > 0)
                {
                    result[r].SetTo(Scalar.Black);
                    masked++;
                }

                // 只有「右半页」的表格块用来确定文字分区起点（左下角表題欄不算）
                if (a.X + a.W / 2.0 >= result.Cols * 0.5)
                    minX = Math.Min(minX, a.X);
            }

            _maskStartX   = minX == int.MaxValue ? -1 : minX;
            _maskByBlocks = true;

            _opts.Log($"[INFO] 表格遮蔽：按块遮蔽 {masked} 块（保留表格区以外的零件视图），"
                      + $"表格区起点 X={_maskStartX}");

            return result;
        }

        /// <summary>把矩形裁剪到页面范围内；完全在页面外时返回空矩形。</summary>
        private static Rect IntersectPage(Rect r, int width, int height)
        {
            int left   = Math.Max(0, r.X);
            int top    = Math.Max(0, r.Y);
            int right  = Math.Min(width,  r.X + r.Width);
            int bottom = Math.Min(height, r.Y + r.Height);

            if (right <= left || bottom <= top)
                return new Rect(0, 0, 0, 0);

            return new Rect(left, top, right - left, bottom - top);
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
        ///   ④ 外接框左边沿 &lt; 遮蔽边界（排除完全落在 BOM 区的轮廓，仅整列遮蔽时生效）
        ///      注意：用左边沿而非中心 X，允许跨越边界的宽零件被完整检测到；
        ///            边界取自 <see cref="MaskRight"/> 实际采用的 X（自动检测或固定比例）。
        ///            表格块遮蔽时表格像素已被涂黑，不再按左边界筛选，
        ///            否则会误删「X 落在表格区范围内、但位于表格下方 / 左侧」的零件视图。
        /// </summary>
        private List<Rect> ExtractPartRects(Mat dilated, int imgWidth, int imgHeight)
        {
            Point[][] contours;
            HierarchyIndex[] hierarchy;

            Cv2.FindContours(
                dilated, out contours, out hierarchy,
                RetrievalModes.Tree,
                ContourApproximationModes.ApproxSimple);

            // 与遮蔽方式保持一致：整列遮蔽用遮蔽线；表格块遮蔽时表格已涂黑，不再限制左边界
            int rightBoundary = _maskByBlocks
                ? imgWidth
                : (_maskStartX >= 0
                    ? _maskStartX
                    : (int)(imgWidth * (1.0 - _opts.RightMaskRatio)));
            double fullArea      = (double)imgWidth * imgHeight;

            var  candidates        = new List<Rect>();
            bool warnedTruncation  = false;
            int  fragmentCount     = 0;

            foreach (Point[] contour in contours)
            {
                double area = Cv2.ContourArea(contour);
                if (area < _opts.MinContourArea) continue;

                Rect bbox = Cv2.BoundingRect(contour);

                if (bbox.Width  < _opts.MinBoundingWidth)  continue;
                if (bbox.Height < _opts.MinBoundingHeight) continue;

                // 排除覆盖面积过大的图纸外框
                // 图框已在二值图阶段被擦除（RemovePageFrame），这里只需拦住「几乎整页」的异常块，
                // 否则会把「多个视图通过引线连成的大块」（例如竖排 4 个断面部约占 25%）误判为图框。
                double fillLimit = _opts.RemovePageFrame
                    ? Math.Max(_opts.MaxBboxFillRatio, 0.60)
                    : _opts.MaxBboxFillRatio;

                double bboxArea = bbox.Width * (double)bbox.Height;
                if (bboxArea / fullArea > fillLimit) continue;

                // 排除完全落在遮蔽区右侧的轮廓（用左边沿判断，允许跨边界的零件）
                if (bbox.Left >= rightBoundary) continue;

                // 块遮蔽模式：整块都落在「表格列带」里的候选，是块遮蔽漏掉的表格残片
                // （表格之间的连接线 / 空白行残留连成的块），直接丢弃。
                if (_maskByBlocks && _tableBandStartX > 0 && bbox.Left >= _tableBandStartX)
                {
                    fragmentCount++;
                    continue;
                }

                Rect paddedRect = AddPadding(bbox, _opts.Padding, imgWidth, imgHeight);
                candidates.Add(paddedRect);

                if (!_maskByBlocks && !warnedTruncation && bbox.Right + _opts.Padding >= rightBoundary)
                {
                    warnedTruncation = true;
                    _opts.Log($"[WARN] 有零件延伸至遮蔽边界 X={rightBoundary}，右侧可能被裁断；" +
                              "可减小「右侧遮蔽」比例后重试。");
                }
            }

            _opts.Log($"[INFO] 轮廓筛选后候选区域：{candidates.Count} 个（合并前）"
                      + (fragmentCount > 0 ? $"，另有 {fragmentCount} 个表格残片已丢弃" : string.Empty));

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
        /// 丢弃「文字行合计面积占比」过高的块 —— 它们不是要分割的零件图。
        ///
        /// 判据：块内所有文字行框与该块的<b>交集面积之和 ÷ 块面积</b> ≥
        /// <see cref="DetectorOptions.TextHeavyRatio"/>（默认 0.60）。
        /// 用交集而不是把整行框计入，跨在块边界上的行不会把自己整块算给这个块。
        ///
        /// 为什么能区分：零件图上的文字是尺寸数字、引线注记，一行只有几个字、散在各处，
        /// 行框加起来占不了多大面积；而表題欄 / 変更履歴表 / 部品表每一行都横跨整张表、
        /// 行行相邻，行框几乎铺满整块。实测（300 DPI，A2/A1 图纸）：
        /// 正常零件图 0.01~0.10，表格残片 0.55~0.85。
        ///
        /// 全部候选都被判为文字块时<b>保留原样并告警</b>：宁可多输出一张，
        /// 也不要让「整页都是文字说明」的图纸返回空结果。
        /// </summary>
        private List<Rect> DropTextHeavyRects(List<Rect> rects, IList<PageRect> textBoxes)
        {
            double threshold = _opts.TextHeavyRatio;
            if (threshold <= 0.0 || threshold > 1.0)
                threshold = 0.60;

            var kept  = new List<Rect>();
            var drops = new List<string>();

            foreach (var r in rects)
            {
                double area = r.Width * (double)r.Height;
                if (area <= 0.0)
                    continue;

                double textArea = 0.0;
                foreach (var t in textBoxes)
                {
                    int x0 = Math.Max(r.Left,   t.X);
                    int y0 = Math.Max(r.Top,    t.Y);
                    int x1 = Math.Min(r.Right,  t.Right);
                    int y1 = Math.Min(r.Bottom, t.Bottom);
                    if (x1 <= x0 || y1 <= y0) continue;

                    textArea += (x1 - x0) * (double)(y1 - y0);
                }

                if (textArea > area) textArea = area;   // 词框互相重叠时封顶
                double ratio = textArea / area;

                // 调参用：--debug 时列出每个候选块的实测占比（决定 TextHeavyRatio 取多少）
                if (_opts.SaveDebugImages)
                    _opts.Log($"[RATIO] x={r.X} y={r.Y} w={r.Width} h={r.Height} 文字行占比 {ratio:0.000}");

                if (ratio >= threshold)
                {
                    drops.Add($"x={r.X} y={r.Y} w={r.Width} h={r.Height} 文字行占比 {ratio:0.00}");
                    continue;
                }

                kept.Add(r);
            }

            if (drops.Count == 0)
                return rects;

            if (kept.Count == 0)
            {
                _opts.Log($"[WARN] {drops.Count} 个候选块的文字行占比都 ≥ {threshold:0.00}，"
                          + "已全部保留（避免输出为空；可用 --text-ratio 调高阈值）。");
                return rects;
            }

            foreach (string d in drops)
                _opts.Log($"[DROP] 文字行占比过高，判定不是零件图：{d}");

            _opts.Log($"[INFO] 文字行占比筛除：丢弃 {drops.Count} 个 / 保留 {kept.Count} 个（阈值 {threshold:0.00}）");
            return kept;
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
