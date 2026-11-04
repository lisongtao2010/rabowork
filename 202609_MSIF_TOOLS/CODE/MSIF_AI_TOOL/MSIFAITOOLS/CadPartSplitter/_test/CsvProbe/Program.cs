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
            TestStruckRows();
            TestComposeText();
            TestIsolatedCells();

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

        /// <summary>
        /// 划线作废行（取り消し線）与缺格行：
        ///   · 作废行不进 CSV、不进二维表，但要在 <c>tables_text.txt</c> 里留痕；
        ///   · 没有单元格的行（缺格带）仍然占一行，不能被挤上去；
        ///   · 上一次留下的 <c>table_NN.csv</c> 要被清掉（表张数会变）。
        /// </summary>
        private static void TestStruckRows()
        {
            Section("划线作废行（取り消し線）/ 缺格行 / 旧 CSV 清理");

            string dir = GetOutDirectory();

            // 先放一个「上一次运行留下的」CSV，验证 Save 会清掉它
            string csvDir = Path.Combine(dir, "tables");
            Directory.CreateDirectory(csvDir);
            File.WriteAllText(Path.Combine(csvDir, "table_09.csv"), "旧,表\r\n");

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
                RowCount = 4,            // 第 3 行没有单元格（缺格带），输出里仍要占一行留空
                ColumnCount = 2,
                Rows = new List<RuledTableRow>
                {
                    new RuledTableRow
                    {
                        Index = 1,
                        Cells = new List<RuledTableCell>
                        {
                            new RuledTableCell { Row = 1, Column = 1, Text = "項目", WordCount = 1 },
                            new RuledTableCell { Row = 1, Column = 2, Text = "図番", WordCount = 1 },
                        }
                    },
                    new RuledTableRow
                    {
                        Index = 2,
                        IsStruck = true,                                     // 被划线划掉 = 作废
                        Cells = new List<RuledTableCell>
                        {
                            new RuledTableCell { Row = 2, Column = 1, Text = "D", WordCount = 1, StruckWordCount = 1 },
                            new RuledTableCell { Row = 2, Column = 2, Text = "H310U10025", WordCount = 1, StruckWordCount = 1 },
                        }
                    },
                    new RuledTableRow
                    {
                        Index = 4,
                        Cells = new List<RuledTableCell>
                        {
                            new RuledTableCell { Row = 4, Column = 1, Text = "G", WordCount = 1 },
                            new RuledTableCell { Row = 4, Column = 2, Text = "H310U10028", WordCount = 1 },
                        }
                    },
                }
            };

            Check("作废行数", table.StruckRowCount, 1);
            Check("实出行数（4 - 1）", table.VisibleRowCount, 3);
            Check("ToGrid 行数（含缺格行）", table.ToGrid().Length, 4);
            Check("ToVisibleGrid 行数（4 - 1）", table.ToVisibleGrid().Length, 3);
            Check("缺格行没有被挤掉：实出第 3 行是下面那一行", table.ToVisibleGrid()[2][0], "G");

            RuledTableWriter.Save(new List<RuledTable> { table }, dir, Path.Combine(dir, "probe.pdf"), opts);

            string csvPath = Path.Combine(csvDir, "table_01.csv");
            string[] physical = File.ReadAllLines(csvPath);

            Check("CSV 物理行数（4 行 - 1 作废行）", physical.Length, 3);
            Check("CSV 第 1 行", physical[0], "項目,図番");
            Check("缺格行留空（第 2 行）", physical[1], ",");
            Check("CSV 第 3 行", physical[2], "G,H310U10028");
            Check("作废行的文字没有进 CSV", File.ReadAllText(csvPath).Contains("H310U10025") ? 1 : 0, 0);
            Check("旧 CSV（table_09.csv）已清掉", File.Exists(Path.Combine(csvDir, "table_09.csv")) ? 1 : 0, 0);

            string report = File.ReadAllText(Path.Combine(dir, "tables_text.txt"));

            Check("报告里有「划线作废」小节", report.Contains("划线作废") ? 1 : 0, 1);
            Check("作废行的内容仍可在报告里核对", report.Contains("H310U10025") ? 1 : 0, 1);
        }

        /// <summary>
        /// 一格多词 → 文字拼接：空格判据是「词间距 &gt; 平均字宽 30%（下限字高 1/8，上限字高 1/4）」。
        ///
        /// 为什么要有「平均字宽」这一路：CAD 里常见压扁排字的长标签（字高不变、字宽约 6 折），
        /// 只按字高判会把该有的空格吃掉 —— H310U10004 表題欄 F 行「枠 ブレーキ部品取付/調整ラベル…」
        /// 以前就被拼成「枠ブレーキ部品…」。下面用图纸上的真实坐标（像素）做数据。
        /// </summary>
        private static void TestComposeText()
        {
            Section("ComposeText（词间距 → 空格）");

            Check("压扁长标签也要补空格（H310U10004 F 行）",
                  RuledTableExtractor.ComposeText(new List<RuledTableExtractor.WordBox>
                  {
                      Word("枠", 7829.8, 7872.6, 5627.8, 5685.5),
                      Word("ブレーキ部品取付/調整ラベル・注意ラベル貼付要領図",
                           7885.7, 8877.1, 5625.0, 5685.5)
                  }),
                  "枠 ブレーキ部品取付/調整ラベル・注意ラベル貼付要領図");

            Check("常规宽度同样补空格（H310U10004 G 行）",
                  RuledTableExtractor.ComposeText(new List<RuledTableExtractor.WordBox>
                  {
                      Word("枠", 7827.0, 7898.4, 5706.4, 5764.1),
                      Word("ジョイント金具埋込要領図", 7920.2, 8695.6, 5705.3, 5764.1)
                  }),
                  "枠 ジョイント金具埋込要領図");

            Check("数字 + 汉字（H310U10004 变更履历）",
                  RuledTableExtractor.ComposeText(new List<RuledTableExtractor.WordBox>
                  {
                      Word("TJ-3086", 7206.8, 7345.1, 6292.9, 6345.5),
                      Word("色追加", 7356.7, 7471.0, 6282.8, 6345.5)
                  }),
                  "TJ-3086 色追加");

            Check("紧贴的两个词不加空格",
                  RuledTableExtractor.ComposeText(new List<RuledTableExtractor.WordBox>
                  {
                      Word("枠", 0, 60, 0, 58),
                      Word("図", 62, 130, 0, 58)
                  }),
                  "枠図");

            Check("同一格里的下一行用空格分隔",
                  RuledTableExtractor.ComposeText(new List<RuledTableExtractor.WordBox>
                  {
                      Word("部品", 0, 50, 100, 150),
                      Word("セット", 0, 50, 0, 50)
                  }),
                  "部品 セット");

            Check("没有词", RuledTableExtractor.ComposeText(new List<RuledTableExtractor.WordBox>()), string.Empty);
        }

        /// <summary>
        /// 文字笔画凑出来的「假格子」必须先剔掉，否则 <see cref="RuledTableExtractor.RemoveContainers"/>
        /// 会把包住它的真格子当「套娃」删掉 —— 真格子一没，里面那整段文字就再没有归属，
        /// H310U10004 表題欄 F 行就是这样整段丢字的。
        /// </summary>
        private static void TestIsolatedCells()
        {
            Section("文字笔画假格子 / 套娃容器剔除");

            const double tol = 1.0;

            // 左边是「真格子」，右边是共用边界的邻居（凑成一张 2 格表），
            // 第三个是落在真格子内部的假格子（尺寸 20 x 10，就像「図」「要」的笔画框）
            List<RuledTableExtractor.Cell> Build()
            {
                return new List<RuledTableExtractor.Cell>
                {
                    new RuledTableExtractor.Cell { X1 = 0, X2 = 300, Y1 = 0, Y2 = 28 },
                    new RuledTableExtractor.Cell { X1 = 300, X2 = 600, Y1 = 0, Y2 = 28 },
                    new RuledTableExtractor.Cell { X1 = 200, X2 = 220, Y1 = 8, Y2 = 18 },
                };
            }

            // 对照：不先剔假格子的话，真格子会被当成「包住别的格子的大矩形」删掉（修好之前的旧行为）
            List<RuledTableExtractor.Cell> control = Build();
            RuledTableExtractor.RemoveContainers(control, tol);
            Check("对照：旧行为下真格子被误删", control.Count, 2);
            Check("对照：误删的正是左边那个真格子",
                  control.Exists(c => Math.Abs(c.X1) < 0.001 && Math.Abs(c.X2 - 300) < 0.001) ? 1 : 0, 0);

            // 修好之后：先剔孤立假格子，再做套娃剔除，真格子留下
            List<RuledTableExtractor.Cell> cells = Build();
            Check("剔掉的孤立假格子数", RuledTableExtractor.DropIsolatedCells(cells, tol), 1);
            Check("假格子已不在清单里", cells.Count, 2);

            RuledTableExtractor.RemoveContainers(cells, tol);
            Check("真格子没有被套娃剔除", cells.Count, 2);
            Check("真格子还在",
                  cells.Exists(c => Math.Abs(c.X1) < 0.001 && Math.Abs(c.X2 - 300) < 0.001) ? 1 : 0, 1);

            // 单格表（没有邻居）本来就不算表，属于孤立矩形，一并剔掉
            List<RuledTableExtractor.Cell> lonely = new List<RuledTableExtractor.Cell>
            {
                new RuledTableExtractor.Cell { X1 = 0, X2 = 100, Y1 = 0, Y2 = 50 },
            };
            Check("孤立单格不动（本来就到不了建表这一步）",
                  RuledTableExtractor.DropIsolatedCells(lonely, tol), 0);
        }

        private static RuledTableExtractor.WordBox Word(string text, double x1, double x2, double y1, double y2)
        {
            return new RuledTableExtractor.WordBox { Text = text, X1 = x1, X2 = x2, Y1 = y1, Y2 = y2 };
        }


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
