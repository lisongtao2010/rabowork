using System;
using System.Collections.Generic;
using System.IO;

namespace CadPartSplitter
{
    /// <summary>
    /// 要領図一覧：读 <c>tables\table_NN.csv</c>，把「項目 / 要領図漢字 / 図番」这种表抽成下游 AI 环节约定的
    /// <c>01_アッセンブリ図_要領図.csv</c>（开关 <see cref="DetectorOptions.BuildAssemblyYoryoList"/> / <c>--no-assembly-yoryo</c>）。
    ///
    /// 图纸上这张小表列的是「这张組立図用到了哪些要領図」，样子（表头被拆散、中间那列没有标题）：
    /// <code>
    /// ,,,項目,,,,,,,,,,,,図番,
    /// ,,,"A,M",,枠 縦枠/小縦枠スペーサー取付要領図,,,,,,H310U10022,
    /// ,,,B,,枠 ロットNO.ラベル 貼付要領図,,,,,,H310U10034,
    /// </code>
    /// 整理成（固定三列，表头统一写全）：
    /// <code>
    /// 項目,要領図漢字,図番
    /// "A,M",枠 縦枠/小縦枠スペーサー取付要領図,H310U10022
    /// B,枠 ロットNO.ラベル 貼付要領図,H310U10034
    /// </code>
    ///
    /// 判定与取列规则：
    ///   · 表头（含「項目」与「図番」的那一行）同时出现这两个词，这张 CSV 才是对象文件，其余表格原样跳过；
    ///   · 「項目」「図番」按列名找；「要領図漢字」多数图纸上**没有标题** ——
    ///     表头里有就按标题找，没有就取「項目」与「図番」之间第一条有内容的列（图纸上它就夹在这两列中间）；
    ///   · 表头下面一行一条，读到「図番」为空或不是 10 位（如 <c>H310U10022</c>）就停 ——
    ///     再往下是表題欄、変更履歴表之类的杂行，不是要領図一覧。
    ///
    /// 编码与其它清单一致：CSV 用带 BOM 的 UTF-8 + CRLF + RFC 4180 转义（Excel 双击即可打开），
    /// 一格一行（单元格内的换行压成 `~`）。多张表按文件名顺序追加到同一个文件；只读 <c>tables\*.csv</c>，
    /// 不改动任何已有产物（除了图面分类不符时删掉自己上一次写的同名文件）。
    ///
    /// **只有アッセンブリ図（組立図）才写这份清单**（要領図一覧本来就是組立図上的索引）：图面分类读自分类清单
    /// <c>classify\PdfInfoList.txt</c>（<see cref="PdfInfoListBuilder"/> 在流水线里先跑一步生成；清单缺失 /
    /// 没有这张图时退回按 <c>tables\*.csv</c> 现判，同一套规则）。基本図 / 加工図 / 要領図 / 未识别 一律跳过，
    /// 并删掉上一次运行可能留下的同名产物。
    /// </summary>
    public static class AssemblyYoryoBuilder
    {
        /// <summary>
        /// 下游约定的文件名（写在输出目录根，与 <c>01_アッセンブリ図_部品機能情報.csv</c> 同级）。
        /// 编号沿用「01 = アッセンブリ図 系产物」的约定，后面的 <c>要領図</c> 指内容：
        /// 这张組立図用到的要領図一覧（項目 / 要領図漢字 / 図番）。
        /// </summary>
        public const string OutputFileName = "01_アッセンブリ図_要領図.csv";

        /// <summary>「項目」列名（A、B、F … 或 <c>A,M</c> 这种合写；表头里一定有）。</summary>
        private const string ItemHeader = "項目";

        /// <summary>「図番」列名：要領図的図番（表头里一定有，值固定 10 位）。</summary>
        private const string NumberHeader = "図番";

        /// <summary>「要領図漢字」列名：**多数图纸这一列没有标题**，能找到更好，找不到就按位置推断。</summary>
        private const string NameHeader = "要領図漢字";

        /// <summary>図番 的长度：<c>H310U10022</c> 这类固定 10 位；不是 10 位就说明数据区结束了。</summary>
        private const int NumberLength = 10;

        /// <summary>找表头（含「項目」+「図番」的那一行）时最多看前几行。</summary>
        private const int MaxHeaderScanRows = 12;

        /// <summary>推断「要領図漢字」列时，看表头下面几行来判断哪一列有内容。</summary>
        private const int NameProbeRows = 3;

        /// <summary>输出表头：固定三列，下游按列取数。</summary>
        private static readonly string[] OutHeader = { ItemHeader, NameHeader, NumberHeader };

