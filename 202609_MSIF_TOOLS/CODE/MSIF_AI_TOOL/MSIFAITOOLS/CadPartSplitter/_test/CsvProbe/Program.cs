using System;
using System.Collections.Generic;
using System.IO;

namespace CadPartSplitter
{
    /// <summary>
    /// 「单元格内换行 → `~`」探针：验证 <see cref="CsvFile.SingleLine"/> 以及它带来的
    /// 「一格一行」效果。典型场景：PDF 的一格里写着两行字（`部品セット` / `三方枠`），
    /// 以前会带着换行进 CSV，Excel 与按行读 CSV 的下游脚本就串行了。
    ///
    /// 用法（无需参数）：
    ///   dotnet run --project _test\CsvProbe
    ///
    /// 退出码：0 = 全部通过；1 = 有断言失败。
    /// </summary>
    internal static class Program
    {
        private static int _failed;

        private static int Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            TestSingleLine();
            TestEscape();
            TestCsvRoundTrip();
            TestRuledTableCsv();

            Console.WriteLine();
            Console.WriteLine(_failed == 0
                ? "[PASS] 全部通过（换行一律压成 " + CsvFile.LineBreakPlaceholder + "，一格一行）"
                : "[FAIL] " + _failed + " 项不通过");

            return _failed == 0 ? 0 : 1;
        }

        // ── 规则本体 ──────────────────────────────────────────────────────────

        /// <summary>换行压平：CRLF / CR / LF 都压成 '~'，连续换行只留一个，行首行尾不补。</summary>
        private static void TestSingleLine()
        {
            Section("CsvFile.SingleLine");

            Check("空串", CsvFile.SingleLine(string.Empty), string.Empty);
            Check("null", CsvFile.SingleLine(null), string.Empty);
            Check("无换行原样返回", CsvFile.SingleLine("部品セット"), "部品セット");
            Check("\\n  → ~", CsvFile.SingleLine("部品\nセット"), "部品~セット");
            Check("\\r\\n → ~", CsvFile.SingleLine("部品\r\nセット"), "部品~セット");
            Check("\\r  → ~", CsvFile.SingleLine("見込み\r部品"), "見込み~部品");
            Check("连续换行只留一个 ~", CsvFile.SingleLine("A\n\r\nB"), "A~B");
            Check("行首 / 行尾换行丢掉", CsvFile.SingleLine("\nA\n"), "A");
        }

        /// <summary>转义：先压平再按 RFC 4180 加引号（逗号 / 引号仍按原规则转义）。</summary>
        private static void TestEscape()
        {
            Section("CsvFile.Escape");

            Check("普通字段不加引号", CsvFile.Escape("MZA968"), "MZA968");
            Check("换行格：无逗号不加引号", CsvFile.Escape("部品\nセット"), "部品~セット");
            Check("换行 + 逗号：压平后加引号", CsvFile.Escape("A,B\nC"), "\"A,B~C\"");
            Check("换行 + 引号：引号翻倍", CsvFile.Escape("他说\"好\"\n吧"), "\"他说\"\"好\"\"~吧\"");
            Check("整格只有换行 → 空", CsvFile.Escape("\r\n"), string.Empty);
        }

        /// <summary>写 → 读：行数 / 列数不会被格子里的换行打乱。</summary>
        private static void TestCsvRoundTrip()
        {
            Section("CsvFile.Write / Read");

            string path = Path.Combine(GetOutDirectory(), "roundtrip.csv");

            var header = new[] { "番号", "部品名", "部品コード" };

            var rows = new List<string[]>
            {
                new[] { "1", "部品セット\r\n三方枠", "MDU762" },
                new[] { "2", "鬼目ナット\n（ブレーキ取付用）", "MDT955,\"特注\"" },
            };

            CsvFile.Write(path, header, rows);

            string[] physical = File.ReadAllLines(path);
            Check("物理行数 = 表头 1 + 明细 2", physical.Length, 3);
            Check("第 2 行即第 1 条明细", physical[1], "1,部品セット~三方枠,MDU762");
            Check("第 3 行即第 2 条明细", physical[2], "2,鬼目ナット~（ブレーキ取付用）,\"MDT955,\"\"特注\"\"\"");

            List<string[]> back = CsvFile.Read(path);
            Check("读回行数", back.Count, 3);
            Check("读回列数（表头）", back[0].Length, 3);
            Check("读回列数（明细 1）", back[1].Length, 3);
            Check("读回列数（明细 2）", back[2].Length, 3);
            Check("读回明细 1 的部品名", back[1][1], "部品セット~三方枠");
            Check("读回明细 2 的部品コード", back[2][2], "MDT955,\"特注\"");
        }

