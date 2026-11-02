using System;
using System.Collections.Generic;
using System.IO;

namespace CadPartSplitter
{
    /// <summary>
    /// 品種表整理：读 <c>tables\table_NN.csv</c>，把「横向重复 N 组 (部品コード, 員数)」这类
    /// 「多列重复」的表摊平成一行一个部品的 <c>LT_STEP2.csv</c>。
    ///
    /// 认得的样子（图纸上常见，表头被拆成两行 + 合并单元格）：
    /// <code>
    /// 品種,部品名 ： 縦枠上部スペーサー,,部品名 ： 小縦枠上部スペーサー,, …
    /// ,部品コード,員数,部品コード,員数, …
    /// ,,,,,,,
    /// 片引戸,MEACC428,1,MEACC428,2, …
    /// </code>
    /// 整理成（一行一个部品，品種 / 品名往下填满）：
    /// <code>
    /// 品種,部品名,部品コード,員数
    /// 片引戸,縦枠上部スペーサー,MEACC428,1
    /// 片引戸,小縦枠上部スペーサー,MEACC428,2
    /// </code>
    ///
    /// 判定（两条同时满足才认）：
    ///   · 表头区里有含「品種」的单元格 → 品種列；
    ///   · 表头区里存在「同一个列名出现 ≥ 2 次」的行（多列重复）→ 每次出现的列就是一组部品ブロック的起点。
    /// 只有一块、没有重复列的表不是这种表，原样跳过（不改动）。
    ///
    /// 输出：<c>&lt;输出目录&gt;\LT_STEP2.csv</c>（UTF-8 带 BOM + CRLF + RFC 4180），
    /// 列固定为 品種 / 部品名 / 部品コード / 員数 —— 下游按固定列取数。
    /// 多张品種表按文件名顺序追加到同一个文件。只读 <c>tables\*.csv</c>，不改动任何已有产物。
    /// </summary>
    public static class LtStep2Builder
    {
        /// <summary>下游约定的文件名（写在输出目录根，与 LT_STEP1.csv 同级）。</summary>
        public const string OutputFileName = "LT_STEP2.csv";

        /// <summary>品種表的标志文字（表头里出现它就认这一列是品種）。</summary>
        private const string VarietyKeyword = "品種";

        /// <summary>块标题的前缀（图纸上写成「部品名 ： 縦枠上部スペーサー」）。</summary>
        private const string PartNameKeyword = "部品名";

        /// <summary>块里的品番列名。</summary>
        private const string PartCodeKeyword = "部品コード";

        /// <summary>块里的数量列名。</summary>
        private const string QuantityKeyword = "員数";

        /// <summary>找表头（品種列 / 重复列名行）时最多看前几行。</summary>
        private const int MaxHeaderScanRows = 6;

        /// <summary>输出表头：固定 4 列，下游按列取数。</summary>
        private static readonly string[] OutHeader =
        {
            VarietyKeyword, PartNameKeyword, PartCodeKeyword, QuantityKeyword
        };

        /// <summary>
        /// 扫描 <paramref name="outputDirectory"/> 下的 <c>tables\*.csv</c> 并写出 <c>LT_STEP2.csv</c>。
        /// </summary>
        /// <returns>写出的明细行数（不含表头）；0 表示没有生成文件。</returns>
        public static int Build(string outputDirectory, DetectorOptions opts)
        {
            DetectorOptions o = opts ?? new DetectorOptions();

            if (!o.BuildLtStep2Table)
            {
                o.Log("[INFO] LT_STEP2 整理已关闭（BuildLtStep2Table = false）。");
                return 0;
            }

            if (string.IsNullOrWhiteSpace(outputDirectory))
                return 0;

            string csvDir = Path.Combine(outputDirectory, "tables");

            if (!Directory.Exists(csvDir))
            {
                o.Log("[INFO] LT_STEP2：没有 tables 目录（本次未生成表格 CSV），跳过。");
                return 0;
            }

            string[] files = Directory.GetFiles(csvDir, "*.csv");

            // 文件名补零（table_01 … table_10），按名字排序即图纸上从上到下的顺序
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            var output = new List<string[]>();
            int tables = 0;

            foreach (string file in files)
            {
                List<string[]> rows;

                try
                {
                    rows = CsvFile.Read(file);
                }
                catch (IOException)
                {
                    continue;   // 被 Excel 占用等情况：跳过这一张，不影响其它表
                }

                if (rows.Count == 0)
                    continue;

                List<string[]> detail = Expand(rows);

                if (detail == null)
                    continue;   // 不是「品種 + 多列重复」表

                tables++;
                output.AddRange(detail);

                o.Log("[INFO] LT_STEP2：" + Path.GetFileName(file) + " 是多列重复的品種表 → 摊平 "
                      + detail.Count + " 条明细。");
            }

            if (tables == 0)
            {
                o.Log("[INFO] LT_STEP2：未找到「含品種列且列名重复」的表格，不生成 " + OutputFileName + "。");
                return 0;
            }

            string path = Path.Combine(outputDirectory, OutputFileName);
            CsvFile.Write(path, OutHeader, output);

            o.Log("[INFO] LT_STEP2：" + tables + " 张品種表 → 共 " + output.Count + " 条明细，已保存：" + path);

            return output.Count;
        }

