using System;
using System.Collections.Generic;
using System.IO;

namespace CadPartSplitter
{
    /// <summary>
    /// 图面一览里的一行：図番号 / 文件名 / 分类，外加「判定依据」（命中的表格 CSV 与单元格原文），
    /// 便于核对分类判得对不对。
    /// </summary>
    public sealed class DrawingListItem
    {
        /// <summary>図番号：PDF 文件名去掉扩展名（例如 <c>H310A20009</c>）。</summary>
        public string DrawingNumber { get; set; }

        /// <summary>文件名：PDF 文件名（带扩展名，例如 <c>H310A20009.pdf</c>）。</summary>
        public string FileName { get; set; }

        /// <summary>
        /// 分类：基本図 / 加工図 / アッセンブリ図 / 要領図；判不出来时为空串
        /// （下游按空值自行决定怎么处理，这里不做猜测）。
        /// </summary>
        public string Category { get; set; }

        /// <summary>命中的单元格原文（判不出来时为 null）。</summary>
        public string MatchedCell { get; set; }

        /// <summary>命中的表格 CSV 文件名（例如 <c>table_05.csv</c>；判不出来时为 null）。</summary>
        public string MatchedTable { get; set; }

        /// <summary>
        /// 本次判分类查的目录：<c>&lt;图纸目录&gt;\AiAppData\&lt;図番号&gt;\tables</c>
        /// （批量模式给了输出根目录时是 <c>&lt;输出根目录&gt;\&lt;図番号&gt;\tables</c>）。
        /// </summary>
        public string TablesDirectory { get; set; }

        /// <summary>是否已经判出分类。</summary>
        public bool Classified
        {
            get { return !string.IsNullOrEmpty(Category); }
        }

        /// <summary>调试用一行描述。</summary>
        public override string ToString()
        {
            return DrawingNumber + " | " + FileName + " | " + (Classified ? Category : "（未识别）");
        }
    }

    /// <summary>
    /// 「図面一覧」生成：遍历一个文件夹里的 PDF，按每张图纸已落盘的
    /// <c>&lt;输出根目录&gt;\&lt;図番号&gt;\tables\*.csv</c>（输出根目录留空即
    /// <c>&lt;输入文件夹&gt;\AiAppData</c>）判定图面分类，
    /// 输出下游约定的 <c>drawing_list.csv</c>（図番号 / 文件名 / 分类）。
    ///
    /// 输出示例：
    /// <code>
    /// 図番号,文件名,分类
    /// H310A20009,H310A20009.pdf,基本図
    /// H310U10004,H310U10004.pdf,加工図
    /// </code>
    ///
    /// 判定规则（与图纸表題欄的写法一一对应，四类按固定优先级依次找）：
    ///   · 図番号 = PDF 文件名去掉扩展名；文件名 = PDF 文件名（带扩展名）；
    ///   · 取「PDF 文件名前 4 位」（<see cref="DrawingPrefixLength"/>）作为识别前缀；
    ///   · 按文件名顺序（<c>table_01.csv</c> … <c>table_10.csv</c>）遍历 <c>tables\*.csv</c> 的
    ///     每一个单元格，找同时含「该前缀」与关键字的单元格，取最后一个命中的；
    ///   · 先找 <c>基本図</c>，命中即定为 基本図；没命中再找 <c>加工図</c>，再
    ///     <c>アッセンブリ図</c>，最后 <c>要領図</c>。四类都没命中 → 分类留空并记日志；
    ///   · 单元格文字先经 <see cref="CsvFile.Normalize"/> 去掉半角 / 全角空格与换行再比较
    ///     （PDF 里的番号常被空格拆开，如 <c>H310 A20009</c>），所以「前 4 位」不受空格影响。
    ///
    /// 说明：
    ///   · 只读 <c>tables\*.csv</c>，不读 PDF、不改动任何已有产物（可随时重跑）；
    ///   · 输出的 CSV 与其它清单同一套规则：UTF-8 带 BOM + CRLF + RFC 4180（Excel 双击不乱码）；
    ///   · 表格 CSV 还没生成（没跑过分割 / 用了 <c>--no-tables</c>）时该行分类留空。
    /// </summary>
    public static class DrawingListBuilder
    {
        /// <summary>下游约定的文件名（与 LT_STEP1.csv / LT_STEP2.csv 同一层目录）。</summary>
        public const string OutputFileName = "drawing_list.csv";