        /// <summary>
        /// 扫描 <paramref name="outputDirectory"/> 下的 <c>tables\*.csv</c> 并写出
        /// <c>01_アッセンブリ図_要領図.csv</c>（图面分类不是「アッセンブリ図」时不写）。
        /// </summary>
        /// <returns>写出的要領図条数（不含表头）；0 表示没有生成文件。</returns>
        public static int Build(string outputDirectory, DetectorOptions opts)
        {
            DetectorOptions o = opts ?? new DetectorOptions();

            if (!o.BuildAssemblyYoryoList)
            {
                o.Log("[INFO] 01_アッセンブリ図_要領図 抽取已关闭（BuildAssemblyYoryoList = false）。");
                return 0;
            }

            if (string.IsNullOrWhiteSpace(outputDirectory))
                return 0;

            // 只有アッセンブリ図（組立図）才有这份索引：先读分类清单 classify\PdfInfoList.txt
            // （流水线刚写好的图面分类），清单缺失 / 没有这张图时退回按 tables\*.csv 现判
            // （与清单同一套规则）。判成别的图类就什么都不写，并清掉上一次留下的同名产物。
            string drawingNumber = PdfInfoListBuilder.DrawingNumberFromOutputDirectory(outputDirectory);
            string categorySource;
            string category = PdfInfoListBuilder.ResolveCategory(outputDirectory, drawingNumber, o, out categorySource);

            if (!PdfInfoListBuilder.IsAssemblyCategory(category))
            {
                o.Log("[INFO] 01_アッセンブリ図_要領図：图面分类「"
                      + (string.IsNullOrEmpty(category) ? "未识别" : category)
                      + "」不是アッセンブリ図，跳过（并清掉旧的同名产物）。");

                DeleteStaleOutput(outputDirectory, o);
                return 0;
            }

            o.Log("[INFO] 01_アッセンブリ図_要領図：图面分类「アッセンブリ図」（依据：" + categorySource + "），开始抽取。");

            string csvDir = Path.Combine(outputDirectory, "tables");

            if (!Directory.Exists(csvDir))
            {
                o.Log("[INFO] 01_アッセンブリ図_要領図：没有 tables 目录（本次未生成表格 CSV），跳过。");
                return 0;
            }

            string[] files = Directory.GetFiles(csvDir, "*.csv");

            // 文件名补零（table_01 … table_10），按名字排序即图纸上从上到下的顺序
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            var output = new List<string[]>();
            int hitTables = 0;

            foreach (string file in files)
            {
                List<string[]> table = LoadCsv(file);

                if (table == null || table.Count == 0)
                    continue;

                int headerRow = FindHeaderRow(table);

                if (headerRow < 0)
                    continue;   // 表头没有「項目」+「図番」：不是要領図一覧，跳过

                int itemColumn   = CsvFile.IndexOfHeader(table[headerRow], ItemHeader);
                int numberColumn = CsvFile.IndexOfHeader(table[headerRow], NumberHeader);
                int nameColumn   = FindNameColumn(table, headerRow, itemColumn, numberColumn);

                hitTables++;
                int before = output.Count;

                for (int r = headerRow + 1; r < table.Count; r++)
                {
                    string[] row = table[r];
                    string number = CsvFile.Normalize(Cell(row, numberColumn));

                    // 図番 空了 / 不是 10 位 → 数据区结束（再往下是表題欄、変更履歴等杂行）
                    if (number.Length != NumberLength)
                        break;

                    output.Add(new[]
                    {
                        CsvFile.SingleLine(Cell(row, itemColumn).Trim()),
                        CsvFile.SingleLine(Cell(row, nameColumn).Trim()),
                        number
                    });
                }

                o.Log("[INFO] 01_アッセンブリ図_要領図：" + Path.GetFileName(file)
                      + "（項目列 " + itemColumn + " / 要領図漢字列 " + (nameColumn < 0 ? "无" : nameColumn.ToString())
                      + " / 図番列 " + numberColumn + "）取到 " + (output.Count - before) + " 条。");
            }

            if (hitTables == 0)
            {
                o.Log("[INFO] 01_アッセンブリ図_要領図：tables\\*.csv 里没有表头含「" + ItemHeader + "」+「"
                      + NumberHeader + "」的表，不生成 " + OutputFileName + "。");
                return 0;
            }

            if (output.Count == 0)
            {
                o.Log("[WARN] 01_アッセンブリ図_要領図：找到 " + hitTables + " 张「" + ItemHeader + " / "
                      + NumberHeader + "」表，但「" + NumberHeader + "」都不是 " + NumberLength + " 位，不生成 "
                      + OutputFileName + "。");
                return 0;
            }

            string path = Path.Combine(outputDirectory, OutputFileName);

            CsvFile.Write(path, OutHeader, output);

            o.Log("[INFO] 01_アッセンブリ図_要領図：" + hitTables + " 张表 → 共 " + output.Count + " 条要領図，已保存：" + path);

            return output.Count;
        }

