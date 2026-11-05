using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ExcelDataReader;

namespace CadPartSplitter
{
    /// <summary>
    /// tz.json 里的一条明细 = Excel 部品表（「カメラ」sheet）的一行：
    /// 品種 / 部品名 / 枠区分 / 部品コード / 追加対応特注寸法，
    /// 外加来源信息（文件名 / 図番号 / 表名 / 行号），方便下游回溯到是哪个 Excel 的哪一行。
    /// </summary>
    public sealed class TzEntry
    {
        /// <summary>来源 Excel 的文件名（带扩展名，例如 <c>H310Z10004.XLS</c>）。</summary>
        public string File { get; set; }

        /// <summary>図番号：Excel 文件名去扩展名（例如 <c>H310Z10004</c>）。</summary>
        public string DrawingNumber { get; set; }

        /// <summary>这条明细所在的表名（正常就是 <c>カメラ</c>）。</summary>
        public string Sheet { get; set; }

        /// <summary>这条明细在 Excel 里的行号（1 起，指整张表的行序，不是明细序号）。</summary>
        public int Row { get; set; }

        /// <summary>品種。</summary>
        public string Variety { get; set; }

        /// <summary>部品名。</summary>
        public string PartName { get; set; }

        /// <summary>枠区分。</summary>
        public string FrameKind { get; set; }

        /// <summary>部品コード。</summary>
        public string PartCode { get; set; }

        /// <summary>追加対応特注寸法。</summary>
        public string SpecialSize { get; set; }
    }

    /// <summary>
    /// Excel 部品表 → <c>classify\tz.json</c>：流水线的最后一步
    /// （在分割、<c>tables\*.csv</c>、<c>01_…</c> / <c>02_…</c> 两张成品清单之后）。
    ///
    /// 做两件事：
    /// <list type="number">
    ///   <item>扫「图纸文件夹」里的所有 Excel（<c>.xls</c> / <c>.xlsx</c> / <c>.xlsm</c>，
    ///         <c>~$</c> 临时文件与 <c>AiAppData</c> 里的产物跳过）；</item>
    ///   <item>每个工作簿读「カメラ」sheet 的部品表，取出这五列
    ///         <c>品種 / 部品名 / 枠区分 / 部品コード / 追加対応特注寸法</c>，
    ///         汇总成一份 <c>&lt;输出根目录&gt;\classify\tz.json</c>
    ///         （与 <c>PdfInfoList.txt</c> 同目录，整批共用一份）。</item>
    /// </list>
    ///
    /// 表头认法（与 <see cref="LtStep2Builder"/> 一样按「列名」认，不按列号）：
    ///   · 往下逐行扫（不设深度上限，表头上方的表题 / 変更履歴 / 図面情報都能跳过），
    ///     同一行（或相邻 2~3 行拼起来，表头被拆行时常见）里凑齐这 5 个列名才算找到表头；
    ///   · 先按「整格等于列名」找，找不齐再按「以列名开头」找（表头写成「部品名 ： 縦枠上部スペーサー」那种）；
    ///   · 列名被拆在**同一列的上下两行**时（上面「追加対応」、下面「特注寸法」），
    ///     按「同列上下拼接」再认一遍 —— 一格一格认的做法在这种版式上永远凑不齐 5 个列名；
    ///   · 表头下面一条明细占一行；遇到空行、表头又出现一次（新的一块表）、
    ///     或整行既没有部品名也没有部品コード（合计行 / 备注行）就收这一块；
    ///   · 一份工作簿里有多块这样的表也照样收；
    ///   · 品種 空着（合并单元格的续行）时沿用上一行的值 —— 与 <c>02_要領図_部品機能情報.csv</c> 的摊平一致。
    ///
    /// 选表规则：工作簿里有名为「カメラ」的表就只读它（其余表版式不同，混进来只会添噪音）；
    /// 名字里带「カメラ」的（「カメラ表」之类）也算；都没有时退回
    /// 「每张表都按上面那套 5 列标题找一遍」，并在日志里说明。
    /// 「カメラ」表里一条明细都没收到时（那张表是空表 / 索引），也会退到「全部表再找一遍」。
    ///
    /// 读取用 ExcelDataReader（只读，不依赖机器上装没装 Excel）：
    /// <c>.xls</c> 是老 BIFF 格式，文字按文件里的代码页解，兜底代码页用 932（日文 Shift-JIS）。
    ///
    /// 输出：UTF-8 不带 BOM 的 JSON（与 <c>parts_info.json</c> 同一约定），
    /// 字段内的换行按项目统一规则压成 <c>~</c>（同 <see cref="CsvFile.SingleLine"/>）。
    /// 扫不到 Excel、或一张表都没凑齐那 5 列时不写文件，也不删旧文件 ——
    /// 它是整批共用的一份，单张重跑不该把它清掉，只记日志。
    /// 一个工作簿一条明细都没收到时，把读到的工作表原样导成 CSV 放到
    /// <c>&lt;classify&gt;\tz_debug\</c>（<c>文件名__表名.csv</c>）：表头到底写在第几行、
    /// 五个列名到底怎么写，打开这份 CSV 一眼就能对上版式，不用专门去开 Excel。
    /// </summary>
    public static class TzJsonBuilder
    {
        /// <summary>输出文件名（放在 <c>classify</c> 目录下，与 <c>PdfInfoList.txt</c> 同级）。</summary>
        public const string OutputFileName = "tz.json";

        /// <summary>
        /// 优先读的表名：<c>H310Z10004.XLS</c> 那种部品表放在「カメラ」表里，
        /// 工作簿有这张表就只读它。表名比较前经 <see cref="CsvFile.Normalize"/>（去空格 / 换行）。
        /// </summary>
        public const string PreferredSheetName = "カメラ";

        /// <summary>品種列（以下五个列名即要抽出来的五列）。</summary>
        public const string VarietyKeyword = "品種";

        /// <summary>部品名列。</summary>
        public const string PartNameKeyword = "部品名";

        /// <summary>枠区分列。</summary>
        public const string FrameKeyword = "枠区分";

        /// <summary>部品コード列。</summary>
        public const string PartCodeKeyword = "部品コード";

        /// <summary>追加対応特注寸法列。</summary>
        public const string SpecialSizeKeyword = "追加対応特注寸法";

        /// <summary>要抽出来的五列，顺序即 JSON 里的字段顺序（下游按列名取数）。</summary>
        private static readonly string[] ColumnNames =
        {
            VarietyKeyword, PartNameKeyword, FrameKeyword, PartCodeKeyword, SpecialSizeKeyword
        };