        /// <summary>识别前缀取 PDF 文件名的前几位（图纸上的图番号前缀，例如 <c>H310</c>）。</summary>
        public const int DrawingPrefixLength = 4;

        /// <summary>输出表头：三列固定，下游按列取数。</summary>
        private static readonly string[] Header = { "図番号", "文件名", "分类" };

        /// <summary>
        /// 分类关键字，顺序即优先级：前面的先找、命中即定，后面的不再看。
        /// 例：某张图的表格里既有「H310A20009 基本図」又有「H310A20009 加工図」时定为 基本図。
        /// </summary>
        private static readonly string[] CategoryKeywords = { "基本図", "加工図", "アッセンブリ図", "要領図" };

        /// <summary>tables 子目录名（与 <see cref="RuledTableWriter"/> 写出的位置一致）。</summary>
        private const string TablesFolderName = "tables";

        // ── 采集 ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 遍历 <paramref name="inputFolder"/> 下的 PDF（<paramref name="recursive"/> = true 时含子文件夹，
        /// 与批量分割同一套枚举规则），逐张判定分类。
        /// </summary>
        /// <param name="inputFolder">输入文件夹。</param>
        /// <param name="outputRoot">
        /// 批量模式的输出根目录（<c>&lt;根&gt;\&lt;図番号&gt;\tables</c>）；留空时按单张约定
        /// 取 <c>&lt;图纸目录&gt;\AiAppData\&lt;図番号&gt;\tables</c>。
        /// </param>
        /// <param name="recursive">是否连子文件夹一起扫。</param>
        /// <param name="opts">参数 / 日志（可为 null）。</param>
        public static List<DrawingListItem> Collect(string inputFolder, string outputRoot, bool recursive,
                                                    DetectorOptions opts)
        {
            DetectorOptions o = opts ?? new DetectorOptions();
            var items = new List<DrawingListItem>();

            // 输出根目录：留空 → <输入文件夹>\AiAppData，与 BatchRunner 完全一致。
            // 这里不逐张按「PDF 所在目录」推导，否则递归批量时子文件夹里的图纸会指向别处。
            string root = string.IsNullOrWhiteSpace(outputRoot)
                              ? DetectorOptions.GetDefaultBatchOutputDirectory(inputFolder)
                              : outputRoot;

            foreach (string pdf in BatchRunner.FindPdfFiles(inputFolder, recursive))
                items.Add(ClassifyDrawing(pdf, root, o));

            return items;
        }

        /// <summary>
        /// 判定单张图纸的分类（不读 PDF，只读 <c>tables\*.csv</c>）。
        /// </summary>
        /// <param name="pdfPath">PDF 路径。</param>
        /// <param name="outputRoot">批量输出根目录；留空按默认约定推导。</param>
        /// <param name="opts">参数 / 日志（可为 null）。</param>
        public static DrawingListItem ClassifyDrawing(string pdfPath, string outputRoot, DetectorOptions opts)
        {
            DetectorOptions o = opts ?? new DetectorOptions();

            var item = new DrawingListItem
            {
                DrawingNumber = Path.GetFileNameWithoutExtension(pdfPath),
                FileName = Path.GetFileName(pdfPath),
                Category = string.Empty,
                TablesDirectory = GetTablesDirectory(pdfPath, outputRoot)
            };

            string matchedCell;
            string matchedTable;

            item.Category = Classify(item.DrawingNumber, item.TablesDirectory, o, out matchedCell, out matchedTable);
            item.MatchedCell = matchedCell;
            item.MatchedTable = matchedTable;

            return item;
        }

        // ── 分类判定 ──────────────────────────────────────────────────────────

