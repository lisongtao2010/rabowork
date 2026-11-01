using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenCvSharp;

namespace CadPartSplitter
{
    public static class PartSplitterService
    {
        public static Mat LoadImage(string filePath, DetectorOptions opts)
        {
            if (opts == null)
                throw new ArgumentNullException("opts");

            string ext = Path.GetExtension(filePath).ToLowerInvariant();

            switch (ext)
            {
                case ".pdf":
                    opts.Log("[INFO] Rendering PDF page " + (opts.PdfPageIndex + 1) + " at " + opts.RenderDpi + " DPI...");
                    return PdfRenderer.RenderPage(filePath, opts.PdfPageIndex, opts.RenderDpi);

                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".bmp":
                case ".tif":
                case ".tiff":
                    opts.Log("[INFO] Loading raster image...");
                    Mat img = Cv2.ImRead(filePath, ImreadModes.Color);
                    if (img.Empty())
                    {
                        img.Dispose();
                        throw new InvalidOperationException("OpenCV could not read image: " + filePath);
                    }
                    return img;

                default:
                    throw new NotSupportedException("Unsupported format: " + ext);
            }
        }

        public static List<PartInfo> Run(string filePath, DetectorOptions opts)
        {
            if (opts == null)
                throw new ArgumentNullException("opts");

            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                throw new FileNotFoundException("Input file not found.", filePath);

            opts.Log("[INFO] Input: " + Path.GetFullPath(filePath));
            opts.Log("[INFO] Output: " + Path.GetFullPath(opts.OutputDirectory));

            List<PartInfo> parts;
            int imgWidth, imgHeight;
            int maskStartX = -1;

            using (Mat original = LoadImage(filePath, opts))
            {
                imgWidth  = original.Cols;
                imgHeight = original.Rows;

                // PDF 有文字层时，先用文字的行列结构定位「表格 / 表題欄」矩形，
                // 让检测阶段只遮蔽这些矩形 —— 而不是把右侧整列涂黑
                // （表格在上、零件视图在下且 X 投影重叠时，整列遮蔽会把零件视图一起抹掉）。
                List<PageRect> tableAreas = null;
                List<PageRect> lineBoxes  = null;
                bool isPdf = string.Equals(Path.GetExtension(filePath), ".pdf", StringComparison.OrdinalIgnoreCase);
                if (isPdf)
                {
                    tableAreas = PartTextExtractor.DetectTableAreas(
                        filePath, opts.PdfPageIndex, imgWidth, imgHeight, opts.Log);

                    // 「文字行合计面积占自身外框 ≥ 60% 的块不是零件图」——
                    // 先算出整页文字行的外接框，交给检测阶段在导出 PNG 之前把表格残片筛掉。
                    if (opts.DropTextHeavyParts)
                        lineBoxes = PartTextExtractor.ReadTextLineBoxes(
                            filePath, opts.PdfPageIndex, imgWidth, imgHeight, opts.Log);
                }

                var detector = new PartDetector(opts);
                parts        = detector.Detect(original, tableAreas, lineBoxes);
                maskStartX   = detector.MaskStartX;   // 表格区起点：该列右侧即 BOM / 标题栏
            }

            if (parts.Count > 0)
            {
                PageTextResult pageText = null;

                // PDF 有文字层时，把「零件框内 / 框外」的文字与坐标一并记录
                if (string.Equals(Path.GetExtension(filePath), ".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    pageText = PartTextExtractor.Attach(filePath, opts.PdfPageIndex, imgWidth, imgHeight,
                                                        opts.RenderDpi, maskStartX, parts, opts.Log);

                    // 圆圈是矢量图形、不在文字层里，单独识别「圈 + 圈内数字」（①1、②2 …）
                    if (pageText != null)
                    {
                        pageText.Callouts = CalloutExtractor.Extract(filePath, opts.PdfPageIndex,
                                                                     imgWidth, imgHeight, opts.RenderDpi, parts, opts);
                    }
                }
                else
                {
                    opts.Log("[INFO] 位图输入没有文字层，跳过文字坐标提取（如需识别文字请接入 OCR）。");
                }

                string jsonPath = Path.Combine(opts.OutputDirectory, "parts_info.json");
                SavePartsJson(parts, jsonPath);
                opts.Log("[INFO] Coordinates saved: " + jsonPath);

                string txtPath = Path.Combine(opts.OutputDirectory, "parts_text.txt");
                SaveTextReport(parts, txtPath, filePath, opts);
                opts.Log("[INFO] Text report saved: " + txtPath);

                // 被分割部分以外（右侧表格 / 标题栏 / 页边注记）的文字与坐标
                if (pageText != null && pageText.HasTextLayer)
                {
                    string outsideJson = Path.Combine(opts.OutputDirectory, "outside_info.json");
                    SaveOutsideJson(pageText, outsideJson, filePath, opts);
                    opts.Log("[INFO] Outside coords saved: " + outsideJson);

                    string outsideTxt = Path.Combine(opts.OutputDirectory, "outside_text.txt");
                    SaveOutsideTextReport(pageText, outsideTxt, filePath, opts);
                    opts.Log("[INFO] Outside text report saved: " + outsideTxt);

                    if (opts.DetectCallouts)
                    {
                        string calloutPath = Path.Combine(opts.OutputDirectory, "callouts.txt");
                        SaveCalloutsReport(parts, pageText, calloutPath, filePath, opts);
                        opts.Log("[INFO] Callout report saved: " + calloutPath);
                    }
                }
            }

            return parts;
        }

