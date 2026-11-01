using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CadPartSplitter
{
    /// <summary>
    /// 零件分割工具主窗体：选择 PDF / 图片 → 设置识别参数 → 执行分割 → 查看日志与结果。
    /// </summary>
    public partial class MainForm : Form
    {
        /// <summary>上次由程序自动填写的输出目录，用于判断用户是否手工改过。</summary>
        private string _autoOutputText;

        /// <summary>当前输入文件为 PDF 时记录其总页数；非 PDF 为 0。</summary>
        private int _pdfPageCount;

        public MainForm()
        {
            InitializeComponent();
            WireEvents();
            ApplyDefaultOptions();
        }

        // ── 初始化 ────────────────────────────────────────────────────────────

        /// <summary>集中绑定控件事件，保持 Designer 文件仅负责布局。</summary>
        private void WireEvents()
        {
            txtInput.TextChanged     += txtInput_TextChanged;
            btnBrowseInput.Click     += btnBrowseInput_Click;
            btnBrowseOutput.Click    += btnBrowseOutput_Click;
            btnStart.Click           += btnStart_Click;
            btnOpenOutput.Click      += btnOpenOutput_Click;
            DragEnter                += MainForm_DragEnter;
            DragDrop                 += MainForm_DragDrop;
        }

        /// <summary>将 DetectorOptions 的默认值同步到界面控件。</summary>
        private void ApplyDefaultOptions()
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

            SetOutputDirectory(Path.Combine(Environment.CurrentDirectory, "output"));
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

                string currentDir = GetDirectoryOrNull(txtInput.Text);
                if (currentDir != null)
                    dialog.InitialDirectory = currentDir;

                if (dialog.ShowDialog(this) == DialogResult.OK)
                    SetInputFile(dialog.FileName);
            }
        }

        private void btnBrowseOutput_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择零件分割结果的输出目录";
                dialog.ShowNewFolderButton = true;

                string currentDir = txtOutput.Text.Trim();
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

            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null && files.Length > 0 && File.Exists(files[0]))
                SetInputFile(files[0]);
        }

        /// <summary>设置输入文件，并自动推导输出目录与 PDF 页数。</summary>
        private void SetInputFile(string filePath)
        {
            txtInput.Text = filePath;

            // 自动推导输出目录：<文件所在目录>\<文件名>_parts
            // 仅当用户未手工修改过输出目录时才自动覆盖
            string dir  = Path.GetDirectoryName(filePath) ?? Environment.CurrentDirectory;
            string name = Path.GetFileNameWithoutExtension(filePath);
            string current = txtOutput.Text.Trim();

            if (string.IsNullOrEmpty(current) || current == _autoOutputText)
                SetOutputDirectory(Path.Combine(dir, name + "_parts"));

            // PDF：读取总页数并限制页码范围
            _pdfPageCount = 0;
            if (string.Equals(Path.GetExtension(filePath), ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    _pdfPageCount = PdfRenderer.GetPageCount(filePath);
                }
                catch (Exception ex)
                {
                    AppendLog($"[WARN] 无法读取 PDF 页数：{ex.Message}");
                }
            }

            numPage.Maximum = _pdfPageCount > 0 ? _pdfPageCount - 1 : 9999;
            if (numPage.Value > numPage.Maximum)
                numPage.Value = numPage.Maximum;

            AppendLog(string.Empty);
            AppendLog($"已选择文件：{filePath}");
            if (_pdfPageCount > 0)
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
            btnStart.Enabled = !string.IsNullOrWhiteSpace(txtInput.Text);
        }

        // ── 执行分割 ──────────────────────────────────────────────────────────

        private async void btnStart_Click(object sender, EventArgs e)
        {
            string inputFile = txtInput.Text.Trim();

            if (string.IsNullOrEmpty(inputFile) || !File.Exists(inputFile))
            {
                MessageBox.Show(this, "请先选择有效的 PDF 或图片文件。", "提示",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string outputDir = txtOutput.Text.Trim();
            if (string.IsNullOrEmpty(outputDir))
            {
                MessageBox.Show(this, "请先指定输出目录。", "提示",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DetectorOptions opts;
            try
            {
                opts = BuildOptions(inputFile, outputDir);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                MessageBox.Show(this, ex.Message, "参数错误",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SetBusy(true);
            txtLog.Clear();
            AppendLog($"===== 开始处理：{Path.GetFileName(inputFile)} =====");
            AppendLog($"[INFO] DPI={opts.RenderDpi:0}  页码={opts.PdfPageIndex}  遮蔽={opts.RightMaskRatio:0.00}"
                    + $"（自动={ (opts.AutoMaskRightColumn ? "开" : "关") }）"
                    + $"  最小面积={opts.MinContourArea:0}  边距={opts.Padding}"
                    + $"  核={opts.DilateKernelWidth}×{opts.DilateKernelHeight}");

            try
            {
                // CPU 密集的处理放到线程池执行，避免界面假死
                var parts = await Task.Run(() => PartSplitterService.Run(inputFile, opts));
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