        /// <summary>
        /// 在 <paramref name="tablesDirectory"/> 的 <c>*.csv</c> 里判定分类，返回
        /// 基本図 / 加工図 / アッセンブリ図 / 要領図；四类都没命中返回空串。
        /// </summary>
        /// <param name="drawingNumber">図番号（PDF 文件名去掉扩展名）。</param>
        /// <param name="tablesDirectory">该图纸的 <c>tables</c> 目录。</param>
        /// <param name="opts">参数 / 日志（可为 null）。</param>
        /// <param name="matchedCell">命中的单元格原文（取最后一个命中的）。</param>
        /// <param name="matchedTable">命中的 CSV 文件名。</param>
        public static string Classify(string drawingNumber, string tablesDirectory, DetectorOptions opts,
                                      out string matchedCell, out string matchedTable)
        {
            matchedCell = null;
            matchedTable = null;

            string prefix = DrawingPrefix(drawingNumber);

            if (prefix.Length == 0 || string.IsNullOrWhiteSpace(tablesDirectory))
                return string.Empty;

            List<string> csvFiles = FindTableCsvFiles(tablesDirectory);

            if (csvFiles.Count == 0)
                return string.Empty;

            // 先把 tables 下的 CSV 一次读进内存（通常十几张小表），四类共用，
            // 避免为了找四个关键字把同一批文件读四遍
            var tables = new List<KeyValuePair<string, List<string[]>>>(csvFiles.Count);

            foreach (string file in csvFiles)
            {
                try
                {
                    tables.Add(new KeyValuePair<string, List<string[]>>(Path.GetFileName(file), CsvFile.Read(file)));
                }
                catch (IOException ex)
                {
                    // 被 Excel 占用 / 读到一半被删：跳过这一张，不影响其它表
                    if (opts != null)
                        opts.Log("[WARN] 图面一览：读不到 " + Path.GetFileName(file)
                                 + "（" + ex.GetType().Name + "），已跳过。");
                }
                catch (UnauthorizedAccessException ex)
                {
                    if (opts != null)
                        opts.Log("[WARN] 图面一览：无权读取 " + Path.GetFileName(file)
                                 + "（" + ex.GetType().Name + "），已跳过。");
                }
            }

            // 四类按优先级依次找：前面的命中就不再往下看
            foreach (string keyword in CategoryKeywords)
            {
                string lastCell = null;
                string lastTable = null;

                foreach (KeyValuePair<string, List<string[]>> table in tables)
                {
                    foreach (string[] row in table.Value)
                    {
                        foreach (string cell in row)
                        {
                            if (string.IsNullOrEmpty(cell))
                                continue;

                            // 先归一化（去空格 / 换行）再比较：PDF 里的番号常被空格拆开
                            string flat = CsvFile.Normalize(cell);

                            if (flat.IndexOf(prefix, StringComparison.OrdinalIgnoreCase) < 0)
                                continue;

                            if (flat.IndexOf(keyword, StringComparison.Ordinal) < 0)
                                continue;

                            // 「最后一个」＝ 文件顺序 + 行序 + 列序里最后出现的那个
                            lastCell = cell;
                            lastTable = table.Key;
                        }
                    }
                }

                if (lastCell != null)
                {
                    matchedCell = lastCell;
                    matchedTable = lastTable;
                    return keyword;
                }
            }

            return string.Empty;
        }

        /// <summary>取识别前缀：PDF 文件名前 <see cref="DrawingPrefixLength"/> 位（不足则整名）。</summary>
        public static string DrawingPrefix(string drawingNumber)
        {
            if (string.IsNullOrWhiteSpace(drawingNumber))
                return string.Empty;

            string name = drawingNumber.Trim();

            return name.Length <= DrawingPrefixLength ? name : name.Substring(0, DrawingPrefixLength);
        }

