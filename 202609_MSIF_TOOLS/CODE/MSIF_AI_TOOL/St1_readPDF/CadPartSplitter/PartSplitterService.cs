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

                var detector = new PartDetector(opts);
                parts        = detector.Detect(original);
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
        /// 生成人眼可读的文字清单：每张零件图一节，先给出该图坐标，
        /// 再按行列出「文字 + 绝对坐标 + 局部坐标」。
        /// 同一行内的词会被合并成一句（见 <see cref="PartTextExtractor.BuildLines"/>）。
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
            sb.AppendLine();

            foreach (var p in parts)
            {
                var texts = p.Texts ?? new List<PartText>();
                var lines = PartTextExtractor.BuildLines(texts);

                sb.AppendLine("[" + p.FileName + "]"
                              + "  绝对(x=" + p.PixelX + " y=" + p.PixelY + " w=" + p.PixelW + " h=" + p.PixelH + ")"
                              + "  mm(x=" + p.PdfX_mm.ToString("0.##") + " y=" + p.PdfY_mm.ToString("0.##")
                              + " w=" + p.PdfW_mm.ToString("0.##") + " h=" + p.PdfH_mm.ToString("0.##") + ")"
                              + "  文字行 " + lines.Count + " 条 / 词 " + texts.Count + " 个");

                if (lines.Count == 0)
                {
                    sb.AppendLine("    （该图范围未检测到文字）");
                }
                else
                {
                    foreach (var line in lines)
                    {
                        sb.AppendLine("    y=" + line.PixelY.ToString().PadRight(5)
                                      + " x=" + line.PixelX.ToString().PadRight(5)
                                      + " w=" + line.PixelW.ToString().PadRight(5)
                                      + " h=" + line.PixelH.ToString().PadRight(4)
                                      + " 局部(" + line.LocalX + "," + line.LocalY + ")  "
                                      + line.Text);
                    }
                }

                sb.AppendLine();
            }

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
        ///   二、其它未归属文字（零件框外非表格区）按行列出。
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

            if (r.OtherWords.Count == 0)
            {
                sb.AppendLine("  （无）");
            }
            else
            {
                foreach (var line in PartTextExtractor.BuildLines(r.OtherWords))
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
