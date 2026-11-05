using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace MSIFAITOOLS.Services
{
    /// <summary>
    /// <c>classify\tz.json</c> 里的一条明细 = Excel 部品表（「カメラ」表）的一行：
    /// 品種 / 部品名 / 枠区分 / 部品コード / 追加対応特注寸法，另带来源（file / sheet / row）。
    /// 结构与 <c>CadPartSplitter</c> 的 <c>TzJsonBuilder</c> 写出的一致（见 docs\分割原理.md 3.15）。
    /// </summary>
    internal sealed class CmdTzEntry
    {
        /// <summary>品種（分类依据里要比对的词）。</summary>
        public string Variety { get; set; }

        /// <summary>部品名。</summary>
        public string PartName { get; set; }

        /// <summary>枠区分。</summary>
        public string FrameKind { get; set; }

        /// <summary>部品コード。</summary>
        public string PartCode { get; set; }

        /// <summary>追加対応特注寸法。</summary>
        public string SpecialSize { get; set; }

        /// <summary>来源 Excel 文件名。</summary>
        public string FileName { get; set; }

        /// <summary>来源表名（正常是 カメラ）。</summary>
        public string Sheet { get; set; }

        /// <summary>来源 Excel 的行号（1 起，回溯用）。</summary>
        public int Row { get; set; }
    }

    /// <summary>
    /// 一次「tz.json 部品追加」的结果：命中的部品明细 + 统计 + 提示。
    /// 由 <see cref="CmdTzPartCollector.Collect"/> 生成，供【CMD 分析】写库。
    /// </summary>
    internal sealed class CmdTzPartResult
    {
        public CmdTzPartResult(string drawingNo)
        {
            DrawingNo = drawingNo ?? string.Empty;
            Parts = new List<PartBasicInfo>();
            Notes = new List<string>();
            MatchedKindTexts = new List<string>();
        }

        /// <summary>アッセンブリ図 の 図番（= 这些部品挂到的図番）。</summary>
        public string DrawingNo { get; private set; }

        /// <summary>命中品種的部品明细（尚未入库）。</summary>
        public List<PartBasicInfo> Parts { get; private set; }

        /// <summary>处理过程中的提示（由调用方写进日志）。</summary>
        public List<string> Notes { get; private set; }

        /// <summary>命中的品種原文（去重，日志用）。</summary>
        public List<string> MatchedKindTexts { get; private set; }

        /// <summary><c>tz.json</c> 里的明细条数。</summary>
        public int EntryCount { get; set; }

        /// <summary>品種命中分类依据的明细条数。</summary>
        public int MatchedEntryCount { get; set; }

        /// <summary>品種为空（不知道属于哪个品種）而跳过的条数。</summary>
        public int BlankKindCount { get; set; }

        /// <summary>部品コード 无效（空 / 占位符 / 不含英文字母）而跳过的条数。</summary>
        public int InvalidCodeRowCount { get; set; }

        /// <summary>同一品種 + 部品名 + 部品コード 重复而跳过的条数。</summary>
        public int SkippedRowCount { get; set; }
    }

    /// <summary>
    /// 【CMD 分析】的追加步骤：<c>classify\tz.json</c>（Excel 部品表）里「品種」出现在
    /// アッセンブリ図 的<b>分类依据</b>中的明细，登记到部品基本信息表（<b>不调用 AI</b>、不写任何文件）。
    ///
    /// 流程：
    /// <list type="number">
    ///   <item>读 <c>&lt;输出根目录&gt;\classify\PdfInfoList.txt</c>（図番号 / 文件名 / 图面分类 / 分类依据），
    ///         取出「图面分类 = アッセンブリ図」的记录的<b>分类依据</b>（形如
    ///         <c>table_05.csv：H310 １７ラシッサ 片引戸 枠 アッセンブリ図</c>）；</item>
    ///   <item>读同目录的 <c>tz.json</c>：顶层 <c>items</c> 数组，每条有
    ///         品種 / 部品名 / 枠区分 / 部品コード / 追加対応特注寸法；</item>
    ///   <item>把分类依据按空白 / 标点切成单词，与 <c>品種</c> 做<b>完全一致</b>比较
    ///         （全角转半角、去空白、忽略大小写）；一个都没命中时再退回<b>包含</b>比较
    ///         —— 与第 3 步「要領図部品追加」的判定完全一致；</item>
    ///   <item>命中的条目转成 <see cref="PartBasicInfo"/>：図番 = アッセンブリ図 の図番、页码 = 1，
    ///         部品名 / 枠区分 / 部品コード 取 tz.json 的同名列，追加対応特注寸法进备注
    ///         （<c>品種：片引戸；特注寸法：2199 ≦Ｗ</c>），校验状态 = 未校验；</item>
    ///   <item>部品コード 必须是含英文字母的编码（空 / 占位符 / 纯数字一律丢弃），
    ///         同一「品種 + 部品名 + 部品コード」只登录一条；</item>
    ///   <item>番号由调用方给起始值后连续编号（唯一索引是 図番 + 页码 + 番号 + 部品コード）。</item>
    /// </list>
    /// </summary>
    internal sealed class CmdTzPartCollector
    {
        /// <summary>Excel 部品表文件名（放在 <c>classify</c> 目录下，与 <c>PdfInfoList.txt</c> 同级）。</summary>
        public const string FileName = "tz.json";

        /// <summary>分类清单目录名。</summary>
        public const string ClassifyFolderName = "classify";

        /// <summary>分类清单文件名。</summary>
        public const string PdfInfoListFileName = "PdfInfoList.txt";

        /// <summary>备注里「品種」的前缀（与第 3 步一致）。</summary>
        public const string RemarkKindPrefix = "品種：";

        /// <summary>备注里「特注寸法」的前缀（追加対応特注寸法）。</summary>
        public const string RemarkSpecialSizePrefix = "特注寸法：";

        /// <summary>tz.json 顶层存放明细的键。</summary>
        private const string ItemKey = "items";

        private const string VarietyKey = "品種";
        private const string PartNameKey = "部品名";
        private const string FrameKindKey = "枠区分";
        private const string PartCodeKey = "部品コード";
        private const string SpecialSizeKey = "追加対応特注寸法";
        private const string FileKey = "file";
        private const string SheetKey = "sheet";
        private const string RowKey = "row";

        /// <summary>PdfInfoList.txt 的列名。</summary>
        private const string HeaderDrawingNo = "図番号";

        private const string HeaderCategory = "图面分类";
        private const string HeaderEvidence = "分类依据";

        /// <summary>分词用的分隔符（与第 3 步 <c>CmdInstructionPartCollector</c> 完全一致）。</summary>
        private static readonly char[] WordSeparators = new char[]
        {
            ' ', '\u3000', '\t', '\r', '\n', ',', '、', '，', '/', '／', '・', '|', '｜',
            ':', '：', ';', '；', '(', ')', '（', '）', '「', '」', '【', '】', '［', '］'
        };

        /// <summary><c>&lt;输出根目录&gt;\classify</c>。</summary>
        public static string GetClassifyDirectory(string outputDirectory)
        {
            return Path.Combine(outputDirectory ?? string.Empty, ClassifyFolderName);
        }

        /// <summary><c>&lt;输出根目录&gt;\classify\tz.json</c>。</summary>
        public static string GetFilePath(string outputDirectory)
        {
            return Path.Combine(GetClassifyDirectory(outputDirectory), FileName);
        }

        /// <summary><c>&lt;输出根目录&gt;\classify\PdfInfoList.txt</c>。</summary>
        public static string GetPdfInfoListPath(string outputDirectory)
        {
            return Path.Combine(GetClassifyDirectory(outputDirectory), PdfInfoListFileName);
        }

        /// <summary>
        /// 读 <c>classify\PdfInfoList.txt</c>，返回「图面分类 = アッセンブリ図」的
        /// 図番 → 分类依据（原文）。读不到文件 / 表头不对时记一条 <paramref name="notes"/> 并返回空表
        /// （调用方随后退回用一览里的分类依据）。
        /// </summary>
        public static Dictionary<string, string> ReadAssemblyEvidence(string outputDirectory,
            IList<string> notes)
        {
            Dictionary<string, string> evidence =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string path = GetPdfInfoListPath(outputDirectory);

            if (!File.Exists(path))
            {
                AddNote(notes, string.Format("找不到 {0}（{1}）：改用一览里的分类依据",
                    PdfInfoListFileName, GetClassifyDirectory(outputDirectory)));

                return evidence;
            }

            List<string[]> rows;

            try
            {
                rows = ReadCsv(path);
            }
            catch (Exception ex)
            {
                AddNote(notes, string.Format("{0}：读取失败（{1}：{2}）", PdfInfoListFileName,
                    ex.GetType().Name, ex.Message));

                return evidence;
            }

            if (rows.Count == 0)
            {
                AddNote(notes, PdfInfoListFileName + "：文件为空");
                return evidence;
            }

            string[] header = rows[0];
            int drawingNoCol = IndexOfHeader(header, HeaderDrawingNo);
            int categoryCol = IndexOfHeader(header, HeaderCategory);
            int evidenceCol = IndexOfHeader(header, HeaderEvidence);

            if (drawingNoCol < 0 || categoryCol < 0)
            {
                AddNote(notes, string.Format("{0}：表头没有「{1} / {2}」列（表头：{3}）",
                    PdfInfoListFileName, HeaderDrawingNo, HeaderCategory, JoinHeader(header)));

                return evidence;
            }

            for (int rowIndex = 1; rowIndex < rows.Count; rowIndex++)
            {
                string[] row = rows[rowIndex];

                if (IsEmptyRow(row))
                {
                    continue;
                }

                if (PdfCategoryNames.Parse(Cell(row, categoryCol)) != PdfCategory.Assembly)
                {
                    continue;
                }

                string drawingNo = CleanDrawingNo(Cell(row, drawingNoCol));

                if (drawingNo.Length == 0 || evidence.ContainsKey(drawingNo))
                {
                    continue;
                }

                evidence.Add(drawingNo, evidenceCol < 0 ? string.Empty : Cell(row, evidenceCol).Trim());
            }

            return evidence;
        }

        /// <summary>
        /// 读 <c>classify\tz.json</c>，返回全部明细（顺序与文件一致）。
        /// 文件缺失 / 解析失败时记一条 <paramref name="notes"/> 并返回空表。
        /// 顶层结构：<c>{ "item_count": N, "items": [ { "品種": …, "部品名": … }, … ] }</c>。
        /// </summary>
        public static List<CmdTzEntry> LoadEntries(string outputDirectory, IList<string> notes)
        {
            List<CmdTzEntry> entries = new List<CmdTzEntry>();
            string path = GetFilePath(outputDirectory);

            if (!File.Exists(path))
            {
                AddNote(notes, string.Format("找不到 {0}（先跑【开始分解】生成 Excel 部品表）：跳过 tz.json 部品",
                    Path.Combine(ClassifyFolderName, FileName)));

                return entries;
            }

            string text;

            try
            {
                text = File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                AddNote(notes, string.Format("{0}：读取失败（{1}：{2}）", FileName,
                    ex.GetType().Name, ex.Message));

                return entries;
            }

            if (text.Trim().Length == 0)
            {
                AddNote(notes, FileName + "：文件为空");

                return entries;
            }

            object root;

            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = int.MaxValue;
                root = serializer.DeserializeObject(text);
            }
            catch (Exception ex)
            {
                AddNote(notes, string.Format("{0}：解析失败（{1}：{2}）", FileName,
                    ex.GetType().Name, ex.Message));

                return entries;
            }

            Dictionary<string, object> map = root as Dictionary<string, object>;
            object itemsValue = null;

            if (map == null || !map.TryGetValue(ItemKey, out itemsValue))
            {
                AddNote(notes, string.Format("{0}：没有「{1}」数组", FileName, ItemKey));

                return entries;
            }

            object[] items = itemsValue as object[];

            if (items == null)
            {
                AddNote(notes, string.Format("{0}：「{1}」不是数组", FileName, ItemKey));

                return entries;
            }

            foreach (object item in items)
            {
                Dictionary<string, object> row = item as Dictionary<string, object>;

                if (row == null)
                {
                    continue;
                }

                CmdTzEntry entry = new CmdTzEntry();
                entry.Variety = GetText(row, VarietyKey);
                entry.PartName = GetText(row, PartNameKey);
                entry.FrameKind = GetText(row, FrameKindKey);
                entry.PartCode = GetText(row, PartCodeKey);
                entry.SpecialSize = GetText(row, SpecialSizeKey);
                entry.FileName = GetText(row, FileKey);
                entry.Sheet = GetText(row, SheetKey);
                entry.Row = GetInt(row, RowKey);
                entries.Add(entry);
            }

            return entries;
        }

        /// <summary>
        /// 把 <c>tz.json</c> 里「品種 ∈ 分类依据」的明细转成部品基本信息（不写库）。
        /// </summary>
        /// <param name="drawingNo">アッセンブリ図 の図番（这些部品挂到它名下）。</param>
        /// <param name="evidence">该図番的「分类依据」原文（来自 PdfInfoList.txt）。</param>
        /// <param name="sourcePath"><c>tz.json</c> 的完整路径（写进「来源表格文件」）。</param>
        /// <param name="pdfFile">该アッセンブリ図 的 PDF 路径（写进「来源PDF」，可为空）。</param>
        /// <param name="entries">已经读好的 tz.json 明细。</param>
        /// <param name="startNumber">本次追加行的起始「番号」（接在已有行之后）。</param>
        public CmdTzPartResult Collect(string drawingNo, string evidence, string sourcePath, string pdfFile,
            IList<CmdTzEntry> entries, int startNumber)
        {
            CmdTzPartResult result = new CmdTzPartResult(drawingNo);

            if (entries == null || entries.Count == 0)
            {
                return result;
            }

            result.EntryCount = entries.Count;

            string evidenceText = evidence ?? string.Empty;
            Dictionary<string, bool> tokens = BuildTokenSet(evidenceText);
            string flatEvidence = NormalizeKey(evidenceText);

            // 先看这次 tz.json 里有哪些品種（非空），再一次性决定「完全一致」还是退回「包含」
            List<string> kindTexts = new List<string>();
            HashSet<string> kindKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool anyTokenHit = false;

            foreach (CmdTzEntry entry in entries)
            {
                if (entry == null)
                {
                    continue;
                }

                string key = NormalizeKey(entry.Variety);

                if (IsBlankKind(key) || kindKeys.Contains(key))
                {
                    continue;
                }

                kindKeys.Add(key);
                kindTexts.Add(entry.Variety.Trim());

                if (tokens.ContainsKey(key))
                {
                    anyTokenHit = true;
                }
            }

            bool useContains = !anyTokenHit && flatEvidence.Length > 0;
            HashSet<string> addedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (CmdTzEntry entry in entries)
            {
                if (entry == null)
                {
                    continue;
                }

                string kindKey = NormalizeKey(entry.Variety);

                if (IsBlankKind(kindKey))
                {
                    result.BlankKindCount++;

                    continue;
                }

                bool matched = useContains
                    ? flatEvidence.IndexOf(kindKey, StringComparison.OrdinalIgnoreCase) >= 0
                    : tokens.ContainsKey(kindKey);

                if (!matched)
                {
                    continue;
                }

                result.MatchedEntryCount++;
                AddKindText(result.MatchedKindTexts, entry.Variety);

                if (!IsValidPartCode(entry.PartCode))
                {
                    result.InvalidCodeRowCount++;

                    continue;
                }

                string dedupeKey = kindKey + "#" + NormalizeKey(entry.PartName) + "#" +
                    NormalizeKey(entry.PartCode);

                if (!addedKeys.Add(dedupeKey))
                {
                    result.SkippedRowCount++;

                    continue;
                }

                PartBasicInfo part = new PartBasicInfo(drawingNo, 1, startNumber + result.Parts.Count,
                    entry.PartName, string.Empty, entry.FrameKind, string.Empty, string.Empty,
                    string.Empty, entry.PartCode);

                part.Remark = BuildRemark(entry.Variety, entry.SpecialSize);
                part.SourceKind = PartSourceKind.CmdTz;
                part.SourceTag = PartSourceTag.CmdTz;
                part.SourceFile = sourcePath ?? string.Empty;
                part.PdfFile = pdfFile ?? string.Empty;
                part.VerifyStatus = PartVerifyStatus.NotChecked;

                result.Parts.Add(part);
            }

            AddCollectNotes(result, kindTexts, kindKeys.Count, useContains);

            return result;
        }

        // ── 日志说明 ─────────────────────────────────────────────────────────

        /// <summary>把「命中 / 未命中 / 丢弃」的统计写成提示行（由调用方写进日志）。</summary>
        private static void AddCollectNotes(CmdTzPartResult result, IList<string> kindTexts, int kindCount,
            bool useContains)
        {
            if (result.MatchedEntryCount == 0)
            {
                if (kindCount == 0)
                {
                    AddNote(result.Notes, FileName + "：没有带品種的明细");
                }
                else
                {
                    AddNote(result.Notes, string.Format("{0}：品種（{1}）都不在分类依据里，跳过",
                        FileName, JoinLimited(kindTexts, 4)));
                }
            }
            else
            {
                AddNote(result.Notes, string.Format("{0}：品種命中 {1} 条（命中品種 {2}{3}）", FileName,
                    result.MatchedEntryCount, JoinLimited(result.MatchedKindTexts, 4),
                    useContains ? "，按包含比较命中" : string.Empty));
            }

            if (result.InvalidCodeRowCount > 0)
            {
                AddNote(result.Notes, string.Format("{0}：部品コード 为空 / 无效（不含英文字母）{1} 条，已跳过",
                    FileName, result.InvalidCodeRowCount));
            }

            if (result.SkippedRowCount > 0)
            {
                AddNote(result.Notes, string.Format("{0}：品種 + 部品名 + 部品コード 重复 {1} 条，已去重",
                    FileName, result.SkippedRowCount));
            }

            if (result.BlankKindCount > 0)
            {
                AddNote(result.Notes, string.Format("{0}：没有品種的明细 {1} 条（无法比对分类依据），已跳过",
                    FileName, result.BlankKindCount));
            }
        }

        /// <summary>备注文字：<c>品種：片引戸；特注寸法：2199 ≦Ｗ</c>（没有特注寸法时只写品種）。</summary>
        private static string BuildRemark(string kindText, string specialSize)
        {
            string remark = RemarkKindPrefix + (kindText ?? string.Empty).Trim();
            string size = (specialSize ?? string.Empty).Trim();

            if (size.Length > 0)
            {
                remark += "；" + RemarkSpecialSizePrefix + size;
            }

            return remark;
        }

        /// <summary>往「命中品種」列表里加一个（忽略大小写去重）。</summary>
        private static void AddKindText(IList<string> texts, string kindText)
        {
            string text = (kindText ?? string.Empty).Trim();

            if (text.Length == 0)
            {
                return;
            }

            foreach (string existing in texts)
            {
                if (string.Equals(existing, text, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            texts.Add(text);
        }

        // ── 品種匹配 ─────────────────────────────────────────────────────────

        /// <summary>把分类依据按空白 / 标点切成单词，返回「归一化词 → true」的集合。</summary>
        private static Dictionary<string, bool> BuildTokenSet(string text)
        {
            Dictionary<string, bool> tokens = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            foreach (string token in Tokenize(text))
            {
                string key = NormalizeKey(token);

                if (key.Length > 0 && !tokens.ContainsKey(key))
                {
                    tokens.Add(key, true);
                }
            }

            return tokens;
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
        /// 匹配用的归一化（与第 3 步 <c>CmdInstructionPartCollector</c> 同一套）：
        /// 全角 ASCII（含全角数字 / 字母）转半角，去掉空白，统一大写。
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

        /// <summary>
        /// 「品種」为空、或只写了占位内容（<c>―</c> / <c>-</c> / <c>／</c> / <c>・</c> /
        /// <c>共 通</c> / <c>なし</c> / <c>無</c> …）= 没有品種，无法比对分类依据。
        /// </summary>
        private static bool IsBlankKind(string kindKey)
        {
            if (string.IsNullOrEmpty(kindKey))
            {
                return true;
            }

            if (string.Equals(kindKey, CmdInstructionPartCollector.SharedKindText, StringComparison.Ordinal) ||
                string.Equals(kindKey, "なし", StringComparison.Ordinal) ||
                string.Equals(kindKey, "ナシ", StringComparison.Ordinal) ||
                string.Equals(kindKey, "無", StringComparison.Ordinal))
            {
                return true;
            }

            foreach (char ch in kindKey)
            {
                if (ch == '-' || ch == '\u2015' || ch == '\u2014' || ch == '/' ||
                    ch == '\u30FB' || ch == '~' || ch == '*' || ch == '_' || ch == '.')
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        /// <summary>
        /// 部品コード 是否有效：去掉空白、全角转半角并大写后，必须至少含一个英文字母（A-Z）。
        /// 因此「空」「―」「-」「/」「・」等占位符、纯数字都判为无效（与第 3 步同一规则）。
        /// </summary>
        private static bool IsValidPartCode(string value)
        {
            string key = NormalizeKey(value);

            if (key.Length == 0)
            {
                return false;
            }

            foreach (char ch in key)
            {
                if (ch >= 'A' && ch <= 'Z')
                {
                    return true;
                }
            }

            return false;
        }

        // ── 小工具 ───────────────────────────────────────────────────────────

        /// <summary>取 JSON 对象里的文字字段（缺字段 / null 时返回空串，数字按不变文化转文字）。</summary>
        private static string GetText(Dictionary<string, object> row, string key)
        {
            object value;

            if (row == null || !row.TryGetValue(key, out value) || value == null)
            {
                return string.Empty;
            }

            string text = value as string;

            return text != null
                ? text.Trim()
                : Convert.ToString(value, CultureInfo.InvariantCulture).Trim();
        }

        /// <summary>取 JSON 对象里的整数字段（读不出来时为 0）。</summary>
        private static int GetInt(Dictionary<string, object> row, string key)
        {
            int value;

            return int.TryParse(GetText(row, key), NumberStyles.Integer, CultureInfo.InvariantCulture,
                out value)
                ? value
                : 0;
        }

        /// <summary>加一条提示（空串忽略）。</summary>
        private static void AddNote(IList<string> notes, string text)
        {
            if (notes == null || string.IsNullOrEmpty(text))
            {
                return;
            }

            notes.Add(text);
        }

        /// <summary>把文字列表拼成 <c>a、b、c</c>，超过 <paramref name="limit"/> 个时加「…」。</summary>
        private static string JoinLimited(IList<string> texts, int limit)
        {
            if (texts == null || texts.Count == 0)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder();

            for (int index = 0; index < texts.Count; index++)
            {
                if (index >= limit)
                {
                    builder.Append("…");

                    break;
                }

                if (builder.Length > 0)
                {
                    builder.Append("、");
                }

                builder.Append(texts[index]);
            }

            return builder.ToString();
        }

        /// <summary>図番去空白（清单里可能写成 <c>H310 U10004</c>）。</summary>
        private static string CleanDrawingNo(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(value.Length);

            foreach (char ch in value)
            {
                if (char.IsWhiteSpace(ch) || (int)ch == 0xFEFF)
                {
                    continue;
                }

                builder.Append(ch);
            }

            return builder.ToString();
        }

        // ── CSV 读取（PdfInfoList.txt：RFC 4180 + BOM，字段内换行已压成 ~） ──

        /// <summary>读一份 CSV（含表头行），交给 <see cref="ParseCsvLine"/> 按行拆分。</summary>
        private static List<string[]> ReadCsv(string path)
        {
            List<string[]> rows = new List<string[]>();

            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                rows.Add(ParseCsvLine(line).ToArray());
            }

            return rows;
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
                    if (c == '\"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '\"')
                        {
                            current.Append('\"');
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

                if (c == '\"')
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

        /// <summary>按归一化后的列名找列下标（找不到返回 -1）。</summary>
        private static int IndexOfHeader(string[] header, string name)
        {
            if (header == null || string.IsNullOrEmpty(name))
            {
                return -1;
            }

            string target = NormalizeHeader(name);

            for (int index = 0; index < header.Length; index++)
            {
                if (string.Equals(NormalizeHeader(header[index]), target, StringComparison.Ordinal))
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

        /// <summary>安全取格子（列不存在时返回空串）。</summary>
        private static string Cell(string[] row, int index)
        {
            if (row == null || index < 0 || index >= row.Length || row[index] == null)
            {
                return string.Empty;
            }

            return row[index];
        }

        /// <summary>整行都是空串时视为空行。</summary>
        private static bool IsEmptyRow(string[] row)
        {
            if (row == null)
            {
                return true;
            }

            foreach (string cell in row)
            {
                if (!string.IsNullOrWhiteSpace(cell))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>表头拼成一行文字（日志用）。</summary>
        private static string JoinHeader(string[] header)
        {
            if (header == null || header.Length == 0)
            {
                return string.Empty;
            }

            return string.Join(" / ", header);
        }
    }
}