        /// <summary>
        /// 该图纸的 <c>tables</c> 目录：
        /// <paramref name="outputRoot"/> 留空 → <c>&lt;图纸目录&gt;\AiAppData\&lt;図番号&gt;\tables</c>；
        /// 给了输出根目录 → <c>&lt;输出根目录&gt;\&lt;図番号&gt;\tables</c>（批量模式）。
        /// </summary>
        public static string GetTablesDirectory(string pdfPath, string outputRoot)
        {
            string drawingFolder;

            if (string.IsNullOrWhiteSpace(outputRoot))
            {
                drawingFolder = DetectorOptions.GetDefaultOutputDirectory(pdfPath, 0);
            }
            else
            {
                string root = outputRoot;

                try
                {
                    root = Path.GetFullPath(outputRoot);
                }
                catch (ArgumentException)
                {
                    // 留着原样拼，写到文件时再报错
                }
                catch (NotSupportedException)
                {
                }
                catch (PathTooLongException)
                {
                }

                drawingFolder = Path.Combine(root, Path.GetFileNameWithoutExtension(pdfPath));
            }

            return Path.Combine(drawingFolder, TablesFolderName);
        }

        /// <summary>
        /// 列出 <c>tables</c> 下的 CSV（按文件名排序 —— table_01 … table_10 已补零，
        /// 排序即图纸上从上到下的顺序）。目录不存在 / 读不到时返回空列表。
        /// </summary>
        public static List<string> FindTableCsvFiles(string tablesDirectory)
        {
            var files = new List<string>();

            if (string.IsNullOrWhiteSpace(tablesDirectory) || !Directory.Exists(tablesDirectory))
                return files;

            string[] all;

            try
            {
                all = Directory.GetFiles(tablesDirectory, "*", SearchOption.TopDirectoryOnly);
            }
            catch (UnauthorizedAccessException)
            {
                return files;
            }
            catch (DirectoryNotFoundException)
            {
                return files;
            }

            foreach (string file in all)
            {
                // 不用 "*.csv" 通配：Windows 上它会把 *.csvx 之类的长扩展名也匹配进来
                if (string.Equals(Path.GetExtension(file), ".csv", StringComparison.OrdinalIgnoreCase))
                    files.Add(file);
            }

            files.Sort(StringComparer.OrdinalIgnoreCase);
            return files;
        }

        // ── 落盘 ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 遍历 <paramref name="inputFolder"/> 的 PDF，判定分类后写出一份
        /// <c>drawing_list.csv</c>（図番号 / 文件名 / 分类）。
        /// </summary>
        /// <param name="inputFolder">输入文件夹（只看它下面的 PDF）。</param>
        /// <param name="outputRoot">
        /// 批量输出根目录；留空时按「&lt;图纸目录&gt;\AiAppData\&lt;図番号&gt;」找 tables。
        /// </param>
        /// <param name="outputCsvPath">
        /// CSV 输出路径；留空时落到 <c>&lt;输出根目录&gt;\drawing_list.csv</c>，
        /// 输出根目录也留空时取 <c>&lt;输入文件夹&gt;\AiAppData\drawing_list.csv</c>。
        /// </param>
        /// <param name="recursive">是否连子文件夹一起扫。</param>
        /// <param name="opts">参数 / 日志（可为 null）。</param>
        /// <returns>写出的 CSV 全路径；没有 PDF 或写出失败时返回 null（原因见日志）。</returns>
        public static string Build(string inputFolder, string outputRoot, string outputCsvPath,
                                   bool recursive, DetectorOptions opts)
        {
            DetectorOptions o = opts ?? new DetectorOptions();

            if (string.IsNullOrWhiteSpace(inputFolder) || !Directory.Exists(inputFolder))
            {
                o.Log("[ERROR] 図面一覧：输入文件夹不存在：" + (inputFolder ?? string.Empty));
                return null;
            }

            List<DrawingListItem> items = Collect(inputFolder, outputRoot, recursive, o);

            if (items.Count == 0)
            {
                o.Log("[WARN] 図面一覧：" + inputFolder + " 下没有 PDF，未生成 " + OutputFileName + "。");
                return null;
            }

            foreach (DrawingListItem item in items)
                LogItem(o, item);

            string csvPath = ResolveOutputPath(inputFolder, outputRoot, outputCsvPath);

            var rows = new List<string[]>(items.Count);
            foreach (DrawingListItem item in items)
                rows.Add(new[] { item.DrawingNumber, item.FileName, item.Category });

            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(csvPath));

                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                CsvFile.Write(csvPath, Header, rows);
            }
            catch (IOException ex)
            {
                o.Log("[ERROR] 図面一覧写出失败：" + ex.GetType().Name + "：" + ex.Message);
                return null;
            }
            catch (UnauthorizedAccessException ex)
            {
                o.Log("[ERROR] 図面一覧写出失败：" + ex.GetType().Name + "：" + ex.Message);
                return null;
            }
            catch (ArgumentException ex)
            {
                o.Log("[ERROR] 図面一覧写出失败：" + ex.GetType().Name + "：" + ex.Message);
                return null;
            }
            catch (NotSupportedException ex)
            {
                o.Log("[ERROR] 図面一覧写出失败：" + ex.GetType().Name + "：" + ex.Message);
                return null;
            }