        /// <summary>
        /// 表头最多拆成几行来拼（图纸导出的 Excel 常把表头压在 2 行里，
        /// 也有把「追加対応」/「特注寸法」上下两行写在**同一列**里的）。
        /// </summary>
        private const int MaxHeaderRows = 3;

        /// <summary>
        /// 「分块版式」的关键字：行里任何一格含这个字样，就是
        /// 「追加部品セット ： 片引戸用部品セット（ MZA674 ）」那种行 —— 部品名与部品コード 都在这行里。
        /// </summary>
        private const string PartSetKeyword = "追加部品セット";

        /// <summary>分块版式里当「块标题（枠区分）」认的最大字数；超出的当注記行，不当标题。</summary>
        private const int BlockTitleMaxLength = 20;

        /// <summary>
        /// 一条明细都没收到时，把读到的工作表原样导成 CSV 放在 <c>&lt;classify&gt;\tz_debug\</c>，
        /// 用来对照「表头到底写在第几行、五个列名到底怎么写」。
        /// 这里是单个工作表最多导多少行。
        /// </summary>
        private const int DumpMaxRows = 400;

        /// <summary>导出诊断 CSV 时最多写多少列（表很宽时右半边多半是备注，用不着）。</summary>
        private const int DumpMaxColumns = 60;

        /// <summary>一格最多导多少字符（整段说明文截断，诊断只需要看表头怎么写）。</summary>
        private const int DumpMaxCellLength = 200;

        /// <summary>一个工作簿里最多导几张表（表太多时只导前几张，免得刷屏）。</summary>
        private const int DumpMaxSheets = 5;

        /// <summary>诊断 CSV 放在 <c>classify</c> 下的哪个子目录。</summary>
        private const string DumpFolderName = "tz_debug";

        /// <summary>能读的 Excel 扩展名（<c>.xlsb</c> ExcelDataReader 不支持，不列进来）。</summary>
        private static readonly string[] ExcelExtensions = { ".xls", ".xlsx", ".xlsm" };

        /// <summary>老 <c>.xls</c> 没写代码页时的兜底代码页：932 = 日文 Shift-JIS。</summary>
        private const int FallbackCodePage = 932;

        // ── 生成 ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 扫 <paramref name="excelFolder"/> 下的 Excel 部品表，汇总写出
        /// <c>&lt;classifyDirectory&gt;\tz.json</c>。
        /// </summary>
        /// <param name="excelFolder">要扫的文件夹（图纸所在文件夹）。不存在时什么都不做。</param>
        /// <param name="classifyDirectory"><c>classify</c> 目录（tz.json 写在这里）；留空时跳过。</param>
        /// <param name="recursive">是否连子文件夹一起扫（与批量模式的 <c>--recursive</c> 保持一致）。</param>
        /// <param name="opts">参数 / 日志（可为 null）。</param>
        /// <returns>写出的 tz.json 全路径；未生成时返回 null。</returns>
        public static string Build(string excelFolder, string classifyDirectory, bool recursive, DetectorOptions opts)
        {
            DetectorOptions o = opts ?? new DetectorOptions();

            if (!o.BuildTzJson)
            {
                o.Log("[INFO] tz.json（Excel 部品表）已关闭（BuildTzJson = false）。");
                return null;
            }

            if (string.IsNullOrWhiteSpace(excelFolder) || !Directory.Exists(excelFolder))
            {
                o.Log("[INFO] tz.json：扫描文件夹不存在（" + (excelFolder ?? "空") + "），跳过。");
                return null;
            }

            if (string.IsNullOrWhiteSpace(classifyDirectory))
            {
                o.Log("[INFO] tz.json：分类目录为空，跳过。");
                return null;
            }

            List<string> files = FindExcelFiles(excelFolder, recursive);

            if (files.Count == 0)
            {
                o.Log("[INFO] tz.json：文件夹里没有 Excel（.xls / .xlsx / .xlsm），不生成 "
                      + OutputFileName + "（旧文件保持不动）。");
                return null;
            }

            o.Log("[INFO] tz.json：扫到 " + files.Count + " 个 Excel（" + Path.GetFullPath(excelFolder)
                  + (recursive ? "，含子文件夹" : "，仅当前文件夹") + "），读 "
                  + string.Join(" / ", ColumnNames) + " 五列。");

            var items = new List<TzEntry>();
            int usedFiles = 0;

            // 一条明细都没收到时，把读到的工作表原样导到这里：表头到底写在哪儿、五个列名怎么写，
            // 看导出的 CSV 一眼就知道（不用装读取器、也不用开 Excel）。
            string dumpDirectory = Path.Combine(classifyDirectory, DumpFolderName);

            foreach (string file in files)
            {
                List<TzEntry> rows;

                try
                {
                    rows = ReadWorkbook(file, o, dumpDirectory);
                }
                catch (Exception ex)
                {
                    // 外部文件不可控（损坏 / 加了打开密码 / 被别的进程独占）：跳过这一个文件，
                    // 不打断整批（与批量处理「单张失败不影响后面的图纸」同一策略）。
                    o.Log("[WARN] tz.json：读不动 " + Path.GetFileName(file)
                          + "（" + ex.GetType().Name + "：" + ex.Message + "），跳过这个文件。");
                    continue;
                }

                if (rows.Count == 0)
                {
                    o.Log("[INFO] tz.json：" + Path.GetFileName(file) + " 里既没找到同时含 "
                          + string.Join(" / ", ColumnNames) + " 的表头，也不是「追加部品セット + 品種」那种分块版式，跳过"
                          + "（该表的原样导出见 " + dumpDirectory + " 里的 CSV）。");
                    continue;
                }

                usedFiles++;
                items.AddRange(rows);

                o.Log("[INFO] tz.json：" + Path.GetFileName(file) + " → " + rows.Count + " 条明细。");
            }

            if (items.Count == 0)
            {
                // 一条明细都没收到：不写空的 tz.json（整批共用一份，写成空的会把上一次的成果清掉）
                o.Log("[INFO] tz.json：" + files.Count + " 个 Excel 既没凑齐 "
                      + string.Join(" / ", ColumnNames) + " 五列、也没有分块版式，不写 " + OutputFileName
                      + "（旧文件保持不动）；表头实际怎么写见 " + dumpDirectory
                      + " 里导出的 CSV（没有导出说明那张表是空的）。");
                return null;
            }

            string path;

            try
            {
                Directory.CreateDirectory(classifyDirectory);
                path = Path.Combine(classifyDirectory, OutputFileName);
                WriteJson(path, excelFolder, files.Count, items);
            }
            catch (IOException ex)
            {
                o.Log("[WARN] tz.json：写文件失败（" + ex.GetType().Name + "：" + ex.Message + "）。");
                return null;
            }
            catch (UnauthorizedAccessException ex)
            {
                o.Log("[WARN] tz.json：无权限写 " + classifyDirectory + "（" + ex.GetType().Name + "）。");
                return null;
            }

            o.Log("[INFO] tz.json：" + files.Count + " 个 Excel（有表头的 " + usedFiles + " 个）→ 共 "
                  + items.Count + " 条明细，已保存：" + path);

            return path;
        }

