using System;
using System.Collections.Generic;
using System.IO;

namespace CadPartSplitter
{
    /// <summary>
    /// 部品表「下表参照」展开：读 <c>tables\table_NN.csv</c>，把主表的这类行按参照表的明细拆开，
    /// 输出下游 AI 环节约定的 <c>LT_STEP1.csv</c>。
    ///
    /// 规则（与图纸上的写法一一对应）：
    ///   · 主表 —— 表头（第一行）同时含「番号」「部品名」「部品コード」，它的明细就是输出内容；
    ///   · 明细行里「部品コード」含「下表参照」→ 往后找第一张「表头含部品コード且有明细」的表（图纸上的「下表」）；
    ///   · 参照表有几条明细，这一行就拆成几条：只把「部品コード」换成参照表对应的值，
    ///     其余列（番号 / 部品名 / 枠区分 / 員数 / 寸法）沿用原行 —— 同一个部品的多个变体共用番号；
    ///   · 参照表找不到 / 明细全空 → 该行原样保留并写 WARN，不做猜测。
    ///
    /// 编码与其它清单一致：CSV 用带 BOM 的 UTF-8 + CRLF + RFC 4180 转义（Excel 双击即可打开）。
    /// 只读 <c>tables\*.csv</c>，不改动任何已有产物。
    /// </summary>
    public static class LtStep1Builder
    {
        /// <summary>下游约定的文件名（写在输出目录根，与 tables_info.json 同级）。</summary>
        public const string OutputFileName = "LT_STEP1.csv";

        /// <summary>主表表头必须同时出现的三个词。</summary>
        private static readonly string[] PartsHeaders = { "番号", "部品名", "部品コード" };

        /// <summary>「部品コード」列名（也是参照表必须有的列）。</summary>
        private const string PartCodeHeader = "部品コード";

        /// <summary>「部品コード」列出现这个字样时，表示要用「下表」的明细展开。</summary>
        private const string NextTableKeyword = "下表参照";

        /// <summary>
        /// 扫描 <paramref name="outputDirectory"/> 下的 <c>tables\*.csv</c> 并合成 <c>LT_STEP1.csv</c>。
        /// </summary>
        /// <returns>写出的明细行数（不含表头）；0 表示没有生成文件。</returns>
        public static int Build(string outputDirectory, DetectorOptions opts)
        {
            DetectorOptions o = opts ?? new DetectorOptions();

            if (!o.BuildLtStep1Table)
            {
                o.Log("[INFO] LT_STEP1 合成已关闭（BuildLtStep1Table = false）。");
                return 0;
            }

            if (string.IsNullOrWhiteSpace(outputDirectory))
                return 0;

            string csvDir = Path.Combine(outputDirectory, "tables");

            if (!Directory.Exists(csvDir))
            {
                o.Log("[INFO] LT_STEP1：没有 tables 目录（本次未生成表格 CSV），跳过。");
                return 0;
            }

            string[] files = Directory.GetFiles(csvDir, "*.csv");

            // 文件名补零（table_01 … table_10），按名字排序即图纸上从上到下的顺序
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            List<Sheet> sheets = LoadSheets(files);

            if (sheets.Count == 0)
            {
                o.Log("[INFO] LT_STEP1：" + csvDir + " 下没有可读的表格 CSV，跳过。");
                return 0;
            }

            var output = new List<string[]>();
            string[] outHeader = null;
            int mainSheets = 0;
            int expandedRows = 0;

            for (int i = 0; i < sheets.Count; i++)
            {
                Sheet sheet = sheets[i];

                if (!IsPartsTable(sheet.Header))
                    continue;

                if (outHeader == null)
                    outHeader = sheet.Header;

                mainSheets++;

                int codeCol = sheet.CodeColumn;
                int outCodeCol = CsvFile.IndexOfHeader(outHeader, PartCodeHeader);

                for (int r = 1; r < sheet.Rows.Count; r++)
                {
                    string[] row = sheet.Rows[r];

                    if (CsvFile.IsEmptyRow(row))
                        continue;

                    string code = codeCol >= 0 && codeCol < row.Length ? row[codeCol] : string.Empty;

                    if (code.IndexOf(NextTableKeyword, StringComparison.Ordinal) < 0)
                    {
                        output.Add(MapToHeader(row, sheet.Header, outHeader));
                        continue;
                    }

                    // 「下表参照」→ 往后找第一张有部品コード列且有明细的表
                    List<string> refCodes = null;
                    string refName = null;

                    for (int k = i + 1; k < sheets.Count; k++)
                    {
                        List<string> found = CollectReferenceCodes(sheets[k]);

                        if (found != null)
                        {
                            refCodes = found;
                            refName = sheets[k].Name;
                            break;
                        }
                    }

                    if (refCodes == null)
                    {
                        o.Log("[WARN] LT_STEP1：" + sheet.Name + " 第 " + (r + 1) + " 行「" + code
                              + "」找不到可用的参照表，该行原样保留。");
                        output.Add(MapToHeader(row, sheet.Header, outHeader));
                        continue;
                    }

                    foreach (string refCode in refCodes)
                    {
                        string[] clone = MapToHeader(row, sheet.Header, outHeader);

                        if (outCodeCol >= 0)
                            clone[outCodeCol] = refCode;

                        output.Add(clone);
                        expandedRows++;
                    }

                    o.Log("[INFO] LT_STEP1：" + sheet.Name + " 第 " + (r + 1) + " 行（" + code + "）→ "
                          + refName + " 的 " + refCodes.Count + " 条部品コード："
                          + string.Join(" / ", refCodes.ToArray()));
                }
            }

            if (outHeader == null)
            {
                o.Log("[INFO] LT_STEP1：未找到表头含「番号 / 部品名 / 部品コード」的表格，不生成 "
                      + OutputFileName + "。");
                return 0;
            }

            string path = Path.Combine(outputDirectory, OutputFileName);
            CsvFile.Write(path, outHeader, output);

            o.Log("[INFO] LT_STEP1：" + mainSheets + " 张主表 → 展开 " + expandedRows + " 行 → 共 "
                  + output.Count + " 条明细，已保存：" + path);

            return output.Count;
        }

