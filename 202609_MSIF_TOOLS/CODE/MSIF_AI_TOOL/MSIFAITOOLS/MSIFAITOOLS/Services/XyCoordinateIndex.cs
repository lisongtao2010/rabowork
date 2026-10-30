using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace MSIFAITOOLS.Services
{
    /// <summary>
    /// *_xy.txt 中的一行文字及其坐标。
    /// 坐标系与 PdfTextExtractor 输出一致：原点在页面左上角、Y 轴向下。
    /// </summary>
    internal sealed class XyWord
    {
        public XyWord(int page, string text, double x0, double y0, double x1, double y1)
        {
            Page = page;
            Text = text ?? string.Empty;
            X0 = x0;
            Y0 = y0;
            X1 = x1;
            Y1 = y1;
        }

        /// <summary>页码（从 1 开始）。</summary>
        public int Page { get; private set; }

        /// <summary>文字内容（原样，未归一化）。</summary>
        public string Text { get; private set; }

        /// <summary>左边界。</summary>
        public double X0 { get; private set; }

        /// <summary>上边界。</summary>
        public double Y0 { get; private set; }

        /// <summary>右边界。</summary>
        public double X1 { get; private set; }

        /// <summary>下边界。</summary>
        public double Y1 { get; private set; }
    }

    /// <summary>单条文字的坐标校验结果。</summary>
    internal enum XyMatchResult
    {
        /// <summary>在指定页找到。</summary>
        Found,

        /// <summary>指定页没有，但其他页找到（通常是页码判定有误）。</summary>
        FoundOnOtherPage,

        /// <summary>整份文档都找不到。</summary>
        NotFound,

        /// <summary>没有可用的坐标数据，未做校验。</summary>
        NotAvailable
    }

    /// <summary>
    /// *_xy.txt（文字 + 坐标）的只读索引，用于校验 AI 提取的部品名 / 部品コード
    /// 是否真实出现在图纸上，并可为 AI 生成“该页有哪些文字”的辅助片段。
    /// </summary>
    internal sealed class XyCoordinateIndex
    {
        /// <summary>页标题行：--- 第 1 页 (共 123 个词) ---</summary>
        private static readonly Regex PageTitlePattern = new Regex(
            @"^---\s*第\s*(?<page>\d+)\s*页", RegexOptions.Compiled);

        /// <summary>文字行：文字: 'ABC' | 坐标: x0=100.00, y0=200.00, x1=140.00, y1=212.00</summary>
        private static readonly Regex WordPattern = new Regex(
            @"^文字:\s*'(?<text>.*)'\s*\|\s*坐标:\s*x0=(?<x0>-?\d+(?:\.\d+)?),\s*y0=(?<y0>-?\d+(?:\.\d+)?)," +
            @"\s*x1=(?<x1>-?\d+(?:\.\d+)?),\s*y1=(?<y1>-?\d+(?:\.\d+)?)\s*$",
            RegexOptions.Compiled);

        /// <summary>按页保存的词（页号 → 词列表），页号升序。</summary>
        private readonly SortedDictionary<int, List<XyWord>> _wordsByPage =
            new SortedDictionary<int, List<XyWord>>();

        /// <summary>按页保存的归一化文字（词按原始顺序直接拼接，便于跨词匹配）。</summary>
        private readonly Dictionary<int, string> _normalizedTextByPage = new Dictionary<int, string>();

        private XyCoordinateIndex()
        {
        }

        /// <summary>解析出的页数。</summary>
        public int PageCount
        {
            get { return _wordsByPage.Count; }
        }

        /// <summary>全部词的数量。</summary>
        public int WordCount { get; private set; }

        /// <summary>
        /// 解析指定的 *_xy.txt。
        /// </summary>
        /// <param name="xyFilePath">*_xy.txt 的完整路径。</param>
        /// <exception cref="FileNotFoundException">文件不存在。</exception>
        public static XyCoordinateIndex Load(string xyFilePath)
        {
            if (string.IsNullOrWhiteSpace(xyFilePath))
            {
                throw new ArgumentException("坐标文件路径不能为空。", nameof(xyFilePath));
            }

            if (!File.Exists(xyFilePath))
            {
                throw new FileNotFoundException("找不到坐标文件: " + xyFilePath, xyFilePath);
            }

            XyCoordinateIndex index = new XyCoordinateIndex();

            index.Parse(File.ReadAllLines(xyFilePath, new UTF8Encoding(false)));

            return index;
        }

        /// <summary>逐行解析文件内容（格式异常的行直接跳过，保证单个坏行不影响整体）。</summary>
        private void Parse(IEnumerable<string> lines)
        {
            int currentPage = 0;

            foreach (string rawLine in lines)
            {
                string line = rawLine == null ? string.Empty : rawLine.TrimEnd();

                if (line.Length == 0)
                {
                    continue;
                }

                Match titleMatch = PageTitlePattern.Match(line);

                if (titleMatch.Success)
                {
                    int pageNumber;
                    string pageText = titleMatch.Groups["page"].Value;

                    if (int.TryParse(pageText, NumberStyles.Integer, CultureInfo.InvariantCulture, out pageNumber))
                    {
                        currentPage = pageNumber;
                    }

                    continue;
                }

                if (currentPage <= 0)
                {
                    continue;
                }

                Match wordMatch = WordPattern.Match(line);

                if (!wordMatch.Success)
                {
                    continue;
                }

                string text = wordMatch.Groups["text"].Value;

                double x0;
                double y0;
                double x1;
                double y1;

                if (!TryParseDouble(wordMatch.Groups["x0"].Value, out x0) ||
                    !TryParseDouble(wordMatch.Groups["y0"].Value, out y0) ||
                    !TryParseDouble(wordMatch.Groups["x1"].Value, out x1) ||
                    !TryParseDouble(wordMatch.Groups["y1"].Value, out y1))
                {
                    continue;
                }

                XyWord word = new XyWord(currentPage, text, x0, y0, x1, y1);

                List<XyWord> pageWords;

                if (!_wordsByPage.TryGetValue(currentPage, out pageWords))
                {
                    pageWords = new List<XyWord>();
                    _wordsByPage.Add(currentPage, pageWords);
                }

                pageWords.Add(word);
                WordCount++;
            }

            // 拼接归一化页文字：去掉所有空白后直接相连，可命中被 PDF 拆成多个词的部品名
            foreach (KeyValuePair<int, List<XyWord>> pair in _wordsByPage)
            {
                StringBuilder builder = new StringBuilder();

                foreach (XyWord word in pair.Value)
                {
                    builder.Append(Normalize(word.Text));
                }

                _normalizedTextByPage[pair.Key] = builder.ToString();
            }
        }

        private static bool TryParseDouble(string value, out double result)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }

        /// <summary>是否解析到该页的文字。</summary>
        public bool HasPage(int page)
        {
            return _wordsByPage.ContainsKey(page);
        }

        /// <summary>取该页的全部词（没有时返回空列表）。</summary>
        public IList<XyWord> GetPageWords(int page)
        {
            List<XyWord> pageWords;

            if (_wordsByPage.TryGetValue(page, out pageWords))
            {
                return pageWords.AsReadOnly();
            }

            return new List<XyWord>().AsReadOnly();
        }

        /// <summary>
        /// 校验指定文字是否出现在指定页（先查该页，再查全文）。
        /// 比较前会做 NFKC 归一化并去掉空白，避免全角/半角与空格的干扰。
        /// </summary>
        /// <param name="page">期望的页码（从 1 开始；0 表示不限定页）。</param>
        /// <param name="text">要校验的文字（部品名 / 部品コード 等）。</param>
        public XyMatchResult Verify(int page, string text)
        {
            string key = Normalize(text);

            if (key.Length == 0 || WordCount == 0)
            {
                return XyMatchResult.NotAvailable;
            }

            string pageText;

            if (page > 0 && _normalizedTextByPage.TryGetValue(page, out pageText) &&
                pageText.IndexOf(key, StringComparison.Ordinal) >= 0)
            {
                return XyMatchResult.Found;
            }

            foreach (KeyValuePair<int, string> pair in _normalizedTextByPage)
            {
                if (pair.Key == page)
                {
                    continue;
                }

                if (pair.Value.IndexOf(key, StringComparison.Ordinal) >= 0)
                {
                    return XyMatchResult.FoundOnOtherPage;
                }
            }

            return XyMatchResult.NotFound;
        }

        /// <summary>
        /// 生成指定页的坐标文本片段，供 AI 交叉核对（每页限制词数与总字符数）。
        /// </summary>
        /// <param name="pages">要包含的页码集合。</param>
        /// <param name="maxWordsPerPage">每页最多输出的词数。</param>
        /// <param name="maxChars">整段最多字符数。</param>
        public string BuildSnippet(IEnumerable<int> pages, int maxWordsPerPage, int maxChars)
        {
            if (pages == null)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder();

            foreach (int page in pages)
            {
                IList<XyWord> pageWords = GetPageWords(page);

                if (pageWords.Count == 0)
                {
                    continue;
                }

                builder.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "--- 第 {0} 页（文字坐标）---", page));

                int written = 0;

                foreach (XyWord word in pageWords)
                {
                    if (written >= maxWordsPerPage || builder.Length >= maxChars)
                    {
                        break;
                    }

                    builder.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0}\t(x0={1:F1}, y0={2:F1}, x1={3:F1}, y1={4:F1})",
                        word.Text, word.X0, word.Y0, word.X1, word.Y1));

                    written++;
                }

                if (builder.Length >= maxChars)
                {
                    builder.AppendLine("（坐标文本已截断）");
                    break;
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// 归一化文字：NFKC（全角→半角、兼容字符统一）→ 转大写 → 去掉所有空白。
        /// 校验与页文字拼接必须使用同一套规则。
        /// </summary>
        public static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            string normalized = value.Normalize(NormalizationForm.FormKC);

            StringBuilder builder = new StringBuilder(normalized.Length);

            foreach (char c in normalized)
            {
                if (char.IsWhiteSpace(c) || c == '\u3000')
                {
                    continue;
                }

                builder.Append(char.ToUpperInvariant(c));
            }

            return builder.ToString();
        }
    }
}
