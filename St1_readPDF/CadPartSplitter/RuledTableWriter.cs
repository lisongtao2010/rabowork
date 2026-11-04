using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CadPartSplitter
{
    /// <summary>
    /// 把 <see cref="RuledTableExtractor"/> 识别出的有线表格落盘：
    ///
    ///   · <c>tables_info.json</c> ── 机器可读：每张表的坐标、行列数、逐单元格文字与跨度；
    ///   · <c>tables_text.txt</c>  ── 人眼可读：先用框线画的二维表显示整表内容，再列单元格明细；
    ///   · <c>tables\table_NN.csv</c> ── Excel 可直接打开：一格一列，合并单元格的文字在左上角。
    ///
    /// 编码约定与项目其它清单一致：json 用无 BOM 的 UTF-8，txt / csv 用带 BOM 的 UTF-8
    /// （Windows 记事本 / Excel 打开日文、中文不会乱码）。
    /// </summary>
    public static class RuledTableWriter
    {
        /// <summary>二维表显示时单列最大宽度（显示宽度，CJK 字符算 2），超长截断加省略号。</summary>
        private const int MaxGridCellWidth = 32;

        /// <summary>
        /// 写出有线表格的三个产物（json / txt / csv）。<paramref name="tables"/> 为空时什么都不写，
        /// 由调用方决定是否提示（避免每次位图或空表图纸都生成空文件）。
        /// </summary>
        /// <returns>写出的文件个数。</returns>
        public static int Save(
            List<RuledTable> tables, string outputDirectory, string sourceFile, DetectorOptions opts)
        {
            if (tables == null || tables.Count == 0) return 0;
            if (string.IsNullOrWhiteSpace(outputDirectory)) return 0;

            DetectorOptions o = opts ?? new DetectorOptions();

            Directory.CreateDirectory(outputDirectory);

            string jsonPath = Path.Combine(outputDirectory, "tables_info.json");
            SaveJson(tables, jsonPath, sourceFile, o);
            o.Log("[INFO] Table coords saved: " + jsonPath);

            string txtPath = Path.Combine(outputDirectory, "tables_text.txt");
            SaveTextReport(tables, txtPath, sourceFile, o);
            o.Log("[INFO] Table text report saved: " + txtPath);

            int written = 2;

            // 每张表一个 CSV：Excel 里看一行一行最直观，也方便下游脚本按列取值
            string csvDir = Path.Combine(outputDirectory, "tables");
            Directory.CreateDirectory(csvDir);

            // 先清掉上一次的 table_NN.csv：表张数会变（划线剔除后几张碎表会并成一张、
            // 图纸改了也可能少一张），留着旧文件会被 LtStep1Builder 的 tables\*.csv 扫描当成真的读进去。
            int erased = 0;

            foreach (string old in Directory.GetFiles(csvDir, "table_*.csv"))
            {
                try
                {
                    File.Delete(old);
                    erased++;
                }
                catch (Exception ex)
                {
                    o.Log("[WARN] 旧表格 CSV 删不掉（不影响本次结果）：" + old + " —— " + ex.Message);
                }
            }

            if (erased > 0)
                o.Log("[INFO] Table csv 清理旧文件 " + erased + " 个（" + csvDir + "）");

            foreach (var t in tables)
            {
                string csvPath = Path.Combine(csvDir, t.Name + ".csv");
                SaveCsv(t, csvPath);
                written++;
            }

            o.Log("[INFO] Table csv saved: " + csvDir + "（" + tables.Count + " 个文件）");
            return written;
        }

        // ── tables_info.json ──────────────────────────────────────────────────

        /// <summary>
        /// 机器可读清单。结构与 <c>outside_info.json</c> 保持一致：
        /// <c>pixel</c> / <c>pdf_mm</c> 两套坐标、<c>rows[].cells[]</c> 两级容器。
        /// </summary>
        private static void SaveJson(List<RuledTable> tables, string path, string sourceFile, DetectorOptions opts)
        {
            var sb = new StringBuilder();

            sb.AppendLine("{");
            sb.AppendLine("  \"page\": " + (opts.PdfPageIndex + 1) + ",");
            sb.AppendLine("  \"dpi\": " + opts.RenderDpi.ToString("0") + ",");
            sb.AppendLine("  \"source\": \"" + EscapeJson(sourceFile != null ? Path.GetFullPath(sourceFile) : string.Empty) + "\",");
            sb.AppendLine("  \"tables\": [");

            for (int i = 0; i < tables.Count; i++)
            {
                var t = tables[i];

                sb.AppendLine("    {");
                sb.AppendLine("      \"name\": \"" + EscapeJson(t.Name) + "\",");
                AppendRectJson(sb, "      ", t.PixelX, t.PixelY, t.PixelW, t.PixelH,
                               t.PdfX_mm, t.PdfY_mm, t.PdfW_mm, t.PdfH_mm);
                sb.AppendLine("      \"row_count\": " + t.RowCount + ",");
                sb.AppendLine("      \"column_count\": " + t.ColumnCount + ",");
                sb.AppendLine("      \"cell_count\": " + t.CellCount + ",");
                sb.AppendLine("      \"text_cell_count\": " + t.TextCellCount + ",");
                sb.AppendLine("      \"struck_row_count\": " + t.StruckRowCount + ",");
                sb.AppendLine("      \"visible_row_count\": " + t.VisibleRowCount + ",");
                sb.AppendLine("      \"rows\": [");

                for (int r = 0; r < t.Rows.Count; r++)
                {
                    var row = t.Rows[r];

                    sb.AppendLine("        {");
                    sb.AppendLine("          \"index\": " + row.Index + ",");
                    sb.AppendLine("          \"struck\": " + (row.IsStruck ? "true" : "false") + ",");
                    sb.AppendLine("          \"pixel\": { \"x\": " + row.PixelX + ", \"y\": " + row.PixelY
                                  + ", \"w\": " + row.PixelW + ", \"h\": " + row.PixelH + " },");
                    sb.AppendLine("          \"cells\": [");

                    for (int c = 0; c < row.Cells.Count; c++)
                    {
                        var cell = row.Cells[c];

                        sb.AppendLine("            {");
                        sb.AppendLine("              \"row\": " + cell.Row + ",");
                        sb.AppendLine("              \"column\": " + cell.Column + ",");
                        sb.AppendLine("              \"row_span\": " + cell.RowSpan + ",");
                        sb.AppendLine("              \"column_span\": " + cell.ColumnSpan + ",");
                        sb.AppendLine("              \"text\": \"" + EscapeJson(cell.Text) + "\",");
                        sb.AppendLine("              \"word_count\": " + cell.WordCount + ",");
                        sb.AppendLine("              \"struck_words\": " + cell.StruckWordCount + ",");
                        sb.AppendLine("              \"pixel\": { \"x\": " + cell.PixelX + ", \"y\": " + cell.PixelY
                                      + ", \"w\": " + cell.PixelW + ", \"h\": " + cell.PixelH + " },");
                        sb.AppendLine("              \"pdf_mm\": { \"x\": " + cell.PdfX_mm.ToString("F2")
                                      + ", \"y\": " + cell.PdfY_mm.ToString("F2")
                                      + ", \"w\": " + cell.PdfW_mm.ToString("F2")
                                      + ", \"h\": " + cell.PdfH_mm.ToString("F2") + " }");
                        sb.AppendLine(c < row.Cells.Count - 1 ? "            }," : "            }");
                    }

                    sb.AppendLine("          ]");
                    sb.AppendLine(r < t.Rows.Count - 1 ? "        }," : "        }");
                }

                sb.AppendLine("      ]");
                sb.AppendLine(i < tables.Count - 1 ? "    }," : "    }");
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        /// <summary>写一段 <c>pixel</c> + <c>pdf_mm</c> 坐标（末尾不带逗号）。</summary>
        private static void AppendRectJson(
            StringBuilder sb, string indent,
            int px, int py, int pw, int ph,
            double mx, double my, double mw, double mh)
        {
            sb.AppendLine(indent + "\"pixel\": { \"x\": " + px + ", \"y\": " + py
                          + ", \"w\": " + pw + ", \"h\": " + ph + " },");
            sb.AppendLine(indent + "\"pdf_mm\": { \"x\": " + mx.ToString("F2")
                          + ", \"y\": " + my.ToString("F2")
                          + ", \"w\": " + mw.ToString("F2")
                          + ", \"h\": " + mh.ToString("F2") + " },");
        }

        private static string EscapeJson(string s)
        {
            if (s == null) return string.Empty;
            return s.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\r", "\\r")
                    .Replace("\n", "\\n")
                    .Replace("\t", "\\t");
        }

        // ── tables_text.txt ───────────────────────────────────────────────────

        /// <summary>
        /// 人眼可读清单：每张表先用框线画出二维内容（一眼能看出行列与空单元格），
        /// 再列单元格明细（行列号、跨度、坐标、文字），便于和图纸核对。
        /// </summary>
        private static void SaveTextReport(
            List<RuledTable> tables, string path, string sourceFile, DetectorOptions opts)
        {
            var sb = new StringBuilder();

            int cellTotal = 0, textCellTotal = 0, mergedTotal = 0, struckRowTotal = 0;
            foreach (var t in tables)
            {
                cellTotal += t.CellCount;
                textCellTotal += t.TextCellCount;
                struckRowTotal += t.StruckRowCount;

                foreach (var row in t.Rows)
                    foreach (var c in row.Cells)
                        if (c.RowSpan > 1 || c.ColumnSpan > 1) mergedTotal++;
            }

            sb.AppendLine("# CadPartSplitter 表格内容清单（单元格四面都有框线的「有线表格」）");
            sb.AppendLine("# 生成时间   : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("# 源文件     : " + Path.GetFullPath(sourceFile));
            sb.AppendLine("# 页面 / DPI : 第 " + (opts.PdfPageIndex + 1) + " 页 / " + opts.RenderDpi.ToString("0") + " DPI");
            sb.AppendLine("# 识别方式   : PDF 矢量路径取横 / 竖框线（描边线、填充细矩形两种画法同样处理）");
            sb.AppendLine("#              → 最小闭合矩形 = 单元格 → 相邻单元格按共用边界并成表");
            sb.AppendLine("# 坐标说明   : 绝对 = 页面像素坐标（左上原点）；mm = 页面毫米坐标");
            sb.AppendLine("# 表格数量   : " + tables.Count + " 张 / 单元格 " + cellTotal
                          + " 个（有文字 " + textCellTotal + " 个，合并单元格 " + mergedTotal + " 个）");
            if (struckRowTotal > 0)
                sb.AppendLine("# 划线作废   : " + struckRowTotal + " 行（图纸上用 取り消し線 划掉 = 该行作废，"
                              + "已从二维表与 tables\\table_NN.csv 剔除，明细里仍可见）");
            sb.AppendLine("# 内容说明   : 二维表里的文字就是该单元格的内容；空单元格留空；");
            sb.AppendLine("#              合并单元格的文字写在左上角那一格，被它覆盖的格子留空（tables\\table_NN.csv 同）");
            sb.AppendLine();

            foreach (var t in tables)
            {
                sb.AppendLine("[" + t.Name + "]"
                              + "  绝对(x=" + t.PixelX + " y=" + t.PixelY + " w=" + t.PixelW + " h=" + t.PixelH + ")"
                              + "  mm(x=" + t.PdfX_mm.ToString("0.##") + " y=" + t.PdfY_mm.ToString("0.##")
                              + " w=" + t.PdfW_mm.ToString("0.##") + " h=" + t.PdfH_mm.ToString("0.##") + ")"
                              + "  行 " + t.RowCount + " / 列 " + t.ColumnCount
                              + " / 单元格 " + t.CellCount + "（有文字 " + t.TextCellCount + "）"
                              + (t.StruckRowCount > 0
                                  ? "  划线作废 " + t.StruckRowCount + " 行 → 实出 " + t.VisibleRowCount + " 行"
                                  : string.Empty));

                AppendGrid(sb, t, "  ");

                AppendStruckRows(sb, t, "  ");

                sb.AppendLine();
                sb.AppendLine("  单元格明细：");

                foreach (var row in t.Rows)
                {
                    sb.AppendLine("    行 " + row.Index.ToString().PadLeft(3)
                                  + "  y=" + row.PixelY.ToString().PadRight(5)
                                  + " h=" + row.PixelH.ToString().PadRight(5)
                                  + "  单元格 " + row.Cells.Count
                                  + (row.IsStruck ? "  ← 划线作废（已从 CSV / 二维表剔除）" : string.Empty));

                    foreach (var c in row.Cells)
                    {
                        sb.AppendLine("      [行 " + c.Row.ToString().PadRight(3)
                                      + " 列 " + c.Column.ToString().PadRight(3) + "]"
                                      + (c.RowSpan > 1 || c.ColumnSpan > 1
                                          ? " 跨度(" + c.RowSpan + "行" + c.ColumnSpan + "列)" : string.Empty)
                                      + " x=" + c.PixelX.ToString().PadRight(5)
                                      + " y=" + c.PixelY.ToString().PadRight(5)
                                      + " w=" + c.PixelW.ToString().PadRight(5)
                                      + " h=" + c.PixelH.ToString().PadRight(5)
                                      + " " + (string.IsNullOrEmpty(c.Text) ? "（空）" : CsvFile.SingleLine(c.Text))
                                      + (c.IsStruck ? "  ← 划线" : string.Empty));
                    }
                }

                sb.AppendLine();
            }

            // 带 BOM 的 UTF-8：Windows 记事本 / Excel 打开日文、中文不会乱码
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        /// <summary>
        /// 用制表符框线把表格内容画成二维表（列宽按显示宽度对齐，CJK 字符算 2）。
        /// 空单元格显示为空白，能直观看出「这一格确实没有字」，不会被误读成漏行漏列。
        /// 划线作废（取り消し線）的行不画 —— 与 <c>tables\table_NN.csv</c> 保持一致，另行列出。
        /// </summary>
        private static void AppendGrid(StringBuilder sb, RuledTable table, string indent)
        {
            string[][] grid = table.ToVisibleGrid();

            if (grid.Length <= 0 || table.ColumnCount <= 0) return;

            int rowCount = grid.Length;

            // 单元格里的换行压成 '~'：否则框线二维表会被撑坏（一格跨成两行）
            for (int r = 0; r < rowCount; r++)
                for (int c = 0; c < table.ColumnCount; c++)
                    grid[r][c] = CsvFile.SingleLine(grid[r][c]);

            var widths = new int[table.ColumnCount];

            for (int c = 0; c < table.ColumnCount; c++)
            {
                widths[c] = 1;

                for (int r = 0; r < rowCount; r++)
                {
                    int w = DisplayWidth(grid[r][c]);
                    if (w > widths[c]) widths[c] = w;
                }

                if (widths[c] > MaxGridCellWidth) widths[c] = MaxGridCellWidth;
            }

            sb.AppendLine(indent + BuildRule(widths, '┌', '┬', '┐'));

            for (int r = 0; r < rowCount; r++)
            {
                sb.Append(indent + "│");

                for (int c = 0; c < table.ColumnCount; c++)
                    sb.Append(' ' + PadTo(grid[r][c], widths[c]) + " │");

                sb.AppendLine();

                if (r < rowCount - 1)
                    sb.AppendLine(indent + BuildRule(widths, '├', '┼', '┤'));
            }

            sb.AppendLine(indent + BuildRule(widths, '└', '┴', '┘'));
        }

        /// <summary>
        /// 列出被 取り消し線 划掉（图纸上作废）的行 —— 这些行不进 CSV / 二维表，
        /// 但要在报告里留个痕迹，方便核对「是漏了还是画上划掉了」。
        /// </summary>
        private static void AppendStruckRows(StringBuilder sb, RuledTable table, string indent)
        {
            if (table.StruckRowCount <= 0) return;

            sb.AppendLine();
            sb.AppendLine(indent + "划线作废（取り消し線，已从二维表 / tables\\" + table.Name
                          + ".csv 剔除，图纸上表示该行作废）：");

            foreach (var row in table.Rows)
            {
                if (!row.IsStruck) continue;

                var parts = new List<string>();

                foreach (var c in row.Cells)
                {
                    string text = CsvFile.SingleLine(c.Text ?? string.Empty);
                    if (text.Length > 0) parts.Add(text);
                }

                sb.AppendLine(indent + "  行 " + row.Index.ToString().PadLeft(3)
                              + "  y=" + row.PixelY.ToString().PadRight(5)
                              + "  " + (parts.Count == 0 ? "（空）" : string.Join(" | ", parts.ToArray())));
            }
        }

        /// <summary>生成一条表格横线：<c>┌──┬──┐</c> 形式。</summary>
        private static string BuildRule(int[] widths, char left, char middle, char right)
        {
            var sb = new StringBuilder();
            sb.Append(left);

            for (int c = 0; c < widths.Length; c++)
            {
                sb.Append(new string('─', widths[c] + 2));

                if (c < widths.Length - 1) sb.Append(middle);
            }

            sb.Append(right);
            return sb.ToString();
        }

        /// <summary>按显示宽度右侧补空格；超过 <see cref="MaxGridCellWidth"/> 时截断加省略号。</summary>
        private static string PadTo(string text, int width)
        {
            string s = text ?? string.Empty;

            if (DisplayWidth(s) > width)
            {
                var sb = new StringBuilder();
                int used = 0;

                foreach (char ch in s)
                {
                    int w = CharWidth(ch);
                    if (used + w > width - 1) break;

                    sb.Append(ch);
                    used += w;
                }

                s = sb.Append('…').ToString();
            }

            int pad = width - DisplayWidth(s);
            return pad > 0 ? s + new string(' ', pad) : s;
        }

        /// <summary>字符串的显示宽度：全角 / CJK 字符占 2 格，其余占 1 格。</summary>
        private static int DisplayWidth(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;

            int w = 0;
            foreach (char ch in s) w += CharWidth(ch);
            return w;
        }

        /// <summary>单个字符的显示宽度（CJK / 全角 / 假名 / 韩文按 2 计）。</summary>
        private static int CharWidth(char ch)
        {
            if (ch < 0x1100) return 1;

            if (ch <= 0x115F) return 2;                       // 韩文字母
            if (ch >= 0x2E80 && ch <= 0xA4CF) return 2;       // CJK 部首、假名、汉字、注音
            if (ch >= 0xAC00 && ch <= 0xD7A3) return 2;       // 韩文音节
            if (ch >= 0xF900 && ch <= 0xFAFF) return 2;       // CJK 兼容汉字
            if (ch >= 0xFE30 && ch <= 0xFE6F) return 2;       // CJK 兼容形式
            if (ch >= 0xFF00 && ch <= 0xFF60) return 2;       // 全角 ASCII
            if (ch >= 0xFFE0 && ch <= 0xFFE6) return 2;       // 全角符号

            return 1;
        }

        // ── tables\table_NN.csv ───────────────────────────────────────────────

        /// <summary>
        /// 单张表输出成 CSV（一行一记录、一格一列，Excel 可直接打开）。
        /// 合并单元格的文字写在左上角那一格，被覆盖的格子留空 —— 与 <see cref="RuledTable.ToVisibleGrid"/> 一致。
        /// 划线作废（取り消し線）的行不输出：那张图里这一行已经作废。
        /// 格子里的换行由 <see cref="CsvFile.Escape"/> 压成 `~`（一格一行，避免下游按行读时串行）。
        /// </summary>
        private static void SaveCsv(RuledTable table, string path)
        {
            var sb = new StringBuilder();
            string[][] grid = table.ToVisibleGrid();

            for (int r = 0; r < grid.Length; r++)
            {
                for (int c = 0; c < table.ColumnCount; c++)
                {
                    if (c > 0) sb.Append(',');

                    sb.Append(EscapeCsv(grid[r][c]));
                }

                sb.Append("\r\n");
            }

            // 带 BOM 的 UTF-8：Excel 双击打开时日文 / 中文不会乱码
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        /// <summary>
        /// CSV 字段转义：含逗号 / 引号的字段加双引号，内部引号翻倍（RFC 4180）；
        /// 字段内的换行先压成 `~`（<see cref="CsvFile.SingleLine"/>）。
        /// 实现放在 <see cref="CsvFile.Escape"/>，tables\*.csv / LT_STEP1.csv / LT_STEP2.csv 共用同一份规则。
        /// </summary>
        internal static string EscapeCsv(string s)
        {
            return CsvFile.Escape(s);
        }
    }
}
