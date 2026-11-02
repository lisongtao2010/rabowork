using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MSIFAITOOLS.Services
{
    /// <summary>一个 LT_STEP1.csv 及其对应的页码（&lt;図番&gt;\ = 第 1 页，&lt;図番&gt;_P2\ = 第 2 页 …）。</summary>
    internal sealed class CmdPartSource
    {
        public CmdPartSource(string filePath, int page)
        {
            FilePath = filePath ?? string.Empty;
            Page = page < 1 ? 1 : page;
        }

        /// <summary>LT_STEP1.csv 的完整路径。</summary>
        public string FilePath { get; private set; }

        /// <summary>该 CSV 所在的页码（第 1 页 = 1）。</summary>
        public int Page { get; private set; }
    }

    /// <summary>一个 PDF 的【CMD 分析】结果。</summary>
    internal sealed class CmdPartAnalysisResult
    {
        public CmdPartAnalysisResult(string drawingNo, IList<PartBasicInfo> parts, IList<string> notes,
            IList<CmdPartSource> sources)
        {
            DrawingNo = drawingNo ?? string.Empty;
            Parts = parts ?? new List<PartBasicInfo>().AsReadOnly();
            Notes = notes ?? new List<string>().AsReadOnly();
            Sources = sources ?? new List<CmdPartSource>().AsReadOnly();
        }

        /// <summary>図番（PDF 文件名去扩展名）。</summary>
        public string DrawingNo { get; private set; }

        /// <summary>提取到的部品基本信息。</summary>
        public IList<PartBasicInfo> Parts { get; private set; }

        /// <summary>分析过程中的提示（读取条数、去重、坐标校验统计等）。</summary>
        public IList<string> Notes { get; private set; }

        /// <summary>本次读取的 LT_STEP1.csv 清单。</summary>
        public IList<CmdPartSource> Sources { get; private set; }
    }

    /// <summary>
    /// 【CMD 分析】的核心：<b>不调用 AI</b>，用本工具自己的逻辑把 CadPartSplitter 生成的
    /// <c>LT_STEP1.csv</c> 解析成【部品基本信息列表】，再用 <c>xy.txt</c> 的文字坐标交叉校验。
    ///
    /// 为什么可以直接读 CSV：
    ///   · <c>LT_STEP1.csv</c> 就是部品表本体（CadPartSplitter 已把「下表参照」按参照表展开，
    ///     同一番号会变成多条不同部品コード的明细），字段与数据库表一一对应；
    ///   · 表头沿用图纸原表头（列顺序 / 列数不固定，还可能带换行与单位），
    ///     因此这里一律<b>按列名</b>映射，而不是按列号取；
    ///   · 文件编码与 CadPartSplitter 一致：带 BOM 的 UTF-8 + RFC 4180 转义。
    /// </summary>
    internal sealed class CmdPartAnalyzer
    {
        /// <summary>CadPartSplitter 为下游约定的部品表文件名。</summary>
        public const string LtStep1FileName = "LT_STEP1.csv";

        /// <summary>「番号」列候选名（先完全一致匹配，再退到「包含」匹配）。</summary>
        private static readonly string[] NumberHeaders = { "番号" };

        /// <summary>「部品名」列候选名。</summary>
        private static readonly string[] PartNameHeaders = { "部品名", "部品名称", "品名", "名称" };

        /// <summary>「部品コード」列候选名。</summary>
        private static readonly string[] PartCodeHeaders = { "部品コード", "コード" };

        /// <summary>「部品図番」列候选名。</summary>
        private static readonly string[] PartDrawingNoHeaders = { "部品図番", "部品番号", "図番", "品番" };

        /// <summary>「枠区分」列候选名。</summary>
        private static readonly string[] FrameKindHeaders = { "枠区分", "枠" };

        /// <summary>「員数」列候选名。</summary>
        private static readonly string[] QuantityHeaders = { "員数", "数量", "個数" };

        /// <summary>「基本寸法」列候选名。</summary>
        private static readonly string[] BasicSizeHeaders = { "基本寸法", "寸法" };

        /// <summary>「切断寸法」列候选名（「基本寸法」先占用，剩下的「寸法」列才归这里）。</summary>
        private static readonly string[] CutSizeHeaders = { "切断寸法", "カット寸法" };

        /// <summary>
        /// 在分解输出根目录下找出该 PDF 对应的全部 <c>LT_STEP1.csv</c>：
        /// <c>&lt;输出根目录&gt;\&lt;図番&gt;\LT_STEP1.csv</c>（第 1 页），
        /// <c>&lt;输出根目录&gt;\&lt;図番&gt;_P2\LT_STEP1.csv</c>（第 2 页）…按页码升序。
        /// </summary>
        /// <param name="outputDirectory">分解输出根目录（&lt;PDF 文件夹&gt;\AiAppData）。</param>
        /// <param name="stem">図番（PDF 文件名去扩展名）。</param>
        public static List<CmdPartSource> LocateSources(string outputDirectory, string stem)
        {
            List<CmdPartSource> sources = new List<CmdPartSource>();

            if (string.IsNullOrWhiteSpace(outputDirectory) || string.IsNullOrWhiteSpace(stem) ||
                !Directory.Exists(outputDirectory))
            {
                return sources;
            }

            string drawingNo = stem.Trim();

            // 第 1 页：<根目录>\<図番>\LT_STEP1.csv
            AddSource(sources, Path.Combine(outputDirectory, drawingNo), 1);

            // 第 2 页起：<根目录>\<図番>_P2\LT_STEP1.csv（CadPartSplitter 逐页分割时的命名规则）
            string[] folders;

            try
            {
                folders = Directory.GetDirectories(outputDirectory, drawingNo + "_P*");
            }
            catch (IOException)
            {
                return sources;
            }
            catch (UnauthorizedAccessException)
            {
                return sources;
            }

            // 按页码排序（_P10 要排在 _P9 之后，字符串排序不够用）
            List<KeyValuePair<int, string>> pageFolders = new List<KeyValuePair<int, string>>();

            foreach (string folder in folders)
            {
                int page = ParsePageSuffix(Path.GetFileName(folder), drawingNo);

                if (page > 0)
                {
                    pageFolders.Add(new KeyValuePair<int, string>(page, folder));
                }
            }

            pageFolders.Sort(delegate(KeyValuePair<int, string> left, KeyValuePair<int, string> right)
            {
                return left.Key.CompareTo(right.Key);
            });

            foreach (KeyValuePair<int, string> pair in pageFolders)
            {
                AddSource(sources, pair.Value, pair.Key);
            }

            return sources;
        }

        /// <summary>
        /// 解析一份 PDF 的全部 <paramref name="sources"/>，返回部品基本信息列表（含坐标校验结果）。
        /// </summary>
        /// <param name="pdfFilePath">PDF 完整路径（取図番、写入来源）。</param>
        /// <param name="sources">该 PDF 的 LT_STEP1.csv 清单（见 <see cref="LocateSources"/>）。</param>
        /// <param name="xyFilePath">xy.txt 的完整路径；不存在时跳过坐标校验。</param>
        public CmdPartAnalysisResult Analyze(string pdfFilePath, IList<CmdPartSource> sources, string xyFilePath)
        {
            if (sources == null || sources.Count == 0)
            {
                throw new ArgumentException("没有可读取的 " + LtStep1FileName + "。", nameof(sources));
            }

            string drawingNo = string.IsNullOrWhiteSpace(pdfFilePath)
                ? string.Empty
                : Path.GetFileNameWithoutExtension(pdfFilePath);

            List<string> notes = new List<string>();
            List<PartBasicInfo> parts = new List<PartBasicInfo>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            int duplicateCount = 0;
            int unparsedNumberCount = 0;

            foreach (CmdPartSource source in sources)
            {
                string label = Path.GetFileName(Path.GetDirectoryName(source.FilePath));
                List<string[]> rows;

                try
                {
                    rows = ReadCsv(source.FilePath);
                }
                catch (Exception ex)
                {
                    notes.Add(label + "：读取失败（" + ex.GetType().Name + "：" + ex.Message + "），已跳过");

                    continue;
                }

                if (rows.Count == 0)
                {
                    notes.Add(label + "：文件为空，已跳过");

                    continue;
                }

                string[] header = rows[0];
                Dictionary<string, int> columns = ResolveColumns(header);

                int numberCol = Column(columns, "番号");
                int nameCol = Column(columns, "部品名");
                int codeCol = Column(columns, "部品コード");
                int drawingNoCol = Column(columns, "部品図番");
                int frameCol = Column(columns, "枠区分");
                int quantityCol = Column(columns, "員数");
                int basicSizeCol = Column(columns, "基本寸法");
                int cutSizeCol = Column(columns, "切断寸法");

                if (nameCol < 0 && codeCol < 0)
                {
                    notes.Add(label + "：表头没有「部品名 / 部品コード」列，已跳过（表头：" +
                        JoinHeader(header) + "）");

                    continue;
                }

                int rowCount = 0;

                for (int r = 1; r < rows.Count; r++)
                {
                    string[] row = rows[r];

                    if (IsEmptyRow(row))
                    {
                        continue;
                    }

                    string numberText = Cell(row, numberCol);

                    // 表头重复出现（分页 / 合并单元格）时跳过这一行
                    if (Normalize(numberText).IndexOf("番号", StringComparison.Ordinal) >= 0)
                    {
                        continue;
                    }

                    string partName = Cell(row, nameCol);
                    string partCode = Cell(row, codeCol);
                    string partDrawingNo = Cell(row, drawingNoCol);

                    if (partName.Length == 0 && partCode.Length == 0 && partDrawingNo.Length == 0)
                    {
                        continue;
                    }

                    int number;

                    if (!TryParseNumber(numberText, out number))
                    {
                        number = rowCount + 1;
                        unparsedNumberCount++;
                    }

                    if (number <= 0)
                    {
                        number = rowCount + 1;
                    }

                    // 同页 / 同番号 / 同部品コード 视为同一行（「下表参照」展开后仍完全相同的情况）
                    string key = string.Format(CultureInfo.InvariantCulture, "{0}#{1}#{2}",
                        source.Page, number, partCode);

                    if (!seen.Add(key))
                    {
                        duplicateCount++;

                        continue;
                    }

                    PartBasicInfo part = new PartBasicInfo(drawingNo, source.Page, number, partName,
                        partDrawingNo, Cell(row, frameCol), Cell(row, quantityCol),
                        Cell(row, basicSizeCol), Cell(row, cutSizeCol), partCode);

                    part.SourceFile = source.FilePath;
                    part.PdfFile = pdfFilePath ?? string.Empty;

                    parts.Add(part);
                    rowCount++;
                }

                notes.Add(string.Format("{0}（第 {1} 页）：读取 {2} 条", label, source.Page, rowCount));

                if (numberCol < 0)
                {
                    notes.Add(label + "：表头没有「番号」列，番号按出现顺序赋值");
                }
            }

            if (unparsedNumberCount > 0)
            {
                notes.Add(string.Format("「番号」无法解析 {0} 条，已按出现顺序补齐", unparsedNumberCount));
            }

            if (duplicateCount > 0)
            {
                notes.Add(string.Format("已去掉重复行 {0} 条（同页 / 同番号 / 同部品コード）", duplicateCount));
            }

            if (parts.Count > 0)
            {
                XyCoordinateIndex coordinates = PartCoordinateVerifier.LoadOrNull(xyFilePath, notes);

                if (coordinates != null)
                {
                    PartCoordinateVerifier.Verify(parts, coordinates, notes);
                }
            }

            return new CmdPartAnalysisResult(drawingNo, parts.AsReadOnly(),
                new List<string>(notes).AsReadOnly(), new List<CmdPartSource>(sources).AsReadOnly());
        }

        /// <summary>把 <paramref name="folder"/> 下的 LT_STEP1.csv 加入清单（存在才加）。</summary>
        private static void AddSource(List<CmdPartSource> sources, string folder, int page)
        {
            if (!Directory.Exists(folder))
            {
                return;
            }

            string csvPath = Path.Combine(folder, LtStep1FileName);

            if (File.Exists(csvPath))
            {
                sources.Add(new CmdPartSource(csvPath, page));
            }
        }

        // ── 列名映射 ─────────────────────────────────────────────────────────

        /// <summary>
        /// 按列名把表头解析成「字段 → 列号」：先完全一致匹配，再退到「包含」匹配；
        /// 已被占用的列不会再分配给别的字段（避免「切断寸法」抢走「基本寸法」这类冲突）。
        /// </summary>
        private static Dictionary<string, int> ResolveColumns(string[] header)
        {
            Dictionary<string, int> columns = new Dictionary<string, int>(StringComparer.Ordinal);
            HashSet<int> used = new HashSet<int>();

            columns["番号"] = ResolveColumn(header, used, NumberHeaders);
            columns["部品コード"] = ResolveColumn(header, used, PartCodeHeaders);
            columns["部品名"] = ResolveColumn(header, used, PartNameHeaders);
            columns["部品図番"] = ResolveColumn(header, used, PartDrawingNoHeaders);
            columns["枠区分"] = ResolveColumn(header, used, FrameKindHeaders);
            columns["員数"] = ResolveColumn(header, used, QuantityHeaders);
            columns["基本寸法"] = ResolveColumn(header, used, BasicSizeHeaders);
            columns["切断寸法"] = ResolveColumn(header, used, CutSizeHeaders);

            return columns;
        }

        /// <summary>取某个字段的列号（没有该列时返回 -1）。</summary>
        private static int Column(Dictionary<string, int> columns, string key)
        {
            int index;

            return columns.TryGetValue(key, out index) ? index : -1;
        }

        /// <summary>在表头里找列号：先完全一致，再「包含」；找不到返回 -1。</summary>
        private static int ResolveColumn(string[] header, HashSet<int> used, string[] candidates)
        {
            if (header == null || candidates == null)
            {
                return -1;
            }

            foreach (string candidate in candidates)
            {
                string key = Normalize(candidate);

                if (key.Length == 0)
                {
                    continue;
                }

                for (int c = 0; c < header.Length; c++)
                {
                    if (used.Contains(c))
                    {
                        continue;
                    }

                    if (string.Equals(Normalize(header[c]), key, StringComparison.Ordinal))
                    {
                        used.Add(c);

                        return c;
                    }
                }
            }

            foreach (string candidate in candidates)
            {
                string key = Normalize(candidate);

                if (key.Length == 0)
                {
                    continue;
                }

                for (int c = 0; c < header.Length; c++)
                {
                    if (used.Contains(c))
                    {
                        continue;
                    }

                    if (Normalize(header[c]).IndexOf(key, StringComparison.Ordinal) >= 0)
                    {
                        used.Add(c);

                        return c;
                    }
                }
            }

            return -1;
        }

        // ── CSV 读取（RFC 4180，与 CadPartSplitter 的 CsvFile 一致） ─────────

        /// <summary>
        /// 读取 CSV：逗号分隔、双引号包裹的字段内可含逗号 / 换行、内部引号用两个引号转义；
        /// BOM 由 <see cref="File.ReadAllText(string, Encoding)"/> 自动丢弃。
        /// </summary>
        private static List<string[]> ReadCsv(string path)
        {
            List<string[]> rows = new List<string[]>();
            string text = File.ReadAllText(path, Encoding.UTF8);

            if (text.Length == 0)
            {
                return rows;
            }

            List<string> row = new List<string>();
            StringBuilder field = new StringBuilder();
            bool inQuotes = false;
            int i = 0;

            while (i < text.Length)
            {
                char ch = text[i];

                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            field.Append('"');
                            i += 2;

                            continue;
                        }

                        inQuotes = false;
                        i++;

                        continue;
                    }

                    field.Append(ch);
                    i++;

                    continue;
                }

                switch (ch)
                {
                    case '"':
                        inQuotes = true;
                        i++;
                        break;

                    case ',':
                        row.Add(field.ToString());
                        field.Length = 0;
                        i++;
                        break;

                    case '\r':
                        i++;
                        break;

                    case '\n':
                        row.Add(field.ToString());
                        field.Length = 0;
                        rows.Add(row.ToArray());
                        row.Clear();
                        i++;
                        break;

                    default:
                        field.Append(ch);
                        i++;
                        break;
                }
            }

            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                rows.Add(row.ToArray());
            }

            return rows;
        }

        // ── 小工具 ───────────────────────────────────────────────────────────

        /// <summary>取指定列的文本（列号为 -1 或越界时返回空字符串）。</summary>
        private static string Cell(string[] row, int column)
        {
            if (row == null || column < 0 || column >= row.Length)
            {
                return string.Empty;
            }

            return (row[column] ?? string.Empty).Trim();
        }

        /// <summary>归一化：去掉半角 / 全角空格与换行（PDF 表头常被拆成两行）。</summary>
        private static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace(" ", string.Empty)
                        .Replace("\u3000", string.Empty)
                        .Replace("\r", string.Empty)
                        .Replace("\n", string.Empty)
                        .Trim();
        }

        /// <summary>整行都是空白（尾部空行、合并单元格造成的空行）。</summary>
        private static bool IsEmptyRow(string[] row)
        {
            if (row == null)
            {
                return true;
            }

            foreach (string cell in row)
            {
                if (Normalize(cell).Length > 0)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>解析「番号」：支持「1」「1.」「001」「 2 」，失败返回 false。</summary>
        private static bool TryParseNumber(string value, out int number)
        {
            number = 0;

            string text = Normalize(value).Trim('.', ')', '）', '-', '－');

            if (text.Length == 0)
            {
                return false;
            }

            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
        }

        /// <summary>把表头拼成一行，便于在日志里看清「为什么没匹配上」。</summary>
        private static string JoinHeader(string[] header)
        {
            if (header == null || header.Length == 0)
            {
                return "（空）";
            }

            List<string> cells = new List<string>();

            foreach (string cell in header)
            {
                string text = Normalize(cell);
                cells.Add(text.Length == 0 ? "''" : text);
            }

            return string.Join(" | ", cells.ToArray());
        }

        /// <summary>从「&lt;図番&gt;_P3」这类文件夹名取出页码；不匹配返回 0。</summary>
        private static int ParsePageSuffix(string folderName, string drawingNo)
        {
            if (string.IsNullOrEmpty(folderName) || string.IsNullOrEmpty(drawingNo))
            {
                return 0;
            }

            string prefix = drawingNo + "_P";

            if (!folderName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            string digits = folderName.Substring(prefix.Length);
            int page;

            if (int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out page) && page > 0)
            {
                return page;
            }

            return 0;
        }
    }
}