            o.Log(Summary(items, csvPath));
            return csvPath;
        }

        /// <summary>
        /// CSV 输出路径：显式给出就用它；否则 <c>&lt;输出根目录&gt;\drawing_list.csv</c>；
        /// 都没有时 <c>&lt;输入文件夹&gt;\AiAppData\drawing_list.csv</c>（与批量结果同层）。
        /// </summary>
        public static string ResolveOutputPath(string inputFolder, string outputRoot, string outputCsvPath)
        {
            if (!string.IsNullOrWhiteSpace(outputCsvPath))
                return outputCsvPath;

            if (!string.IsNullOrWhiteSpace(outputRoot))
                return Path.Combine(outputRoot, OutputFileName);

            return DefaultOutputPath(inputFolder);
        }

        /// <summary>
        /// 默认输出位置：<c>&lt;输入文件夹&gt;\AiAppData\drawing_list.csv</c>
        /// （与批量分割的每图纸子文件夹同层，下游一次就能找到）。
        /// </summary>
        public static string DefaultOutputPath(string inputFolder)
        {
            return Path.Combine(DetectorOptions.GetDefaultBatchOutputDirectory(inputFolder), OutputFileName);
        }

        // ── 日志 ──────────────────────────────────────────────────────────────

        /// <summary>逐张图纸记一行：分类 + 判定依据（命中的 CSV 与单元格原文）。</summary>
        private static void LogItem(DetectorOptions o, DrawingListItem item)
        {
            if (item.Classified)
            {
                o.Log("[LIST] " + item.FileName + " → " + item.Category
                      + "（" + item.MatchedTable + "：" + CsvFile.SingleLine(item.MatchedCell) + "）");
                return;
            }

            string reason = Directory.Exists(item.TablesDirectory)
                ? "tables 里没有同时含「" + DrawingPrefix(item.DrawingNumber) + "」和"
                  + " 基本図 / 加工図 / アッセンブリ図 / 要領図 的单元格"
                : "没有 tables 目录（" + item.TablesDirectory + "）";

            o.Log("[LIST] " + item.FileName + " → 未识别：" + reason);
        }

        /// <summary>汇总一行：总数 + 各类计数 + 输出路径。</summary>
        private static string Summary(List<DrawingListItem> items, string csvPath)
        {
            var counts = new Dictionary<string, int>();

            foreach (string keyword in CategoryKeywords)
                counts[keyword] = 0;

            int unknown = 0;

            foreach (DrawingListItem item in items)
            {
                if (item.Classified && counts.ContainsKey(item.Category))
                    counts[item.Category]++;
                else
                    unknown++;
            }

            var text = new System.Text.StringBuilder();
            text.Append("[DONE] 図面一覧：共 ").Append(items.Count).Append(" 张（");

            foreach (string keyword in CategoryKeywords)
                text.Append(keyword).Append(' ').Append(counts[keyword]).Append('，');

            text.Append("未识别 ").Append(unknown).Append("）→ ").Append(csvPath);

            return text.ToString();
        }
    }
}
