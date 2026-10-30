using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MSIFAITOOLS.Services
{
    /// <summary>*_plu_table.txt 中的一页。</summary>
    internal sealed class PluTablePage
    {
        public PluTablePage(int pageNumber, string body)
        {
            PageNumber = pageNumber;
            Body = body ?? string.Empty;
        }

        /// <summary>页码（从 1 开始）。</summary>
        public int PageNumber { get; private set; }

        /// <summary>该页的表格文本（不含页标题）。</summary>
        public string Body { get; private set; }

        /// <summary>该页是否出现表头关键字（番号 + 部品名）。</summary>
        public bool HasPartTableHeader
        {
            get
            {
                return Body.IndexOf("番号", StringComparison.Ordinal) >= 0 &&
                       Body.IndexOf("部品名", StringComparison.Ordinal) >= 0;
            }
        }
    }

    /// <summary>
    /// *_plu_table.txt（由 PdfTextExtractor 生成，页标题为 “--- 第 N 页 ---”，
    /// 表格块为 “[表格 N]” + “| 单元格 | ...” 行）的页级切分与片段提取。
    /// 用于给 AI 准备“只含目标表格页”的输入，避免整份文档超出上下文。
    /// </summary>
    internal static class PluTablePageLocator
    {
        /// <summary>页标题行：--- 第 1 页 ---</summary>
        private static readonly Regex PageTitlePattern = new Regex(
            @"^-{2,}\s*第\s*(?<page>\d+)\s*页", RegexOptions.Compiled);

        /// <summary>
        /// 把整份表格文本按页切开（找不到页标题时返回空列表）。
        /// </summary>
        public static IList<PluTablePage> SplitPages(string tableText)
        {
            List<PluTablePage> pages = new List<PluTablePage>();

            if (string.IsNullOrEmpty(tableText))
            {
                return pages;
            }

            string[] lines = tableText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            int currentPage = 0;
            StringBuilder body = null;

            foreach (string line in lines)
            {
                Match match = PageTitlePattern.Match(line);

                if (match.Success)
                {
                    if (body != null)
                    {
                        pages.Add(new PluTablePage(currentPage, body.ToString()));
                    }

                    int pageNumber;

                    if (!int.TryParse(match.Groups["page"].Value, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out pageNumber))
                    {
                        pageNumber = currentPage + 1;
                    }

                    currentPage = pageNumber;
                    body = new StringBuilder();

                    continue;
                }

                if (body != null)
                {
                    body.AppendLine(line);
                }
            }

            if (body != null)
            {
                pages.Add(new PluTablePage(currentPage, body.ToString()));
            }

            return pages;
        }

        /// <summary>
        /// 找出表头同时含「番号」「部品名」的页（这些页最可能包含部品明细表）。
        /// </summary>
        public static IList<int> FindCandidatePages(IList<PluTablePage> pages)
        {
            List<int> result = new List<int>();

            if (pages == null)
            {
                return result;
            }

            foreach (PluTablePage page in pages)
            {
                if (page.HasPartTableHeader)
                {
                    result.Add(page.PageNumber);
                }
            }

            return result;
        }

        /// <summary>
        /// 把候选页扩充为“候选页 + 其后的连续页”（表格可能跨页），并限制总页数。
        /// </summary>
        /// <param name="pages">全部页（页号升序）。</param>
        /// <param name="candidatePages">表头命中的页号。</param>
        /// <param name="followingPageCount">每个候选页向后额外包含几页。</param>
        /// <param name="maxPageCount">总页数上限。</param>
        public static IList<int> ExpandWithFollowingPages(IList<PluTablePage> pages,
            IList<int> candidatePages, int followingPageCount, int maxPageCount)
        {
            List<int> selected = new List<int>();

            if (pages == null || candidatePages == null || candidatePages.Count == 0)
            {
                return selected;
            }

            int following = followingPageCount < 0 ? 0 : followingPageCount;
            int limit = maxPageCount <= 0 ? candidatePages.Count : maxPageCount;

            for (int index = 0; index < pages.Count && selected.Count < limit; index++)
            {
                if (!candidatePages.Contains(pages[index].PageNumber))
                {
                    continue;
                }

                for (int offset = 0; offset <= following && index + offset < pages.Count; offset++)
                {
                    int pageNumber = pages[index + offset].PageNumber;

                    if (!selected.Contains(pageNumber) && selected.Count < limit)
                    {
                        selected.Add(pageNumber);
                    }
                }
            }

            selected.Sort();

            return selected;
        }

        /// <summary>
        /// 取出指定页的表格文本；页集合为空时回退为整份文本（截断）。
        /// </summary>
        /// <param name="pages">全部页。</param>
        /// <param name="selectedPages">要输出的页号（空表示全部）。</param>
        /// <param name="maxChars">输出字符上限。</param>
        public static string BuildTableSnippet(IList<PluTablePage> pages, IList<int> selectedPages, int maxChars)
        {
            if (pages == null || pages.Count == 0)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder();
            bool filterByPage = selectedPages != null && selectedPages.Count > 0;

            foreach (PluTablePage page in pages)
            {
                if (filterByPage && !selectedPages.Contains(page.PageNumber))
                {
                    continue;
                }

                builder.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "--- 第 {0} 页 ---", page.PageNumber));
                builder.AppendLine(page.Body);

                if (builder.Length >= maxChars)
                {
                    break;
                }
            }

            if (builder.Length > maxChars)
            {
                return builder.ToString(0, maxChars) + "\n（表格文本已截断）";
            }

            return builder.ToString();
        }
    }
}