        private static void SavePartsJson(List<PartInfo> parts, string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[");
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                var texts = p.Texts ?? new List<PartText>();

                sb.AppendLine("  {");
                sb.AppendLine("    \"index\": " + p.Index + ",");
                sb.AppendLine("    \"fileName\": \"" + EscapeJson(p.FileName) + "\",");
                sb.AppendLine("    \"pixel\": { \"x\": " + p.PixelX + ", \"y\": " + p.PixelY + ", \"w\": " + p.PixelW + ", \"h\": " + p.PixelH + " },");
                sb.AppendLine("    \"pdf_mm\": { \"x\": " + p.PdfX_mm.ToString("F2") + ", \"y\": " + p.PdfY_mm.ToString("F2") + ", \"w\": " + p.PdfW_mm.ToString("F2") + ", \"h\": " + p.PdfH_mm.ToString("F2") + " },");
                AppendCalloutsJson(sb, p.Callouts);
                sb.AppendLine("    \"texts\": [");

                for (int t = 0; t < texts.Count; t++)
                {
                    var x = texts[t];
                    sb.AppendLine("      {");
                    sb.AppendLine("        \"text\": \"" + EscapeJson(x.Text) + "\",");
                    sb.AppendLine("        \"pixel\": { \"x\": " + x.PixelX + ", \"y\": " + x.PixelY + ", \"w\": " + x.PixelW + ", \"h\": " + x.PixelH + " },");
                    sb.AppendLine("        \"local\": { \"x\": " + x.LocalX + ", \"y\": " + x.LocalY + ", \"w\": " + x.PixelW + ", \"h\": " + x.PixelH + " },");
                    sb.AppendLine("        \"pdf_mm\": { \"x\": " + x.PdfX_mm.ToString("F2") + ", \"y\": " + x.PdfY_mm.ToString("F2") + ", \"w\": " + x.PdfW_mm.ToString("F2") + ", \"h\": " + x.PdfH_mm.ToString("F2") + " }");
                    sb.AppendLine(t < texts.Count - 1 ? "      }," : "      }");
                }

                sb.AppendLine("    ]");
                sb.Append(i < parts.Count - 1 ? "  }," : "  }");
                sb.AppendLine();
            }
            sb.AppendLine("]");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        /// <summary>
        /// 往 parts_info.json 的一个零件对象里追加圈号标注数组（<c>"callouts": [...]</c>）。
        ///
        /// 每条记录 = 一个圆圈 + 圆内数字：
        ///   · no / text / display ── 圈内数字、圈内原始文字、组合形式（①1）；
        ///   · circle              ── 圆心与半径（页面像素）；
        ///   · pixel               ── 圆的外接框（页面像素）；
        ///   · local               ── 圆心相对该零件图左上角的位置；
        ///   · pdf_mm              ── 圆心与半径的页面毫米值。
        /// 末尾总是带逗号，因为其后还有 "texts" 字段。
        /// </summary>
        private static void AppendCalloutsJson(StringBuilder sb, List<Callout> callouts)
        {
            var list = callouts ?? new List<Callout>();
            if (list.Count == 0)
            {
                sb.AppendLine("    \"callouts\": [],");
                return;
            }

            sb.AppendLine("    \"callouts\": [");
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                sb.AppendLine("      {");
                sb.AppendLine("        \"no\": " + c.Number + ",");
                sb.AppendLine("        \"text\": \"" + EscapeJson(c.Text) + "\",");
                sb.AppendLine("        \"display\": \"" + EscapeJson(c.Display) + "\",");
                sb.AppendLine("        \"circle\": { \"cx\": " + c.CenterX.ToString("0.#") + ", \"cy\": " + c.CenterY.ToString("0.#") + ", \"r\": " + c.Radius.ToString("0.#") + " },");
                sb.AppendLine("        \"pixel\": { \"x\": " + c.PixelX + ", \"y\": " + c.PixelY + ", \"w\": " + c.PixelW + ", \"h\": " + c.PixelH + " },");
                sb.AppendLine("        \"local\": { \"cx\": " + c.LocalX + ", \"cy\": " + c.LocalY + " },");
                sb.AppendLine("        \"pdf_mm\": { \"cx\": " + c.PdfX_mm.ToString("F2") + ", \"cy\": " + c.PdfY_mm.ToString("F2") + ", \"r\": " + c.PdfRadius_mm.ToString("F2") + " }");
                sb.AppendLine(i < list.Count - 1 ? "      }," : "      }");
            }
            sb.AppendLine("    ],");
        }

