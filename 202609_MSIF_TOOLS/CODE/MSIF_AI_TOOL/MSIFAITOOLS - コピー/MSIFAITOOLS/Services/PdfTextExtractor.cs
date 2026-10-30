using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace MSIFAITOOLS.Services
{
    /// <summary>
    /// 基于 PdfPig（纯托管 .NET PDF 库）的内容提取器。
    /// 完全替代原 PDF_SPLIT.py 中的 PyMuPDF（文字 + 坐标）与 MarkItDown（整页文本）两处 Python 依赖，
    /// 并额外提供 pdfplumber 风格的五份结果（纯文本 / 版面文本 / 表格 / 图上文字 / 文本+表格），
    /// 目标机器无需安装 Python 及任何第三方运行库。
    /// </summary>
    internal static class PdfTextExtractor
    {
        /// <summary>写文件统一使用不带 BOM 的 UTF-8，与原 Python 脚本输出保持一致。</summary>
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>
        /// 提取 PDF 每一页的文字及其坐标，写入 <paramref name="outputPath"/>。
        /// 输出格式与原 PyMuPDF 版本完全一致：
        /// <code>
        /// --- 第 1 页 (共 123 个词) ---
        /// 文字: 'ABC' | 坐标: x0=100.00, y0=200.00, x1=140.00, y1=212.00
        /// </code>
        /// </summary>
        /// <param name="pdfFilePath">PDF 文件的完整路径。</param>
        /// <param name="outputPath">生成的 &lt;図番号&gt;_xy.txt 完整路径。</param>
        public static void WriteWordCoordinates(string pdfFilePath, string outputPath)
        {
            if (string.IsNullOrWhiteSpace(pdfFilePath))
            {
                throw new ArgumentException("PDF 文件路径不能为空。", nameof(pdfFilePath));
            }

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException("输出文件路径不能为空。", nameof(outputPath));
            }

            using (PdfDocument document = PdfDocument.Open(pdfFilePath))
            using (StreamWriter writer = new StreamWriter(outputPath, false, Utf8NoBom))
            {
                int pageCount = document.NumberOfPages;

                for (int pageNumber = 1; pageNumber <= pageCount; pageNumber++)
                {
                    Page page = document.GetPage(pageNumber);

                    // PdfPig 使用 PDF 原生坐标系：原点在左下角、Y 轴向上。
                    // 这里换算成阅读器常用的左上角原点、Y 轴向下的坐标，与 PyMuPDF 的 words 输出一致。
                    double pageHeight = page.Height;

                    // 过滤纯空白“词”，使结果与 PyMuPDF 的 words 一致（PyMuPDF 不返回空格）
                    List<Word> words = new List<Word>();

                    foreach (Word item in page.GetWords(NearestNeighbourWordExtractor.Instance))
                    {
                        if (string.IsNullOrEmpty(item.Text) || item.Text.Trim().Length == 0)
                        {
                            continue;
                        }

                        words.Add(item);
                    }

                    writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "--- 第 {0} 页 (共 {1} 个词) ---", pageNumber, words.Count));

                    foreach (Word word in words)
                    {
                        // 用 var 推断，避免不同 PdfPig 版本间 PdfRectangle 命名空间( Core / Geometry )差异
                        var box = word.BoundingBox;

                        writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "文字: '{0}' | 坐标: x0={1:F2}, y0={2:F2}, x1={3:F2}, y1={4:F2}",
                            word.Text,
                            box.Left,
                            pageHeight - box.Top,
                            box.Right,
                            pageHeight - box.Bottom));
                    }
                }
            }
        }

        /// <summary>
        /// 按阅读顺序提取 PDF 的整页文本，写入 <paramref name="outputPath"/>。
        /// 对应原 MarkItDown 的 text_content（整份文档纯文本），每页前加一行页标题便于定位。
        /// </summary>
        /// <param name="pdfFilePath">PDF 文件的完整路径。</param>
        /// <param name="outputPath">生成的 &lt;図番号&gt;_table.txt 完整路径。</param>
        public static void WriteDocumentText(string pdfFilePath, string outputPath)
        {
            if (string.IsNullOrWhiteSpace(pdfFilePath))
            {
                throw new ArgumentException("PDF 文件路径不能为空。", nameof(pdfFilePath));
            }

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException("输出文件路径不能为空。", nameof(outputPath));
            }

            using (PdfDocument document = PdfDocument.Open(pdfFilePath))
            using (StreamWriter writer = new StreamWriter(outputPath, false, Utf8NoBom))
            {
                int pageCount = document.NumberOfPages;

                for (int pageNumber = 1; pageNumber <= pageCount; pageNumber++)
                {
                    Page page = document.GetPage(pageNumber);

                    string text;
                    try
                    {
                        // 阅读顺序提取（相当于 PyMuPDF 的 get_text("text") / pdfminer 的版面排序）
                        text = ContentOrderTextExtractor.GetText(page);
                    }
                    catch (Exception)
                    {
                        // 极端情况下退回内容流顺序，保证单个畸形页面不会中断整份文档的处理
                        text = page.Text;
                    }

                    writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "--- 第 {0} 页 ---", pageNumber));

                    if (!string.IsNullOrEmpty(text))
                    {
                        writer.Write(text.TrimEnd('\r', '\n'));
                    }

                    writer.WriteLine();
                    writer.WriteLine();
                }
            }
        }

        /// <summary>
        /// 一次性生成 plu 的五种输出（只打开 PDF 一次，避免同一文档被重复解析）：
        /// 纯文本、版面文本、表格、图上文字、文本+表格。
        /// 本重载不生成图上文字文件，保留给只关心原有四种输出的调用方。
        /// </summary>
        /// <param name="pdfFilePath">PDF 文件的完整路径。</param>
        /// <param name="plainPath">&lt;図番号&gt;_plu_text.txt 的完整路径。</param>
        /// <param name="layoutPath">&lt;図番号&gt;_plu_layout.txt 的完整路径。</param>
        /// <param name="tablePath">&lt;図番号&gt;_plu_table.txt 的完整路径。</param>
        /// <param name="allPath">&lt;図番号&gt;_plu_all.txt 的完整路径。</param>
        public static void WritePluSet(string pdfFilePath, string plainPath, string layoutPath,
            string tablePath, string allPath)
        {
            WritePluSet(pdfFilePath, plainPath, layoutPath, tablePath, allPath, null);
        }

        /// <summary>
        /// 一次性生成 plu 的五种输出（只打开 PDF 一次，避免同一文档被重复解析）：
        /// 纯文本、版面文本、表格、图上文字、文本+表格。
        /// 图上文字按“图片区域与文字坐标比对”判定，表格块内的文字不重复计入，
        /// 并且只写独立的 _plu_image.txt，不并入 _plu_all（避免影响分类判定）。
        /// </summary>
        /// <param name="pdfFilePath">PDF 文件的完整路径。</param>
        /// <param name="plainPath">&lt;図番号&gt;_plu_text.txt 的完整路径。</param>
        /// <param name="layoutPath">&lt;図番号&gt;_plu_layout.txt 的完整路径。</param>
        /// <param name="tablePath">&lt;図番号&gt;_plu_table.txt 的完整路径。</param>
        /// <param name="allPath">&lt;図番号&gt;_plu_all.txt 的完整路径。</param>
        /// <param name="imagePath">&lt;図番号&gt;_plu_image.txt 的完整路径；为 null 或空白时不生成该文件。</param>
        public static void WritePluSet(string pdfFilePath, string plainPath, string layoutPath,
            string tablePath, string allPath, string imagePath)
        {
            if (string.IsNullOrWhiteSpace(pdfFilePath))
            {
                throw new ArgumentException("PDF 文件路径不能为空。", nameof(pdfFilePath));
            }

            if (string.IsNullOrWhiteSpace(plainPath) || string.IsNullOrWhiteSpace(layoutPath) ||
                string.IsNullOrWhiteSpace(tablePath) || string.IsNullOrWhiteSpace(allPath))
            {
                throw new ArgumentException("plu 输出文件路径不能为空。", nameof(plainPath));
            }

            using (PdfDocument document = PdfDocument.Open(pdfFilePath))
            using (StreamWriter plainWriter = new StreamWriter(plainPath, false, Utf8NoBom))
            using (StreamWriter layoutWriter = new StreamWriter(layoutPath, false, Utf8NoBom))
            using (StreamWriter tableWriter = new StreamWriter(tablePath, false, Utf8NoBom))
            using (StreamWriter allWriter = new StreamWriter(allPath, false, Utf8NoBom))
            using (StreamWriter imageWriter = string.IsNullOrWhiteSpace(imagePath)
                ? null
                : new StreamWriter(imagePath, false, Utf8NoBom))
            {
                int pageCount = document.NumberOfPages;

                for (int pageNumber = 1; pageNumber <= pageCount; pageNumber++)
                {
                    Page page = document.GetPage(pageNumber);

                    string pageTitle = string.Format(CultureInfo.InvariantCulture,
                        "--- 第 {0} 页 ---", pageNumber);

                    // 每页只做一次版面分析，五种输出共用结果
                    List<PdfLayoutAnalyzer.LayoutWord> words;

                    try
                    {
                        words = PdfLayoutAnalyzer.GetWords(page);
                    }
                    catch (Exception)
                    {
                        // 文字提取失败时退化为空集合，保证单页异常不中断整份文档
                        words = new List<PdfLayoutAnalyzer.LayoutWord>();
                    }

                    List<PdfLayoutAnalyzer.LayoutLine> lines;

                    try
                    {
                        lines = PdfLayoutAnalyzer.GetLines(words);
                    }
                    catch (Exception)
                    {
                        // 版面分析失败时退化为空集合，保证单页异常不中断整份文档
                        lines = new List<PdfLayoutAnalyzer.LayoutLine>();
                    }

                    string plainText = PdfLayoutAnalyzer.RenderPlain(lines);
                    string layoutText = PdfLayoutAnalyzer.RenderLayout(lines);

                    // 表格块只识别一次：既用于表格输出，也用于从图上文字里排除表格文字
                    List<PdfLayoutAnalyzer.LayoutBlock> tableBlocks = PdfLayoutAnalyzer.FindTableBlocks(lines);
                    List<string> tableLines = PdfLayoutAnalyzer.RenderTables(tableBlocks);

                    WriteSection(plainWriter, pageTitle, plainText);
                    WriteSection(layoutWriter, pageTitle, layoutText);

                    tableWriter.WriteLine(pageTitle);
                    WriteTableBlock(tableWriter, tableLines);
                    tableWriter.WriteLine();

                    List<PdfLayoutAnalyzer.LayoutImage> images;

                    try
                    {
                        images = PdfLayoutAnalyzer.GetImages(page);
                    }
                    catch (Exception)
                    {
                        // 图片解析失败时退化为“没有图片对象”，仍按表格外文字输出
                        images = new List<PdfLayoutAnalyzer.LayoutImage>();
                    }

                    List<string> imageLines = PdfLayoutAnalyzer.RenderImageTexts(words, images, tableBlocks);

                    if (imageWriter != null)
                    {
                        imageWriter.WriteLine(pageTitle);
                        WriteImageBlock(imageWriter, imageLines);
                        imageWriter.WriteLine();
                    }

                    allWriter.WriteLine(pageTitle);
                    allWriter.WriteLine("[文本]");
                    allWriter.Write(plainText);
                    allWriter.WriteLine();
                    allWriter.WriteLine();
                    allWriter.WriteLine("[表格]");
                    WriteTableBlock(allWriter, tableLines);
                    allWriter.WriteLine();

                    // 注意：图上文字只写独立文件，不并入 _plu_all，
                    // 以免图号在图内重复出现而影响 PdfClassifier 对 plu_all 的行扫描。
                }
            }
        }

        /// <summary>写一页的“页标题 + 正文 + 空行”。</summary>
        private static void WriteSection(StreamWriter writer, string pageTitle, string body)
        {
            writer.WriteLine(pageTitle);
            writer.Write(body);
            writer.WriteLine();
            writer.WriteLine();
        }

        /// <summary>输出一页的表格块；没有识别到表格时写占位说明。</summary>
        private static void WriteTableBlock(StreamWriter writer, IList<PdfLayoutAnalyzer.LayoutLine> lines)
        {
            WriteTableBlock(writer, PdfLayoutAnalyzer.RenderTables(lines));
        }

        /// <summary>输出已计算好的表格行；为空时写占位说明。</summary>
        private static void WriteTableBlock(StreamWriter writer, IList<string> tableLines)
        {
            if (tableLines == null || tableLines.Count == 0)
            {
                writer.WriteLine("（未识别到表格）");
                return;
            }

            foreach (string line in tableLines)
            {
                writer.WriteLine(line);
            }
        }

        /// <summary>输出已计算好的图上文字行；为空时写占位说明。</summary>
        private static void WriteImageBlock(StreamWriter writer, IList<string> imageLines)
        {
            if (imageLines == null || imageLines.Count == 0)
            {
                writer.WriteLine("（未识别到图上文字）");
                return;
            }

            foreach (string line in imageLines)
            {
                writer.WriteLine(line);
            }
        }
    }
}
