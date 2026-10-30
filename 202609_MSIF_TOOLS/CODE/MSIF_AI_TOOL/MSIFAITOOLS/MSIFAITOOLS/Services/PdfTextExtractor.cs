using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
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
        /// 判定“图面文字”水平分界所用的表头文字：取其中更靠左（左边坐标较小）的一个作为分界。
        /// 这两个表头位于图纸右侧的部品表 / 色展開表的第一列，其左侧即为图纸区域。
        /// </summary>
        private static readonly string[] ImageTextAnchors = new string[] { "番号", "タイプ名" };

        /// <summary>
        /// 图面文字的垂直范围：页面总高度自顶部向下 85% 以内。
        /// 页面最下方 15% 是注記 / 改訂履歴 / 表題欄，不属于图面文字。
        /// </summary>
        private const double ImageTextHeightRatio = 0.85;

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
        /// 提取“图面文字”（图纸区域内的文字）及其坐标，写入 <paramref name="outputPath"/>。
        /// 判定规则（依据图纸上标题栏 / 明细表都在右侧的固定排布）：
        ///   1) 在同一页文字中找出「番号」「タイプ名」两处表头，取左边坐标较小（更靠左）的一个作为水平分界；
        ///   2) 文字中心在分界左侧（严格小于）的，属于图面文字；
        ///      用中心而不是左边界，是为了不把表头所在列（表头居中、单元格左对齐）的文字也算进来；
        ///   3) 垂直方向只取页面总高度 85% 以内的文字（自页面顶部向下计算），
        ///      最下方 15% 的注記 / 改訂履歴 / 表題欄不计入；
        ///   4) 找不到这两处表头时该页不做水平限制，只按 85% 高度过滤。
        /// 输出格式与 &lt;図番号&gt;_xy.txt 完全一致，便于用同一套解析代码读取。
        /// </summary>
        /// <param name="pdfFilePath">PDF 文件的完整路径。</param>
        /// <param name="outputPath">生成的 &lt;図番号&gt;_xy_imgtext.txt 完整路径。</param>
        public static void WriteImageTextCoordinates(string pdfFilePath, string outputPath)
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

                    // 与 _xy.txt 使用相同的取词方式与坐标换算，保证两份结果的坐标可以直接对照
                    double pageHeight = page.Height;

                    List<Word> words = new List<Word>();

                    foreach (Word item in page.GetWords(NearestNeighbourWordExtractor.Instance))
                    {
                        if (string.IsNullOrEmpty(item.Text) || item.Text.Trim().Length == 0)
                        {
                            continue;
                        }

                        words.Add(item);
                    }

                    // 水平分界：取「番号」「タイプ名」中更靠左的一个；都没有时不做水平限制
                    double separator = double.MaxValue;

                    foreach (Word word in words)
                    {
                        if (!IsImageTextAnchor(word.Text))
                        {
                            continue;
                        }

                        var anchorBox = word.BoundingBox;
                        separator = Math.Min(separator, anchorBox.Left);
                    }

                    double limitY = pageHeight * ImageTextHeightRatio;

                    List<Word> selected = new List<Word>();

                    foreach (Word word in words)
                    {
                        var box = word.BoundingBox;
                        double centerX = (box.Left + box.Right) / 2.0;
                        // 换算成左上角原点、Y 轴向下的坐标（与 _xy.txt 一致）
                        double centerY = pageHeight - ((box.Top + box.Bottom) / 2.0);

                        if (centerY > limitY)
                        {
                            continue;
                        }

                        if (separator < double.MaxValue && centerX >= separator)
                        {
                            continue;
                        }

                        selected.Add(word);
                    }

                    writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "--- 第 {0} 页 (共 {1} 个词) ---", pageNumber, selected.Count));

                    foreach (Word word in selected)
                    {
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

        /// <summary>是否为图面文字分界所用的表头文字（「番号」「タイプ名」）。</summary>
        private static bool IsImageTextAnchor(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            string trimmed = text.Trim();

            foreach (string anchor in ImageTextAnchors)
            {
                if (string.Equals(trimmed, anchor, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 按“表格块”提取 PDF 的表格并写入 <paramref name="outputPath"/>（对应原 MarkItDown 的表格输出）。
        /// 每页先写一行页标题，再写该页识别到的表格：每行一条记录、单元格之间以 “|” 分隔，
        /// 未识别到表格的页写占位说明，便于按页对照原图纸。
        /// </summary>
        /// <param name="pdfFilePath">PDF 文件的完整路径。</param>
        /// <param name="outputPath">生成的 &lt;図番号&gt;_table.txt 完整路径。</param>
        public static void WriteTableText(string pdfFilePath, string outputPath)
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

                    // 与 plu 输出共用同一套版面分析，保证两种结果的表格划分完全一致
                    List<PdfLayoutAnalyzer.LayoutWord> words;

                    try
                    {
                        words = PdfLayoutAnalyzer.GetWords(page);
                    }
                    catch (Exception)
                    {
                        // 文字提取失败时退化为空集合，保证单页异常不中断整份文档的处理
                        words = new List<PdfLayoutAnalyzer.LayoutWord>();
                    }

                    List<PdfLayoutAnalyzer.LayoutLine> lines;

                    try
                    {
                        lines = PdfLayoutAnalyzer.GetLines(words);
                    }
                    catch (Exception)
                    {
                        // 版面分析失败时退化为空集合，保证单页异常不中断整份文档的处理
                        lines = new List<PdfLayoutAnalyzer.LayoutLine>();
                    }

                    List<PdfLayoutAnalyzer.LayoutBlock> tableBlocks = PdfLayoutAnalyzer.FindTableBlocks(lines);
                    List<string> tableLines = PdfLayoutAnalyzer.RenderTables(tableBlocks);

                    writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "--- 第 {0} 页 ---", pageNumber));
                    WriteTableBlock(writer, tableLines);
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