        /// <summary>
        /// 生成「圈号标注清单」（callouts.txt）：把圆圈与圈内数字配成 ①1、②2 …，按零件图分组列出，
        /// 并给出圆心 / 半径的绝对像素、局部像素与毫米坐标，方便把编号贴回原图位置。
        /// 零件图以外的圈号（落在表格 / 页边）单独归入最后一节。
        /// </summary>
        private static void SaveCalloutsReport(List<PartInfo> parts, PageTextResult pageText,
                                               string path, string sourceFile, DetectorOptions opts)
        {
            var sb = new StringBuilder();
            var all = pageText != null && pageText.Callouts != null
                      ? pageText.Callouts
                      : new List<Callout>();

            int inParts = 0;
            foreach (var c in all) if (c.PartIndex > 0) inParts++;

            sb.AppendLine("# CadPartSplitter 圈号标注清单（矢量圆圈 + 圈内数字）");
            sb.AppendLine("# 生成时间   : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("# 源文件     : " + Path.GetFullPath(sourceFile));
            sb.AppendLine("# 页面 / DPI : 第 " + (opts.PdfPageIndex + 1) + " 页 / " + opts.RenderDpi.ToString("0") + " DPI");
            sb.AppendLine("# 识别方式   : PDF 矢量路径找整圆（四段贝塞尔 / 折线圆均可），再与文字层圆内数字配对");
            sb.AppendLine("# 坐标说明   : 圆心 = 页面像素（左上原点）；局部 = 相对该零件图左上角；mm = 页面毫米坐标");
            sb.AppendLine("# 合计       : " + all.Count + " 处（零件图内 " + inParts + " / 零件图外 " + (all.Count - inParts) + "）");
            sb.AppendLine();

            foreach (var p in parts)
            {
                var list = p.Callouts ?? new List<Callout>();
                if (list.Count == 0) continue;

                sb.AppendLine("[" + p.FileName + "]  圈号 " + list.Count + " 个"
                              + "  绝对(x=" + p.PixelX + " y=" + p.PixelY + " w=" + p.PixelW + " h=" + p.PixelH + ")");

                foreach (var c in list)
                    sb.AppendLine("    " + c.Display.PadRight(6)
                                  + " 圆心(cx=" + c.CenterX.ToString("0") + " cy=" + c.CenterY.ToString("0")
                                  + " r=" + c.Radius.ToString("0") + "px/" + c.PdfRadius_mm.ToString("0.##") + "mm)"
                                  + "  局部(" + c.LocalX + "," + c.LocalY + ")"
                                  + "  外接框(x=" + c.PixelX + " y=" + c.PixelY + " w=" + c.PixelW + " h=" + c.PixelH + ")");

                sb.AppendLine();
            }

            // 零件图以外的圈号（落在表格 / 标题栏 / 页边）
            var outside = new List<Callout>();
            foreach (var c in all) if (c.PartIndex <= 0) outside.Add(c);
            if (outside.Count > 0)
            {
                sb.AppendLine("[零件图外]  圈号 " + outside.Count + " 个");
                foreach (var c in outside)
                    sb.AppendLine("    " + c.Display.PadRight(6)
                                  + " 圆心(cx=" + c.CenterX.ToString("0") + " cy=" + c.CenterY.ToString("0")
                                  + " r=" + c.Radius.ToString("0") + "px/" + c.PdfRadius_mm.ToString("0.##") + "mm)"
                                  + "  mm(cx=" + c.PdfX_mm.ToString("0.##") + " cy=" + c.PdfY_mm.ToString("0.##") + ")");
                sb.AppendLine();
            }

            if (all.Count == 0)
                sb.AppendLine("（未识别到「圆圈 + 圈内数字」形式的标注）");

            // 带 BOM 的 UTF-8：Windows 记事本 / Excel 打开日文、中文不会乱码
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        /// <summary>
        /// 生成人眼可读的文字清单：每张零件图一节，先给出该图坐标，
        /// 再逐条列出「文字 + 绝对坐标 + 局部坐标」。
        ///
        /// 粒度由 <see cref="DetectorOptions.SplitTextToWords"/> 决定：
        ///   · true（默认）── 一个词一行，坐标是该词自己的外接框（<see cref="PartTextExtractor.BuildWordLines"/>）；
        ///   · false       ── 同一水平线上的词拼成一句、只给并集坐标（<see cref="PartTextExtractor.BuildLines"/>）。
        /// </summary>
        private static void SaveTextReport(List<PartInfo> parts, string path, string sourceFile, DetectorOptions opts)
        {
            var sb = new StringBuilder();

            sb.AppendLine("# CadPartSplitter 文字清单（每张零件图的坐标 + 该图上的文字及坐标）");
            sb.AppendLine("# 生成时间   : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("# 源文件     : " + Path.GetFullPath(sourceFile));
            sb.AppendLine("# 页面 / DPI : 第 " + (opts.PdfPageIndex + 1) + " 页 / " + opts.RenderDpi.ToString("0") + " DPI");
            sb.AppendLine("# 坐标说明   : 绝对 = 页面像素坐标（左上原点）；局部 = 相对该零件图左上角；mm = 页面毫米坐标");
            sb.AppendLine("# 零件图数量 : " + parts.Count);
            sb.AppendLine("# 文字来源   : PDF 文字层（按词边界）；位图输入无文字层，内容为空");
            sb.AppendLine(opts.SplitTextToWords
                ? "# 文字粒度   : 逐词一行（每个 PDF 词单独一条，坐标为该词自己的外接框；按 Y→X 顺序排列）"
                : "# 文字粒度   : 按行合并（同一水平线上的词拼成一句，坐标为该行并集外框）");
            sb.AppendLine("# 圈号标注   : 圆圈是 PDF 矢量图形、不在文字层；圈内数字在下方文字中按原位置显示为 ①1 形式"
                          + "（词级原始文本见 parts_info.json），每节末尾另列圆心坐标清单");
            sb.AppendLine();

            int markedTotal = 0;

            foreach (var p in parts)
            {
                var texts    = p.Texts ?? new List<PartText>();
                var callouts = p.Callouts ?? new List<Callout>();

                // 圈号标注回填：把「圈内那个数字词」标记成 ①1。
                // 必须在 BuildLines 之前做——BuildLines 拼行时会读 PartText.CalloutDisplay，
                // 从而在原来的位置上把裸数字换成带圈编号（同行其它相同数字不受影响）。
                int marked = CalloutExtractor.MarkCalloutWords(callouts, texts);
                markedTotal += marked;

                if (marked < callouts.Count)
                    opts.Log("[WARN] " + p.FileName + "：圈号 " + callouts.Count + " 个，其中 "
                             + (callouts.Count - marked) + " 个未在该图的文字中定位到圈内数字（该词可能落在零件框外），未做替换");

                // 行合并结果只用于统计「文字行 N 条」；实际列出的是逐词（默认）还是整行，看 SplitTextToWords
                var mergedLines = PartTextExtractor.BuildLines(texts);
                var rows = opts.SplitTextToWords ? PartTextExtractor.BuildWordLines(texts) : mergedLines;

                sb.AppendLine("[" + p.FileName + "]"
                              + "  绝对(x=" + p.PixelX + " y=" + p.PixelY + " w=" + p.PixelW + " h=" + p.PixelH + ")"
                              + "  mm(x=" + p.PdfX_mm.ToString("0.##") + " y=" + p.PdfY_mm.ToString("0.##")
                              + " w=" + p.PdfW_mm.ToString("0.##") + " h=" + p.PdfH_mm.ToString("0.##") + ")"
                              + "  文字行 " + mergedLines.Count + " 条 / 词 " + texts.Count + " 个"
                              + (opts.SplitTextToWords ? "（逐词列出）" : "（按行合并）")
                              + "  圈号 " + callouts.Count + " 个"
                              + (marked > 0 ? "（其中 " + marked + " 处数字已显示为带圈形式）" : string.Empty));

                if (rows.Count == 0)
                {
                    sb.AppendLine("    （该图范围未检测到文字）");
                }
                else
                {
                    foreach (var line in rows)
                    {
                        sb.AppendLine("    y=" + line.PixelY.ToString().PadRight(5)
                                      + " x=" + line.PixelX.ToString().PadRight(5)
                                      + " w=" + line.PixelW.ToString().PadRight(5)
                                      + " h=" + line.PixelH.ToString().PadRight(4)
                                      + " 局部(" + line.LocalX + "," + line.LocalY + ")  "
                                      + line.Text);
                    }
                }

                // 圆圈 + 圈内数字：文字层里只有那个孤立数字，圈是矢量图形，单独列出组合结果
                if (callouts.Count > 0)
                {
                    sb.AppendLine("    ── 圈号标注（圆圈 + 圈内数字）——");
                    foreach (var c in callouts)
                    {
                        sb.AppendLine("    " + c.Display.PadRight(6)
                                      + " 圆心(cx=" + c.CenterX.ToString("0") + " cy=" + c.CenterY.ToString("0")
                                      + " r=" + c.Radius.ToString("0") + "px)  局部(" + c.LocalX + "," + c.LocalY + ")"
                                      + "  mm(cx=" + c.PdfX_mm.ToString("0.##") + " cy=" + c.PdfY_mm.ToString("0.##") + ")");
                    }
                }

                sb.AppendLine();
            }

            if (markedTotal > 0)
                opts.Log("[INFO] 圈号文字替换：parts_text.txt 中 " + markedTotal + " 处圈内数字已显示为带圈形式（①1）");

            // 带 BOM 的 UTF-8：Windows 记事本 / Excel 打开日文、中文不会乱码
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        // ── 被分割部分以外的文字记录 ──────────────────────────────────────────

        /// <summary>
        /// 生成「被分割部分以外」的文字坐标清单（JSON）：
        ///   · tables      ── 表格 / 标题栏区按「表 → 行 → 单元格」组织的文字与坐标；
        ///   · other_words ── 零件框以外、且不在表格区的零散文字（页边注记等），词级坐标。
        /// 坐标均为页面绝对像素 + 页面毫米，与 parts_info.json 同一坐标系。
        /// </summary>
        private static void SaveOutsideJson(PageTextResult r, string path, string sourceFile, DetectorOptions opts)
        {
            var sb = new StringBuilder();
            double mmPerPx = 25.4 / Math.Max(1.0, opts.RenderDpi);

            sb.AppendLine("{");
            sb.AppendLine("  \"source\": \"" + EscapeJson(Path.GetFullPath(sourceFile)) + "\",");
            sb.AppendLine("  \"page\": " + (opts.PdfPageIndex + 1) + ",");
            sb.AppendLine("  \"dpi\": " + opts.RenderDpi.ToString("0.#") + ",");
            sb.AppendLine("  \"image\": { \"w\": " + r.ImageWidth + ", \"h\": " + r.ImageHeight + " },");
            sb.AppendLine("  \"table_start_x\": " + r.TableStartX + ",");
            sb.AppendLine("  \"word_count\": " + r.WordCount + ",");
            sb.AppendLine("  \"outside_word_count\": " + r.OutsideWordCount + ",");
            sb.AppendLine("  \"table_word_count\": " + r.TableWords.Count + ",");
            sb.AppendLine("  \"other_word_count\": " + r.OtherWords.Count + ",");

            // ── 表格区：表 → 行 → 单元格 ────────────────────────────────────
            sb.AppendLine("  \"tables\": [");
            for (int i = 0; i < r.Tables.Count; i++)
            {
                TableBlock tb = r.Tables[i];

                sb.AppendLine("    {");
                sb.AppendLine("      \"name\": \"" + EscapeJson(tb.Name) + "\",");
                sb.AppendLine("      \"pixel\": " + PixelBox(tb.PixelX, tb.PixelY, tb.PixelW, tb.PixelH) + ",");
                sb.AppendLine("      \"pdf_mm\": " + MmBox(tb.PixelX * mmPerPx, tb.PixelY * mmPerPx,
                                                          tb.PixelW * mmPerPx, tb.PixelH * mmPerPx) + ",");
                sb.AppendLine("      \"row_count\": " + tb.Rows.Count + ",");
                sb.AppendLine("      \"cell_count\": " + tb.CellCount + ",");
                sb.AppendLine("      \"rows\": [");

                for (int j = 0; j < tb.Rows.Count; j++)
                {
                    TableRow row = tb.Rows[j];

                    sb.AppendLine("        { \"row\": " + row.Index
                                  + ", \"pixel\": " + PixelBox(row.PixelX, row.PixelY, row.PixelW, row.PixelH)
                                  + ", \"cells\": [");

                    for (int k = 0; k < row.Cells.Count; k++)
                    {
                        TableCell c = row.Cells[k];

                        sb.AppendLine("          { \"col\": " + c.Column
                                      + ", \"text\": \"" + EscapeJson(c.Text) + "\",");
                        sb.AppendLine("            \"pixel\": " + PixelBox(c.PixelX, c.PixelY, c.PixelW, c.PixelH) + ",");
                        sb.AppendLine("            \"pdf_mm\": " + MmBox(c.PdfX_mm, c.PdfY_mm, c.PdfW_mm, c.PdfH_mm) + " }"
                                      + (k < row.Cells.Count - 1 ? "," : string.Empty));
                    }

                    sb.AppendLine("        ] }" + (j < tb.Rows.Count - 1 ? "," : string.Empty));
                }

                sb.AppendLine("      ]");
                sb.AppendLine(i < r.Tables.Count - 1 ? "    }," : "    }");
            }
            sb.AppendLine("  ],");

            // ── 其它未归属文字（词级）──────────────────────────────────────
            sb.AppendLine("  \"other_words\": [");
            for (int i = 0; i < r.OtherWords.Count; i++)
            {
                PartText w = r.OtherWords[i];

                sb.AppendLine("    { \"text\": \"" + EscapeJson(w.Text) + "\",");
                sb.AppendLine("      \"pixel\": " + PixelBox(w.PixelX, w.PixelY, w.PixelW, w.PixelH) + ",");
                sb.AppendLine("      \"pdf_mm\": " + MmBox(w.PdfX_mm, w.PdfY_mm, w.PdfW_mm, w.PdfH_mm) + " }"
                              + (i < r.OtherWords.Count - 1 ? "," : string.Empty));
            }
            sb.AppendLine("  ]");
            sb.AppendLine("}");

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private static string PixelBox(int x, int y, int w, int h)
        {
            return "{ \"x\": " + x + ", \"y\": " + y + ", \"w\": " + w + ", \"h\": " + h + " }";
        }

        private static string MmBox(double x, double y, double w, double h)
        {
            return "{ \"x\": " + x.ToString("F2") + ", \"y\": " + y.ToString("F2")
                 + ", \"w\": " + w.ToString("F2") + ", \"h\": " + h.ToString("F2") + " }";
        }

        /// <summary>
        /// 生成人眼可读的「未分割区域文字清单」：
        ///   一、表格 / 标题栏区（X ≥ 遮蔽线）按「表 → 行 → 单元格」列出文字与坐标；
        ///   二、其它未归属文字（零件框外非表格区）按行或逐词列出
        ///       （粒度见 <see cref="DetectorOptions.SplitTextToWords"/>）。
        /// </summary>
        private static void SaveOutsideTextReport(PageTextResult r, string path, string sourceFile, DetectorOptions opts)
        {
            var sb = new StringBuilder();

            int rowCount = 0, cellCount = 0;
            foreach (var tb in r.Tables)
            {
                rowCount  += tb.Rows.Count;
                cellCount += tb.CellCount;
            }

            sb.AppendLine("# CadPartSplitter 未分割区域文字清单（被分割部分以外的文字与坐标）");
            sb.AppendLine("# 生成时间   : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("# 源文件     : " + Path.GetFullPath(sourceFile));
            sb.AppendLine("# 页面 / DPI : 第 " + (opts.PdfPageIndex + 1) + " 页 / " + opts.RenderDpi.ToString("0") + " DPI");
            sb.AppendLine("# 图像尺寸   : " + r.ImageWidth + " × " + r.ImageHeight + " px");
            sb.AppendLine("# 表格区起点 : X = " + r.TableStartX + "（零件检测时遮蔽的右侧区域，其右即表格 / 标题栏）");
            sb.AppendLine("# 坐标说明   : 绝对 = 页面像素坐标（左上原点）；mm = 页面毫米坐标");
            sb.AppendLine("# 统计       : 全页文字 " + r.WordCount + " 个词；零件框外 " + r.OutsideWordCount + " 个"
                          + "（表格区 " + r.TableWords.Count + " / 其它 " + r.OtherWords.Count + "）");
            sb.AppendLine("#              表格 " + r.Tables.Count + " 张 / 行 " + rowCount + " / 单元格 " + cellCount);
            sb.AppendLine();

            sb.AppendLine("── 一、表格 / 标题栏区文字（X ≥ " + r.TableStartX + "）──────────────────────────");

            if (r.Tables.Count == 0)
            {
                sb.AppendLine("  （该区域未检测到文字）");
            }
            else
            {
                foreach (var tb in r.Tables)
                {
                    sb.AppendLine();
                    sb.AppendLine("[" + tb.Name + "]  区域 绝对(x=" + tb.PixelX + " y=" + tb.PixelY
                                  + " w=" + tb.PixelW + " h=" + tb.PixelH + ")"
                                  + "  行 " + tb.Rows.Count + " / 单元格 " + tb.CellCount);

                    foreach (var row in tb.Rows)
                    {
                        sb.AppendLine("  行 " + row.Index.ToString().PadLeft(3)
                                      + "  y=" + row.PixelY.ToString().PadRight(5)
                                      + " x=" + row.PixelX.ToString().PadRight(5)
                                      + " w=" + row.PixelW.ToString().PadRight(5)
                                      + " h=" + row.PixelH.ToString().PadRight(4)
                                      + "  单元格 " + row.Cells.Count);

                        foreach (var c in row.Cells)
                        {
                            sb.AppendLine("    [列" + c.Column.ToString().PadRight(2) + "]"
                                          + " x=" + c.PixelX.ToString().PadRight(5)
                                          + " w=" + c.PixelW.ToString().PadRight(5)
                                          + " h=" + c.PixelH.ToString().PadRight(4)
                                          + "  " + c.Text);
                        }
                    }
                }
            }

            sb.AppendLine();
            sb.AppendLine("── 二、其它未归属文字（零件框外、表格区左侧的页边注记等）─────────────");
            sb.AppendLine(opts.SplitTextToWords
                ? "  （逐词一行：坐标为该词自己的外接框，按 Y→X 顺序排列）"
                : "  （按行合并：同一水平线上的词拼成一句，坐标为并集外框）");

            if (r.OtherWords.Count == 0)
            {
                sb.AppendLine("  （无）");
            }
            else
            {
                var rows = opts.SplitTextToWords
                    ? PartTextExtractor.BuildWordLines(r.OtherWords)
                    : PartTextExtractor.BuildLines(r.OtherWords);

                foreach (var line in rows)
                {
                    sb.AppendLine("  y=" + line.PixelY.ToString().PadRight(5)
                                  + " x=" + line.PixelX.ToString().PadRight(5)
                                  + " w=" + line.PixelW.ToString().PadRight(5)
                                  + " h=" + line.PixelH.ToString().PadRight(4)
                                  + "  " + line.Text);
                }
            }

            // 带 BOM 的 UTF-8：Windows 记事本 / Excel 打开日文、中文不会乱码
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        private static string EscapeJson(string s)
        {
            if (s == null) return string.Empty;
            return s.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\r", "\\r")
                    .Replace("\n", "\\n")
                    .Replace("\t", "\\t");
        }
    }
}