        // ── 摊平 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 摊平一张表：找「列名重复」的表头行 → 按块拆出 (品種, 部品名, 部品コード, 員数)。
        /// 不是「品種 + 多列重复」表 → 返回 null（调用方跳过，不动这张表）。
        /// </summary>
        private static List<string[]> Expand(List<string[]> rows)
        {
            int maxColumns = 0;

            for (int r = 0; r < rows.Count; r++)
            {
                if (rows[r].Length > maxColumns)
                    maxColumns = rows[r].Length;
            }

            if (maxColumns < 3)
                return null;

            // 1) 表头区里找「列名重复」的行 + 含「品種」的列
            int blockRow = -1;
            int varietyColumn = -1;
            string keyName = null;

            int scan = Math.Min(MaxHeaderScanRows, rows.Count);

            for (int r = 0; r < scan; r++)
            {
                if (!HasDuplicateName(rows[r]))
                    continue;

                int varietyColumnFound = FindVarietyColumn(rows, r);

                if (varietyColumnFound < 0)
                    continue;   // 列名是重复的，但没有品種列 → 不是这种表（例如型枠表的 A/B/C 員数列）

                blockRow = r;
                varietyColumn = varietyColumnFound;
                keyName = MostRepeatedName(rows[r]);
                break;
            }

            if (blockRow < 0 || keyName == null)
                return null;

            // 2) 重复列名出现的每一列 = 一组部品ブロック的起点（左 → 右）
            var starts = new List<int>();

            for (int c = 0; c < rows[blockRow].Length; c++)
            {
                if (CsvFile.Normalize(rows[blockRow][c]) == keyName)
                    starts.Add(c);
            }

            if (starts.Count < 2)
                return null;    // 只有一块 → 不是「多列重复」

            // 3) 每块：品名（块标题）+ 品番列 + 員数列
            var names = new List<string>();
            var codeColumns = new List<int>();
            var quantityColumns = new List<int>();

            for (int b = 0; b < starts.Count; b++)
            {
                int start = starts[b];
                int end = (b + 1 < starts.Count) ? starts[b + 1] - 1 : maxColumns - 1;

                names.Add(BlockName(rows, blockRow, start));

                int codeOffset = -1;
                int quantityOffset = -1;

                for (int c = start; c <= end && c < rows[blockRow].Length; c++)
                {
                    string child = CsvFile.Normalize(rows[blockRow][c]);

                    if (codeOffset < 0 && child.IndexOf(PartCodeKeyword, StringComparison.Ordinal) >= 0)
                        codeOffset = c - start;

                    if (quantityOffset < 0 && child.IndexOf(QuantityKeyword, StringComparison.Ordinal) >= 0)
                        quantityOffset = c - start;
                }

                // 块里没有「部品コード」列名时，退回块首列（很多表把品番直接写在标题行那一列下面）
                codeColumns.Add(start + (codeOffset >= 0 ? codeOffset : 0));
                quantityColumns.Add(quantityOffset >= 0 ? start + quantityOffset : -1);
            }

            // 4) 明细行：品種往下填满，每块有品番就出一条
            var detail = new List<string[]>();
            string variety = string.Empty;

            for (int r = blockRow + 1; r < rows.Count; r++)
            {
                string[] row = rows[r];

                if (CsvFile.IsEmptyRow(row))
                    continue;   // 合并单元格 / 表格线造成的空行

                string value = Cell(row, varietyColumn).Trim();
                string normalized = CsvFile.Normalize(value);

                // 合并单元格：品種只写在第一行，下面留空 → 沿用上一个（表头行本身不算）
                if (normalized.Length > 0
                    && normalized.IndexOf(VarietyKeyword, StringComparison.Ordinal) < 0)
                    variety = value;

                for (int b = 0; b < starts.Count; b++)
                {
                    string code = Cell(row, codeColumns[b]).Trim();
                    string codeNormalized = CsvFile.Normalize(code);

                    if (codeNormalized.Length == 0)
                        continue;   // 这块这一行没有品番

                    if (codeNormalized.IndexOf(PartCodeKeyword, StringComparison.Ordinal) >= 0)
                        continue;   // 表中间重复出现的表头行

                    string quantity = quantityColumns[b] >= 0
                        ? Cell(row, quantityColumns[b]).Trim()
                        : string.Empty;

                    detail.Add(new string[] { variety, names[b], code, quantity });
                }
            }

            return detail;
        }