        // ── 枚举 Excel ────────────────────────────────────────────────────────

        /// <summary>
        /// 列出文件夹下的 Excel（默认仅当前层，<paramref name="recursive"/> = true 时含子文件夹），
        /// 按完整路径排序（日志与结果可复现）。
        ///
        /// 跳过两类文件：
        ///   · Excel 打开文件时留下的 <c>~$xxx.xls</c> 临时文件（内容不是工作簿）；
        ///   · 输出目录 <c>AiAppData</c> 里的东西（那是本工具的产物，不是图纸）。
        /// 目录不存在或无权访问时返回空列表，不抛异常。
        /// </summary>
        public static List<string> FindExcelFiles(string directory, bool recursive)
        {
            var files = new List<string>();

            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return files;

            string[] all;

            try
            {
                all = Directory.GetFiles(
                    directory,
                    "*",
                    recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
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
                // 不用 "*.xls" 通配：Windows 上它按 8.3 短名规则还会带出别的文件，
                // 这里逐个比较扩展名（不分大小写）
                string ext = Path.GetExtension(file);
                bool match = false;

                for (int i = 0; i < ExcelExtensions.Length; i++)
                {
                    if (string.Equals(ext, ExcelExtensions[i], StringComparison.OrdinalIgnoreCase))
                    {
                        match = true;
                        break;
                    }
                }

                if (!match)
                    continue;

                if (Path.GetFileName(file).StartsWith("~$", StringComparison.Ordinal))
                    continue;

                if (IsUnderAiAppData(directory, file))
                    continue;

                files.Add(file);
            }

            files.Sort(StringComparer.OrdinalIgnoreCase);
            return files;
        }

        /// <summary>该文件是否落在 <c>&lt;扫描目录&gt;\AiAppData\…</c> 里面（输出目录里的不是图纸）。</summary>
        private static bool IsUnderAiAppData(string root, string file)
        {
            string fullRoot, fullFile;

            try
            {
                fullRoot = Path.GetFullPath(root);
                fullFile = Path.GetFullPath(file);
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (NotSupportedException)
            {
                return false;
            }
            catch (PathTooLongException)
            {
                return false;
            }

            if (fullFile.Length <= fullRoot.Length)
                return false;

            string relative = fullFile.Substring(fullRoot.Length).TrimStart(Path.DirectorySeparatorChar,
                                                                             Path.AltDirectorySeparatorChar);
            string[] parts = relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                                            StringSplitOptions.RemoveEmptyEntries);

            // 任意一层目录名是 AiAppData 就当作产物（含子文件夹扫时的 <子目录>\AiAppData\… 也算）；
            // 最后一段是文件名，不参与比较
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (string.Equals(parts[i], DetectorOptions.AiAppDataFolderName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        // ── 读工作簿 ──────────────────────────────────────────────────────────

        /// <summary>
        /// 读一个工作簿：优先只读「カメラ」表，没有这张表时每张表都按 5 列标题找一遍。
        /// 读不动时把异常抛给调用方（那里按单个文件跳过）。
        /// 一条明细都没收到时，把读到的工作表原样导到 <paramref name="dumpDirectory"/>（诊断用）。
        /// </summary>
        private static List<TzEntry> ReadWorkbook(string path, DetectorOptions o, string dumpDirectory)
        {
            string fileName = Path.GetFileName(path);
            string drawingNumber = Path.GetFileNameWithoutExtension(path);

            // .xls 是老 BIFF：文字代码页读文件头里那个；兜底给 932（日文）——
            // 不给兜底时按 1252 解，日文部品名会变成问号。
            var config = new ExcelReaderConfiguration
            {
                FallbackEncoding = Encoding.GetEncoding(FallbackCodePage),
                LeaveOpen = false
            };

            var sheets = new List<KeyValuePair<string, List<string[]>>>();

            using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (IExcelDataReader reader = ExcelReaderFactory.CreateReader(stream, config))
            {
                int index = 0;

                do
                {
                    index++;
                    string sheetName = GetSheetName(reader, index);
                    List<string[]> grid = ReadSheet(reader);
                    sheets.Add(new KeyValuePair<string, List<string[]>>(sheetName, grid));
                }
                while (reader.NextResult());
            }

            // 先把读到的表名列出来：日志一看就知道工作簿里到底有哪些表、有没有「カメラ」
            if (sheets.Count > 0)
            {
                var sheetNames = new List<string>();

                for (int i = 0; i < sheets.Count && i < 8; i++)
                    sheetNames.Add("「" + sheets[i].Key + "」");

                o.Log("[INFO] tz.json：" + fileName + " 共 " + sheets.Count + " 张表："
                      + string.Join(" ", sheetNames.ToArray())
                      + (sheets.Count > sheetNames.Count ? " …" : string.Empty));
            }

            var entries = new List<TzEntry>();
            var targets = new List<KeyValuePair<string, List<string[]>>>();

            // ① 表名正好是「カメラ」→ 只读它（其余表版式不同，混进来只会添噪音）
            foreach (var sheet in sheets)
            {
                if (IsPreferredSheet(sheet.Key, false))
                {
                    targets.Add(sheet);
                    break;      // 「カメラ」只会有一张，命中就不再找
                }
            }

            // ② 表名里带「カメラ」（「カメラ表」「カメラ部品表」之类）→ 也当它是目标表
            if (targets.Count == 0)
            {
                foreach (var sheet in sheets)
                {
                    if (IsPreferredSheet(sheet.Key, true))
                    {
                        targets.Add(sheet);
                        break;
                    }
                }
            }

            // ③ 都没有 → 退回「每张表都按 5 列标题找一遍」
            if (targets.Count == 0)
            {
                targets.AddRange(sheets);

                if (sheets.Count > 0)
                {
                    o.Log("[INFO] tz.json：" + fileName + " 里没有名为「" + PreferredSheetName + "」的表，"
                          + "改按 5 列标题在全部 " + sheets.Count + " 张表里找。");
                }
            }

            foreach (var sheet in targets)
            {
                int before = entries.Count;
                CollectEntries(sheet.Value, fileName, drawingNumber, sheet.Key, entries);

                if (entries.Count > before)
                {
                    o.Log("[INFO] tz.json：" + fileName + " 的「" + sheet.Key + "」表 → "
                          + (entries.Count - before) + " 条明细。");
                }
            }

            // 指定的「カメラ」表里一条都没收到 → 全部表再按 5 列标题找一遍
            // （有些工作簿的「カメラ」表只是空表 / 索引，真正的部品表在别的表里）
            if (entries.Count == 0 && targets.Count > 0 && targets.Count < sheets.Count)
            {
                o.Log("[INFO] tz.json：" + fileName + " 的「" + PreferredSheetName + "」表里没收到底表 5 列，"
                      + "改按 5 列标题在全部 " + sheets.Count + " 张表里再找一遍。");

                foreach (var sheet in sheets)
                {
                    int before = entries.Count;
                    CollectEntries(sheet.Value, fileName, drawingNumber, sheet.Key, entries);

                    if (entries.Count > before)
                    {
                        o.Log("[INFO] tz.json：" + fileName + " 的「" + sheet.Key + "」表 → "
                              + (entries.Count - before) + " 条明细。");
                    }
                }
            }

            // ④ 5 列表头版式一条都没收到 → 换「分块版式」再读一遍（H310Z10004.XLS 的「カメラ」表就是这种：
            //    ① 枠の種類 → 追加部品セット（ 部品コード ）→ 品種 × 追加対応特注寸法 の入れ子）。
            //    5 列を探す読み方では、この版式は絶対に 1 条も取れない。
            if (entries.Count == 0)
            {
                foreach (var sheet in sheets)
                {
                    int before = entries.Count;
                    CollectBlockEntries(sheet.Value, fileName, drawingNumber, sheet.Key, entries);

                    if (entries.Count > before)
                    {
                        o.Log("[INFO] tz.json：" + fileName + " 的「" + sheet.Key + "」表（分块版式）→ "
                              + (entries.Count - before) + " 条明细。");
                    }
                }
            }

            if (entries.Count == 0 && !string.IsNullOrWhiteSpace(dumpDirectory))
                DumpSheetsForDiagnosis(dumpDirectory, fileName, sheets, o);

            return entries;
        }

        /// <summary>取当前表名；读不到时给个 <c>表N</c> 的占位（只影响日志与 JSON 里的 sheet 字段）。</summary>
        private static string GetSheetName(IExcelDataReader reader, int index)
        {
            try
            {
                string name = reader.Name;

                if (!string.IsNullOrWhiteSpace(name))
                    return name.Trim();
            }
            catch (NotSupportedException)
            {
                // 个别格式不带表名：用占位名
            }
            catch (InvalidOperationException)
            {
                // 同上
            }

            return "表" + index.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 表名归一化后等于「カメラ」即认为是要读的那张表；
        /// <paramref name="allowPartial"/> = true 时「カメラ表」「カメラ部品表」这种也算。
        /// </summary>
        private static bool IsPreferredSheet(string sheetName, bool allowPartial)
        {
            string name = CsvFile.Normalize(sheetName);

            if (name.Length == 0)
                return false;

            if (string.Equals(name, PreferredSheetName, StringComparison.Ordinal))
                return true;

            return allowPartial
                   && name.IndexOf(PreferredSheetName, StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// 把当前表整张读成「行 × 列」的字符串表（空行也保留，行号才对应得上 Excel 的行号）。
        /// 单个格子读失败（损坏单元格）只让那一格变成空串，不打断整张表。
        /// </summary>
        private static List<string[]> ReadSheet(IExcelDataReader reader)
        {
            var rows = new List<string[]>();

            while (reader.Read())
            {
                int count = reader.FieldCount;
                var row = new string[count];

                for (int c = 0; c < count; c++)
                {
                    object value;

                    try
                    {
                        value = reader.GetValue(c);
                    }
                    catch (Exception)
                    {
                        value = null;
                    }

                    row[c] = ToText(value);
                }

                rows.Add(row);
            }

            return rows;
        }

        /// <summary>
        /// 单元格值 → 文本：字符串原样，数字去掉多余的 <c>.0</c>（員数 1 不写成 1.0），
        /// 日期写成 <c>yyyy-MM-dd</c>。用不变文化格式化，不受系统区域设置影响。
        /// </summary>
        private static string ToText(object value)
        {
            if (value == null)
                return string.Empty;

            string text = value as string;

            if (text != null)
                return text;

            if (value is bool)
                return ((bool)value) ? "TRUE" : "FALSE";

            if (value is DateTime)
                return ((DateTime)value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            if (value is double || value is float || value is decimal)
            {
                double number = Convert.ToDouble(value, CultureInfo.InvariantCulture);

                if (Math.Abs(number - Math.Round(number)) < 1e-9)
                    return ((long)Math.Round(number)).ToString(CultureInfo.InvariantCulture);

                return number.ToString("0.########", CultureInfo.InvariantCulture);
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        // ── 认表头 / 收明细 ───────────────────────────────────────────────────

        /// <summary>
        /// 在一张表里找出所有「5 列部品表」的块，把明细追加进 <paramref name="output"/>。
        /// 每块 = 表头（可能拆成 2~3 行）+ 若干明细行；块与块之间允许夹空行 / 表题。
        /// </summary>
        private static void CollectEntries(
            List<string[]> grid,
            string fileName,
            string drawingNumber,
            string sheetName,
            List<TzEntry> output)
        {
            if (grid == null || grid.Count == 0)
                return;

            int row = 0;

            while (row < grid.Count)
            {
                int[] columns;
                int headerRows;

                if (!TryFindHeader(grid, row, out columns, out headerRows))
                {
                    row++;      // 不是表头行：往下挪一行继续找（表题 / 变更履歴 / 备注都靠这个跳过）
                    continue;
                }

                int cursor = row + headerRows;
                string lastVariety = string.Empty;

                while (cursor < grid.Count)
                {
                    string[] line = grid[cursor];

                    if (CsvFile.IsEmptyRow(line))
                        break;      // 空行：这一块到此为止

                    int[] again;
                    int againRows;

                    if (TryFindHeader(grid, cursor, out again, out againRows))
                        break;      // 表头又出现一次 → 新的一块，交回外层重新认

                    string partName = Cell(line, columns[1]);
                    string partCode = Cell(line, columns[3]);

                    // 既没有部品名也没有部品コード：合计行 / 备注行 / 表尾，收块
                    if (string.IsNullOrEmpty(CsvFile.Normalize(partName))
                        && string.IsNullOrEmpty(CsvFile.Normalize(partCode)))
                    {
                        break;
                    }

                    string variety = Cell(line, columns[0]);

                    // 品種 是纵向合并单元格：续行的品種格是空的，沿用上一行的值
                    if (string.IsNullOrEmpty(CsvFile.Normalize(variety)))
                        variety = lastVariety;
                    else
                        lastVariety = variety;

                    output.Add(new TzEntry
                    {
                        File          = fileName,
                        DrawingNumber = drawingNumber,
                        Sheet         = sheetName,
                        Row           = cursor + 1,     // 1 起，与 Excel 左边那列行号一致
                        Variety       = variety,
                        PartName      = partName,
                        FrameKind     = Cell(line, columns[2]),
                        PartCode      = partCode,
                        SpecialSize   = Cell(line, columns[4])
                    });

                    cursor++;
                }

                // cursor 一定大于 row（至少跨过表头），这里再兜一次底，避免原地打转
                row = Math.Max(cursor, row + 1);
            }
        }

        // ── 分块版式（「カメラ」表的另一种写法） ─────────────────────────────

        /// <summary>
        /// 按「分块版式」收明细 —— <c>H310Z10004.XLS</c> 的「カメラ」表就是这种：見出しは 5 列そろっておらず、
        /// 枠の種類ごとに次のような入れ子で書かれている（1 ブロックに左右 2 組が並ぶこともある）：
        /// <code>
        /// ① 三方枠
        ///   追加部品セット ： 片引戸用部品セット（ MZA674 ）
        ///   品種          追加対応特注寸法            品種          追加対応特注寸法
        ///   片引戸        2199  ≦Ｗ                   引違戸２枚建  2199  ≦Ｗ
        ///   片引戸２枚建  3549  ≦Ｗ                   引違戸３枚建  3549  ≦Ｗ
        /// </code>
        /// 読み方：
        /// <list type="number">
        ///   <item>行のどこかに「追加部品セット」を含む行 → <c>部品名</c>（括弧の前）と
        ///         <c>部品コード</c>（括弧の中）を取り、以後の明細に効かせる；</item>
        ///   <item>同じ行に「品種」と「追加対応特注寸法」が並ぶ行 → そこから下が明細。
        ///         「品種」の右隣から次の「品種」の手前までを<em>その組の</em>特注寸法とみなす
        ///         （寸法値と <c>≦Ｗ</c> のような条件が 2 列に分かれているので空白で繋いで 1 つにする）；</item>
        ///   <item>唯一の文字が短い見出しの行（<c>① 三方枠</c> / <c>② フラット下レール</c>）→
        ///         それを <c>枠区分</c> として覚える（丸数字と前後の空白は落とす）；</item>
        ///   <item>明細行の品種が全組で空になったら（<c>※…</c> / <c>注）…</c> の注記行）そのブロックは終わり。</item>
        /// </list>
        /// </summary>
        private static void CollectBlockEntries(
            List<string[]> grid,
            string fileName,
            string drawingNumber,
            string sheetName,
            List<TzEntry> output)
        {
            if (grid == null || grid.Count == 0)
                return;

            string frameKind = string.Empty;    // 枠区分（块标题）
            string partName = string.Empty;     // 部品名（追加部品セット の名前）
            string partCode = string.Empty;     // 部品コード（括弧の中の番号）

            int row = 0;

            while (row < grid.Count)
            {
                string[] line = grid[row];

                if (line == null)
                {
                    row++;
                    continue;
                }

                // ① 追加部品セット 行：部品名 / 部品コード を更新（块标题より先に判定する）
                string newName, newCode;

                if (TryParsePartSet(line, out newName, out newCode))
                {
                    partName = newName;
                    partCode = newCode;
                    row++;
                    continue;
                }

                // ② 「品種」＋「追加対応特注寸法」が並ぶ行 = 明細の見出し
                int[] groups;

                if (TryFindVarietyGroups(line, out groups))
                {
                    row = CollectVarietyRows(grid, row + 1, groups, fileName, drawingNumber,
                                             sheetName, frameKind, partName, partCode, output);
                    continue;
                }

                // ③ 短い見出しだけの行 = 块标题（枠区分）
                string title;

                if (TryGetBlockTitle(line, out title))
                    frameKind = title;

                row++;
            }
        }

        /// <summary>
        /// 从「品種 / 追加対応特注寸法」見出し行的下一行起收明细，返回「下一处该继续扫的行号」。
        /// 一行里并排 2 组时（<paramref name="groups"/> 是各组「品種」的列下标）逐组展开成一条明细。
        /// </summary>
        private static int CollectVarietyRows(
            List<string[]> grid,
            int startRow,
            int[] groups,
            string fileName,
            string drawingNumber,
            string sheetName,
            string frameKind,
            string partName,
            string partCode,
            List<TzEntry> output)
        {
            int row = startRow;

            while (row < grid.Count)
            {
                string[] line = grid[row];

                if (line == null || CsvFile.IsEmptyRow(line))
                    break;      // 空行：这一块到此为止

                string title;

                if (TryGetBlockTitle(line, out title))
                    break;      // 下一块的見出し（枠区分）→ 交回外层

                string setName, setCode;

                if (TryParsePartSet(line, out setName, out setCode))
                    break;      // 下一个追加部品セット → 交回外层

                int[] again;

                if (TryFindVarietyGroups(line, out again))
                    break;      // 又出现一行見出し → 交回外层

                int produced = 0;

                for (int g = 0; g < groups.Length; g++)
                {
                    string variety = Cell(line, groups[g]);

                    // 左右两组条数不一样：这一行上那一组本来就是空的，跳过
                    if (CsvFile.Normalize(variety).Length == 0)
                        continue;

                    int from = groups[g] + 1;
                    int to = (g + 1 < groups.Length) ? groups[g + 1] : line.Length;

                    output.Add(new TzEntry
                    {
                        File          = fileName,
                        DrawingNumber = drawingNumber,
                        Sheet         = sheetName,
                        Row           = row + 1,        // 1 起，与 Excel 左边那列行号一致
                        Variety       = variety,
                        PartName      = partName,
                        FrameKind     = frameKind,
                        PartCode      = partCode,
                        SpecialSize   = JoinCells(line, from, to)
                    });

                    produced++;
                }

                if (produced == 0)
                    break;      // 品種 全空 = 注記（※… / 注）…）→ 这一块结束

                row++;
            }

            return row;
        }

        /// <summary>
        /// 找「品種」明细見出し：同一行里至少有一个 <c>品種</c>，且还有 <c>追加対応特注寸法</c>
        /// （含「以列名开头」的写法）。同一行并排 2 组时返回 2 个列下标（左边那组在前）。
        /// </summary>
        private static bool TryFindVarietyGroups(string[] line, out int[] groups)
        {
            groups = null;

            if (line == null)
                return false;

            var found = new List<int>();
            bool hasSpecialSize = false;

            for (int c = 0; c < line.Length; c++)
            {
                string cell = CsvFile.Normalize(line[c]);

                if (cell.Length == 0)
                    continue;

                if (IsColumnName(cell, VarietyKeyword, 1))
                    found.Add(c);
                else if (IsColumnName(cell, SpecialSizeKeyword, 1))
                    hasSpecialSize = true;
            }

            if (found.Count == 0 || !hasSpecialSize)
                return false;

            groups = found.ToArray();
            return true;
        }

        /// <summary>
        /// 「追加部品セット ： 片引戸用部品セット（ MZA674 ）」这种行 → 部品名 / 部品コード。
        /// 部品コードは括弧（全角 / 半角どちらでも）の中、部品名は括弧の手前。
        /// </summary>
        private static bool TryParsePartSet(string[] line, out string partName, out string partCode)
        {
            partName = string.Empty;
            partCode = string.Empty;

            if (line == null)
                return false;

            for (int c = 0; c < line.Length; c++)
            {
                string cell = Cell(line, c);
                int key = cell.IndexOf(PartSetKeyword, StringComparison.Ordinal);

                if (key < 0)
                    continue;

                string rest = cell.Substring(key + PartSetKeyword.Length);
                rest = rest.TrimStart('：', ':', ' ', '\u3000', '＝', '=').Trim();

                int open = rest.IndexOfAny(new[] { '（', '(' });
                string name = rest;
                string code = string.Empty;

                if (open >= 0)
                {
                    name = rest.Substring(0, open).Trim();

                    string tail = rest.Substring(open + 1);
                    int close = tail.IndexOfAny(new[] { '）', ')' });

                    if (close >= 0)
                        tail = tail.Substring(0, close);

                    code = CsvFile.Normalize(tail);     // 括弧の中の前後の空白は落とす
                }

                partName = name;
                partCode = code;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 「① 三方枠」「② フラット下レール」那种：整行只有一个格子有字、字数不多、不是注記 →
        /// 当块标题（= 枠区分），返回去掉前导丸数字与空白后的文字（<c>① 三方枠</c> → <c>三方枠</c>）。
        /// </summary>
        private static bool TryGetBlockTitle(string[] line, out string title)
        {
            title = string.Empty;

            if (line == null)
                return false;

            string only = null;
            int count = 0;

            for (int c = 0; c < line.Length; c++)
            {
                string cell = Cell(line, c);

                if (cell.Length == 0)
                    continue;

                only = cell;
                count++;

                if (count > 1)
                    return false;       // 2 格以上有字：明细行 / 表头行，不是块标题
            }

            if (only == null || only.Length > BlockTitleMaxLength)
                return false;

            if (only.IndexOf(PartSetKeyword, StringComparison.Ordinal) >= 0)
                return false;

            // 注記行（「※…」「注）…」「1．…。」以及带句読点的说明文）不是标题
            if (only.StartsWith("※", StringComparison.Ordinal)
                || only.StartsWith("注", StringComparison.Ordinal)
                || char.IsDigit(only[0])
                || only.IndexOf('。') >= 0
                || only.IndexOf('、') >= 0)
            {
                return false;
            }

            title = StripCircledNumber(only);
            return title.Length > 0;
        }

        /// <summary>去掉块标题开头的丸数字（①〜⑳ / ⑴〜⒇ / ⒈〜⒛）与空白：「① 三方枠」→「三方枠」。</summary>
        private static string StripCircledNumber(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            int i = 0;

            while (i < text.Length)
            {
                char ch = text[i];
                bool circled = (ch >= '\u2460' && ch <= '\u2473')      // ①〜⑳
                               || (ch >= '\u2474' && ch <= '\u2487')    // ⑴〜⒇
                               || (ch >= '\u2488' && ch <= '\u249B')    // ⒈〜⒛
                               || ch == ' ' || ch == '\u3000' || ch == '\t';

                if (!circled)
                    break;

                i++;
            }

            string rest = text.Substring(i).Trim();
            return rest.Length > 0 ? rest : text.Trim();
        }

        /// <summary>把一行的 <paramref name="from"/>〜<paramref name="to"/>-1 格拼成一个字符串（空格分隔、跳过空格子）。</summary>
        private static string JoinCells(string[] line, int from, int to)
        {
            var parts = new List<string>();

            for (int c = from; c < to; c++)
            {
                string cell = Cell(line, c);

                if (cell.Length > 0)
                    parts.Add(cell);
            }

            return string.Join(" ", parts.ToArray());
        }

        /// <summary>
        /// 从 <paramref name="startRow"/> 起找表头：同一行、或相邻 2~3 行拼起来能凑齐 5 个列名即算找到。
        /// 每个行数先按「整格等于列名」找，找不齐再放宽到「以列名开头」（表头写成「部品名 ： …」那种）；
        /// 表头被拆在**同一列的上下两行**时（上面「追加対応」、下面「特注寸法」），再按「同列上下拼接」找一遍。
        /// </summary>
        /// <param name="columns">找到时：5 个列名各自对应的列下标（顺序同 <see cref="ColumnNames"/>）。</param>
        /// <param name="headerRows">找到时：表头占了几行。</param>
        private static bool TryFindHeader(List<string[]> grid, int startRow, out int[] columns, out int headerRows)
        {
            columns = null;
            headerRows = 0;

            if (grid == null || startRow < 0 || startRow >= grid.Count)
                return false;

            // 明细区、备注区里不会有表头：先粗筛「这几行里出现过列名没有」，没出现就直接跳过下一行。
            // 有了这道粗筛就能整张表往下找 —— 表头上方常有表题 / 変更履歴 / 図面情報一大块，
            // 只扫前几十行会整张表漏掉（H310Z10004 那种部品表就在很靠下的位置）。
            if (!MayContainHeader(grid, startRow))
                return false;

            for (int height = 1; height <= MaxHeaderRows; height++)
            {
                if (startRow + height > grid.Count)
                    break;

                for (int pass = 0; pass < 2; pass++)
                {
                    int[] found = MatchHeaderCells(grid, startRow, height, pass);

                    if (HasColumn(found) && !HasDuplicateColumn(found))
                    {
                        columns = found;
                        headerRows = height;
                        return true;
                    }
                }

                // 表头上下拆行（同一个列名被写成两行）：按列把这几行的文字拼起来再认一遍
                if (height >= 2)
                {
                    for (int pass = 0; pass < 2; pass++)
                    {
                        int[] found = MatchHeaderColumnsJoined(grid, startRow, height, pass);

                        if (HasColumn(found) && !HasDuplicateColumn(found))
                        {
                            columns = found;
                            headerRows = height;
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>粗筛：<paramref name="startRow"/> 起 1~3 行里出现过任意一个列名（含前缀写法）才值得细认。</summary>
        private static bool MayContainHeader(List<string[]> grid, int startRow)
        {
            int last = Math.Min(startRow + MaxHeaderRows, grid.Count);

            for (int r = startRow; r < last; r++)
            {
                string[] line = grid[r];

                if (line == null)
                    continue;

                for (int c = 0; c < line.Length; c++)
                {
                    string cell = CsvFile.Normalize(line[c]);

                    if (cell.Length == 0)
                        continue;

                    for (int i = 0; i < ColumnNames.Length; i++)
                    {
                        if (IsColumnName(cell, ColumnNames[i], 1))
                            return true;
                    }
                }
            }

            return false;
        }

        /// <summary>按「一格认一个列名」认表头：返回 5 个列名各自的列下标（没认到的为 -1）。</summary>
        private static int[] MatchHeaderCells(List<string[]> grid, int startRow, int height, int pass)
        {
            var found = new int[ColumnNames.Length];

            for (int i = 0; i < found.Length; i++)
                found[i] = -1;

            for (int h = 0; h < height; h++)
            {
                string[] line = grid[startRow + h];

                if (line == null)
                    continue;

                for (int c = 0; c < line.Length; c++)
                {
                    string cell = CsvFile.Normalize(line[c]);

                    if (cell.Length == 0)
                        continue;

                    for (int i = 0; i < ColumnNames.Length; i++)
                    {
                        if (found[i] >= 0)
                            continue;

                        if (IsColumnName(cell, ColumnNames[i], pass))
                        {
                            found[i] = c;
                            break;
                        }
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// 按「同一列上下拼起来」认表头：列名被拆成上下两行时（「部品」+「コード」、
        /// 「追加対応」+「特注寸法」）也能认到 —— 一格一格认的做法在这种版式上永远凑不齐 5 个列名。
        /// </summary>
        private static int[] MatchHeaderColumnsJoined(List<string[]> grid, int startRow, int height, int pass)
        {
            var found = new int[ColumnNames.Length];

            for (int i = 0; i < found.Length; i++)
                found[i] = -1;

            int columnCount = 0;

            for (int h = 0; h < height; h++)
            {
                string[] line = grid[startRow + h];

                if (line != null && line.Length > columnCount)
                    columnCount = line.Length;
            }

            for (int c = 0; c < columnCount; c++)
            {
                var joined = new StringBuilder();

                for (int h = 0; h < height; h++)
                {
                    string[] line = grid[startRow + h];

                    if (line == null || c >= line.Length)
                        continue;

                    joined.Append(CsvFile.Normalize(line[c]));
                }

                string cell = joined.ToString();

                if (cell.Length == 0)
                    continue;

                for (int i = 0; i < ColumnNames.Length; i++)
                {
                    if (found[i] >= 0)
                        continue;

                    if (IsColumnName(cell, ColumnNames[i], pass))
                    {
                        found[i] = c;
                        break;
                    }
                }
            }

            return found;
        }

        /// <summary>列名匹配：<paramref name="pass"/> 0 = 整格相等，1 = 以列名开头（且不止列名本身）。</summary>
        private static bool IsColumnName(string normalizedCell, string columnName, int pass)
        {
            if (string.Equals(normalizedCell, columnName, StringComparison.Ordinal))
                return true;

            if (pass == 0)
                return false;

            return normalizedCell.Length > columnName.Length
                   && normalizedCell.StartsWith(columnName, StringComparison.Ordinal);
        }

        /// <summary>5 个列名都找到了列。</summary>
        private static bool HasColumn(int[] found)
        {
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] < 0)
                    return false;
            }

            return true;
        }

        /// <summary>同一列被两个列名占着 → 不算找到（表头拆行 + 合并单元格时的误判防护）。</summary>
        private static bool HasDuplicateColumn(int[] found)
        {
            for (int i = 0; i < found.Length; i++)
            {
                for (int j = i + 1; j < found.Length; j++)
                {
                    if (found[i] == found[j])
                        return true;
                }
            }

            return false;
        }

        /// <summary>取一行的某一格（越界 / null 都当空串），并去掉首尾空白。</summary>
        private static string Cell(string[] line, int index)
        {
            if (line == null || index < 0 || index >= line.Length)
                return string.Empty;

            return (line[index] ?? string.Empty).Trim();
        }

        // ── 诊断导出 ──────────────────────────────────────────────────────────

        /// <summary>
        /// 一条明细都没收到时的诊断：把读到的工作表原样导成 CSV，
        /// 放在 <c>&lt;classify&gt;\tz_debug\</c>（名字 <c>文件名__表名.csv</c>）。
        ///
        /// 为什么要有这个：Excel 部品表的版式各家不同（表头写在第几行、五个列名怎么写、
        /// 是不是拆成上下两行），光看日志判断不出「为什么一条都没收到」。把表原样导出，
        /// 打开 CSV 就能对着改提取规则，不用为了看一眼 Excel 去装读取器。
        /// 导出失败只记日志，不影响流水线。
        /// </summary>
        private static void DumpSheetsForDiagnosis(
            string dumpDirectory,
            string fileName,
            List<KeyValuePair<string, List<string[]>>> sheets,
            DetectorOptions o)
        {
            if (sheets == null || sheets.Count == 0)
                return;

            try
            {
                Directory.CreateDirectory(dumpDirectory);
            }
            catch (IOException ex)
            {
                o.Log("[WARN] tz.json：诊断目录建不出来（" + dumpDirectory + "：" + ex.GetType().Name + "）。");
                return;
            }
            catch (UnauthorizedAccessException ex)
            {
                o.Log("[WARN] tz.json：诊断目录建不出来（" + dumpDirectory + "：" + ex.GetType().Name + "）。");
                return;
            }

            int dumped = 0;

            foreach (var sheet in sheets)
            {
                if (dumped >= DumpMaxSheets)
                    break;

                if (sheet.Value == null || sheet.Value.Count == 0)
                    continue;

                string path = Path.Combine(
                    dumpDirectory,
                    SafeFileName(Path.GetFileNameWithoutExtension(fileName))
                    + "__" + SafeFileName(sheet.Key) + ".csv");

                try
                {
                    CsvFile.Write(path, null, ClipRows(sheet.Value));
                    dumped++;

                    o.Log("[INFO] tz.json：诊断用表已导出 → " + path);
                }
                catch (IOException ex)
                {
                    o.Log("[WARN] tz.json：诊断表导出失败（" + path + "：" + ex.GetType().Name + "）。");
                }
                catch (UnauthorizedAccessException ex)
                {
                    o.Log("[WARN] tz.json：诊断表导出失败（" + path + "：" + ex.GetType().Name + "）。");
                }
            }
        }

        /// <summary>
        /// 诊断导出前截断：最多 <see cref="DumpMaxRows"/> 行 × <see cref="DumpMaxColumns"/> 列，
        /// 一格最多 <see cref="DumpMaxCellLength"/> 个字符（诊断只看表头怎么写，不需要全文）。
        /// </summary>
        private static List<string[]> ClipRows(List<string[]> rows)
        {
            var clipped = new List<string[]>();

            if (rows == null)
                return clipped;

            int lineCount = Math.Min(rows.Count, DumpMaxRows);

            for (int r = 0; r < lineCount; r++)
            {
                string[] line = rows[r];

                if (line == null)
                {
                    clipped.Add(new string[0]);
                    continue;
                }

                int count = Math.Min(line.Length, DumpMaxColumns);
                var cells = new string[count];

                for (int c = 0; c < count; c++)
                {
                    string cell = line[c] ?? string.Empty;

                    cells[c] = cell.Length > DumpMaxCellLength
                               ? cell.Substring(0, DumpMaxCellLength)
                               : cell;
                }

                clipped.Add(cells);
            }

            return clipped;
        }

        /// <summary>文件名里不能用的字符换成下划线，过长截断（表名可能带 <c>/</c> <c>:</c> <c>*</c> 之类）。</summary>
        private static string SafeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "表";

            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Trim().Length);

            foreach (char ch in name.Trim())
            {
                bool bad = false;

                for (int i = 0; i < invalid.Length; i++)
                {
                    if (invalid[i] == ch)
                    {
                        bad = true;
                        break;
                    }
                }

                sb.Append(bad ? '_' : ch);
            }

            string safe = sb.ToString().Trim();

            if (safe.Length == 0)
                return "表";

            return safe.Length > 60 ? safe.Substring(0, 60) : safe;
        }

        // ── 写 tz.json ────────────────────────────────────────────────────────

        /// <summary>
        /// 写出 tz.json：UTF-8 不带 BOM（与 <c>parts_info.json</c> 一致），CRLF 缩进两格，
        /// 一条明细一个对象，五个列名直接当字段名（下游按列名取数）。
        /// </summary>
        private static void WriteJson(string path, string sourceFolder, int fileCount, List<TzEntry> items)
        {
            var sb = new StringBuilder();

            sb.AppendLine("{");
            sb.AppendLine("  \"generated\": \"" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "\",");
            sb.AppendLine("  \"source_folder\": \"" + EscapeJson(sourceFolder) + "\",");
            sb.AppendLine("  \"file_count\": " + fileCount.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"item_count\": " + items.Count.ToString(CultureInfo.InvariantCulture) + ",");
            sb.AppendLine("  \"columns\": [");

            for (int i = 0; i < ColumnNames.Length; i++)
            {
                sb.AppendLine("    \"" + EscapeJson(ColumnNames[i]) + "\""
                              + (i < ColumnNames.Length - 1 ? "," : string.Empty));
            }

            sb.AppendLine("  ],");
            sb.AppendLine("  \"items\": [");

            for (int i = 0; i < items.Count; i++)
            {
                TzEntry e = items[i];

                sb.AppendLine("    {");
                sb.AppendLine("      \"file\": \"" + EscapeJson(e.File) + "\",");
                sb.AppendLine("      \"drawing\": \"" + EscapeJson(e.DrawingNumber) + "\",");
                sb.AppendLine("      \"sheet\": \"" + EscapeJson(e.Sheet) + "\",");
                sb.AppendLine("      \"row\": " + e.Row.ToString(CultureInfo.InvariantCulture) + ",");
                sb.AppendLine("      \"" + EscapeJson(VarietyKeyword) + "\": \"" + EscapeJson(e.Variety) + "\",");
                sb.AppendLine("      \"" + EscapeJson(PartNameKeyword) + "\": \"" + EscapeJson(e.PartName) + "\",");
                sb.AppendLine("      \"" + EscapeJson(FrameKeyword) + "\": \"" + EscapeJson(e.FrameKind) + "\",");
                sb.AppendLine("      \"" + EscapeJson(PartCodeKeyword) + "\": \"" + EscapeJson(e.PartCode) + "\",");
                sb.AppendLine("      \"" + EscapeJson(SpecialSizeKeyword) + "\": \"" + EscapeJson(e.SpecialSize) + "\"");
                sb.AppendLine("    }" + (i < items.Count - 1 ? "," : string.Empty));
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        /// <summary>
        /// JSON 字符串转义：换行先按项目统一规则压成 <c>~</c>（<see cref="CsvFile.SingleLine"/>，
        /// 与 CSV 清单同一套写法，下游不必处理 JSON 里的换行），再转义引号 / 反斜杠 / 控制字符。
        /// 中文日文原样写进 UTF-8，不做 \u 转义（文件本身是 UTF-8，人眼看也方便）。
        /// </summary>
        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            string text = CsvFile.SingleLine(value);
            var sb = new StringBuilder(text.Length + 8);

            foreach (char ch in text)
            {
                switch (ch)
                {
                    case '"':
                        sb.Append("\\\"");
                        break;

                    case '\\':
                        sb.Append("\\\\");
                        break;

                    case '\t':
                        sb.Append("\\t");
                        break;

                    case '\b':
                        sb.Append("\\b");
                        break;

                    case '\f':
                        sb.Append("\\f");
                        break;

                    default:
                        if (ch < ' ')
                            sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(ch);

                        break;
                }
            }

            return sb.ToString();
        }
    }
}