        // ── CSV 读取 ─────────────────────────────────────────────────────────

        /// <summary>一张表（一个 CSV 文件）的解析结果。</summary>
        private sealed class Sheet
        {
            /// <summary>文件名（含扩展名），日志里用。</summary>
            public string Name;

            /// <summary>按行拆好的单元格；<c>[0]</c> 是表头。</summary>
            public List<string[]> Rows;

            /// <summary>表头里「部品コード」所在列，-1 表示没有这一列。</summary>
            public int CodeColumn;

            /// <summary>表头行（等于 <c>Rows[0]</c>）。</summary>
            public string[] Header
            {
                get { return Rows[0]; }
            }
        }

        private static List<Sheet> LoadSheets(string[] files)
        {
            var sheets = new List<Sheet>();

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

                sheets.Add(new Sheet
                {
                    Name = Path.GetFileName(file),
                    Rows = rows,
                    CodeColumn = CsvFile.IndexOfHeader(rows[0], PartCodeHeader)
                });
            }

            return sheets;
        }

        // ── 判定 / 映射 ──────────────────────────────────────────────────────

        /// <summary>是否部品表：表头同时含「番号」「部品名」「部品コード」。</summary>
        private static bool IsPartsTable(string[] header)
        {
            if (header == null)
                return false;

            foreach (string keyword in PartsHeaders)
            {
                if (CsvFile.IndexOfHeader(header, keyword) < 0)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 取参照表的部品コード清单（按明细行顺序、去空、去重复表头、去链式「下表参照」）。
        /// 没有部品コード列或没有有效值 → 返回 null（表示这张表不能当参照表）。
        /// </summary>
        private static List<string> CollectReferenceCodes(Sheet sheet)
        {
            if (sheet.CodeColumn < 0)
                return null;

            var codes = new List<string>();

            for (int r = 1; r < sheet.Rows.Count; r++)
            {
                string[] row = sheet.Rows[r];

                if (row.Length <= sheet.CodeColumn)
                    continue;

                string value = CsvFile.Normalize(row[sheet.CodeColumn]);

                if (value.Length == 0)
                    continue;

                if (value.IndexOf(NextTableKeyword, StringComparison.Ordinal) >= 0)
                    continue;   // 参照表自己也在「下表参照」：不接第二层，避免越扯越乱

                if (value.IndexOf(PartCodeHeader, StringComparison.Ordinal) >= 0)
                    continue;   // 重复的表头行

                codes.Add(value);
            }

            return codes.Count > 0 ? codes : null;
        }

        /// <summary>把源行按「列名」映射到输出表头（列顺序 / 列数不同也能对齐，缺列留空）。</summary>
        private static string[] MapToHeader(string[] row, string[] srcHeader, string[] dstHeader)
        {
            var result = new string[dstHeader.Length];

            for (int c = 0; c < dstHeader.Length; c++)
            {
                string name = CsvFile.Normalize(dstHeader[c]);
                int src = CsvFile.IndexOfHeader(srcHeader, name);

                if (src < 0)
                {
                    // 归一化之后可能变成「被包含」的反向关系，退回同名精确比较
                    for (int s = 0; s < srcHeader.Length; s++)
                    {
                        if (CsvFile.Normalize(srcHeader[s]) == name)
                        {
                            src = s;
                            break;
                        }
                    }
                }

                result[c] = (src >= 0 && src < row.Length) ? row[src] : string.Empty;
            }

            return result;
        }
    }
}
