using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MSIFAITOOLS.Services
{
    /// <summary>
    /// 从一个「アッセンブリ図」出发，顺着要領図清单收集「要領図的部品」的结果。
    /// 由 <see cref="CmdInstructionPartCollector.Collect"/> 生成，供【CMD 分析】入库。
    /// </summary>
    internal sealed class CmdInstructionPartResult
    {
        public CmdInstructionPartResult(string drawingNo)
        {
            DrawingNo = drawingNo ?? string.Empty;
            Parts = new List<PartBasicInfo>();
            Notes = new List<string>();
        }

        /// <summary>アッセンブリ図 の 図番。</summary>
        public string DrawingNo { get; private set; }

        /// <summary>命中的部品明细（尚未入库）。</summary>
        public List<PartBasicInfo> Parts { get; private set; }

        /// <summary>处理过程中的提示（由调用方写进日志）。</summary>
        public List<string> Notes { get; private set; }

        /// <summary><c>01_アッセンブリ図_要領図.csv</c> 里的要領図条数。</summary>
        public int YoryoRowCount { get; set; }

        /// <summary>实际读到的 <c>02_要領図_部品機能情報.csv</c> 个数。</summary>
        public int YoryoPartFileCount { get; set; }

        /// <summary>缺少（01 / 02）文件的个数。</summary>
        public int MissingFileCount { get; set; }

        /// <summary>「品種」为空或未命中分类依据而被丢弃的行数。</summary>
        public int SkippedRowCount { get; set; }
    }

    /// <summary>
    /// 【CMD 分析】的追加步骤：把要領図的部品挂到アッセンブリ図上（<b>不调用 AI</b>、不写任何文件）。
    ///
    /// 流程：
    /// <list type="number">
    ///   <item>输入：一览中分类为「アッセンブリ図」的 PDF，它的「分类依据」来自
    ///         <c>&lt;输出根目录&gt;\classify\PdfInfoList.txt</c>；</item>
    ///   <item>读 <c>&lt;输出根目录&gt;\&lt;図番号&gt;\01_アッセンブリ図_要領図.csv</c>
    ///         （項目 / 要領図漢字 / 図番），取出该装配图引用的全部「要領図」図番；</item>
    ///   <item>按每个要領図図番找到 <c>&lt;输出根目录&gt;\&lt;図番&gt;\02_要領図_部品機能情報.csv</c>
    ///         （品種 / 部品名 / 部品コード / 員数）；</item>
    ///   <item>只保留「品種」出现在分类依据里的行 —— 分类依据的正文形如
    ///         <c>table_05.csv：H310 １７ラシッサ 片引戸 枠 アッセンブリ図</c>，先按空白 / 标点
    ///         切成单词做<b>完全一致</b>比较；一条都没命中时（依据里没有分隔符）再退回
    ///         <b>包含</b>比较，尽量不漏数据；</item>
    ///   <item>命中的行转成 <see cref="PartBasicInfo"/>：図番 = アッセンブリ図 の図番、
    ///         部品図番 = 要領図 の図番、備注 = <c>品種：xxx</c>（+ 項目）、校验状态 = 未校验。</item>
    /// </list>
    ///
    /// 页码固定为 1、番号由调用方给起始值后连续编号（部品基本信息表的唯一索引是
    /// 図番 + 页码 + 番号 + 部品コード），重复执行不会产生重复行。
    /// </summary>
    internal sealed class CmdInstructionPartCollector
    {
        /// <summary>アッセンブリ図 → 要領図 的对照表文件名（CadPartSplitter 生成）。</summary>
        public const string YoryoListFileName = "01_アッセンブリ図_要領図.csv";

        /// <summary>要領図 的部品明细文件名（CadPartSplitter 生成；旧名 <c>LT_STEP2.csv</c>）。</summary>
        public const string YoryoPartFileName = "02_要領図_部品機能情報.csv";

        /// <summary>改名前的旧文件名：老分解结果里可能还留着它，按新名字取不到时兜底用。</summary>
        public const string LegacyYoryoPartFileName = "LT_STEP2.csv";

        /// <summary>备注里「品種」的前缀。</summary>
        public const string RemarkKindPrefix = "品種：";

        /// <summary>01_アッセンブリ図_要領図.csv 的列名。</summary>
        private const string HeaderItem = "項目";

        private const string HeaderYoryoName = "要領図漢字";
        private const string HeaderYoryoDrawingNo = "図番";

        /// <summary>02_要領図_部品機能情報.csv 的列名。</summary>
        private const string HeaderKind = "品種";

        private const string HeaderPartName = "部品名";
        private const string HeaderPartCode = "部品コード";
        private const string HeaderQuantity = "員数";

        /// <summary>分词用的分隔符（半角 / 全角空格、常见中英文标点）。</summary>
        private static readonly char[] WordSeparators = new char[]
        {
            ' ', '\u3000', '\t', '\r', '\n', ',', '、', '，', '/', '／', '・', '|', '｜',
            ':', '：', ';', '；', '(', ')', '（', '）', '「', '」', '【', '】', '［', '］'
        };

        /// <summary>
        /// 读一个要領図 的 <c>02_要領図_部品機能情報.csv</c>，把「品種 ∈ 分类依据」的行转成部品基本信息。
        /// </summary>
        private void CollectFromYoryoParts(PdfClassification assembly, string outputDirectory, string yoryoNo,
            string item, string yoryoName, string evidence, int startNumber, HashSet<string> addedKeys,
            CmdInstructionPartResult result)
        {
            string path = LocateOutputFile(outputDirectory, yoryoNo, YoryoPartFileName);

            if (path == null)
            {
                result.MissingFileCount++;
                AddNote(result, string.Format("  要領図 {0}：缺少 {1}", yoryoNo, YoryoPartFileName));

                return;
            }

            List<string[]> rows;

            try
            {
                rows = ReadCsv(path);
            }
            catch (Exception ex)
            {
                AddNote(result, string.Format("  要領図 {0}：{1} 读取失败（{2}：{3}）", yoryoNo,
                    YoryoPartFileName, ex.GetType().Name, ex.Message));

                return;
            }

            if (rows.Count == 0)
            {
                AddNote(result, string.Format("  要領図 {0}：{1} 为空", yoryoNo, YoryoPartFileName));
                return;
            }

            string[] header = rows[0];
            int kindCol = IndexOfHeader(header, HeaderKind);
            int nameCol = IndexOfHeader(header, HeaderPartName);
            int codeCol = IndexOfHeader(header, HeaderPartCode);
            int quantityCol = IndexOfHeader(header, HeaderQuantity);

            if (kindCol < 0)
            {
                AddNote(result, string.Format("  要領図 {0}：表头没有「{1}」列，无法按分类依据筛选（表头：{2}）",
                    yoryoNo, HeaderKind, JoinHeader(header)));

                return;
            }

            if (nameCol < 0 && codeCol < 0)
            {
                AddNote(result, string.Format("  要領図 {0}：表头没有「部品名 / 部品コード」列，已跳过（表头：{1}）",
                    yoryoNo, JoinHeader(header)));

                return;
            }

            // 先看这个文件里有哪些品種，再一次性判定「命中分类依据」的品種集合
            Dictionary<string, string> kindTexts =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int rowIndex = 1; rowIndex < rows.Count; rowIndex++)
            {
                string[] row = rows[rowIndex];

                if (IsEmptyRow(row))
                {
                    continue;
                }

                string raw = Cell(row, kindCol).Trim();
                string key = NormalizeKey(raw);

                if (key.Length == 0 || kindTexts.ContainsKey(key))
                {
                    continue;
                }

                kindTexts.Add(key, raw);
            }

            if (kindTexts.Count == 0)
            {
                AddNote(result, string.Format("  要領図 {0}：{1} 里没有品種", yoryoNo, YoryoPartFileName));
                return;
            }

            HashSet<string> matched = ResolveMatchedKinds(kindTexts.Keys, evidence);
            List<string> fileRows = new List<string>();

            result.YoryoPartFileCount++;

            if (matched.Count == 0)
            {
                AddNote(result, string.Format("  要領図 {0}：品種（{1}）都不在分类依据里，跳过",
                    yoryoNo, JoinLimited(new List<string>(kindTexts.Values), 4)));

                return;
            }

            int rowCount = 0;

            for (int rowIndex = 1; rowIndex < rows.Count; rowIndex++)
            {
                string[] row = rows[rowIndex];

                if (IsEmptyRow(row))
                {
                    continue;
                }

                string rawKind = Cell(row, kindCol).Trim();
                string kindKey = NormalizeKey(rawKind);

                if (kindKey.Length == 0)
                {
                    result.SkippedRowCount++;

                    continue;
                }

                if (!matched.Contains(kindKey))
                {
                    continue;
                }

                string partName = Cell(row, nameCol);
                string partCode = Cell(row, codeCol);

                if (partName.Length == 0 && partCode.Length == 0)
                {
                    continue;
                }

                // 同一要領図 里同品種 / 同部品 / 同コード 只取一次
                string dedupeKey = kindKey + "#" + Normalize(partName) + "#" + Normalize(partCode);

                if (!addedKeys.Add(dedupeKey))
                {
                    result.SkippedRowCount++;

                    continue;
                }

                PartBasicInfo part = new PartBasicInfo(
                    assembly.Stem,
                    1,
                    startNumber + result.Parts.Count,
                    partName,
                    yoryoNo,
                    string.Empty,
                    Cell(row, quantityCol),
                    string.Empty,
                    string.Empty,
                    partCode);

                part.Remark = BuildRemark(kindTexts[kindKey], item);
                part.SourceFile = path;
                part.PdfFile = assembly.FilePath ?? string.Empty;
                part.VerifyStatus = PartVerifyStatus.NotChecked;

                result.Parts.Add(part);
                rowCount++;

                if (!fileRows.Contains(kindTexts[kindKey]))
                {
                    fileRows.Add(kindTexts[kindKey]);
                }
            }

            AddNote(result, string.Format("  要領図 {0}（{1}）：品種命中 {2} 行 / 命中品種 {3}",
                yoryoNo, Shorten(yoryoName, 18), rowCount, JoinLimited(fileRows, 4)));
        }

        /// <summary>
        /// 收集该アッセンブリ図 名下、分类依据命中的要領図部品。
        /// </summary>
        /// <param name="assembly">一览中的アッセンブリ図（<see cref="PdfClassification.Evidence"/> 即分类依据）。</param>
        /// <param name="outputDirectory">分解输出根目录（&lt;PDF 文件夹&gt;\AiAppData）。</param>
        /// <param name="startNumber">本次追加行的起始「番号」（接在已有行之后）。</param>
        public CmdInstructionPartResult Collect(PdfClassification assembly, string outputDirectory,
            int startNumber)
        {
            if (assembly == null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }

            string drawingNo = assembly.Stem;
            CmdInstructionPartResult result = new CmdInstructionPartResult(drawingNo);
            string evidence = assembly.Evidence ?? string.Empty;

            string yoryoPath = LocateOutputFile(outputDirectory, drawingNo, YoryoListFileName);

            if (yoryoPath == null)
            {
                result.MissingFileCount++;

                AddNote(result, string.Format("缺少 {0}（{1}）：跳过要領図部品",
                    YoryoListFileName, Path.Combine(outputDirectory ?? string.Empty, drawingNo)));

                return result;
            }

            List<string[]> yoryoRows;

            try
            {
                yoryoRows = ReadCsv(yoryoPath);
            }
            catch (Exception ex)
            {
                AddNote(result, string.Format("{0}：读取失败（{1}：{2}）", YoryoListFileName,
                    ex.GetType().Name, ex.Message));

                return result;
            }

            if (yoryoRows.Count == 0)
            {
                AddNote(result, YoryoListFileName + "：文件为空");
                return result;
            }

            string[] yoryoHeader = yoryoRows[0];
            int drawingNoCol = IndexOfHeader(yoryoHeader, HeaderYoryoDrawingNo);
            int yoryoNameCol = IndexOfHeader(yoryoHeader, HeaderYoryoName);
            int itemCol = IndexOfHeader(yoryoHeader, HeaderItem);

            if (drawingNoCol < 0)
            {
                AddNote(result, YoryoListFileName + "：表头没有「" + HeaderYoryoDrawingNo +
                    "」列（表头：" + JoinHeader(yoryoHeader) + "）");

                return result;
            }

            // 要領図 図番 → 項目 / 要領図漢字（同一図番出现多次时合并項目）
            List<string> yoryoOrder = new List<string>();
            Dictionary<string, string> items = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int rowIndex = 1; rowIndex < yoryoRows.Count; rowIndex++)
            {
                string[] row = yoryoRows[rowIndex];

                if (IsEmptyRow(row))
                {
                    continue;
                }

                string yoryoNo = CleanDrawingNo(Cell(row, drawingNoCol));

                if (yoryoNo.Length == 0)
                {
                    continue;
                }

                result.YoryoRowCount++;

                if (!items.ContainsKey(yoryoNo))
                {
                    yoryoOrder.Add(yoryoNo);
                    items.Add(yoryoNo, Cell(row, itemCol));
                    names.Add(yoryoNo, Cell(row, yoryoNameCol));
                }
                else
                {
                    // 同一要領図 在多个「項目」下出现：項目合并显示（A,M / B / F …）
                    string extra = Cell(row, itemCol);

                    if (extra.Length > 0 && items[yoryoNo].IndexOf(extra, StringComparison.Ordinal) < 0)
                    {
                        items[yoryoNo] = items[yoryoNo].Length == 0
                            ? extra
                            : items[yoryoNo] + " / " + extra;
                    }
                }
            }

            if (yoryoOrder.Count == 0)
            {
                AddNote(result, YoryoListFileName + "：没有读到要領図図番");
                return result;
            }

            AddNote(result, string.Format("{0}：{1} 行 → 要領図 {2} 个（{3}）",
                YoryoListFileName, result.YoryoRowCount, yoryoOrder.Count,
                JoinLimited(yoryoOrder, 6)));

            HashSet<string> addedKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (string yoryoNo in yoryoOrder)
            {
                CollectFromYoryoParts(assembly, outputDirectory, yoryoNo, items[yoryoNo], names[yoryoNo],
                    evidence, startNumber, addedKeys, result);
            }

            return result;
        }


        // ── 品種匹配 ─────────────────────────────────────────────────────────

        /// <summary>
        /// 判定哪些品種出现在分类依据里。
        /// 先按空白 / 标点把依据切成单词做完全一致比较；一个都没命中时退回包含比较
        /// （依据里的各个词可能没有分隔符，例如 <c>H310１７ラシッサ片引戸枠アッセンブリ図</c>）。
        /// </summary>
        /// <param name="kindKeys">该 02 文件里出现过的品種（已归一化）。</param>
        /// <param name="evidence">分类依据原文。</param>
        private static HashSet<string> ResolveMatchedKinds(IEnumerable<string> kindKeys, string evidence)
        {
            HashSet<string> matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> kindList = new List<string>();

            foreach (string kindKey in kindKeys)
            {
                if (!string.IsNullOrEmpty(kindKey))
                {
                    kindList.Add(kindKey);
                }
            }

            if (kindList.Count == 0)
            {
                return matched;
            }

            // ① 单词完全一致
            List<string> tokens = Tokenize(evidence);

            if (tokens.Count > 0)
            {
                Dictionary<string, bool> tokenSet = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

                foreach (string token in tokens)
                {
                    string key = NormalizeKey(token);

                    if (key.Length > 0 && !tokenSet.ContainsKey(key))
                    {
                        tokenSet.Add(key, true);
                    }
                }

                foreach (string kindKey in kindList)
                {
                    if (tokenSet.ContainsKey(kindKey))
                    {
                        matched.Add(kindKey);
                    }
                }
            }

            if (matched.Count > 0)
            {
                return matched;
            }

            // ② 退回包含比较
            string flat = NormalizeKey(evidence);

            if (flat.Length == 0)
            {
                return matched;
            }

            foreach (string kindKey in kindList)
            {
                if (flat.IndexOf(kindKey, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    matched.Add(kindKey);
                }
            }

            return matched;
        }

        /// <summary>把分类依据按空白与常见标点切成单词（去掉空串）。</summary>
        private static List<string> Tokenize(string text)
        {
            List<string> tokens = new List<string>();

            if (string.IsNullOrEmpty(text))
            {
                return tokens;
            }

            string[] parts = text.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);

            foreach (string part in parts)
            {
                string token = part.Trim();

                if (token.Length > 0)
                {
                    tokens.Add(token);
                }
            }

            return tokens;
        }

        /// <summary>
        /// 匹配用的归一化：全角 ASCII（含全角数字 / 字母）转半角，去掉空白，便于与 PDF 正文比较。
        /// </summary>
        private static string NormalizeKey(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(value.Length);

            foreach (char ch in value)
            {
                char current = ch;

                if (ch >= '\uFF01' && ch <= '\uFF5E')
                {
                    current = (char)(ch - 0xFEE0);
                }
                else if (ch == '\u3000')
                {
                    current = ' ';
                }

                if (current == ' ' || current == '\t' || current == '\r' || current == '\n')
                {
                    continue;
                }

                builder.Append(current);
            }

            return builder.ToString().ToUpperInvariant();
        }

        /// <summary>把清单里的図番整理成文件夹名（去掉可能的「.pdf」与路径）。</summary>
        private static string CleanDrawingNo(string value)
        {
            string text = (value ?? string.Empty).Trim();

            if (text.Length == 0)
            {
                return string.Empty;
            }

            if (text.IndexOf('\\') >= 0 || text.IndexOf('/') >= 0)
            {
                text = Path.GetFileName(text);
            }

            if (text.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(0, text.Length - 4);
            }

            return text.Trim();
        }

        /// <summary>备注文本：<c>品種：片引戸；項目：A,M</c>。</summary>
        private static string BuildRemark(string kindText, string item)
        {
            string remark = RemarkKindPrefix + (kindText ?? string.Empty).Trim();

            string itemText = (item ?? string.Empty).Trim();

            if (itemText.Length > 0)
            {
                remark += "；項目：" + itemText;
            }

            return remark;
        }

        // ── 文件定位 ─────────────────────────────────────────────────────────

        /// <summary>
        /// 在输出根目录下找 <c>&lt;図番&gt;\&lt;fileName&gt;</c>；没有再找
        /// <c>&lt;図番&gt;_P2\&lt;fileName&gt;</c>（多页 PDF 的第 2 页起）等分页目录。
        /// 改名前的旧名字（<see cref="LegacyYoryoPartFileName"/> 等）也认。都找不到返回 null。
        /// </summary>
        public static string LocateOutputFile(string outputDirectory, string drawingNo, string fileName)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory) || string.IsNullOrWhiteSpace(drawingNo) ||
                string.IsNullOrWhiteSpace(fileName) || !Directory.Exists(outputDirectory))
            {
                return null;
            }

            string stem = drawingNo.Trim();
            string direct = FindInFolder(Path.Combine(outputDirectory, stem), fileName);

            if (direct != null)
            {
                return direct;
            }

            string[] folders;

            try
            {
                folders = Directory.GetDirectories(outputDirectory, stem + "_P*");
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }

            List<string> ordered = new List<string>(folders);
            ordered.Sort(StringComparer.OrdinalIgnoreCase);

            foreach (string folder in ordered)
            {
                string path = FindInFolder(folder, fileName);

                if (path != null)
                {
                    return path;
                }
            }

            return null;
        }

        /// <summary>在 <paramref name="folder"/> 里找 <paramref name="fileName"/>；找不到时再试改名前的旧名字。</summary>
        private static string FindInFolder(string folder, string fileName)
        {
            if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(fileName) || !Directory.Exists(folder))
            {
                return null;
            }

            string path = Path.Combine(folder, fileName);

            if (File.Exists(path))
            {
                return path;
            }

            if (string.Equals(fileName, YoryoPartFileName, StringComparison.OrdinalIgnoreCase))
            {
                path = Path.Combine(folder, LegacyYoryoPartFileName);

                if (File.Exists(path))
                {
                    return path;
                }
            }

            return null;
        }

        /// <summary>该路径是否是要領図 的部品明细文件（<c>02_要領図_部品機能情報.csv</c> 或旧名 <c>LT_STEP2.csv</c>）。</summary>
        public static bool IsYoryoPartFile(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            return path.EndsWith(YoryoPartFileName, StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(LegacyYoryoPartFileName, StringComparison.OrdinalIgnoreCase);
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
                    if (ch == '\"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '\"')
                        {
                            field.Append('\"');
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
                    case '\"':
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

        /// <summary>取指定列的文本（列号为 -1 或越界时返回空字符串），去掉首尾空白。</summary>
        private static string Cell(string[] row, int column)
        {
            if (row == null || column < 0 || column >= row.Length)
            {
                return string.Empty;
            }

            return (row[column] ?? string.Empty).Trim();
        }

        /// <summary>先按完全一致、再按「包含」找表头列，找不到返回 -1。</summary>
        private static int IndexOfHeader(string[] header, string name)
        {
            if (header == null || string.IsNullOrEmpty(name))
            {
                return -1;
            }

            for (int index = 0; index < header.Length; index++)
            {
                if (string.Equals(Normalize(header[index]), name, StringComparison.Ordinal))
                {
                    return index;
                }
            }

            for (int index = 0; index < header.Length; index++)
            {
                if (Normalize(header[index]).IndexOf(name, StringComparison.Ordinal) >= 0)
                {
                    return index;
                }
            }

            return -1;
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

        /// <summary>用「/」连接前 <paramref name="max"/> 项，超出部分用「…」表示。</summary>
        private static string JoinLimited(IList<string> values, int max)
        {
            if (values == null || values.Count == 0)
            {
                return "（无）";
            }

            List<string> shown = new List<string>();

            for (int index = 0; index < values.Count && index < max; index++)
            {
                shown.Add(values[index]);
            }

            string text = string.Join(" / ", shown.ToArray());

            return values.Count > max ? text + " …" : text;
        }

        /// <summary>截断过长文本（日志里显示要領図名称用）。</summary>
        private static string Shorten(string value, int max)
        {
            string text = (value ?? string.Empty).Trim();

            if (max <= 0 || text.Length <= max)
            {
                return text.Length == 0 ? "（无名称）" : text;
            }

            return text.Substring(0, max) + "…";
        }

        /// <summary>往结果里追加一条提示。</summary>
        private static void AddNote(CmdInstructionPartResult result, string note)
        {
            if (result != null && !string.IsNullOrEmpty(note))
            {
                result.Notes.Add(note);
            }
        }
    }
}