        /// <summary>tables\table_NN.csv 与 tables_text.txt：一格不跨行。</summary>
        private static void TestRuledTableCsv()
        {
            Section("RuledTableWriter（tables\\*.csv / tables_text.txt）");

            string dir = GetOutDirectory();

            var opts = new DetectorOptions
            {
                PdfPageIndex = 0,
                RenderDpi = 300,
                OutputDirectory = dir,
                Logger = delegate { }
            };

            var table = new RuledTable
            {
                Name = "table_01",
                RowCount = 2,
                ColumnCount = 3,
                Rows = new List<RuledTableRow>
                {
                    new RuledTableRow
                    {
                        Index = 1,
                        Cells = new List<RuledTableCell>
                        {
                            new RuledTableCell { Row = 1, Column = 1, Text = "番号", WordCount = 1 },
                            new RuledTableCell { Row = 1, Column = 2, Text = "部品名", WordCount = 1 },
                            new RuledTableCell { Row = 1, Column = 3, Text = "部品コード", WordCount = 1 },
                        }
                    },
                    new RuledTableRow
                    {
                        Index = 2,
                        Cells = new List<RuledTableCell>
                        {
                            new RuledTableCell { Row = 2, Column = 1, Text = "1", WordCount = 1 },
                            new RuledTableCell { Row = 2, Column = 2, Text = "部品セット\r\n三方枠", WordCount = 2 },
                            new RuledTableCell { Row = 2, Column = 3, Text = "MZA968,\"特注\"", WordCount = 1 },
                        }
                    }
                }
            };

            int written = RuledTableWriter.Save(
                new List<RuledTable> { table },
                dir,
                Path.Combine(dir, "probe.pdf"),
                opts);

            Check("Save 写出的文件数（json + txt + 1 张 csv）", written, 3);

            string csvPath = Path.Combine(dir, "tables", table.Name + ".csv");
            string[] physical = File.ReadAllLines(csvPath);
            Check("table_01.csv 物理行数 = 2（格子里的换行没有撑出新行）", physical.Length, 2);
            Check("table_01.csv 第 2 行", physical[1], "1,部品セット~三方枠,\"MZA968,\"\"特注\"\"\"");

            List<string[]> back = CsvFile.Read(csvPath);
            Check("读回行数", back.Count, 2);
            Check("读回列数", back[1].Length, 3);
            Check("读回目标格", back[1][1], "部品セット~三方枠");

            string txtPath = Path.Combine(dir, "tables_text.txt");
            int gridRows = 0;

            foreach (string line in File.ReadAllLines(txtPath))
            {
                if (!line.StartsWith("  │", StringComparison.Ordinal)) continue;

                gridRows++;
                Check("二维表第 " + gridRows + " 行的竖线数（列数 + 1）", CountChar(line, '│'), 4);
            }

            Check("二维表的行数（换行没有把一格撑成两行）", gridRows, 2);
        }

        // ── 断言 ──────────────────────────────────────────────────────────────

        private static void Section(string title)
        {
            Console.WriteLine();
            Console.WriteLine("== " + title + " ==");
        }

        private static void Check(string title, string actual, string expected)
        {
            bool ok = string.Equals(actual, expected, StringComparison.Ordinal);

            if (!ok) _failed++;

            Console.WriteLine((ok ? "  [ok]   " : "  [NG]   ") + title
                              + (ok ? string.Empty : "  期望 <" + Escape(expected) + "> 实际 <" + Escape(actual) + ">"));
        }

        private static void Check(string title, int actual, int expected)
        {
            bool ok = actual == expected;

            if (!ok) _failed++;

            Console.WriteLine((ok ? "  [ok]   " : "  [NG]   ") + title
                              + (ok ? string.Empty : "  期望 " + expected + " 实际 " + actual));
        }

        /// <summary>把控制字符显示出来，断言失败时一眼看出是 CR / LF 在作怪。</summary>
        private static string Escape(string s)
        {
            if (s == null) return "<null>";

            return s.Replace("\r", "\\r").Replace("\n", "\\n");
        }

        private static int CountChar(string s, char ch)
        {
            int n = 0;

            foreach (char c in s)
                if (c == ch) n++;

            return n;
        }

        /// <summary>本次探针的输出目录（每次运行前清空，避免旧产物干扰断言）。</summary>
        private static string GetOutDirectory()
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "out");

            if (Directory.Exists(dir))
                Directory.Delete(dir, true);

            Directory.CreateDirectory(dir);

            return dir;
        }

    }
}
