using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace CadPartSplitter
{
    /// <summary>
    /// 命令行模式入口（保留原控制台用法，便于批处理调用）。
    ///
    /// 用法：
    ///   CadPartSplitter.exe &lt;输入文件&gt; [输出目录] [选项...]
    ///   CadPartSplitter.exe &lt;输入文件夹&gt; [输出根目录] [选项...]   ← 批量：处理该文件夹下所有 PDF
    ///
    /// 选项：
    ///   --recursive      批量模式下连子文件夹一起处理（默认只看当前文件夹）
    ///   --list           批量模式结束后生成 drawing_list.csv（図番号 / 文件名 / 分类）
    ///   --list-only      只生成 drawing_list.csv，不做分割（读已有的 tables\*.csv）
    ///   --debug          保存中间处理图（二值图、膨胀图）至输出目录
    ///   --dpi &lt;值&gt;       PDF 渲染 DPI（默认 300）
    ///   --page &lt;值&gt;      PDF 页码，从 0 开始（默认 0）
    ///   --mask &lt;值&gt;      右侧遮蔽比例 0.0~1.0（默认 0.50，仅作自动检测失败时的兜底）
    ///   --fixed-mask     关闭自动遮蔽，强制按 --mask 的固定比例遮蔽（旧行为）
    ///   --mask-ink &lt;值&gt;  自动遮蔽的列墨迹阈值（图高比例，默认 0.010）
    ///   --no-block-mask  关闭「按表格块精确遮蔽」，退回整列遮蔽
    ///   --no-frame-erase 关闭「图框擦除」
    ///   --no-callout     关闭「圈号标注识别」（①1、②2 …）
    ///   --keep-text-heavy  保留文字行占比过高的块（默认丢弃表題欄 / 変更履歴表等表格残片）
    ///   --text-ratio &lt;值&gt; 文字行面积占比判定阈值（默认 0.60）
    ///   --merge-text-lines 文字清单按行合并（默认一个词一行）
    ///   --no-tables      关闭「有线表格内容提取」（不生成 tables_info.json 等）
    ///   --no-pdf-info-list 关闭「PDF 信息清单」（不生成 classify\PdfInfoList.txt）
    ///   --no-lt-step1    关闭「部品表下表参照展开」（不生成 01_アッセンブリ図_部品機能情報.csv）
    ///   --no-lt-step2    关闭「品種 × 多列重复表摊平」（不生成 02_要領図_部品機能情報.csv）
    ///   --no-assembly-yoryo 关闭「要領図一覧」（不生成 01_アッセンブリ図_要領図.csv）
    ///   --area &lt;值&gt;      最小轮廓面积阈值（默认 25000）
    ///   --pad &lt;值&gt;       外接框扩展像素（默认 30）
    ///   --kw &lt;值&gt;        膨胀核宽度（默认 60）
    ///   --kh &lt;值&gt;        膨胀核高度（默认 10）
    ///
    /// 输出目录省略时写到 &lt;输入文件所在目录&gt;\AiAppData\&lt;图纸名&gt;
    /// （与下游 AI 环节约定：每张图纸一个子文件夹）。
    /// 批量模式下输出根目录省略时写到 &lt;输入文件夹&gt;\AiAppData，每张图纸再各自一个子文件夹。
    /// </summary>
    internal static class CommandLineRunner
    {
        private const int AttachParentProcess = -1;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);

        public static int Run(string[] args)
        {
            // WinExe 进程默认没有控制台，附加到父进程（cmd / PowerShell）以便输出可见
            try
            {
                AttachConsole(AttachParentProcess);
                Console.OutputEncoding = System.Text.Encoding.UTF8;
            }
            catch (IOException)
            {
                // 无可用控制台时忽略，输出将被丢弃
            }
            catch (UnauthorizedAccessException)
            {
                // 同上
            }

            PrintBanner();

            if (args == null || args.Length == 0)
            {
                PrintUsage();
                return 1;
            }

            if (args[0] == "--help" || args[0] == "-h")
            {
                PrintUsage();
                return 0;
            }

            string inputPath = args[0];
            bool isFolder = Directory.Exists(inputPath);

            if (!isFolder && !File.Exists(inputPath))
            {
                Console.Error.WriteLine($"[ERROR] 文件 / 文件夹不存在：{inputPath}");
                return 1;
            }

            // 构建选项（默认值已在 DetectorOptions 中定义）
            // 输出目录：显式给出就用给定的，否则留空，等参数解析完（--page 会影响子文件夹名）再推导
            var opts = new DetectorOptions
            {
                OutputDirectory = args.Length >= 2 && !args[1].StartsWith("--") ? args[1] : null
            };

            bool recursive = false;
            bool list = false;
            bool listOnly = false;
            ParseArgs(args, opts, ref recursive, ref list, ref listOnly);

            // --list-only：只生成图面一览（读已落盘的 tables\*.csv，不做分割，可随时重跑）
            if (listOnly)
            {
                if (!isFolder)
                {
                    Console.Error.WriteLine("[ERROR] --list-only 只支持文件夹（批量）模式：" + inputPath);
                    return 1;
                }

                return RunDrawingList(inputPath, opts, recursive);
            }

            // 输入是文件夹 → 批量模式：一套参数跑完该文件夹下所有 PDF
            if (isFolder)
            {
                int code = RunBatch(inputPath, opts, recursive);

                // --list：批量分割结束后附带生成图面一览（刚写出的 tables\*.csv 立刻可用）
                if (list && RunDrawingList(inputPath, opts, recursive) != 0 && code == 0)
                    return 2;

                return code;
            }

            // 输出目录留空 → <输入文件所在目录>\AiAppData\<图纸名>[_P页号]
            if (string.IsNullOrWhiteSpace(opts.OutputDirectory))
                opts.OutputDirectory = DetectorOptions.GetDefaultOutputDirectory(inputPath, opts.PdfPageIndex);

            Console.WriteLine($"[INFO] 调试图像  ：{(opts.SaveDebugImages ? "开启" : "关闭")}");
            Console.WriteLine($"[INFO] 右侧遮蔽  ：{(opts.AutoMaskRightColumn ? "自动检测表格/标题栏" : "固定比例")}"
                              + $"（兜底比例 {opts.RightMaskRatio:0.00}）");
            Console.WriteLine("[INFO] 表格残片筛除：" + (opts.DropTextHeavyParts
                              ? "文字行面积占比 ≥ " + opts.TextHeavyRatio.ToString("0.###") + " 的块丢弃"
                              : "关闭"));
            Console.WriteLine("[INFO] 文字清单   ：" + (opts.SplitTextToWords
                              ? "一个词一行（每个词各自的坐标）"
                              : "按行合并（同一水平线上的词拼成一句）"));
            Console.WriteLine("[INFO] 表格内容   ：" + (opts.ExtractRuledTables
                              ? "提取有线表格（tables_info.json / tables_text.txt / tables\\*.csv）"
                              : "关闭"));
            Console.WriteLine("[INFO] 成品清单 01：" + (opts.BuildLtStep1Table
                              ? "部品表「下表参照」按参照表明细展开 → 01_アッセンブリ図_部品機能情報.csv（仅アッセンブリ図）"
                              : "关闭"));
            Console.WriteLine("[INFO] 成品清单 02：" + (opts.BuildLtStep2Table
                              ? "「品種 × 多列重复」表摊平成 品種/部品名/部品コード/員数 → 02_要領図_部品機能情報.csv（仅要領図，最后一步）"
                              : "关闭"));
            Console.WriteLine("[INFO] 要領図一覧 ：" + (opts.BuildAssemblyYoryoList
                              ? "生成 01_アッセンブリ図_要領図.csv（項目 / 要領図漢字 / 図番 三列，仅アッセンブリ図）"
                              : "关闭"));
            Console.WriteLine("[INFO] 输出目录   ：" + Path.GetFullPath(opts.OutputDirectory));

            if (opts.BuildPdfInfoList)
            {
                // 分类清单整批一份，落在输出根目录的 classify 里（不是图纸目录里）
                Console.WriteLine("[INFO] 分类清单   ："
                                  + PdfInfoListBuilder.GetClassifyDirectory(
                                        opts.OutputDirectory, Path.GetFileNameWithoutExtension(inputPath))
                                  + "\\" + PdfInfoListBuilder.OutputFileName + "（整批共用一份）");
            }

            Console.WriteLine();

            try
            {
                var parts = PartSplitterService.Run(inputPath, opts);
                int count = parts.Count;

                Console.WriteLine();
                if (count > 0)
                {
                    Console.WriteLine(
                        $"[DONE] 共分割零件 {count} 个，已保存至：{Path.GetFullPath(opts.OutputDirectory)}");
                }
                else
                {
                    Console.WriteLine("[DONE] 未检测到零件，请根据提示调整参数后重试。");
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] {ex.GetType().Name}: {ex.Message}");
                Console.Error.WriteLine(ex.StackTrace);
                return 2;
            }
        }

        /// <summary>
        /// 批量模式（输入是文件夹）：一套参数跑完该文件夹下所有 PDF，
        /// 每张图纸各自落到 <c>&lt;输出根目录&gt;\&lt;图纸名&gt;</c>；返回 0 = 全部成功，2 = 有失败。
        /// </summary>
        private static int RunBatch(string inputDirectory, DetectorOptions opts, bool recursive)
        {
            Console.WriteLine($"[INFO] 批量模式  ：{(recursive ? "含子文件夹" : "仅当前文件夹")}");
            Console.WriteLine($"[INFO] 调试图像  ：{(opts.SaveDebugImages ? "开启" : "关闭")}");
            Console.WriteLine($"[INFO] 右侧遮蔽  ：{(opts.AutoMaskRightColumn ? "自动检测表格/标题栏" : "固定比例")}"
                              + $"（兜底比例 {opts.RightMaskRatio:0.00}）");
            Console.WriteLine("[INFO] 表格内容  ：" + (opts.ExtractRuledTables ? "提取有线表格" : "关闭"));
            Console.WriteLine("[INFO] 成品清单 01：" + (opts.BuildLtStep1Table
                              ? "生成 01_アッセンブリ図_部品機能情報.csv（仅アッセンブリ図）"
                              : "关闭"));
            Console.WriteLine("[INFO] 成品清单 02：" + (opts.BuildLtStep2Table
                              ? "生成 02_要領図_部品機能情報.csv（仅要領図，最后一步）"
                              : "关闭"));
            Console.WriteLine("[INFO] 要領図一覧 ：" + (opts.BuildAssemblyYoryoList
                              ? "生成 01_アッセンブリ図_要領図.csv（仅アッセンブリ図）"
                              : "关闭"));
            Console.WriteLine($"[INFO] DPI / 页码：{opts.RenderDpi:0} / {opts.PdfPageIndex}（对每张 PDF 生效）");
            Console.WriteLine();

            List<BatchItemResult> results;

            try
            {
                results = BatchRunner.Run(inputDirectory, opts, recursive, null);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] {ex.GetType().Name}: {ex.Message}");
                return 2;
            }

            if (results.Count == 0)
            {
                Console.WriteLine("[DONE] 该文件夹下没有可处理的 PDF。");
                return 0;
            }

            int ok = 0;
            int failed = 0;
            int partTotal = 0;

            foreach (BatchItemResult item in results)
            {
                if (!item.Success)
                    continue;

                ok++;
                partTotal += item.PartCount;
            }

            failed = results.Count - ok;

            Console.WriteLine();
            Console.WriteLine($"[DONE] 共 {results.Count} 张：成功 {ok}，失败 {failed}，零件合计 {partTotal} 个。");

            if (failed > 0)
            {
                Console.WriteLine("[WARN] 失败清单：");

                foreach (BatchItemResult item in results)
                {
                    if (!item.Success)
                        Console.WriteLine("        " + Path.GetFileName(item.InputFile) + " → " + item.ErrorMessage);
                }

                return 2;
            }

            return 0;
        }

        /// <summary>
        /// 生成图面一览 <see cref="DrawingListBuilder.OutputFileName"/>（図番号 / 文件名 / 分类）：
        /// 只读每张图纸已落盘的 <c>tables\*.csv</c>，不读 PDF、不重新分割，因此可随时重跑。
        /// 返回 0 = 已写出，2 = 没写出来（原因见日志）。
        /// </summary>
        private static int RunDrawingList(string inputDirectory, DetectorOptions opts, bool recursive)
        {
            Console.WriteLine("[INFO] 图面一览  ：" + DrawingListBuilder.OutputFileName
                              + "（按 tables\\*.csv 判 基本図 / 加工図 / アッセンブリ図 / 要領図）");
            Console.WriteLine();

            string csvPath = DrawingListBuilder.Build(inputDirectory, opts.OutputDirectory, null, recursive, opts);

            return csvPath == null ? 2 : 0;
        }

        // ── 命令行解析 ────────────────────────────────────────────────────────

        private static void ParseArgs(string[] args, DetectorOptions opts, ref bool recursive,
                                      ref bool list, ref bool listOnly)
        {
            for (int i = 1; i < args.Length; i++)
            {
                string arg = args[i];

                switch (arg.ToLowerInvariant())
                {
                    case "--recursive":
                        // 批量模式：连子文件夹一起处理
                        recursive = true;
                        break;

                    case "--list":
                        // 批量模式结束后附带生成图面一览 drawing_list.csv
                        list = true;
                        break;

                    case "--list-only":
                        // 只生成图面一览，不做分割（读已有的 tables 目录里的 CSV）
                        listOnly = true;
                        break;

                    case "--debug":
                        opts.SaveDebugImages = true;
                        break;

                    case "--dpi":
                        if (TryGetNextDouble(args, ref i, out double dpi))
                            opts.RenderDpi = dpi;
                        break;

                    case "--page":
                        if (TryGetNextInt(args, ref i, out int page))
                            opts.PdfPageIndex = page;
                        break;

                    case "--mask":
                        if (TryGetNextDouble(args, ref i, out double mask))
                            opts.RightMaskRatio = mask;
                        break;

                    case "--fixed-mask":
                        // 关闭自动遮蔽，强制使用 --mask 给定的固定比例（旧行为）
                        opts.AutoMaskRightColumn = false;
                        break;

                    case "--mask-ink":
                        if (TryGetNextDouble(args, ref i, out double inkRatio))
                            opts.AutoMaskColumnInkRatio = inkRatio;
                        break;

                    case "--no-block-mask":
                        // 关闭「按表格块精确遮蔽」，退回整列遮蔽（排查遮蔽误伤时用）
                        opts.MaskTableBlocks = false;
                        break;

                    case "--no-frame-erase":
                        // 关闭「图框擦除」（排查图框相关问题时用）
                        opts.RemovePageFrame = false;
                        break;

                    case "--no-callout":
                        // 关闭「圈号标注识别」（①1、②2 …；不需要编号或想加速时用）
                        opts.DetectCallouts = false;
                        break;

                    case "--keep-text-heavy":
                        // 关闭「文字面积占比筛除」：保留被判定为非零件图的表格残片
                        opts.DropTextHeavyParts = false;
                        break;

                    case "--text-ratio":
                        // 「文字面积占比」判定阈值（默认 0.60）
                        if (TryGetNextDouble(args, ref i, out double textRatio))
                            opts.TextHeavyRatio = textRatio;
                        break;

                    case "--merge-text-lines":
                        // 文字清单还原为「同一水平线上的词拼成一句」（默认是一个词一行）
                        opts.SplitTextToWords = false;
                        break;

                    case "--no-tables":
                        // 关闭「有线表格内容提取」（tables_info.json / tables_text.txt / tables\*.csv）
                        opts.ExtractRuledTables = false;
                        break;

                    case "--no-pdf-info-list":
                        // 关闭「PDF 信息清单」（不生成 classify\PdfInfoList.txt；
                        // 01_アッセンブリ図_部品機能情報.csv / 02_要領図_部品機能情報.csv 会退回按 tables\*.csv 现判图面分类）
                        opts.BuildPdfInfoList = false;
                        break;

                    case "--no-lt-step1":
                        // 关闭「部品表下表参照展开」（不生成 01_アッセンブリ図_部品機能情報.csv）
                        opts.BuildLtStep1Table = false;
                        break;

                    case "--no-lt-step2":
                        // 关闭「品種 × 多列重复表摊平」（不生成 02_要領図_部品機能情報.csv）
                        opts.BuildLtStep2Table = false;
                        break;

                    case "--no-assembly-yoryo":
                        // 关闭「要領図一覧」（不生成 01_アッセンブリ図_要領図.csv）
                        opts.BuildAssemblyYoryoList = false;
                        break;

                    case "--area":
                        if (TryGetNextDouble(args, ref i, out double area))
                            opts.MinContourArea = area;
                        break;

                    case "--pad":
                        if (TryGetNextInt(args, ref i, out int pad))
                            opts.Padding = pad;
                        break;

                    case "--kw":
                        if (TryGetNextInt(args, ref i, out int kw))
                            opts.DilateKernelWidth = kw;
                        break;

                    case "--kh":
                        if (TryGetNextInt(args, ref i, out int kh))
                            opts.DilateKernelHeight = kh;
                        break;
                }
            }
        }

        private static bool TryGetNextDouble(string[] args, ref int i, out double value)
        {
            value = 0;
            if (i + 1 >= args.Length) return false;
            bool ok = double.TryParse(args[++i], out value);
            if (!ok) Console.WriteLine($"[WARN] 无效数值：{args[i]}，使用默认值。");
            return ok;
        }

        private static bool TryGetNextInt(string[] args, ref int i, out int value)
        {
            value = 0;
            if (i + 1 >= args.Length) return false;
            bool ok = int.TryParse(args[++i], out value);
            if (!ok) Console.WriteLine($"[WARN] 无效整数：{args[i]}，使用默认值。");
            return ok;
        }

        // ── 帮助文本 ──────────────────────────────────────────────────────────

        private static void PrintBanner()
        {
            Console.WriteLine("==============================================");
            Console.WriteLine("  CAD Part Splitter  v1.0");
            Console.WriteLine("  .NET 4.8 | OpenCvSharp4 | Docnet.Core");
            Console.WriteLine("  无参数启动将打开图形界面");
            Console.WriteLine("==============================================");
            Console.WriteLine();
        }

        private static void PrintUsage()
        {
            Console.WriteLine("用法：");
            Console.WriteLine("  CadPartSplitter.exe <输入文件> [输出目录] [选项]");
            Console.WriteLine("  CadPartSplitter.exe <输入文件夹> [输出根目录] [选项]   ← 批量处理该文件夹下所有 PDF");
            Console.WriteLine();
            Console.WriteLine("输入文件：.pdf / .png / .jpg / .bmp / .tif");
            Console.WriteLine("输入文件夹：只处理当前文件夹下的 *.pdf（每张图纸各自一个输出子文件夹）");
            Console.WriteLine();
            Console.WriteLine("选项：");
            Console.WriteLine("  --recursive    批量模式下连子文件夹一起处理（默认只看当前文件夹）");
            Console.WriteLine("  --list         批量模式结束后生成 drawing_list.csv（図番号 / 文件名 / 分类）");
            Console.WriteLine("  --list-only    只生成 drawing_list.csv，不做分割（读已有的 tables 目录 CSV）");
            Console.WriteLine("  --debug        保存中间处理图（二值图、膨胀图）");
            Console.WriteLine("  --dpi   <N>    PDF 渲染 DPI（默认 300）");
            Console.WriteLine("  --page  <N>    PDF 页码，从 0 开始（默认 0）");
            Console.WriteLine("  --mask  <N>    右侧遮蔽比例 0.0~1.0（默认 0.50，自动检测失败时的兜底）");
            Console.WriteLine("  --fixed-mask   关闭自动遮蔽，强制按 --mask 的固定比例遮蔽（旧行为）");
            Console.WriteLine("  --mask-ink <N> 自动遮蔽的列墨迹阈值（图高比例，默认 0.010）");
            Console.WriteLine("  --no-block-mask  关闭「按表格块精确遮蔽」，退回整列遮蔽");
            Console.WriteLine("  --no-frame-erase 关闭「图框擦除」");
            Console.WriteLine("  --no-callout     关闭「圈号标注识别」（①1、②2 …）");
            Console.WriteLine("  --keep-text-heavy  保留文字行占比过高的块（默认丢弃表題欄 / 変更履歴表）");
            Console.WriteLine("  --text-ratio <N>   文字行面积占比判定阈值（默认 0.60）");
            Console.WriteLine("  --merge-text-lines 文字清单按行合并（默认一个词一行，各自带坐标）");
            Console.WriteLine("  --no-tables     关闭「有线表格内容提取」（不生成 tables_info.json 等）");
            Console.WriteLine("  --no-pdf-info-list  关闭「PDF 信息清单」（不生成 classify\\PdfInfoList.txt）");
            Console.WriteLine("  --no-lt-step1   关闭「部品表下表参照展开」（不生成 01_アッセンブリ図_部品機能情報.csv）");
            Console.WriteLine("  --no-lt-step2   关闭「品種 × 多列重复表摊平」（不生成 02_要領図_部品機能情報.csv）");
            Console.WriteLine("  --no-assembly-yoryo 关闭「要領図一覧」（不生成 01_アッセンブリ図_要領図.csv）");
            Console.WriteLine("  --area  <N>    最小轮廓面积（默认 25000）");
            Console.WriteLine("  --pad   <N>    外接框扩展像素（默认 30）");
            Console.WriteLine("  --kw    <N>    膨胀核宽度（默认 60）");
            Console.WriteLine("  --kh    <N>    膨胀核高度（默认 10）");
            Console.WriteLine();
            Console.WriteLine("输出：");
            Console.WriteLine("  省略输出目录时写到 <输入文件所在目录>\\AiAppData\\<图纸名>\\");
            Console.WriteLine("  （第 2 页起为 <图纸名>_P页号，避免覆盖前一页结果）");
            Console.WriteLine("  批量模式（输入是文件夹）省略输出根目录时写到");
            Console.WriteLine("  <输入文件夹>\\AiAppData\\<图纸名>\\（每张图纸各自一个子文件夹）");
            Console.WriteLine("  <输出目录>\\Part_01.png / parts_info.json / parts_text.txt / callouts.txt");
            Console.WriteLine("              / outside_info.json / outside_text.txt");
            Console.WriteLine("              / tables_info.json / tables_text.txt / tables\\table_NN.csv");
            Console.WriteLine("              / 01_アッセンブリ図_部品機能情報.csv");
            Console.WriteLine("                （部品表「下表参照」展开后的明细；旧名 LT_STEP1.csv）");
            Console.WriteLine("                （只有分类清单里判为「アッセンブリ図」的图纸才生成）");
            Console.WriteLine("              / 01_アッセンブリ図_要領図.csv");
            Console.WriteLine("                （項目 / 要領図漢字 / 図番 三列：这张組立図用到的要領図一覧）");
            Console.WriteLine("                （同样只有分类清单里判为「アッセンブリ図」的图纸才生成）");
            Console.WriteLine("              / 02_要領図_部品機能情報.csv");
            Console.WriteLine("                （品種 × 多列重复表摊平后的明细，流水线最后一步；旧名 LT_STEP2.csv）");
            Console.WriteLine("                （只有分类清单里判为「要領図」的图纸才生成）");
            Console.WriteLine("  <输出根目录>\\classify\\PdfInfoList.txt");
            Console.WriteLine("  （PDF 信息清单：図番号 / 文件名 / 图面分类 / 分类依据；整批共用一份，");
            Console.WriteLine("    同一张图重跑就更新那一行 —— 不是每张图纸各一份）");
            Console.WriteLine("              （表格 / 清单类产物为 PDF 输入才有；");
            Console.WriteLine("                用 --no-tables / --no-pdf-info-list / --no-lt-step1 / --no-lt-step2");
            Console.WriteLine("                    / --no-assembly-yoryo 可关闭）");
            Console.WriteLine("  <输出根目录> 下的 drawing_list.csv（図番号 / 文件名 / 分类）");
            Console.WriteLine("  （--list 时随批量处理一并生成；--list-only 时只生成它，不做分割）");
            Console.WriteLine();
            Console.WriteLine("示例：");
            Console.WriteLine("  CadPartSplitter.exe drawing.pdf            （输出到 <图纸目录>\\AiAppData\\drawing）");
            Console.WriteLine("  CadPartSplitter.exe D:\\图纸\\2026Q4        （批量：处理该文件夹下所有 PDF）");
            Console.WriteLine("  CadPartSplitter.exe D:\\图纸 out --recursive  （批量，含子文件夹，结果放 out\\<图纸名>）");
            Console.WriteLine("  CadPartSplitter.exe <输入文件夹> --list        （批量分割后附带生成 drawing_list.csv）");
            Console.WriteLine("  CadPartSplitter.exe <输入文件夹> --list-only   （只按已有 tables 生成 drawing_list.csv）");
            Console.WriteLine("  CadPartSplitter.exe drawing.pdf output --debug");
            Console.WriteLine("  CadPartSplitter.exe scan.png parts --mask 0.48 --area 50000 --fixed-mask --debug");
            Console.WriteLine("  CadPartSplitter.exe plan.pdf result --dpi 200 --kw 60 --kh 20");
        }
    }
}
