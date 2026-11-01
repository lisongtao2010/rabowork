using System;
using System.IO;
using System.Runtime.InteropServices;

namespace CadPartSplitter
{
    /// <summary>
    /// 命令行模式入口（保留原控制台用法，便于批处理调用）。
    ///
    /// 用法：
    ///   CadPartSplitter.exe &lt;输入文件&gt; [输出目录] [选项...]
    ///
    /// 选项：
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
    ///   --area &lt;值&gt;      最小轮廓面积阈值（默认 80000）
    ///   --pad &lt;值&gt;       外接框扩展像素（默认 30）
    ///   --kw &lt;值&gt;        膨胀核宽度（默认 80）
    ///   --kh &lt;值&gt;        膨胀核高度（默认 25）
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

            string inputFile = args[0];
            if (!File.Exists(inputFile))
            {
                Console.Error.WriteLine($"[ERROR] 文件不存在：{inputFile}");
                return 1;
            }

            // 构建选项（默认值已在 DetectorOptions 中定义）
            var opts = new DetectorOptions
            {
                OutputDirectory = args.Length >= 2 && !args[1].StartsWith("--")
                    ? args[1]
                    : "output"
            };

            ParseArgs(args, opts);

            Console.WriteLine($"[INFO] 调试图像  ：{(opts.SaveDebugImages ? "开启" : "关闭")}");
            Console.WriteLine($"[INFO] 右侧遮蔽  ：{(opts.AutoMaskRightColumn ? "自动检测表格/标题栏" : "固定比例")}"
                              + $"（兜底比例 {opts.RightMaskRatio:0.00}）");
            Console.WriteLine("[INFO] 表格残片筛除：" + (opts.DropTextHeavyParts
                              ? "文字行面积占比 ≥ " + opts.TextHeavyRatio.ToString("0.###") + " 的块丢弃"
                              : "关闭"));
            Console.WriteLine("[INFO] 文字清单   ：" + (opts.SplitTextToWords
                              ? "一个词一行（每个词各自的坐标）"
                              : "按行合并（同一水平线上的词拼成一句）"));
            Console.WriteLine();

            try
            {
                var parts = PartSplitterService.Run(inputFile, opts);
                int count = parts.Count;

                Console.WriteLine();
                if (count > 0)
                    Console.WriteLine(
                        $"[DONE] 共分割零件 {count} 个，已保存至：{Path.GetFullPath(opts.OutputDirectory)}");
                else
                    Console.WriteLine("[DONE] 未检测到零件，请根据提示调整参数后重试。");

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] {ex.GetType().Name}: {ex.Message}");
                Console.Error.WriteLine(ex.StackTrace);
                return 2;
            }
        }

        // ── 命令行解析 ────────────────────────────────────────────────────────

        private static void ParseArgs(string[] args, DetectorOptions opts)
        {
            for (int i = 1; i < args.Length; i++)
            {
                string arg = args[i];

                switch (arg.ToLowerInvariant())
                {
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
            Console.WriteLine();
            Console.WriteLine("输入文件：.pdf / .png / .jpg / .bmp / .tif");
            Console.WriteLine();
            Console.WriteLine("选项：");
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
            Console.WriteLine("  --area  <N>    最小轮廓面积（默认 80000）");
            Console.WriteLine("  --pad   <N>    外接框扩展像素（默认 30）");
            Console.WriteLine("  --kw    <N>    膨胀核宽度（默认 80）");
            Console.WriteLine("  --kh    <N>    膨胀核高度（默认 25）");
            Console.WriteLine();
            Console.WriteLine("示例：");
            Console.WriteLine("  CadPartSplitter.exe drawing.pdf output --debug");
            Console.WriteLine("  CadPartSplitter.exe scan.png parts --mask 0.48 --area 50000 --fixed-mask --debug");
            Console.WriteLine("  CadPartSplitter.exe plan.pdf result --dpi 200 --kw 60 --kh 20");
        }
    }
}
