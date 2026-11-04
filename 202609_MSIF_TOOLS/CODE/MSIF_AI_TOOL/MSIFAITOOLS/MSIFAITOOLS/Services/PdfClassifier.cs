using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MSIFAITOOLS.Services
{
    /// <summary>图纸分类（与 PdfInfoList.txt 的「图面分类」列一一对应）。</summary>
    internal enum PdfCategory
    {
        /// <summary>基本図。</summary>
        Basic,

        /// <summary>加工図。</summary>
        Machining,

        /// <summary>アッセンブリ図。</summary>
        Assembly,

        /// <summary>要領図。</summary>
        Instruction,

        /// <summary>未知（清单里没有该文件，或清单的「图面分类」列为空，画面上标黄提示）。</summary>
        Unknown
    }

    /// <summary>分类的显示名称与解析（画面、日志、PdfInfoList.txt 共用同一套文字）。</summary>
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

        /// <summary>全部分类名称（未知固定在最后）。</summary>
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

    /// <summary>单个 PDF 的分类结果（来自 PdfInfoList.txt 分类清单）。</summary>
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

        /// <summary>当前分类。</summary>
        public PdfCategory Category { get; private set; }

        /// <summary>判定依据说明（分类清单「分类依据」列的原文）。</summary>
        public string Evidence { get; private set; }

        /// <summary>是否来自分类清单（画面已不提供改判，当前恒为 true）。</summary>
        public bool IsAutomatic { get; private set; }

        /// <summary>是否为未知（画面上标黄提示分类清单里没有该图的记录）。</summary>
        public bool NeedsUserChoice
        {
            get { return Category == PdfCategory.Unknown; }
        }

        /// <summary>当前分类的显示名称。</summary>
        public string CategoryName
        {
            get { return PdfCategoryNames.GetName(Category); }
        }

        /// <summary>手工指定分类（画面已不提供改判，保留给外部工具 / 将来使用）。</summary>
        public void ApplyManualChoice(PdfCategory category)
        {
            Category = category;
            Evidence = "用户指定";
            IsAutomatic = false;
        }
    }

    /// <summary>
    /// 图纸分类清单的读取与统计（不做规则判定，不改动 / 移动 PDF 文件，也不写回清单）。
    /// <para>
    /// 分类来源是外部（CadPartSplitter 等）生成的 PdfInfoList.txt，主清单为
    /// &lt;输出根目录&gt;\classify\PdfInfoList.txt，只有「図番号,文件名,图面分类,分类依据」四列
    /// （UTF-8 带 BOM、RFC 4180）。清单的维护由外部工程负责，本工具只读。
    /// </para>
    /// <para>
    /// 主清单里没有某个 PDF 时，退回读该图自己的
    /// &lt;输出根目录&gt;\&lt;図番号&gt;\PdfInfoList.txt（CadPartSplitter 的单图清单，列更多，按表头名取列）；
    /// 两处都没有则记为未知（画面上标黄），需在上游工具的分类清单里补充。
    /// </para>
    /// </summary>
    internal static class PdfClassifier
    {
        /// <summary>分类清单所在子文件夹名（&lt;输出根目录&gt;\classify）。</summary>
        public const string ClassifyFolderName = "classify";

        /// <summary>分类清单文件名（主清单与单图清单同名）。</summary>
        public const string PdfInfoListFileName = "PdfInfoList.txt";

        /// <summary>清单表头里的図番号列名。</summary>
        private const string HeaderDrawingNo = "図番号";

        /// <summary>清单表头里的文件名列名。</summary>
        private const string HeaderFileName = "文件名";

        /// <summary>清单表头里的图面分类列名。</summary>
        private const string HeaderCategory = "图面分类";

        /// <summary>清单表头里的分类依据列名。</summary>
        private const string HeaderEvidence = "分类依据";

        /// <summary>清单里一条记录（图面分类 + 分类依据）。</summary>
        private sealed class PdfInfoEntry
        {
            public PdfInfoEntry(PdfCategory category, string evidence)
            {
                Category = category;
                Evidence = evidence ?? string.Empty;
            }

            /// <summary>图面分类。</summary>
            public PdfCategory Category { get; private set; }

            /// <summary>分类依据（清单原文）。</summary>
            public string Evidence { get; private set; }
        }

        /// <summary>计算主清单完整路径：&lt;输出根目录&gt;\classify\PdfInfoList.txt（只算路径，不建目录）。</summary>
        /// <param name="outputRoot">分解输出根目录（&lt;文件夹&gt;\AiAppData）。</param>
        public static string GetInfoListPath(string outputRoot)
        {
            if (string.IsNullOrWhiteSpace(outputRoot))
            {
                return string.Empty;
            }

            return Path.Combine(outputRoot, ClassifyFolderName, PdfInfoListFileName);
        }

        /// <summary>计算单图清单完整路径：&lt;输出根目录&gt;\&lt;図番号&gt;\PdfInfoList.txt（只算路径）。</summary>
        /// <param name="outputRoot">分解输出根目录（&lt;文件夹&gt;\AiAppData）。</param>
        /// <param name="stem">図番号（PDF 文件名去掉扩展名）。</param>
        public static string GetDrawingInfoListPath(string outputRoot, string stem)
        {
            if (string.IsNullOrWhiteSpace(outputRoot) || string.IsNullOrWhiteSpace(stem))
            {
                return string.Empty;
            }

            return Path.Combine(outputRoot, stem, PdfInfoListFileName);
        }

        /// <summary>
        /// 读取分类清单，为指定 PDF 集合生成分类列表（顺序与 pdfNames 一致）。
        /// <para>三级查找：主清单（classify\PdfInfoList.txt）→ 该图的单图清单 → 未知。</para>
        /// </summary>
        /// <param name="folder">PDF 所在文件夹（pdfNames 为完整路径时可为 null）。</param>
        /// <param name="pdfNames">PDF 文件名或完整路径集合。</param>
        /// <param name="outputRoot">分解输出根目录（&lt;文件夹&gt;\AiAppData）。</param>
        public static IList<PdfClassification> LoadInfoList(string folder, IEnumerable<string> pdfNames,
            string outputRoot)
        {
            List<PdfClassification> items = new List<PdfClassification>();

            if (pdfNames == null)
            {
                return items;
            }

            string infoListPath = GetInfoListPath(outputRoot);
            Dictionary<string, PdfInfoEntry> infoList = ReadInfoList(infoListPath);

            foreach (string name in pdfNames)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                string path = Path.IsPathRooted(name) || string.IsNullOrEmpty(folder)
                    ? name
                    : Path.Combine(folder, name);

                // 図番号 = 文件名去掉扩展名，也是清单里的主键
                string stem = Path.GetFileNameWithoutExtension(path);
                PdfInfoEntry entry = FindEntry(infoList, stem);

                if (entry == null)
                {
                    // 主清单里没有 → 读该图自己的单图清单（CadPartSplitter 的 PdfInfoList.txt）
                    entry = FindEntry(ReadInfoList(GetDrawingInfoListPath(outputRoot, stem)), stem);
                }

                if (entry != null)
                {
                    string evidence = entry.Evidence;

                    if (entry.Category == PdfCategory.Unknown && evidence.Length == 0)
                    {
                        evidence = PdfInfoListFileName + " 的「图面分类」列为空";
                    }

                    items.Add(new PdfClassification(path, entry.Category, evidence, true));
                    continue;
                }

                items.Add(new PdfClassification(path, PdfCategory.Unknown,
                    infoList == null
                        ? "未找到分类清单：" + infoListPath
                        : PdfInfoListFileName + " 中没有「" + stem + "」的记录",
                    true));
            }

            return items;
        }

        /// <summary>按図番号取清单记录（図番号 / 文件名去扩展名都已登记为键）。</summary>
        private static PdfInfoEntry FindEntry(Dictionary<string, PdfInfoEntry> entries, string stem)
        {
            if (entries == null || string.IsNullOrWhiteSpace(stem))
            {
                return null;
            }

            PdfInfoEntry entry;

            return entries.TryGetValue(stem, out entry) ? entry : null;
        }

        /// <summary>
        /// 读取清单文件 → 以図番号为键的记录表；文件不存在或读取失败时返回 null。
        /// 列按表头名定位，所以四列主清单与列更多的单图清单都能读。
        /// </summary>
        /// <param name="path">清单完整路径。</param>
        private static Dictionary<string, PdfInfoEntry> ReadInfoList(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
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

            Dictionary<string, PdfInfoEntry> entries =
                new Dictionary<string, PdfInfoEntry>(StringComparer.OrdinalIgnoreCase);

            if (lines.Length == 0)
            {
                return entries;
            }

            // 表头定位列号（第 1 行为表头）
            List<string> header = ParseCsvLine(lines[0]);
            int drawingNoIndex = IndexOfHeader(header, HeaderDrawingNo);
            int fileNameIndex = IndexOfHeader(header, HeaderFileName);
            int categoryIndex = IndexOfHeader(header, HeaderCategory);
            int evidenceIndex = IndexOfHeader(header, HeaderEvidence);
            int keyIndex = drawingNoIndex >= 0 ? drawingNoIndex : fileNameIndex;

            if (keyIndex < 0 || categoryIndex < 0)
            {
                return entries;
            }

            for (int lineIndex = 1; lineIndex < lines.Length; lineIndex++)
            {
                List<string> fields = ParseCsvLine(lines[lineIndex]);
                string key = GetField(fields, keyIndex).Trim();

                if (key.Length == 0)
                {
                    continue;
                }

                PdfInfoEntry entry = new PdfInfoEntry(
                    PdfCategoryNames.Parse(GetField(fields, categoryIndex)),
                    GetField(fields, evidenceIndex).Trim());

                entries[key] = entry;

                // 文件名（去扩展名）也登记一次：清单里「図番号」与「文件名」不一致时仍能找到
                string fileStem = Path.GetFileNameWithoutExtension(GetField(fields, fileNameIndex).Trim());

                if (fileStem.Length > 0)
                {
                    entries[fileStem] = entry;
                }
            }

            return entries;
        }

        /// <summary>取指定列的值（列不存在或为 null 时返回空串）。</summary>
        private static string GetField(List<string> fields, int index)
        {
            if (fields == null || index < 0 || index >= fields.Count)
            {
                return string.Empty;
            }

            return fields[index] ?? string.Empty;
        }

        /// <summary>在表头里找列名（忽略空白与 BOM 残留）；找不到时返回 -1。</summary>
        private static int IndexOfHeader(List<string> header, string name)
        {
            if (header == null)
            {
                return -1;
            }

            for (int index = 0; index < header.Count; index++)
            {
                if (string.Equals(NormalizeHeader(header[index]), name, StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return -1;
        }

        /// <summary>表头归一化：去掉所有空白（含换行）与 BOM 残留。</summary>
        private static string NormalizeHeader(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(value.Length);

            foreach (char c in value)
            {
                if (char.IsWhiteSpace(c) || (int)c == 0xFEFF)
                {
                    continue;
                }

                builder.Append(c);
            }

            return builder.ToString();
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
    }
}
