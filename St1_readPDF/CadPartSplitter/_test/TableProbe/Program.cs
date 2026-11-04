using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using UglyToad.PdfPig;

namespace CadPartSplitter
{
    /// <summary>
    /// 有线表格提取探针：直接调用 CadPartSplitter 里的同一份
    /// <see cref="RuledTableExtractor"/> / <see cref="RuledTableWriter"/>（以源文件链接方式参与编译，
    /// 不是复制一份逻辑），在命令行打印识别结果并把产物写到指定目录，用来对照图纸核对行列与单元格文字。
    ///
    /// 用法：
    ///   dotnet run --project _test\TableProbe -- &lt;pdf路径&gt; [页码(1起)] [DPI] [输出目录]
    ///
    /// 例：
    ///   dotnet run --project _test\TableProbe -- H310U10004.pdf 1 300 _test\TableProbe\out
    ///
    /// 退出码：0 = 识别到表格；2 = 没识别到表格（不是错误，位图 / 无框线图纸正常如此）；1 = 参数或异常。
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("用法: TableProbe <pdf路径> [页码(1起)] [DPI] [输出目录]");
                return 1;
            }

            string pdf = Path.GetFullPath(args[0]);

            if (!File.Exists(pdf))
            {
                Console.WriteLine("[ERROR] 文件不存在: " + pdf);
                return 1;
            }

            int pageNo = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 1;
            double dpi = args.Length > 2 ? double.Parse(args[2], CultureInfo.InvariantCulture) : 300.0;

            string outDir = args.Length > 3
                ? Path.GetFullPath(args[3])
                : Path.Combine(Path.GetDirectoryName(pdf), "_tableprobe",
                               Path.GetFileNameWithoutExtension(pdf) + "_P" + pageNo);

            var opts = new DetectorOptions
            {
                PdfPageIndex = pageNo - 1,
                RenderDpi = dpi,
                OutputDirectory = outDir,
                ExtractRuledTables = true,
            };

            try
            {
                // 页面像素尺寸：与 Docnet 按 DPI 渲染的口径一致（pt → inch → px）
                int imgW, imgH;

                using (var doc = PdfDocument.Open(pdf))
                {
                    var page = doc.GetPage(pageNo);

                    imgW = (int)Math.Round(page.Width / 72.0 * dpi);
                    imgH = (int)Math.Round(page.Height / 72.0 * dpi);

                    Console.WriteLine("[探针] 页尺寸 " + page.Width.ToString("F1") + " x "
                                      + page.Height.ToString("F1") + " pt  →  渲染 "
                                      + imgW + " x " + imgH + " px @" + dpi.ToString("0") + " DPI");
                }

                var sw = Stopwatch.StartNew();
                List<RuledTable> tables = RuledTableExtractor.Extract(
                    pdf, opts.PdfPageIndex, imgW, imgH, opts);
                sw.Stop();

                Console.WriteLine("[探针] 识别到 " + tables.Count + " 张表，用时 " + sw.ElapsedMilliseconds + " ms");

                foreach (var t in tables)
                    PrintTable(t);

                if (tables.Count == 0)
                    return 2;

                int written = RuledTableWriter.Save(tables, outDir, pdf, opts);
                Console.WriteLine("[探针] 写出 " + written + " 个文件，目录: " + outDir);

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ERROR] " + ex.GetType().Name + ": " + ex.Message);
                Console.WriteLine(ex.StackTrace);
                return 1;
            }
        }

        /// <summary>打印一张表的概要 + 逐行单元格内容（探针用简表，正式产物见 tables_text.txt）。</summary>
        private static void PrintTable(RuledTable t)
        {
            Console.WriteLine();
            Console.WriteLine("[" + t.Name + "]  绝对(x=" + t.PixelX + " y=" + t.PixelY
                              + " w=" + t.PixelW + " h=" + t.PixelH + ")  mm(x="
                              + t.PdfX_mm.ToString("0.##") + " y=" + t.PdfY_mm.ToString("0.##")
                              + " w=" + t.PdfW_mm.ToString("0.##") + " h=" + t.PdfH_mm.ToString("0.##") + ")"
                              + "  行 " + t.RowCount + " / 列 " + t.ColumnCount
                              + " / 单元格 " + t.CellCount + "（有文字 " + t.TextCellCount + "）");

            foreach (var row in t.Rows)
            {
                var parts = new List<string>();

                foreach (var c in row.Cells)
                {
                    string text = string.IsNullOrEmpty(c.Text) ? "·" : c.Text;
                    string span = (c.RowSpan > 1 || c.ColumnSpan > 1)
                        ? "(" + c.RowSpan + "x" + c.ColumnSpan + ")" : string.Empty;

                    parts.Add("[" + c.Row + "," + c.Column + "]" + text + span);
                }

                Console.WriteLine("  行 " + row.Index.ToString().PadLeft(3) + " │ " + string.Join(" │ ", parts.ToArray()));
            }
        }
    }
}
