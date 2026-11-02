using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CadPartSplitter
{
    /// <summary>
    /// 零件分割工具主窗体：选择 / 粘贴 PDF、图片或整个文件夹 → 设置识别参数 → 执行分割 → 查看日志与结果。
    /// 输入是文件夹时为批量模式：逐张处理该文件夹下的所有 PDF。
    /// </summary>
    public partial class MainForm : Form
    {
        /// <summary>输入路径框的提示语（控件没有原生 Placeholder，用 EM_SETCUEBANNER 画上去）。</summary>
        private const string InputCueBanner = "可直接 Ctrl+V 粘贴路径（文件夹即批量处理，Enter 开始）";

        /// <summary>EM_SETCUEBANNER：给空文本框画一段灰色提示文字。</summary>
        private const int EM_SETCUEBANNER = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        /// <summary>输入 / 输出路径框的悬浮提示（字段持有，避免被 GC 掉）。</summary>
        private readonly ToolTip _pathTip = new ToolTip();

        /// <summary>上次由程序自动填写的输出目录，用于判断用户是否手工改过。</summary>
        private string _autoOutputText;

        /// <summary>当前输入文件为 PDF 时记录其总页数；非 PDF 为 0。</summary>
        private int _pdfPageCount;

        public MainForm()
        {
            InitializeComponent();
            WireEvents();
            LoadUiOptions();
        }

        // ── 初始化 ────────────────────────────────────────────────────────────

        /// <summary>集中绑定控件事件，保持 Designer 文件仅负责布局。</summary>
        private void WireEvents()
        {
            txtInput.TextChanged     += txtInput_TextChanged;
            txtInput.KeyDown         += txtInput_KeyDown;
            txtOutput.KeyDown        += txtOutput_KeyDown;
            btnBrowseInput.Click     += btnBrowseInput_Click;
            btnBrowseFolder.Click    += btnBrowseFolder_Click;
            btnBrowseOutput.Click    += btnBrowseOutput_Click;
            btnStart.Click           += btnStart_Click;
            btnOpenOutput.Click      += btnOpenOutput_Click;
            FormClosing              += MainForm_FormClosing;
            Shown                    += MainForm_Shown;
            DragEnter                += MainForm_DragEnter;
            DragDrop                 += MainForm_DragDrop;
        }

        /// <summary>窗口显示后给路径框加提示语（EM_SETCUEBANNER 需要控件已有窗口句柄）。</summary>
        private void MainForm_Shown(object sender, EventArgs e)
        {
            try
            {
                SendMessage(txtInput.Handle, EM_SETCUEBANNER, IntPtr.Zero, InputCueBanner);
            }
            catch (Exception)
            {
                // 提示语只是锦上添花，设置失败不影响使用
            }

            _pathTip.SetToolTip(txtInput,
                "可以直接把路径粘进来（Ctrl+V）：" + Environment.NewLine
                + "· 文件夹 → 批量处理该文件夹下所有 PDF" + Environment.NewLine
                + "· 文件 → 只处理这一张图纸" + Environment.NewLine
                + "· 资源管理器「复制为路径」带的引号会自动去掉");
            _pathTip.SetToolTip(txtOutput, "批量模式下这里是「输出根目录」，每张图纸再各自一个子文件夹");
        }

        /// <summary>
        /// 把识别参数同步到界面控件：先取 <see cref="DetectorOptions"/> 的默认值，
        /// 再用上次保存的界面参数（<c>%APPDATA%\CadPartSplitter\settings.ini</c>）覆盖，
        /// 实现「上次调好的参数，下次自动读回」。
        /// </summary>
        private void LoadUiOptions()
        {
            var defaults = new DetectorOptions();

            numDpi.Value     = (decimal)defaults.RenderDpi;
            numPage.Value    = defaults.PdfPageIndex;
            numMask.Value    = (decimal)defaults.RightMaskRatio;
            numArea.Value    = (decimal)defaults.MinContourArea;
            numPad.Value     = defaults.Padding;
            numKw.Value      = defaults.DilateKernelWidth;
            numKh.Value      = defaults.DilateKernelHeight;
            chkAutoMask.Checked = defaults.AutoMaskRightColumn;
            chkDebug.Checked = defaults.SaveDebugImages;
            chkDropTextHeavy.Checked = defaults.DropTextHeavyParts;

            // 上次保存的界面参数（无配置文件时读到的就是默认值，不会抛异常）
            GuiSettings saved = GuiSettings.Load();
            SetNumeric(numDpi,  (decimal)saved.RenderDpi);
            SetNumeric(numPage, saved.PdfPageIndex);
            SetNumeric(numMask, (decimal)saved.RightMaskRatio);
            SetNumeric(numArea, (decimal)saved.MinContourArea);
            SetNumeric(numPad,  saved.Padding);
            SetNumeric(numKw,   saved.DilateKernelWidth);
            SetNumeric(numKh,   saved.DilateKernelHeight);
            chkAutoMask.Checked      = saved.AutoMaskRightColumn;
            chkDebug.Checked         = saved.SaveDebugImages;
            chkDropTextHeavy.Checked = saved.DropTextHeavyParts;

            // 输出目录随所选文件变化（<图纸目录>\AiAppData\<图纸名>），未选文件前留空
            SetOutputDirectory(string.Empty);

            if (GuiSettings.Exists)
                AppendLog($"已载入上次保存的识别参数（{GuiSettings.SettingsFilePath}）。");
        }

        /// <summary>把当前界面参数写入配置文件（下次启动自动读回）。</summary>
        private void SaveUiOptions()
        {
            var settings = new GuiSettings
            {
                RenderDpi           = (double)numDpi.Value,
                PdfPageIndex        = (int)numPage.Value,
                RightMaskRatio      = (double)numMask.Value,
                MinContourArea      = (double)numArea.Value,
                Padding             = (int)numPad.Value,
                DilateKernelWidth   = (int)numKw.Value,
                DilateKernelHeight  = (int)numKh.Value,
                AutoMaskRightColumn = chkAutoMask.Checked,
                SaveDebugImages     = chkDebug.Checked,
                DropTextHeavyParts  = chkDropTextHeavy.Checked
            };

            if (!settings.Save())
                AppendLog($"[WARN] 界面参数保存失败：{GuiSettings.SettingsFilePath}（本次设置下次启动不会恢复）");
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveUiOptions();
        }

        /// <summary>给数值控件赋值，超范围时钳到控件的 Min/Max（手改配置文件也不会让界面报错）。</summary>
        private static void SetNumeric(NumericUpDown control, decimal value)
        {
            if (value < control.Minimum) value = control.Minimum;
            if (value > control.Maximum) value = control.Maximum;
            control.Value = value;
        }

        // ── 输入 / 输出选择 ───────────────────────────────────────────────────

        private void btnBrowseInput_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title  = "选择要分割的图纸文件";
                dialog.Filter = "支持的文件|*.pdf;*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff"
                              + "|PDF 文件|*.pdf"
                              + "|图像文件|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff"
                              + "|所有文件|*.*";
                dialog.CheckFileExists = true;

                string currentDir = GetDirectoryOrNull(NormalizePath(txtInput.Text));
                if (currentDir != null)
                    dialog.InitialDirectory = currentDir;

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        SetInputFile(dialog.FileName);
                    }
                    catch (Exception ex)
                    {
                        ShowInputError("选择文件", ex);
                    }
                }
            }
        }

        /// <summary>
        /// 选择「包含图纸 PDF 的文件夹」→ 批量模式：处理该文件夹下所有 PDF
        /// （每张图纸仍各自一个输出子文件夹）。
        /// </summary>
        private void btnBrowseFolder_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description        = "选择包含图纸 PDF 的文件夹（处理该文件夹下所有 PDF）";
                dialog.ShowNewFolderButton = false;

                string current = NormalizePath(txtInput.Text);
                if (!Directory.Exists(current))
                    current = GetDirectoryOrNull(current);

                if (!string.IsNullOrEmpty(current) && Directory.Exists(current))
                    dialog.SelectedPath = current;

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        SetInputFolder(dialog.SelectedPath);
                    }
                    catch (Exception ex)
                    {
                        ShowInputError("选择文件夹", ex);
                    }
                }
            }
        }

        private void btnBrowseOutput_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择零件分割结果的输出目录";
                dialog.ShowNewFolderButton = true;

                string currentDir = NormalizePath(txtOutput.Text);
                if (Directory.Exists(currentDir))
                    dialog.SelectedPath = currentDir;

                if (dialog.ShowDialog(this) == DialogResult.OK)
                    SetOutputDirectory(dialog.SelectedPath);
            }
        }

        private void MainForm_DragEnter(object sender, DragEventArgs e)
        {
            e.Effect = e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        }

        private void MainForm_DragDrop(object sender, DragEventArgs e)
        {
            if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop))
                return;

            var dropped = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (dropped == null || dropped.Length == 0)
                return;

            // 拖进来的是文件夹 → 批量模式；是文件 → 原来的单文件模式
            string first = dropped[0];

            try
            {
                if (Directory.Exists(first))
                    SetInputFolder(first);
                else if (File.Exists(first))
                    SetInputFile(first);
            }
            catch (Exception ex)
            {
                ShowInputError("拖入路径", ex);
            }
        }

        /// <summary>设置输入文件，并自动推导输出目录与 PDF 页数。</summary>
        private void SetInputFile(string filePath)
        {
            txtInput.Text = filePath;

            // 自动推导输出目录：<文件所在目录>\AiAppData\<图纸名>（与下游 AI 环节的约定）
            // 仅当用户未手工修改过输出目录时才自动覆盖
            string current = NormalizePath(txtOutput.Text);

            if (string.IsNullOrEmpty(current) || current == _autoOutputText)
                SetOutputDirectory(DetectorOptions.GetDefaultOutputDirectory(filePath, (int)numPage.Value));

            AppendLog(string.Empty);
            AppendLog($"已选择文件：{filePath}");

            // 页数上限 / 状态栏（顺带在日志里写一句「该 PDF 共 N 页」）
            SyncInputState(filePath, true);
        }

        /// <summary>
        /// 设置输入文件夹（批量模式）：处理该文件夹下所有 PDF，每张图纸各自一个输出子文件夹。
        /// 输出目录自动填 <c>&lt;文件夹&gt;\AiAppData</c>（不再是单文件的「含图纸名」路径）。
        /// </summary>
        private void SetInputFolder(string folderPath)
        {
            txtInput.Text = folderPath;

            // 批量：输出「根」目录 = <文件夹>\AiAppData，图纸名子文件夹由 BatchRunner 逐张补齐
            string current = NormalizePath(txtOutput.Text);
            if (string.IsNullOrEmpty(current) || current == _autoOutputText)
                SetOutputDirectory(DetectorOptions.GetDefaultBatchOutputDirectory(folderPath));

            AppendLog(string.Empty);
            AppendLog($"已选择文件夹：{folderPath}");

            // 张数 / 状态栏（顺带在日志里写一句「该文件夹下有 N 个 PDF」或「没有 PDF」）
            SyncInputState(folderPath, true);
        }

        /// <summary>
        /// 让「页码上限 + 状态栏」跟随输入路径 —— 浏览、拖拽、粘贴、手打四条入口都走这里，行为才一致：
        /// 文件夹 → 页码上限放开到 9999（序号按每张图各自校验）、状态栏显示待处理张数；
        /// PDF → 上限收到「总页数 − 1」，读不到页数就按未知处理（真正分割时会给出明确错误）。
        /// </summary>
        /// <param name="input">已规范化的输入路径（文件或文件夹）。</param>
        /// <param name="logToPanel">
        /// 只有「用户明确选定了路径」（浏览 / 拖拽 / 粘完回车）才为 true —— 手打路径时每敲一个字符都可能
        /// 命中一个已存在的上级目录，不该每敲一下就往日志里写一行。
        /// </param>
        private void SyncInputState(string input, bool logToPanel)
        {
            if (Directory.Exists(input))
            {
                // 批量：页码按「每张图各自」校验，单文件的页数上限不再适用
                _pdfPageCount   = 0;
                numPage.Maximum = 9999;

                int count = BatchRunner.FindPdfFiles(input).Count;

                if (logToPanel)
                {
                    AppendLog(count > 0
                        ? $"该文件夹下有 {count} 个 PDF（仅当前文件夹，不含子文件夹），将逐张处理。"
                        : "[WARN] 该文件夹下没有 PDF —— 请换一个文件夹，或用「浏览…」选单个文件。");
                }

                lblStatus.Text      = count > 0 ? $"就绪：待处理 {count} 张" : "未找到 PDF";
                lblStatus.ForeColor = count > 0 ? SystemColors.ControlText : Color.DarkOrange;
                return;
            }

            if (!File.Exists(input))
                return;

            // PDF：读取总页数并限制页码范围
            _pdfPageCount = 0;
            if (string.Equals(Path.GetExtension(input), ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    _pdfPageCount = PdfRenderer.GetPageCount(input);
                }
                catch (Exception ex)
                {
                    AppendLog($"[WARN] 无法读取 PDF 页数：{ex.Message}");
                }
            }

            numPage.Maximum = _pdfPageCount > 0 ? _pdfPageCount - 1 : 9999;
            if (numPage.Value > numPage.Maximum)
                numPage.Value = numPage.Maximum;

            if (logToPanel && _pdfPageCount > 0)
                AppendLog($"该 PDF 共 {_pdfPageCount} 页，页码范围 0 ~ {_pdfPageCount - 1}。");

            lblStatus.Text      = "就绪";
            lblStatus.ForeColor = SystemColors.ControlText;
        }

        /// <summary>设置输出目录框内容（记录自动填充值，便于判断用户是否手工修改）。</summary>
        private void SetOutputDirectory(string directory)
        {
            _autoOutputText = directory;
            txtOutput.Text  = directory;
        }

        private void txtInput_TextChanged(object sender, EventArgs e)
        {
            string raw   = txtInput.Text;
            string input = NormalizePath(raw);

            // 粘贴进来的路径常带引号（资源管理器「复制为路径」）或换行，
            // 一旦认得出是存在的路径，就顺手把框里的文字也清理干净
            if (!string.Equals(raw, input, StringComparison.Ordinal)
                && (Directory.Exists(input) || File.Exists(input)))
            {
                txtInput.Text         = input;
                txtInput.SelectionStart = txtInput.TextLength;
            }

            bool isFolder = Directory.Exists(input);

            btnStart.Enabled = isFolder || File.Exists(input);
            btnStart.Text    = isFolder ? "开始批量处理" : "开始分割";

            // 手工输入 / 粘贴路径时也让输出目录跟着走（用户改过输出目录则不动）
            string current = NormalizePath(txtOutput.Text);
            if (!string.IsNullOrEmpty(input)
                && (string.IsNullOrEmpty(current) || current == _autoOutputText))
            {
                // 文件夹 → 输出「根」目录；单文件 → <目录>\AiAppData\<图纸名>
                SetOutputDirectory(isFolder
                    ? DetectorOptions.GetDefaultBatchOutputDirectory(input)
                    : DetectorOptions.GetDefaultOutputDirectory(input, (int)numPage.Value));
            }

            // 页码上限 / 状态栏也跟上（静默版：手打路径时不刷日志）
            SyncInputState(input, false);
        }

        // ── 粘贴路径 ──────────────────────────────────────────────────────────

        /// <summary>
        /// 输入框键盘便捷操作：<c>Ctrl+V</c> 走「规范化粘贴」，<c>Enter</c> 直接开始处理。
        /// </summary>
        private void txtInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.V)
            {
                e.SuppressKeyPress = true;
                PastePathInto(txtInput);
                return;
            }

            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;

                if (btnStart.Enabled)
                    btnStart.PerformClick();
            }
        }

        /// <summary>输出目录框：<c>Ctrl+V</c> 粘贴时同样去掉引号 / 取第一行 / 转换 <c>file:///</c>。</summary>
        private void txtOutput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.V)
            {
                e.SuppressKeyPress = true;
                PastePathInto(txtOutput);
            }
        }

        /// <summary>
        /// 把剪贴板里的路径粘进 <paramref name="target"/>：先规范化再整体替换，
        /// 粘贴的是路径框时顺便在日志里说明识别成了文件夹还是文件。
        /// </summary>
        private void PastePathInto(TextBox target)
        {
            string raw;

            try
            {
                raw = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
            }
            catch (Exception ex)
            {
                // 剪贴板被别的进程占用时退回系统的默认粘贴
                AppendLog($"[WARN] 读取剪贴板失败：{ex.Message}");
                return;
            }

            string path = NormalizePath(raw);
            if (path.Length == 0)
                return;

            target.Text            = path;
            target.SelectionStart  = target.TextLength;

            if (ReferenceEquals(target, txtInput))
                DescribePastedPath(path);
        }

        /// <summary>把粘贴进来的路径「识别结果」写进日志，让用户确认粘对了。</summary>
        private void DescribePastedPath(string path)
        {
            if (Directory.Exists(path))
            {
                int count = BatchRunner.FindPdfFiles(path).Count;

                AppendLog(count > 0
                    ? $"[INFO] 已粘贴文件夹：{path}（{count} 个 PDF，将批量处理）"
                    : $"[WARN] 已粘贴文件夹：{path}（该文件夹下没有 PDF）");
            }
            else if (File.Exists(path))
            {
                AppendLog($"[INFO] 已粘贴文件：{path}");
            }
            else
            {
                AppendLog($"[WARN] 粘贴的路径不存在：{path}");
            }
        }

        /// <summary>
        /// 规范化粘贴 / 键入的路径：
        /// 去首尾空白与换行（多行只取第一行）、去掉最外层引号（<c>"…"</c> / <c>'…'</c> / <c>“…”</c>）、
        /// 把 <c>file:///D:/图纸</c> 转成 <c>D:\图纸</c>。
        ///
        /// 资源管理器的「复制为路径」给的是带双引号的 <c>"D:\図面"</c>，
        /// 直接拿去 <see cref="Directory.Exists(string)"/> 会判成不存在（按钮变灰），所以先收拾干净。
        /// </summary>
        private static string NormalizePath(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return string.Empty;

            string text = raw.Trim();

            // 单行输入框里换行没有意义：多行粘贴只认第一行
            int newline = text.IndexOfAny(new[] { '\r', '\n' });
            if (newline >= 0)
                text = text.Substring(0, newline).Trim();

            if (text.Length == 0)
                return string.Empty;

            // file:///D:/图纸 → D:\图纸（浏览器 / 快捷方式常见）
            if (text.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                Uri uri;
                if (Uri.TryCreate(text, UriKind.Absolute, out uri) && uri.IsFile)
                    text = uri.LocalPath;
            }

            // 去掉最外层引号，可能被复制工具套了两层
            while (text.Length >= 2)
            {
                char first = text[0];
                char last  = text[text.Length - 1];

                if ((first == '"'  && last == '"')
                 || (first == '\'' && last == '\'')
                 || (first == '“'  && last == '”')
                 || (first == '‘'  && last == '’'))
                {
                    text = text.Substring(1, text.Length - 2).Trim();
                    continue;
                }

                break;
            }

            return text;
        }

        // ── 执行分割 ──────────────────────────────────────────────────────────

        private async void btnStart_Click(object sender, EventArgs e)
        {
            // 手打 / 粘贴的路径先规范化（去引号、多行取第一行、file:/// 转换），
            // 再写回输入框，让用户看到程序实际用的是哪个路径
            string input = NormalizePath(txtInput.Text);
            if (!string.Equals(txtInput.Text, input, StringComparison.Ordinal))
            {
                txtInput.Text           = input;
                txtInput.SelectionStart = txtInput.TextLength;
            }

            // 输入是文件夹 → 批量处理该文件夹下所有 PDF
            if (Directory.Exists(input))
            {
                await RunBatchAsync(input);
                return;
            }

            if (string.IsNullOrEmpty(input) || !File.Exists(input))
            {
                MessageBox.Show(this, "请先选择 / 粘贴有效的 PDF、图片文件，或包含 PDF 的文件夹。", "提示",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string outputDir = NormalizePath(txtOutput.Text);
            if (!string.Equals(txtOutput.Text, outputDir, StringComparison.Ordinal))
                txtOutput.Text = outputDir;

            if (string.IsNullOrEmpty(outputDir) || outputDir == _autoOutputText)
            {
                // 空 = 用户清空了输出目录；仍等于自动填的值 = 用户没改过（只是调了页码）。
                // 两种情况都按「当前文件 + 当前页码」重算：<图纸目录>\AiAppData\<图纸名>[_P页号]
                outputDir = DetectorOptions.GetDefaultOutputDirectory(input, (int)numPage.Value);
                SetOutputDirectory(outputDir);
            }

            DetectorOptions opts;
            try
            {
                opts = BuildOptions(input, outputDir);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                MessageBox.Show(this, ex.Message, "参数错误",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 参数校验通过后立即落盘：即使处理中途出错 / 程序被强杀，下次也能读回这套参数
            SaveUiOptions();

            SetBusy(true);
            txtLog.Clear();
            AppendLog($"===== 开始处理：{Path.GetFileName(input)} =====");
            AppendLog($"[INFO] DPI={opts.RenderDpi:0}  页码={opts.PdfPageIndex}  遮蔽={opts.RightMaskRatio:0.00}"
                    + $"（自动={ (opts.AutoMaskRightColumn ? "开" : "关") }）"
                    + $"  最小面积={opts.MinContourArea:0}  边距={opts.Padding}"
                    + $"  核={opts.DilateKernelWidth}×{opts.DilateKernelHeight}");

            try
            {
                // CPU 密集的处理放到线程池执行，避免界面假死
                var parts = await Task.Run(() => PartSplitterService.Run(input, opts));
                int count = parts.Count;

                if (count > 0)
                {
                    AppendLog(string.Empty);
                    AppendLog($"[DONE] 共分割零件 {count} 个 → {Path.GetFullPath(outputDir)}");
                    lblStatus.Text      = $"完成：{count} 个零件";
                    lblStatus.ForeColor = Color.ForestGreen;
                }
                else
                {
                    AppendLog(string.Empty);
                    AppendLog("[DONE] 未检测到零件，请根据上方提示调整参数后重试。");
                    lblStatus.Text      = "未检测到零件";
                    lblStatus.ForeColor = Color.DarkOrange;
                }
            }
            catch (Exception ex)
            {
                AppendLog(string.Empty);
                AppendLog($"[ERROR] {ex.GetType().Name}: {ex.Message}");

                lblStatus.Text      = "处理失败";
                lblStatus.ForeColor = Color.Firebrick;

                MessageBox.Show(this,
                    $"处理过程中发生错误：{Environment.NewLine}{ex.Message}",
                    "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }


        // ── 批量处理（整个文件夹） ─────────────────────────────────────────────

        /// <summary>
        /// 批量处理一个文件夹下的所有 PDF：一套界面参数逐张跑，
        /// 每张图纸各自落到 <c>&lt;输出根目录&gt;\&lt;图纸名&gt;</c>，单张失败不影响后面的图纸。
        /// 处理过程与结果都写入日志面板，结束时给出汇总。
        /// </summary>
        private async Task RunBatchAsync(string folderPath)
        {
            List<string> files = BatchRunner.FindPdfFiles(folderPath);
            if (files.Count == 0)
            {
                MessageBox.Show(this,
                    "该文件夹下没有 PDF（只扫当前文件夹，不含子文件夹）：" + Environment.NewLine + folderPath,
                    "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                lblStatus.Text      = "未找到 PDF";
                lblStatus.ForeColor = Color.DarkOrange;
                return;
            }

            // 批量模式下输出框是「输出根目录」，每张图纸再各自一个子文件夹
            string rootDir = NormalizePath(txtOutput.Text);
            if (string.IsNullOrEmpty(rootDir) || rootDir == _autoOutputText)
            {
                rootDir = DetectorOptions.GetDefaultBatchOutputDirectory(folderPath);
                SetOutputDirectory(rootDir);
            }

            DetectorOptions opts;
            try
            {
                // 参数校验沿用单文件那套（DPI / 遮蔽 / 面积 / 边距）；页码在批量里逐张校验
                opts = BuildOptions(files[0], rootDir);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                MessageBox.Show(this, ex.Message, "参数错误",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SaveUiOptions();
            SetBusy(true);

            // 进度条：批量改成「已完成 / 总数」，不再是跑马灯
            progressBar.Style   = ProgressBarStyle.Continuous;
            progressBar.Minimum = 0;
            progressBar.Maximum = files.Count;
            progressBar.Value   = 0;

            txtLog.Clear();

            try
            {
                LogBatchHeader(folderPath, rootDir, files.Count, opts);

                // 逐张处理放到线程池，避免界面假死；日志 / 进度回调内部已做线程切换
                List<BatchItemResult> results = await Task.Run(
                    () => BatchRunner.Run(folderPath, opts, false, (done, total, file) => SetBatchProgress(done)));

                int ok = 0;
                int failed = 0;
                int partTotal = 0;
                var failedNames = new List<string>();

                foreach (BatchItemResult item in results)
                {
                    if (item.Success)
                    {
                        ok++;
                        partTotal += item.PartCount;
                    }
                    else
                    {
                        failed++;
                        failedNames.Add(Path.GetFileName(item.InputFile));
                    }
                }

                AppendLog(string.Empty);
                AppendLog($"[批量完成] 共 {results.Count} 张：成功 {ok}，失败 {failed}，零件合计 {partTotal} 个。");

                if (failed == 0)
                {
                    lblStatus.Text      = $"完成：{ok} 张 / {partTotal} 个零件";
                    lblStatus.ForeColor = Color.ForestGreen;
                }
                else
                {
                    lblStatus.Text      = $"完成：成功 {ok}，失败 {failed}";
                    lblStatus.ForeColor = Color.DarkOrange;
                }

                string summary = $"共 {results.Count} 张：成功 {ok}，失败 {failed}，零件合计 {partTotal} 个。"
                               + Environment.NewLine + Environment.NewLine
                               + "输出根目录：" + Path.GetFullPath(rootDir);

                if (failed > 0)
                    summary += Environment.NewLine + Environment.NewLine
                             + "失败清单（详见日志）：" + Environment.NewLine
                             + string.Join(Environment.NewLine, failedNames.ToArray());

                MessageBox.Show(this, summary,
                    failed == 0 ? "批量处理完成" : "批量处理完成（有失败）",
                    MessageBoxButtons.OK,
                    failed == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                AppendLog(string.Empty);
                AppendLog($"[ERROR] {ex.GetType().Name}: {ex.Message}");

                lblStatus.Text      = "批量处理失败";
                lblStatus.ForeColor = Color.Firebrick;

                MessageBox.Show(this,
                    $"批量处理过程中发生错误：{Environment.NewLine}{ex.Message}",
                    "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                progressBar.Value = 0;
                progressBar.Style = ProgressBarStyle.Marquee;
                SetBusy(false);
            }
        }

        /// <summary>
        /// 批量开始时把「文件夹 / 张数 / 输出根目录 / 参数」写进日志。
        ///
        /// 注意：这里只做字符串拼接，<b>不要把 <c>&lt;图纸名&gt;</c> 之类的说明性占位符
        /// 交给 <c>Path.Combine</c> 之类的路径 API</b> ——
        /// <c>&lt;</c> <c>&gt;</c> 在 Windows 上是非法路径字符，会抛
        /// <c>ArgumentException：パスに無効な文字が含まれています。</c>。
        /// </summary>
        private void LogBatchHeader(string folderPath, string rootDir, int fileCount, DetectorOptions opts)
        {
            string rootForDisplay = (rootDir ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            AppendLog($"===== 开始批量处理：{folderPath} =====");
            AppendLog($"[INFO] 共 {fileCount} 张 PDF，输出根目录：{rootDir}");
            AppendLog($"[INFO] 每张图纸各自一个子文件夹：{rootForDisplay}\\<图纸名>");
            AppendLog($"[INFO] DPI={opts.RenderDpi:0}  页码={opts.PdfPageIndex}  遮蔽={opts.RightMaskRatio:0.00}"
                    + $"（自动={ (opts.AutoMaskRightColumn ? "开" : "关") }）"
                    + $"  最小面积={opts.MinContourArea:0}  边距={opts.Padding}"
                    + $"  核={opts.DilateKernelWidth}×{opts.DilateKernelHeight}");
        }

        /// <summary>批量进度：线程安全地更新进度条与状态文字。</summary>
        private void SetBatchProgress(int done)
        {
            if (progressBar.IsDisposed)
                return;

            if (progressBar.InvokeRequired)
            {
                progressBar.BeginInvoke(new Action<int>(SetBatchProgress), done);
                return;
            }

            if (done < progressBar.Minimum) done = progressBar.Minimum;
            if (done > progressBar.Maximum) done = progressBar.Maximum;

            progressBar.Value = done;
            lblStatus.Text    = $"处理中… {done}/{progressBar.Maximum}";
        }


        /// <summary>根据界面控件生成检测选项，越界时抛出 <see cref="ArgumentOutOfRangeException"/>。</summary>
        private DetectorOptions BuildOptions(string inputFile, string outputDir)
        {
            double maskRatio = (double)numMask.Value;
            if (maskRatio <= 0.0 || maskRatio >= 1.0)
                throw new ArgumentOutOfRangeException("numMask",
                    "「右侧遮蔽」必须位于 0.01 ~ 0.99 之间。");

            double area = (double)numArea.Value;
            if (area < 1000)
                throw new ArgumentOutOfRangeException("numArea",
                    "「最小面积」不能小于 1000 平方像素。");

            int pageIndex = (int)numPage.Value;
            if (_pdfPageCount > 0 && pageIndex >= _pdfPageCount)
                throw new ArgumentOutOfRangeException("numPage",
                    $"「页码」超出范围，该 PDF 共 {_pdfPageCount} 页（0 ~ {_pdfPageCount - 1}）。");

            return new DetectorOptions
            {
                RenderDpi          = (double)numDpi.Value,
                PdfPageIndex       = pageIndex,
                RightMaskRatio     = maskRatio,
                AutoMaskRightColumn = chkAutoMask.Checked,
                DropTextHeavyParts  = chkDropTextHeavy.Checked,
                MinContourArea     = area,
                Padding            = (int)numPad.Value,
                DilateKernelWidth  = Math.Max(1, (int)numKw.Value),
                DilateKernelHeight = Math.Max(1, (int)numKh.Value),
                SaveDebugImages    = chkDebug.Checked,
                OutputDirectory    = outputDir,
                Logger             = AppendLog
            };
        }

        // ── 打开输出目录 ──────────────────────────────────────────────────────

        private void btnOpenOutput_Click(object sender, EventArgs e)
        {
            string outputDir = txtOutput.Text.Trim();
            if (string.IsNullOrEmpty(outputDir) || !Directory.Exists(outputDir))
            {
                MessageBox.Show(this, "输出目录尚不存在，请先执行一次分割。", "提示",
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                Process.Start("explorer.exe", $"\"{Path.GetFullPath(outputDir)}\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"无法打开输出目录：{ex.Message}", "错误",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ── 界面状态辅助 ──────────────────────────────────────────────────────

        /// <summary>切换“处理中 / 空闲”界面状态。</summary>
        private void SetBusy(bool busy)
        {
            btnStart.Enabled        = !busy;
            btnBrowseInput.Enabled  = !busy;
            btnBrowseFolder.Enabled = !busy;
            btnBrowseOutput.Enabled = !busy;
            btnOpenOutput.Enabled   = !busy;
            grpOptions.Enabled      = !busy;
            progressBar.Visible     = busy;
            Cursor                  = busy ? Cursors.WaitCursor : Cursors.Default;

            if (busy)
            {
                lblStatus.Text      = "处理中…";
                lblStatus.ForeColor = SystemColors.ControlText;
            }
        }

        /// <summary>线程安全地向日志面板追加一行文本。</summary>
        private void AppendLog(string message)
        {
            if (txtLog.IsDisposed)
                return;

            if (txtLog.InvokeRequired)
            {
                txtLog.BeginInvoke(new Action<string>(AppendLog), message);
                return;
            }

            txtLog.AppendText(message + Environment.NewLine);
            txtLog.SelectionStart = txtLog.TextLength;
            txtLog.ScrollToCaret();
        }

        /// <summary>
        /// 选路径 / 拖拽时的兜底：把异常写进日志并提示，不让界面因为一个畸形路径直接崩掉
        /// （例如路径里混进 <c>" &lt; &gt; | * ?</c> 时 <see cref="Path"/> 会抛 <c>ArgumentException</c>）。
        /// </summary>
        private void ShowInputError(string action, Exception ex)
        {
            AppendLog($"[ERROR] {action}失败：{ex.GetType().Name}: {ex.Message}");

            MessageBox.Show(this, $"{action}失败：{Environment.NewLine}{ex.Message}", "错误",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        /// <summary>取路径所在目录；路径无效时返回 null。</summary>
        private static string GetDirectoryOrNull(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return null;

            try
            {
                string dir = Path.GetDirectoryName(filePath);
                return Directory.Exists(dir) ? dir : null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}

