using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MSIFAITOOLS.Services
{
    /// <summary>图纸分类。</summary>
    internal enum PdfCategory
    {
        /// <summary>基本図（文件名第 5 个字符为 A）。</summary>
        Basic,

        /// <summary>加工図（文件名第 5 个字符为 C）。</summary>
        Machining,

        /// <summary>アッセンブリ図（第 5 个字符为 U，且 plu_all 命中行前 3 行内含「アッセンブリ図」）。</summary>
        Assembly,

        /// <summary>要領図（第 5 个字符为 U，且 plu_all 命中行上一行含「要領図」）。</summary>
        Instruction,

        /// <summary>未知（需用户在画面上选择）。</summary>
        Unknown
    }

    /// <summary>分类的显示名称与解析（界面下拉框与日志共用同一套文字）。</summary>
    internal static class PdfCategoryNames
    {
        /// <summary>基本図。</summary>
        public const string Basic = "基本図";

        /// <summary>加工図。</summary>
        public const string Machining = "加工図";

        /// <summary>アッセンブリ図。</summary>
        public const string Assembly = "アッセンブリ図";

        /// <summary>要領図。</summary>
        public const string Instruction = "要領図";

        /// <summary>未知。</summary>
        public const string Unknown = "未知";

        /// <summary>全部分类名称（界面下拉框顺序，未知固定在最后）。</summary>
        public static readonly string[] All = new string[] { Basic, Machining, Assembly, Instruction, Unknown };

        /// <summary>取分类的显示名称。</summary>
        public static string GetName(PdfCategory category)
        {
            switch (category)
            {
                case PdfCategory.Basic:
                    return Basic;

                case PdfCategory.Machining:
                    return Machining;

                case PdfCategory.Assembly:
                    return Assembly;

                case PdfCategory.Instruction:
                    return Instruction;

                default:
                    return Unknown;
            }
        }

        /// <summary>把界面上的分类文字解析回枚举；无法识别时返回 Unknown。</summary>
        public static PdfCategory Parse(string name)
        {
            string value = (name ?? string.Empty).Trim();

            if (string.Equals(value, Basic, StringComparison.Ordinal))
            {
                return PdfCategory.Basic;
            }

            if (string.Equals(value, Machining, StringComparison.Ordinal))
            {
                return PdfCategory.Machining;
            }

            if (string.Equals(value, Assembly, StringComparison.Ordinal))
            {
                return PdfCategory.Assembly;
            }

            if (string.Equals(value, Instruction, StringComparison.Ordinal))
            {
                return PdfCategory.Instruction;
            }

            return PdfCategory.Unknown;
        }
    }

    /// <summary>单个 PDF 的分类结果。</summary>
    internal sealed class PdfClassification
    {
        public PdfClassification(string filePath, PdfCategory category, string evidence, bool isAutomatic)
        {
            FilePath = filePath;
            Category = category;
            Evidence = evidence ?? string.Empty;
            IsAutomatic = isAutomatic;
        }

        /// <summary>PDF 文件的完整路径。</summary>
        public string FilePath { get; private set; }

        /// <summary>文件名（含扩展名）。</summary>
        public string FileName
        {
            get { return Path.GetFileName(FilePath); }
        }

        /// <summary>図番号（文件名去掉扩展名）。</summary>
        public string Stem
        {
            get { return Path.GetFileNameWithoutExtension(FilePath); }
        }

        /// <summary>当前分类（可能被用户手工修改）。</summary>
        public PdfCategory Category { get; private set; }

        /// <summary>判定依据说明（自动判定写来源，手工修改写“用户指定”）。</summary>
        public string Evidence { get; private set; }

        /// <summary>是否为自动判定（false 表示用户手工指定）。</summary>
        public bool IsAutomatic { get; private set; }

        /// <summary>是否需要用户在画面上选择。</summary>
        public bool NeedsUserChoice
        {
            get { return Category == PdfCategory.Unknown; }
        }

        /// <summary>当前分类的显示名称。</summary>
        public string CategoryName
        {
            get { return PdfCategoryNames.GetName(Category); }
        }

        /// <summary>由用户手工指定分类。</summary>
        public void ApplyManualChoice(PdfCategory category)
        {
            Category = category;
            Evidence = "用户指定";
            IsAutomatic = false;
        }
    }

    /// <summary>
    /// 图纸分类器（纯规则判定，不改动 / 移动 PDF 文件）：
    ///   1) 文件名第 5 个字符为 A → 基本図；
    ///   2) 文件名第 5 个字符为 C → 加工図；
    ///   3) 文件名第 5 个字符为 U → 在该文件的 plu_all 中查找含文件名（不含扩展名）的行，
    ///      若其前 3 行（含上一行）带「アッセンブリ図」→ アッセンブリ図；
    ///      若其前 1 行（上一行）带「要領図」→ 要領図；都不是 → 未知；
    ///   4) 其它字符或文件名不足 5 个字符 → 未知（由用户在画面上选择）。
    /// </summary>
    internal static class PdfClassifier
    {
        /// <summary>判定用字符下标（第 5 个字符，0 起）。</summary>
        public const int CategoryCharIndex = 4;

        /// <summary>plu_all 中标识アッセンブリ図的关键字。</summary>
        public const string AssemblyKeyword = PdfCategoryNames.Assembly;

        /// <summary>plu_all 中标识要領図的关键字。</summary>
        public const string InstructionKeyword = PdfCategoryNames.Instruction;

        /// <summary>判定アッセンブリ図时，向命中行上方回溯的行数（前 3 行）。</summary>
        public const int AssemblyLookBackLines = 3;

        /// <summary>判定要領図时，向命中行上方回溯的行数（前 1 行，即上一行）。</summary>
        public const int InstructionLookBackLines = 1;

        /// <summary>分类结果子文件夹名。</summary>
        public const string ClassifyFolderName = "classify";

        /// <summary>分类汇总文件名。</summary>
        public const string SummaryFileName = "classification.csv";

        /// <summary>对文件夹内（第一层）所有 PDF 分类。</summary>
        /// <param name="folder">PDF 所在文件夹。</param>
        /// <param name="pluDirectory">plu 输出目录（&lt;文件夹&gt;\AppData\plu）。</param>
        public static IList<PdfClassification> ClassifyFolder(string folder, string pluDirectory)
        {
            return ClassifyFiles(folder, PdfSplitService.FindPdfFiles(folder), pluDirectory);
        }

        /// <summary>对指定的 PDF 文件集合分类。</summary>
        /// <param name="folder">PDF 所在文件夹（pdfNames 为完整路径时可为 null）。</param>
        /// <param name="pdfNames">PDF 文件名或完整路径集合。</param>
        /// <param name="pluDirectory">plu 输出目录。</param>
        public static IList<PdfClassification> ClassifyFiles(string folder, IEnumerable<string> pdfNames,
            string pluDirectory)
        {
            List<PdfClassification> items = new List<PdfClassification>();

            if (pdfNames == null)
            {
                return items;
            }

            foreach (string name in pdfNames)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                string path = Path.IsPathRooted(name) || string.IsNullOrEmpty(folder)
                    ? name
                    : Path.Combine(folder, name);

                items.Add(Classify(path, pluDirectory));
            }

            return items;
        }

        /// <summary>对单个 PDF 分类。</summary>
        /// <param name="pdfFilePath">PDF 文件的完整路径。</param>
        /// <param name="pluDirectory">plu 输出目录（U 分支判定 plu_all 时使用）。</param>
        public static PdfClassification Classify(string pdfFilePath, string pluDirectory)
        {
            if (string.IsNullOrWhiteSpace(pdfFilePath))
            {
                throw new ArgumentException("PDF 文件路径不能为空。", "pdfFilePath");
            }

            string stem = Path.GetFileNameWithoutExtension(pdfFilePath);

            if (stem.Length <= CategoryCharIndex)
            {
                return new PdfClassification(pdfFilePath, PdfCategory.Unknown,
                    string.Format("文件名「{0}」不足 {1} 个字符，无法取第 {1} 个字符判定",
                        stem, CategoryCharIndex + 1), true);
            }

            char flag = char.ToUpperInvariant(stem[CategoryCharIndex]);

            switch (flag)
            {
                case 'A':
                    return new PdfClassification(pdfFilePath, PdfCategory.Basic, "文件名第 5 个字符为 A", true);

                case 'C':
                    return new PdfClassification(pdfFilePath, PdfCategory.Machining, "文件名第 5 个字符为 C", true);

                case 'U':
                    return ClassifyByPluAll(pdfFilePath, stem, pluDirectory);

                default:
                    return new PdfClassification(pdfFilePath, PdfCategory.Unknown,
                        string.Format("文件名第 5 个字符为 {0}（不是 A / C / U）", flag), true);
            }
        }

        /// <summary>U 分支：在 plu_all 中找含文件名的行，按上一行的关键字判定。</summary>
        private static PdfClassification ClassifyByPluAll(string pdfFilePath, string stem, string pluDirectory)
        {
            if (string.IsNullOrWhiteSpace(pluDirectory))
            {
                return new PdfClassification(pdfFilePath, PdfCategory.Unknown,
                    "未提供 plu 目录，无法按 plu_all 判定", true);
            }

            string pluAllPath = Path.Combine(pluDirectory, stem + PdfSplitService.PluAllFileSuffix);
            string pluAllName = Path.GetFileName(pluAllPath);

            if (!File.Exists(pluAllPath))
            {
                return new PdfClassification(pdfFilePath, PdfCategory.Unknown,
                    "未找到 " + pluAllName + "，无法按 plu_all 判定", true);
            }

            string[] lines;

            try
            {
                lines = File.ReadAllLines(pluAllPath, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                return new PdfClassification(pdfFilePath, PdfCategory.Unknown,
                    "读取 " + pluAllName + " 失败：" + ex.Message, true);
            }

            // 同一文件名（图番）可能在 plu_all 中出现多次（多页），逐行检查：
            //   命中行的前 3 行（含上一行）带「アッセンブリ図」→ アッセンブリ図；
            //   命中行的前 1 行（上一行）带「要領図」→ 要領図；
            //   都不带 → 未知（由用户在画面上选择）。
            int firstHitLine = -1;
            string firstHitPrevious = string.Empty;

            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index] ?? string.Empty;

                if (line.IndexOf(stem, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                string previous = index > 0 ? (lines[index - 1] ?? string.Empty) : string.Empty;

                for (int offset = 1; offset <= AssemblyLookBackLines; offset++)
                {
                    int target = index - offset;

                    if (target < 0)
                    {
                        break;
                    }

                    if ((lines[target] ?? string.Empty).IndexOf(AssemblyKeyword, StringComparison.Ordinal) < 0)
                    {
                        continue;
                    }

                    return new PdfClassification(pdfFilePath, PdfCategory.Assembly,
                        offset == 1
                            ? string.Format("{0} 第 {1} 行的上一行含「{2}」", pluAllName, index + 1, AssemblyKeyword)
                            : string.Format("{0} 第 {1} 行的前 {2} 行内（第 {3} 行）含「{4}」",
                                pluAllName, index + 1, AssemblyLookBackLines, target + 1, AssemblyKeyword),
                        true);
                }

                if (previous.IndexOf(InstructionKeyword, StringComparison.Ordinal) >= 0)
                {
                    return new PdfClassification(pdfFilePath, PdfCategory.Instruction,
                        string.Format("{0} 第 {1} 行的上一行含「{2}」", pluAllName, index + 1, InstructionKeyword), true);
                }

                if (firstHitLine < 0)
                {
                    firstHitLine = index + 1;
                    firstHitPrevious = Shorten(previous, 40);
                }
            }

            if (firstHitLine < 0)
            {
                return new PdfClassification(pdfFilePath, PdfCategory.Unknown,
                    pluAllName + " 中未找到含「" + stem + "」的行", true);
            }

            string hint = firstHitPrevious.Length == 0 ? "（空行）" : firstHitPrevious;

            return new PdfClassification(pdfFilePath, PdfCategory.Unknown,
                string.Format("{0} 第 {1} 行的前 {2} 行内不含「{3}」、上一行不含「{4}」（上一行为「{5}」）",
                    pluAllName, firstHitLine, AssemblyLookBackLines, AssemblyKeyword, InstructionKeyword, hint), true);
        }

        /// <summary>截断过长的文本（用于判定依据说明）。</summary>
        private static string Shorten(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            string trimmed = text.Trim();

            return trimmed.Length <= maxLength ? trimmed : trimmed.Substring(0, maxLength) + "...";
        }

        /// <summary>把分类结果写成汇总 CSV（UTF-8 带 BOM，便于 Excel 直接打开）。</summary>
        /// <param name="classifyDirectory">分类结果目录（不存在时自动创建）。</param>
        /// <param name="items">分类结果集合。</param>
        /// <returns>生成的 CSV 完整路径。</returns>
        public static string SaveSummary(string classifyDirectory, IList<PdfClassification> items)
        {
            if (string.IsNullOrWhiteSpace(classifyDirectory))
            {
                throw new ArgumentException("分类结果目录不能为空。", "classifyDirectory");
            }

            Directory.CreateDirectory(classifyDirectory);

            string path = Path.Combine(classifyDirectory, SummaryFileName);
            StringBuilder builder = new StringBuilder();

            builder.AppendLine("図番号,文件名,分类,判定方式,判定依据");

            if (items != null)
            {
                foreach (PdfClassification item in items)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    builder.Append(EscapeCsv(item.Stem)).Append(',');
                    builder.Append(EscapeCsv(item.FileName)).Append(',');
                    builder.Append(EscapeCsv(item.CategoryName)).Append(',');
                    builder.Append(EscapeCsv(item.IsAutomatic ? "自动" : "用户指定")).Append(',');
                    builder.AppendLine(EscapeCsv(item.Evidence));
                }
            }

            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(true));

            return path;
        }

        /// <summary>
        /// 读取汇总 CSV（供【开始分解】选择「使用现有结果」时复原分类列表）。
        /// 文件不存在、读取失败或内容为空时返回 null，由调用方回退到重新分类。
        /// </summary>
        /// <param name="folder">PDF 所在文件夹（用于还原每个条目的完整路径）。</param>
        /// <param name="classifyDirectory">分类结果目录（&lt;文件夹&gt;\AppData\classify）。</param>
        public static IList<PdfClassification> LoadSummary(string folder, string classifyDirectory)
        {
            if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(classifyDirectory))
            {
                return null;
            }

            string path = Path.Combine(classifyDirectory, SummaryFileName);

            if (!File.Exists(path))
            {
                return null;
            }

            string[] lines;

            try
            {
                lines = File.ReadAllLines(path, Encoding.UTF8);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }

            List<PdfClassification> items = new List<PdfClassification>();

            // 第 0 行为表头：図番号,文件名,分类,判定方式,判定依据
            for (int index = 1; index < lines.Length; index++)
            {
                List<string> fields = ParseCsvLine(lines[index]);

                if (fields.Count < 3 || string.IsNullOrWhiteSpace(fields[1]))
                {
                    continue;
                }

                string fileName = fields[1].Trim();
                PdfCategory category = PdfCategoryNames.Parse(fields[2]);
                bool isAutomatic = fields.Count < 4 ||
                    string.IsNullOrWhiteSpace(fields[3]) ||
                    string.Equals(fields[3].Trim(), "自动", StringComparison.Ordinal);
                string evidence = fields.Count > 4 ? fields[4] : string.Empty;

                items.Add(new PdfClassification(Path.Combine(folder, fileName), category, evidence, isAutomatic));
            }

            return items.Count == 0 ? null : items;
        }

        /// <summary>按 CSV 规则拆分一行（支持双引号包裹与 "" 转义）。</summary>
        private static List<string> ParseCsvLine(string line)
        {
            List<string> fields = new List<string>();
            StringBuilder current = new StringBuilder();
            bool inQuotes = false;

            if (line == null)
            {
                return fields;
            }

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            current.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        current.Append(c);
                    }

                    continue;
                }

                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    fields.Add(current.ToString());
                    current.Length = 0;
                }
                else
                {
                    current.Append(c);
                }
            }

            fields.Add(current.ToString());

            return fields;
        }

        /// <summary>统计指定分类的数量。</summary>
        public static int Count(IList<PdfClassification> items, PdfCategory category)
        {
            if (items == null)
            {
                return 0;
            }

            int count = 0;

            foreach (PdfClassification item in items)
            {
                if (item != null && item.Category == category)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>生成分类数量汇总文字，如「基本図 2 / 加工図 1 / アッセンブリ図 1 / 要領図 0 / 未知 3」。</summary>
        public static string BuildSummaryText(IList<PdfClassification> items)
        {
            return string.Format("{0} {1} / {2} {3} / {4} {5} / {6} {7} / {8} {9}",
                PdfCategoryNames.Basic, Count(items, PdfCategory.Basic),
                PdfCategoryNames.Machining, Count(items, PdfCategory.Machining),
                PdfCategoryNames.Assembly, Count(items, PdfCategory.Assembly),
                PdfCategoryNames.Instruction, Count(items, PdfCategory.Instruction),
                PdfCategoryNames.Unknown, Count(items, PdfCategory.Unknown));
        }

        /// <summary>CSV 字段转义（含逗号、引号、换行时加双引号）。</summary>
        private static string EscapeCsv(string value)
        {
            string text = value ?? string.Empty;

            if (text.IndexOfAny(new char[] { ',', '"', '\r', '\n' }) < 0)
            {
                return text;
            }

            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
    }
}