        // ── CSV 读取 ─────────────────────────────────────────────────────────

        /// <summary>读一张表格 CSV；被 Excel 占用 / 读不动时返回 null（跳过这张，不影响其它表）。</summary>
        private static List<string[]> LoadCsv(string path)
        {
            try
            {
                return CsvFile.Read(path);
            }
            catch (IOException)
            {
                return null;
            }
        }

        // ── 判定 / 取列 ──────────────────────────────────────────────────────

        /// <summary>
        /// 找表头行：同一行里同时有「項目」和「図番」的那一行（-1 = 这张表不是要領図一覧）。
        /// 两个列名常隔着十几个空格子，但都落在表头这一行上，所以按行找。
        /// </summary>
        private static int FindHeaderRow(List<string[]> rows)
        {
            int last = Math.Min(rows.Count, MaxHeaderScanRows);

            for (int r = 0; r < last; r++)
            {
                string[] row = rows[r];

                if (row == null)
                    continue;

                if (CsvFile.IndexOfHeader(row, ItemHeader) >= 0
                    && CsvFile.IndexOfHeader(row, NumberHeader) >= 0)
                {
                    return r;
                }
            }

            return -1;
        }

        /// <summary>
        /// 找「要領図漢字」列：① 表头里写了这个列名就直接用（少数图纸有标题）；
        /// ② 没标题时取「項目」与「図番」之间第一条有内容的列（图纸上它就夹在这两列中间，
        /// 中间常隔着一两个空格子）；③ 版式不同、夹在中间的都空着时，整行里找第一条有内容的列。
        /// -1 表示实在找不到（此时这一列留空，不猜）。
        /// </summary>
        private static int FindNameColumn(List<string[]> rows, int headerRow, int itemColumn, int numberColumn)
        {
            int named = CsvFile.IndexOfHeader(rows[headerRow], NameHeader);

            if (named >= 0 && named != itemColumn && named != numberColumn)
                return named;

            int from = Math.Min(itemColumn, numberColumn) + 1;
            int to   = Math.Max(itemColumn, numberColumn) - 1;

            int between = FirstTextColumn(rows, headerRow, from, to, itemColumn, numberColumn);

            if (between >= 0)
                return between;

            return FirstTextColumn(rows, headerRow, 0, int.MaxValue, itemColumn, numberColumn);
        }

        /// <summary>
        /// 在表头下面 <see cref="NameProbeRows"/> 行里，找 <paramref name="from"/>~<paramref name="to"/>
        /// 这段列中第一条有内容的列（跳过 項目 / 図番 两列）；都没有内容返回 -1。
        /// </summary>
        private static int FirstTextColumn(List<string[]> rows, int headerRow, int from, int to,
                                           int itemColumn, int numberColumn)
        {
            int last = Math.Min(headerRow + NameProbeRows, rows.Count - 1);

            for (int r = headerRow + 1; r <= last; r++)
            {
                string[] row = rows[r];

                if (row == null)
                    continue;

                int max = Math.Min(to, row.Length - 1);

                for (int c = Math.Max(from, 0); c <= max; c++)
                {
                    if (c == itemColumn || c == numberColumn)
                        continue;

                    if (CsvFile.Normalize(Cell(row, c)).Length > 0)
                        return c;
                }
            }

            return -1;
        }

        /// <summary>取单元格（越界 / 缺列 / null 都给空串）。</summary>
        private static string Cell(string[] row, int column)
        {
            if (row == null || column < 0 || column >= row.Length || row[column] == null)
                return string.Empty;

            return row[column];
        }

        // ── 清理 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 这张图不是アッセンブリ図时，把上一次运行留下的同名产物清掉
        /// （上次可能判成アッセンブリ図，留着会让下游按固定名字取到过期内容）。
        /// </summary>
        private static void DeleteStaleOutput(string outputDirectory, DetectorOptions o)
        {
            RemoveIfExists(Path.Combine(outputDirectory, OutputFileName), o);
        }

        /// <summary>
        /// 删一个文件；不存在就什么也不做。删不掉（被 Excel 占用 / 无权限）只记 WARN，不影响本次输出。
        /// </summary>
        private static void RemoveIfExists(string path, DetectorOptions o)
        {
            if (!File.Exists(path))
                return;

            try
            {
                File.Delete(path);
                o.Log("[INFO] 01_アッセンブリ図_要領図：已删除 " + path);
            }
            catch (IOException)
            {
                o.Log("[WARN] 01_アッセンブリ図_要領図：删不掉 " + path + "（可能正被 Excel 打开）。");
            }
            catch (UnauthorizedAccessException)
            {
                o.Log("[WARN] 01_アッセンブリ図_要領図：删不掉 " + path + "（无权限）。");
            }
        }
    }
}
