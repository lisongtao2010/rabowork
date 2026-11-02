using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace CadPartSplitter
{
    /// <summary>
    /// 图形界面参数的本地持久化：界面上设定的识别参数在关闭窗口 / 开始分割时写入
    /// <c>%APPDATA%\CadPartSplitter\settings.ini</c>，下次启动自动读回。
    ///
    /// 选 <c>%APPDATA%</c> 而不是 exe 旁边，是因为本程序会被放进别的工程里调用，
    /// 安装目录（如 Program Files）通常对普通用户只读，写不进去就没法记住参数。
    /// 文件是纯文本 INI（<c>键=值</c>，<c>#</c> 开头为注释），可以手工改。
    /// </summary>
    public sealed class GuiSettings
    {
        /// <summary>默认值来源：与 <see cref="DetectorOptions"/> 保持单一真源，避免两处默认值漂移。</summary>
        private static readonly DetectorOptions Defaults = new DetectorOptions();

        private const string FolderName = "CadPartSplitter";
        private const string FileName   = "settings.ini";

        // ── 界面可调参数（与 MainForm 上的控件一一对应）────────────────────────

        /// <summary>渲染 DPI（默认 300）。</summary>
        public double RenderDpi { get; set; } = Defaults.RenderDpi;

        /// <summary>PDF 页码，从 0 开始（默认 0）。</summary>
        public int PdfPageIndex { get; set; } = Defaults.PdfPageIndex;

        /// <summary>右侧遮蔽比例 0.01 ~ 0.99（默认 0.50）。</summary>
        public double RightMaskRatio { get; set; } = Defaults.RightMaskRatio;

        /// <summary>最小轮廓面积（默认 80000）。</summary>
        public double MinContourArea { get; set; } = Defaults.MinContourArea;

        /// <summary>外接框扩展像素（默认 30）。</summary>
        public int Padding { get; set; } = Defaults.Padding;

        /// <summary>膨胀核宽度（默认 80）。</summary>
        public int DilateKernelWidth { get; set; } = Defaults.DilateKernelWidth;

        /// <summary>膨胀核高度（默认 25）。</summary>
        public int DilateKernelHeight { get; set; } = Defaults.DilateKernelHeight;

        /// <summary>是否自动检测表格 / 标题栏起点作为遮蔽边界（默认开）。</summary>
        public bool AutoMaskRightColumn { get; set; } = Defaults.AutoMaskRightColumn;

        /// <summary>是否保存中间处理图（默认关）。</summary>
        public bool SaveDebugImages { get; set; } = Defaults.SaveDebugImages;

        /// <summary>是否筛除表格残片（文字行占比过高的块，默认开）。</summary>
        public bool DropTextHeavyParts { get; set; } = Defaults.DropTextHeavyParts;

        // ── 配置文件位置 ──────────────────────────────────────────────────────

        /// <summary>配置文件所在目录：<c>%APPDATA%\CadPartSplitter</c>（取不到 APPDATA 时退回程序目录）。</summary>
        public static string SettingsDirectory
        {
            get
            {
                string appData = null;
                try
                {
                    appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                }
                catch (Exception)
                {
                    // 极少见的受限环境：下面的兜底路径足够用
                }

                if (string.IsNullOrEmpty(appData))
                    appData = AppDomain.CurrentDomain.BaseDirectory;

                return Path.Combine(appData, FolderName);
            }
        }

        /// <summary>配置文件完整路径：<c>%APPDATA%\CadPartSplitter\settings.ini</c>。</summary>
        public static string SettingsFilePath
        {
            get { return Path.Combine(SettingsDirectory, FileName); }
        }

        /// <summary>配置文件是否已存在（界面用来决定是否提示「已载入上次参数」）。</summary>
        public static bool Exists
        {
            get
            {
                try
                {
                    return File.Exists(SettingsFilePath);
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        // ── 读写 ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 读取界面参数。文件不存在、读失败或格式不对时返回默认值，绝不抛异常
        /// （配置文件是给人手工改的，坏内容不能连累程序启动）。
        /// 读到的数值会按界面控件的取值范围钳制。
        /// </summary>
        public static GuiSettings Load()
        {
            return Load(SettingsFilePath);
        }

        /// <summary>从指定路径读取界面参数（便于测试 / 调用方自备配置）。</summary>
        public static GuiSettings Load(string filePath)
        {
            var settings = new GuiSettings();
            if (string.IsNullOrWhiteSpace(filePath))
                return settings;

            string[] lines;
            try
            {
                if (!File.Exists(filePath))
                    return settings;

                lines = File.ReadAllLines(filePath, Encoding.UTF8);
            }
            catch (IOException)
            {
                return settings;
            }
            catch (UnauthorizedAccessException)
            {
                return settings;
            }
            catch (ArgumentException)
            {
                return settings;
            }

            foreach (string rawLine in lines)
            {
                if (rawLine == null)
                    continue;

                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                    continue;

                int sep = line.IndexOf('=');
                if (sep <= 0)
                    continue;

                string key   = line.Substring(0, sep).Trim();
                string value = line.Substring(sep + 1).Trim();

                switch (key.ToLowerInvariant())
                {
                    case "renderdpi":
                        settings.RenderDpi = Clamp(ReadDouble(value, settings.RenderDpi), 72.0, 1200.0);
                        break;
                    case "pdfpageindex":
                        settings.PdfPageIndex = Clamp(ReadInt(value, settings.PdfPageIndex), 0, 9999);
                        break;
                    case "rightmaskratio":
                        settings.RightMaskRatio = Clamp(ReadDouble(value, settings.RightMaskRatio), 0.01, 0.99);
                        break;
                    case "mincontourarea":
                        settings.MinContourArea = Clamp(ReadDouble(value, settings.MinContourArea), 1000.0, 5000000.0);
                        break;
                    case "padding":
                        settings.Padding = Clamp(ReadInt(value, settings.Padding), 0, 500);
                        break;
                    case "dilatekernelwidth":
                        settings.DilateKernelWidth = Clamp(ReadInt(value, settings.DilateKernelWidth), 1, 500);
                        break;
                    case "dilatekernelheight":
                        settings.DilateKernelHeight = Clamp(ReadInt(value, settings.DilateKernelHeight), 1, 500);
                        break;
                    case "automaskrightcolumn":
                        settings.AutoMaskRightColumn = ReadBool(value, settings.AutoMaskRightColumn);
                        break;
                    case "savedebugimages":
                        settings.SaveDebugImages = ReadBool(value, settings.SaveDebugImages);
                        break;
                    case "droptextheavyparts":
                        settings.DropTextHeavyParts = ReadBool(value, settings.DropTextHeavyParts);
                        break;
                    // 未知键（例如以后版本新增的参数）直接忽略
                }
            }

            return settings;
        }

        /// <summary>
        /// 保存界面参数到默认位置。目录不存在时自动创建；
        /// 返回 false 表示写入失败（权限 / 占用等），调用方自行决定是否提示。
        /// </summary>
        public bool Save()
        {
            return Save(SettingsFilePath);
        }

        /// <summary>保存界面参数到指定路径。</summary>
        public bool Save(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return false;

            var sb = new StringBuilder();
            sb.AppendLine("# CadPartSplitter 界面参数（程序自动生成，可手工修改；删除本文件即恢复默认值）");
            sb.AppendLine();
            sb.AppendLine("[options]");
            sb.AppendLine("RenderDpi="           + Format(RenderDpi));
            sb.AppendLine("PdfPageIndex="        + PdfPageIndex.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("RightMaskRatio="      + Format(RightMaskRatio));
            sb.AppendLine("MinContourArea="      + Format(MinContourArea));
            sb.AppendLine("Padding="             + Padding.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("DilateKernelWidth="   + DilateKernelWidth.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("DilateKernelHeight="  + DilateKernelHeight.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("AutoMaskRightColumn=" + (AutoMaskRightColumn ? "true" : "false"));
            sb.AppendLine("SaveDebugImages="     + (SaveDebugImages ? "true" : "false"));
            sb.AppendLine("DropTextHeavyParts="  + (DropTextHeavyParts ? "true" : "false"));

            try
            {
                string dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(false));
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (NotSupportedException)
            {
                return false;
            }
        }

        // ── 与 DetectorOptions 互转（调用方想「沿用界面上次调好的参数」时用）────

        /// <summary>把界面参数套用到检测选项上（不改动 <see cref="DetectorOptions.OutputDirectory"/> 与日志回调）。</summary>
        public void ApplyTo(DetectorOptions options)
        {
            if (options == null)
                throw new ArgumentNullException("options");

            options.RenderDpi           = RenderDpi;
            options.PdfPageIndex        = PdfPageIndex;
            options.RightMaskRatio      = RightMaskRatio;
            options.MinContourArea      = MinContourArea;
            options.Padding             = Padding;
            options.DilateKernelWidth   = Math.Max(1, DilateKernelWidth);
            options.DilateKernelHeight  = Math.Max(1, DilateKernelHeight);
            options.AutoMaskRightColumn = AutoMaskRightColumn;
            options.SaveDebugImages     = SaveDebugImages;
            options.DropTextHeavyParts  = DropTextHeavyParts;
        }

        /// <summary>从检测选项生成界面参数快照。</summary>
        public static GuiSettings FromOptions(DetectorOptions options)
        {
            if (options == null)
                throw new ArgumentNullException("options");

            var settings = new GuiSettings();
            settings.RenderDpi           = options.RenderDpi;
            settings.PdfPageIndex        = options.PdfPageIndex;
            settings.RightMaskRatio      = options.RightMaskRatio;
            settings.MinContourArea      = options.MinContourArea;
            settings.Padding             = options.Padding;
            settings.DilateKernelWidth   = options.DilateKernelWidth;
            settings.DilateKernelHeight  = options.DilateKernelHeight;
            settings.AutoMaskRightColumn = options.AutoMaskRightColumn;
            settings.SaveDebugImages     = options.SaveDebugImages;
            settings.DropTextHeavyParts  = options.DropTextHeavyParts;
            return settings;
        }

        // ── 解析辅助 ──────────────────────────────────────────────────────────

        private static string Format(double value)
        {
            // 用不变文化写盘，避免德语等区域写出 "0,5" 导致下次读不回来
            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private static double ReadDouble(string text, double fallback)
        {
            double value;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                ? value
                : fallback;
        }

        private static int ReadInt(string text, int fallback)
        {
            int value;
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                ? value
                : fallback;
        }

        private static bool ReadBool(string text, bool fallback)
        {
            if (string.IsNullOrEmpty(text))
                return fallback;

            switch (text.Trim().ToLowerInvariant())
            {
                case "true":
                case "1":
                case "yes":
                case "on":
                    return true;
                case "false":
                case "0":
                case "no":
                case "off":
                    return false;
                default:
                    return fallback;
            }
        }

        private static double Clamp(double value, double min, double max)
        {
            if (double.IsNaN(value))
                return min;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}