        // ── 表头判定 ─────────────────────────────────────────────────────────

        /// <summary>这一行的列名里是否有重复（同一列名出现 ≥ 2 次）—— 多列重复表的标志。</summary>
        private static bool HasDuplicateName(string[] row)
        {
            if (row == null)
                return false;

            for (int c = 0; c < row.Length; c++)
            {
                string name = CsvFile.Normalize(row[c]);

                if (name.Length == 0)
                    continue;

                for (int d = c + 1; d < row.Length; d++)
                {
                    if (CsvFile.Normalize(row[d]) == name)
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 在 <paramref name="blockRow"/> 及它上方这两三行里找含「品種」的列（表头常被拆成两行），
        /// -1 表示这张表没有品種列 → 不是这种表。
        /// </summary>
        private static int FindVarietyColumn(List<string[]> rows, int blockRow)
        {
            int last = Math.Min(blockRow, rows.Count - 1);

            for (int r = 0; r <= last; r++)
            {
                string[] row = rows[r];

                for (int c = 0; c < row.Length; c++)
                {
                    if (CsvFile.Normalize(row[c]).IndexOf(VarietyKeyword, StringComparison.Ordinal) >= 0)
                        return c;
                }
            }

            return -1;
        }

        /// <summary>
        /// 出现次数最多的列名（≥ 2 次）；次数相同时取最左边那个 —— 图纸上块的首列是品番
        /// （「部品コード」），右邻是「員数」，两者都重复 N 次，靠左优先正好选中品番列。
        /// </summary>
        private static string MostRepeatedName(string[] row)
        {
            string best = null;
            int bestCount = 0;

            for (int c = 0; c < row.Length; c++)
            {
                string name = CsvFile.Normalize(row[c]);

                if (name.Length == 0)
                    continue;

                int count = 0;

                for (int d = 0; d < row.Length; d++)
                {
                    if (CsvFile.Normalize(row[d]) == name)
                        count++;
                }

                if (count > bestCount)
                {
                    best = name;
                    bestCount = count;
                }
            }

            return bestCount >= 2 ? best : null;
        }

        // ── 块标题 / 单元格 ──────────────────────────────────────────────────

        /// <summary>
        /// 块的品名：从「列名重复行」往上找该块起始列最后一个非空单元格，去掉「部品名 ：」前缀。
        /// </summary>
        private static string BlockName(List<string[]> rows, int blockRow, int column)
        {
            for (int r = blockRow - 1; r >= 0; r--)
            {
                string text = Cell(rows[r], column).Trim();

                if (text.Length > 0)
                    return StripPrefix(text);
            }

            return string.Empty;
        }

        /// <summary>去掉「部品名 ：」「部品名：」这类前缀（全角 / 半角冒号都认）。</summary>
        private static string StripPrefix(string text)
        {
            string s = text.Trim();

            int colon = s.LastIndexOf('：');

            if (colon < 0)
                colon = s.LastIndexOf(':');

            if (colon >= 0)
            {
                string tail = s.Substring(colon + 1).Trim();

                if (tail.Length > 0)
                    return tail;
            }

            if (s.StartsWith(PartNameKeyword, StringComparison.Ordinal))
                s = s.Substring(PartNameKeyword.Length).Trim();

            return s;
        }

        /// <summary>取单元格（越界返回空串）。</summary>
        private static string Cell(string[] row, int column)
        {
            if (row == null || column < 0 || column >= row.Length || row[column] == null)
                return string.Empty;

            return row[column];
        }
    }
}
