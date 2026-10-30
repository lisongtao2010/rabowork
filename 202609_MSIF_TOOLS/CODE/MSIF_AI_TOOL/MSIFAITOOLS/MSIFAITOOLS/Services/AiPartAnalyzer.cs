using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MSIFAITOOLS.Services
{
    /// <summary>单个 PDF 的 AI 分析结果。</summary>
    internal sealed class AiPartAnalysisResult
    {
        public AiPartAnalysisResult(string drawingNo, IList<PartBasicInfo> parts, IList<int> tablePages,
            IList<string> notes)
        {
            DrawingNo = drawingNo ?? string.Empty;
            Parts = parts ?? new List<PartBasicInfo>().AsReadOnly();
            TablePages = tablePages ?? new List<int>().AsReadOnly();
            Notes = notes ?? new List<string>().AsReadOnly();
        }

        /// <summary>図番（PDF 文件名去扩展名）。</summary>
        public string DrawingNo { get; private set; }

        /// <summary>提取到的部品基本信息。</summary>
        public IList<PartBasicInfo> Parts { get; private set; }

        /// <summary>本次送给 AI 的页码（表头所在页 + 其后连续页）。</summary>
        public IList<int> TablePages { get; private set; }

        /// <summary>分析过程中的提示（无表头、坐标缺失、校验统计等）。</summary>
        public IList<string> Notes { get; private set; }
    }

    /// <summary>
    /// 用 AI 从 *_plu_table.txt 中提取【部品基本信息列表】，
    /// 并用 *_xy.txt 的文字坐标对提取结果做交叉校验。
    /// </summary>
    internal sealed class AiPartAnalyzer
    {
        /// <summary>送给 AI 的表格文本字符上限。</summary>
        private const int MaxTableSnippetChars = 40000;

        /// <summary>送给 AI 的坐标文本字符上限。</summary>
        private const int MaxCoordinateSnippetChars = 20000;

        /// <summary>每个表头页向后额外包含的页数（表格可能跨页）。</summary>
        private const int FollowingPageCount = 1;

        /// <summary>一次最多送给 AI 的页数。</summary>
        private const int MaxTablePageCount = 6;

        /// <summary>每页最多送给 AI 的坐标词数。</summary>
        private const int MaxCoordinateWordsPerPage = 260;

        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        private readonly ChatCompletionClient _client;

        public AiPartAnalyzer(ChatCompletionClient client)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            _client = client;
        }

        /// <summary>
        /// 分析一份 PDF 对应的表格文本，返回部品基本信息列表。
        /// </summary>
        /// <param name="pdfFilePath">PDF 完整路径（用于取図番与写入来源）。</param>
        /// <param name="pluTablePath">*_plu_table.txt 的完整路径。</param>
        /// <param name="xyFilePath">*_xy.txt 的完整路径；不存在时跳过坐标校验。</param>
        public AiPartAnalysisResult Analyze(string pdfFilePath, string pluTablePath, string xyFilePath)
        {
            if (string.IsNullOrWhiteSpace(pluTablePath))
            {
                throw new ArgumentException("表格文件路径不能为空。", nameof(pluTablePath));
            }

            if (!File.Exists(pluTablePath))
            {
                throw new FileNotFoundException("找不到表格文件: " + pluTablePath, pluTablePath);
            }

            string drawingNo = string.IsNullOrWhiteSpace(pdfFilePath)
                ? Path.GetFileNameWithoutExtension(pluTablePath)
                : Path.GetFileNameWithoutExtension(pdfFilePath);

            List<string> notes = new List<string>();
            string tableText = File.ReadAllText(pluTablePath, Utf8NoBom);

            IList<PluTablePage> pages = PluTablePageLocator.SplitPages(tableText);
            IList<int> candidatePages = PluTablePageLocator.FindCandidatePages(pages);

            if (candidatePages.Count == 0)
            {
                notes.Add("本地未检测到同时含「番号 / 部品名」的表头，已将整份表格文本交给 AI 判断");
            }

            IList<int> tablePages = PluTablePageLocator.ExpandWithFollowingPages(
                pages, candidatePages, FollowingPageCount, MaxTablePageCount);

            string tableSnippet = PluTablePageLocator.BuildTableSnippet(pages, tablePages, MaxTableSnippetChars);

            if (tableSnippet.Length == 0)
            {
                tableSnippet = tableText.Length > MaxTableSnippetChars
                    ? tableText.Substring(0, MaxTableSnippetChars) + "\n（表格文本已截断）"
                    : tableText;
            }

            XyCoordinateIndex coordinates = LoadCoordinates(xyFilePath, notes);
            string coordinateSnippet = BuildCoordinateSnippet(coordinates, tablePages, pages);

            // 提示语来自 Prompts\AiPartAnalyzer.Prompt.txt（运行时可编辑，详见 AiPromptStore）
            AiPromptSet prompts = AiPromptStore.Load();

            if (!prompts.IsExternal)
            {
                notes.Add("未使用外部提示语文件，当前提示语来源：" + prompts.Source);
            }

            string userPrompt = prompts.BuildUserPrompt(drawingNo, tableSnippet, coordinateSnippet);
            string response = _client.Complete(prompts.SystemPrompt, userPrompt);

            int fallbackPage = tablePages.Count > 0 ? tablePages[0] : 1;
            IList<PartBasicInfo> parts = AiPartResponseParser.Parse(response, drawingNo, fallbackPage, notes);

            parts = RemoveDuplicates(parts, notes);

            if (coordinates != null && parts.Count > 0)
            {
                VerifyWithCoordinates(parts, coordinates, notes);
            }

            foreach (PartBasicInfo part in parts)
            {
                part.SourceFile = pluTablePath;
                part.PdfFile = pdfFilePath ?? string.Empty;
            }

            return new AiPartAnalysisResult(drawingNo, parts, tablePages,
                new List<string>(notes).AsReadOnly());
        }

        /// <summary>加载坐标索引（失败或不存时返回 null，并把原因记到 notes）。</summary>
        private static XyCoordinateIndex LoadCoordinates(string xyFilePath, IList<string> notes)
        {
            if (string.IsNullOrWhiteSpace(xyFilePath) || !File.Exists(xyFilePath))
            {
                notes.Add("未找到坐标文件（*_xy.txt），本次跳过坐标校验");

                return null;
            }

            try
            {
                XyCoordinateIndex index = XyCoordinateIndex.Load(xyFilePath);

                if (index.WordCount == 0)
                {
                    notes.Add("坐标文件没有解析到任何文字，本次跳过坐标校验");

                    return null;
                }

                return index;
            }
            catch (Exception ex)
            {
                notes.Add("坐标文件解析失败，本次跳过坐标校验：" + ex.Message);

                return null;
            }
        }

        /// <summary>准备送给 AI 的坐标片段（只取表格相关页）。</summary>
        private static string BuildCoordinateSnippet(XyCoordinateIndex coordinates, IList<int> tablePages,
            IList<PluTablePage> pages)
        {
            if (coordinates == null)
            {
                return string.Empty;
            }

            IList<int> targetPages = tablePages;

            if (targetPages == null || targetPages.Count == 0)
            {
                List<int> allPages = new List<int>();

                foreach (PluTablePage page in pages)
                {
                    allPages.Add(page.PageNumber);
                }

                targetPages = allPages;
            }

            return coordinates.BuildSnippet(targetPages, MaxCoordinateWordsPerPage, MaxCoordinateSnippetChars);
        }

        /// <summary>同一页同一番号的重复行只保留第一条。</summary>
        private static IList<PartBasicInfo> RemoveDuplicates(IList<PartBasicInfo> parts, IList<string> notes)
        {
            List<PartBasicInfo> unique = new List<PartBasicInfo>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (PartBasicInfo part in parts)
            {
                string key = part.Page.ToString(CultureInfo.InvariantCulture) + "#" +
                             part.Number.ToString(CultureInfo.InvariantCulture);

                if (seen.Add(key))
                {
                    unique.Add(part);
                }
            }

            if (unique.Count != parts.Count)
            {
                notes.Add(string.Format("已去掉重复行 {0} 条（同页同番号）", parts.Count - unique.Count));
            }

            return unique.AsReadOnly();
        }

        /// <summary>用坐标索引校验每一条提取结果，并写入校验状态与备注。</summary>
        private static void VerifyWithCoordinates(IList<PartBasicInfo> parts, XyCoordinateIndex coordinates,
            IList<string> notes)
        {
            int passed = 0;
            int failed = 0;
            int skipped = 0;

            foreach (PartBasicInfo part in parts)
            {
                List<string> problems = new List<string>();
                bool anyChecked = false;

                CheckValue(coordinates, part.Page, part.PartName, "部品名", problems, ref anyChecked);
                CheckValue(coordinates, part.Page, part.PartCode, "部品コード", problems, ref anyChecked);
                CheckValue(coordinates, part.Page, part.PartDrawingNo, "部品図番", problems, ref anyChecked);

                if (!anyChecked)
                {
                    part.VerifyStatus = PartVerifyStatus.NotChecked;
                    skipped++;
                }
                else if (problems.Count == 0)
                {
                    part.VerifyStatus = PartVerifyStatus.Passed;
                    passed++;
                }
                else
                {
                    part.VerifyStatus = PartVerifyStatus.Failed;
                    part.Remark = string.Join("；", problems.ToArray());
                    failed++;
                }
            }

            notes.Add(string.Format("坐标校验：一致 {0} 条，不符 {1} 条，无有效文字可校验 {2} 条",
                passed, failed, skipped));
        }

        /// <summary>校验单个字段（空值、"-"、"下表参照" 之类不参与校验）。</summary>
        private static void CheckValue(XyCoordinateIndex coordinates, int page, string value, string label,
            List<string> problems, ref bool anyChecked)
        {
            if (!IsCheckableValue(value))
            {
                return;
            }

            anyChecked = true;

            XyMatchResult result = coordinates.Verify(page, value);

            if (result == XyMatchResult.Found)
            {
                return;
            }

            if (result == XyMatchResult.FoundOnOtherPage)
            {
                problems.Add(label + "「" + value + "」只出现在其他页");
            }
            else if (result == XyMatchResult.NotFound)
            {
                problems.Add(label + "「" + value + "」坐标未找到");
            }
        }

        /// <summary>判断字段是否值得做坐标校验（排除空值、单符号、指示性文字）。</summary>
        private static bool IsCheckableValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string normalized = XyCoordinateIndex.Normalize(value);

            if (normalized.Length < 2)
            {
                return false;
            }

            if (normalized.IndexOf("参照", StringComparison.Ordinal) >= 0 ||
                normalized.IndexOf("下表", StringComparison.Ordinal) >= 0 ||
                normalized.IndexOf("同上", StringComparison.Ordinal) >= 0)
            {
                return false;
            }

            foreach (char c in normalized)
            {
                if (char.IsLetterOrDigit(c))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

