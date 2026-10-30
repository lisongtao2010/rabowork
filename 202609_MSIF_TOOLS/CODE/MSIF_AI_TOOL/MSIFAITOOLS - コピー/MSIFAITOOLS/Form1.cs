using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using MSIFAITOOLS.Services;
using UglyToad.PdfPig;

namespace MSIFAITOOLS
{
    public partial class Form1 : Form
    {
        private string _lastOutputDir;

        private string _classifyDirectory;

        private IList<PdfClassification> _classifications;

        private bool _suppressCategoryEvent;

        private bool _isRunning;

        private string _startButtonText;

        /// <summary>PDF 预览窗口（非模态，选中分类列表行时复用同一个窗口）。</summary>
        private PdfPreviewForm _previewForm;

        /// <summary>填充分类表格期间抑制「选中即预览」，避免批量刷新时弹出预览窗口。</summary>
        private bool _suppressAutoPreview;

        /// <summary>上一次自动预览过的 PDF 路径，用于避免同一行被重复预览（例如预览窗口关闭后焦点变化）。</summary>
        private string _lastAutoPreviewKey;

        public Form1()
        {
            InitializeComponent();
            InitializeClassifyGrid();
            _startButtonText = btnStart.Text;
        }

        /// <summary>
        /// 窗体首次显示：输出启动信息并显示内置 PDF 引擎版本（已不再依赖 Python）。
        /// </summary>
        private void Form1_Shown(object sender, EventArgs e)
        {
            AppendLog("MSIF AI TOOLS 已启动，请选择 PDF 文件夹后开始分解。", Color.FromArgb(120, 200, 255));
            ShowEngineInfo();
        }

        /// <summary>
        /// 在状态栏与日志中显示内置 PDF 解析引擎（PdfPig）的版本信息。
        /// </summary>
        private void ShowEngineInfo()
        {
            string version = GetEngineVersion();
            lblEngine.Text = "引擎：PdfPig " + version;
            lblEngine.ForeColor = Color.FromArgb(46, 125, 50);
            AppendLog("PDF 解析引擎：PdfPig " + version + "（纯 .NET，无需 Python 运行时）",
                Color.FromArgb(150, 220, 150));
        }

        /// <summary>
        /// 读取 PdfPig 程序集的版本号。
        /// </summary>
        private static string GetEngineVersion()
        {
            try
            {
                Version version = typeof(PdfDocument).Assembly.GetName().Version;
                if (version == null)
                {
                    return "未知";
                }

                return version.Major + "." + version.Minor + "." + version.Build;
            }
            catch (Exception)
            {
                return "未知";
            }
        }

        /// <summary>
        /// 浏览选择 PDF 所在文件夹。
        /// </summary>
        private void BtnBrowseFolder_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog fbd = new FolderBrowserDialog())
            {
                fbd.Description = "请选择存放 PDF 文件的文件夹";
                fbd.ShowNewFolderButton = false;

                string current = txtFolder.Text.Trim();
                if (current.Length > 0 && Directory.Exists(current))
                {
                    fbd.SelectedPath = current;
                }

                if (fbd.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                txtFolder.Text = fbd.SelectedPath;

                int count = CountPdfFiles(fbd.SelectedPath);
                lblPdfInfo.Text = "PDF：" + count + " 个";
                SetStatus("已选择文件夹（PDF " + count + " 个）", Color.FromArgb(21, 101, 192));
                AppendLog("选择目录：" + fbd.SelectedPath, Color.FromArgb(180, 180, 180));
                AppendLog("目录内 PDF 数量：" + count + " 个", Color.FromArgb(180, 180, 180));
            }
        }

        /// <summary>
        /// 统计指定目录下（不递归）的 PDF 文件数量。
        /// </summary>
        private static int CountPdfFiles(string folder)
        {
            try
            {
                return PdfSplitService.FindPdfFiles(folder).Length;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>
        /// 更新状态栏文本与颜色（可从任意线程调用）。
        /// </summary>
        private void SetStatus(string text, Color color)
        {
            RunOnUi(delegate
            {
                lblStatus.Text = text;
                lblStatus.ForeColor = color;
            });
        }

        /// <summary>
        /// 把界面更新操作切换到 UI 线程执行（后台线程调用时自动 BeginInvoke）。
        /// </summary>
        private void RunOnUi(Action action)
        {
            if (action == null || IsDisposed || Disposing)
            {
                return;
            }

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(action);
                }
                catch (InvalidOperationException)
                {
                    // 窗体正在关闭，忽略界面更新
                }

                return;
            }

            action();
        }

        /// <summary>
        /// 向运行日志追加一行带时间戳的信息（线程安全）。
        /// </summary>
        private void AppendLog(string message, Color color)
        {
            if (rtbLog == null || rtbLog.IsDisposed)
            {
                return;
            }

            if (rtbLog.InvokeRequired)
            {
                rtbLog.BeginInvoke(new Action<string, Color>(AppendLog), message, color);
                return;
            }

            rtbLog.SelectionStart = rtbLog.TextLength;
            rtbLog.SelectionLength = 0;
            rtbLog.SelectionColor = color;
            rtbLog.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);
            rtbLog.SelectionColor = rtbLog.ForeColor;
            rtbLog.ScrollToCaret();
        }

        /// <summary>
        /// 切换执行期间界面控件的可用状态。
        /// </summary>
        private void SetRunning(bool running)
        {
            _isRunning = running;

            btnStart.Enabled = !running;
            btnBrowse.Enabled = !running;
            txtFolder.Enabled = !running;
            btnStart.Text = running ? "分解中..." : _startButtonText;

            if (running)
            {
                progressBar.Style = ProgressBarStyle.Marquee;
                progressBar.MarqueeAnimationSpeed = 30;
            }
            else
            {
                progressBar.MarqueeAnimationSpeed = 0;
                progressBar.Style = ProgressBarStyle.Continuous;
            }
        }

        /// <summary>
        /// 刷新单个文件的处理进度显示（由后台线程通过 RunOnUi 调度到 UI 线程）。
        /// </summary>
        private void UpdateProgress(PdfSplitProgress progress)
        {
            if (progress == null)
            {
                return;
            }

            lblProgress.Text = string.Format("{0} {1}/{2}：{3}",
                progress.Stage, progress.Index, progress.Total, progress.FileName);

            SetStatus(string.Format("正在分解 {0}/{1}（{2}%）",
                progress.Index, progress.Total, progress.Percent), Color.FromArgb(230, 126, 34));
        }

        /// <summary>
        /// 把日志级别映射为控制台配色。
        /// </summary>
        private static Color GetLogColor(LogKind kind)
        {
            switch (kind)
            {
                case LogKind.Success:
                    return Color.FromArgb(150, 220, 150);

                case LogKind.Warning:
                    return Color.FromArgb(255, 200, 120);

                case LogKind.Error:
                    return Color.FromArgb(255, 140, 140);

                default:
                    return Color.FromArgb(212, 212, 212);
            }
        }

        /// <summary>
        /// 开始分解：调用内置 .NET PDF 引擎（PdfPig）批量处理文件夹内所有 PDF。
        /// </summary>
        private async void BtnStart_Click(object sender, EventArgs e)
        {
            if (_isRunning)
            {
                return;
            }

            string folder = txtFolder.Text.Trim();

            if (folder.Length == 0 || !Directory.Exists(folder))
            {
                MessageBox.Show(this, "请确认已选择有效的 PDF 文件夹！", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetStatus("文件夹无效", Color.FromArgb(239, 108, 0));
                return;
            }

            // 统计文件夹中的 PDF 数量（严格匹配 .pdf 扩展名）
            string[] pdfFiles;

            try
            {
                pdfFiles = PdfSplitService.FindPdfFiles(folder);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "读取文件夹失败：\n" + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (pdfFiles.Length == 0)
            {
                MessageBox.Show(this, "该文件夹中没有找到 PDF 文件！", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetStatus("未找到 PDF 文件", Color.FromArgb(239, 108, 0));
                return;
            }

            lblPdfInfo.Text = "PDF：" + pdfFiles.Length + " 个";

            // 先复位进度条（连续模式），再切换到运行态（跑马灯）
            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.Value = 0;
            rtbLog.Clear();

            SetRunning(true);
            AppendLog("==================================================", Color.FromArgb(90, 90, 90));
            AppendLog("PDF 文件夹：" + folder, Color.FromArgb(120, 200, 255));
            AppendLog("待处理 PDF：" + pdfFiles.Length + " 个", Color.FromArgb(120, 200, 255));
            SetStatus("正在分解（共 " + pdfFiles.Length + " 个 PDF）...", Color.FromArgb(230, 126, 34));
            lblProgress.Text = "正在分解，请稍候...";

            PdfSplitResult result = null;
            Exception failure = null;

            try
            {
                PdfSplitService service = new PdfSplitService();

                result = await Task.Run(() => service.Split(
                    folder,
                    progress => RunOnUi(() => UpdateProgress(progress)),
                    (kind, message) => AppendLog(message, GetLogColor(kind))));
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            SetRunning(false);

            if (failure != null)
            {
                AppendLog("分解失败：" + failure.Message, Color.FromArgb(255, 140, 140));
                SetStatus("分解异常", Color.FromArgb(198, 40, 40));
                lblProgress.Text = "执行异常";
                MessageBox.Show(this, "分解失败：\n" + failure.Message, "异常",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (result == null)
            {
                SetStatus("分解失败", Color.FromArgb(198, 40, 40));
                lblProgress.Text = "分解失败";
                return;
            }

            string xyDir = result.XyDirectory;
            string tableDir = result.TableDirectory;
            string pluDir = result.PluDirectory;
            _lastOutputDir = xyDir;

            // 分类结果：分解后由 PdfSplitService 按规则自动判定，未知项在【分类结果】页由用户选择
            _classifyDirectory = result.ClassifyDirectory;
            PopulateClassification(result.Classifications);

            int unknownCount = PdfClassifier.Count(result.Classifications, PdfCategory.Unknown);

            if (unknownCount > 0)
            {
                tabMain.SelectedTab = tabPageClassify;
            }

            AppendLog("--------------------------------------------------", Color.FromArgb(90, 90, 90));

            if (!result.HasErrors)
            {
                AppendLog("全部处理成功，共 " + result.Total + " 个 PDF。", Color.FromArgb(150, 220, 150));
                AppendLog("坐标输出：" + xyDir, Color.FromArgb(180, 180, 180));
                AppendLog("文本输出：" + tableDir, Color.FromArgb(180, 180, 180));
                AppendLog("plu 输出：" + pluDir, Color.FromArgb(180, 180, 180));
                AppendLog("分类输出：" + _classifyDirectory, Color.FromArgb(180, 180, 180));
                AppendLog("分类统计：" + PdfClassifier.BuildSummaryText(result.Classifications),
                    unknownCount > 0 ? Color.FromArgb(255, 200, 120) : Color.FromArgb(150, 220, 150));

                SetStatus("分解完成（" + result.Total + " 个 PDF）", Color.FromArgb(46, 125, 50));
                lblProgress.Text = "分解完成";
                progressBar.Style = ProgressBarStyle.Continuous;
                progressBar.Value = progressBar.Maximum;

                MessageBox.Show(this,
                    "批量分解成功！\n\n共处理 " + result.Total + " 个 PDF\n\n" +
                    "坐标文件（*_xy.txt）：\n" + xyDir + "\n\n" +
                    "文本文件（*_table.txt）：\n" + tableDir + "\n\n" +
                    "plu 文件（*_plu_text / _plu_layout / _plu_table / _plu_image / _plu_all）：\n" + pluDir + "\n\n" +
                    "分类结果（classification.csv）：\n" + _classifyDirectory + "\n\n" +
                    PdfClassifier.BuildSummaryText(result.Classifications) +
                    (unknownCount > 0
                        ? "\n\n有 " + unknownCount + " 个 PDF 无法自动判定，请在【分类结果】页选择后点击「保存分类结果」。"
                        : "\n\n分类已全部确定。"),
                    "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                AppendLog("成功 " + result.Succeeded + " 个，失败 " + result.Failed +
                    " 个，详见上方 ERROR 信息。", Color.FromArgb(255, 140, 140));

                if (unknownCount > 0)
                {
                    AppendLog("有 " + unknownCount + " 个 PDF 无法自动判定分类，请在【分类结果】页选择。",
                        Color.FromArgb(255, 200, 120));
                }

                SetStatus("分解完成（成功 " + result.Succeeded + "，失败 " + result.Failed + "）",
                    Color.FromArgb(198, 40, 40));
                lblProgress.Text = "分解完成（有失败）";
                progressBar.Style = ProgressBarStyle.Continuous;
                progressBar.Value = 0;

                MessageBox.Show(this,
                    "分解完成，但有 " + result.Failed + " 个文件处理失败。\n\n" +
                    "成功 " + result.Succeeded + " / 共 " + result.Total + " 个 PDF\n\n" +
                    "首个错误：\n" + result.Errors[0],
                    "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }


        /// <summary>
        /// 打开最近一次的输出目录（优先 xy 子目录）。
        /// </summary>
        /// <summary>
        /// 初始化分类结果表格的列（序号 / 文件名 / 分类下拉 / 判定依据）。
        /// </summary>
        private void InitializeClassifyGrid()
        {
            dgvClassify.AutoGenerateColumns = false;
            dgvClassify.Columns.Clear();

            DataGridViewTextBoxColumn colNo = new DataGridViewTextBoxColumn();
            colNo.Name = "colNo";
            colNo.HeaderText = "No";
            colNo.Width = 44;
            colNo.ReadOnly = true;
            colNo.SortMode = DataGridViewColumnSortMode.NotSortable;

            DataGridViewTextBoxColumn colFile = new DataGridViewTextBoxColumn();
            colFile.Name = "colFile";
            colFile.HeaderText = "文件名";
            colFile.Width = 220;
            colFile.ReadOnly = true;
            colFile.SortMode = DataGridViewColumnSortMode.NotSortable;

            DataGridViewComboBoxColumn colCategory = new DataGridViewComboBoxColumn();
            colCategory.Name = "colCategory";
            colCategory.HeaderText = "分类（未知请在此选择）";
            colCategory.Width = 170;
            colCategory.FlatStyle = FlatStyle.Flat;
            colCategory.DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox;
            colCategory.Items.AddRange(PdfCategoryNames.All);
            colCategory.SortMode = DataGridViewColumnSortMode.NotSortable;

            DataGridViewTextBoxColumn colEvidence = new DataGridViewTextBoxColumn();
            colEvidence.Name = "colEvidence";
            colEvidence.HeaderText = "判定依据";
            colEvidence.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            colEvidence.ReadOnly = true;
            colEvidence.SortMode = DataGridViewColumnSortMode.NotSortable;

            dgvClassify.Columns.AddRange(new DataGridViewColumn[] { colNo, colFile, colCategory, colEvidence });
            dgvClassify.EditMode = DataGridViewEditMode.EditOnEnter;
        }

        /// <summary>
        /// 用分解结果刷新分类表格（必须调用：清空旧行 → 逐行填充 → 刷新统计）。
        /// </summary>
        private void PopulateClassification(IList<PdfClassification> items)
        {
            _classifications = items ?? new List<PdfClassification>();

            _lastAutoPreviewKey = null;
            _suppressCategoryEvent = true;
            _suppressAutoPreview = true;

            try
            {
                dgvClassify.Rows.Clear();

                for (int index = 0; index < _classifications.Count; index++)
                {
                    PdfClassification item = _classifications[index];

                    int rowIndex = dgvClassify.Rows.Add(
                        (index + 1).ToString(),
                        item.FileName,
                        item.CategoryName,
                        item.Evidence);

                    dgvClassify.Rows[rowIndex].Tag = item;
                    ApplyRowStyle(rowIndex);
                }
            }
            finally
            {
                _suppressCategoryEvent = false;
                _suppressAutoPreview = false;
            }

            RefreshClassifySummary();
        }
        /// <summary>
        /// 按分类状态给行着色：未知（待用户选择）为淡黄色，其余为白底。
        /// </summary>
        private void ApplyRowStyle(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= dgvClassify.Rows.Count)
            {
                return;
            }

            DataGridViewRow row = dgvClassify.Rows[rowIndex];
            PdfClassification item = row.Tag as PdfClassification;

            if (item == null)
            {
                return;
            }

            if (item.NeedsUserChoice)
            {
                row.DefaultCellStyle.BackColor = Color.FromArgb(255, 243, 205);
                row.DefaultCellStyle.ForeColor = Color.FromArgb(153, 84, 0);
            }
            else
            {
                row.DefaultCellStyle.BackColor = Color.White;
                row.DefaultCellStyle.ForeColor = Color.FromArgb(33, 37, 41);
            }

            row.Cells["colEvidence"].Value = item.Evidence;
        }

        /// <summary>
        /// 用户在分类下拉框里改选分类：更新数据、重画该行并刷新统计。
        /// </summary>
        private void DgvClassify_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_suppressCategoryEvent || e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            DataGridViewColumn column = dgvClassify.Columns[e.ColumnIndex];

            if (column == null || column.Name != "colCategory")
            {
                return;
            }

            DataGridViewRow row = dgvClassify.Rows[e.RowIndex];
            PdfClassification item = row.Tag as PdfClassification;

            if (item == null)
            {
                return;
            }

            PdfCategory category = PdfCategoryNames.Parse(row.Cells[e.ColumnIndex].Value as string);

            if (category == item.Category)
            {
                return;
            }

            item.ApplyManualChoice(category);

            _suppressCategoryEvent = true;

            try
            {
                ApplyRowStyle(e.RowIndex);
            }
            finally
            {
                _suppressCategoryEvent = false;
            }

            RefreshClassifySummary();

            AppendLog(string.Format("分类调整：{0} → {1}", item.FileName, item.CategoryName),
                Color.FromArgb(255, 200, 120));
        }

        /// <summary>
        /// 下拉框取值异常时静默处理，避免弹出系统错误对话框。
        /// </summary>
        private void DgvClassify_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            e.ThrowException = false;
        }

        /// <summary>
        /// 分类列表当前行变化：勾选「选中即预览」时自动在预览窗口中显示该 PDF。
        /// 同一行的重复事件会被忽略（避免预览窗口关闭后又被焦点变化重新拉起）。
        /// </summary>
        private void DgvClassify_CurrentCellChanged(object sender, EventArgs e)
        {
            if (_suppressAutoPreview || !chkAutoPreview.Checked)
            {
                return;
            }

            PdfClassification item = GetSelectedClassification();

            if (item == null || string.IsNullOrEmpty(item.FilePath))
            {
                return;
            }

            if (string.Equals(_lastAutoPreviewKey, item.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ShowPdfPreview(item, false);
        }

        /// <summary>
        /// 双击分类列表中的某一行：显示该 PDF 并前置预览窗口。
        /// 双击「分类」列时不触发（该列用于直接改分类）。
        /// </summary>
        private void DgvClassify_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            DataGridViewColumn column = dgvClassify.Columns[e.ColumnIndex];

            if (column != null && column.Name == "colCategory")
            {
                return;
            }

            ShowPdfPreview(GetSelectedClassification(), true);
        }

        /// <summary>
        /// 点击「预览 PDF」按钮：显示当前选中行的 PDF。
        /// </summary>
        private void BtnPreviewPdf_Click(object sender, EventArgs e)
        {
            PdfClassification item = GetSelectedClassification();

            if (item == null)
            {
                SetStatus("请先在分类结果列表中选择一行", Color.FromArgb(198, 40, 40));
                return;
            }

            ShowPdfPreview(item, true);
        }

        /// <summary>
        /// 「选中即预览」开关变化时在日志中提示当前状态。
        /// </summary>
        private void ChkAutoPreview_CheckedChanged(object sender, EventArgs e)
        {
            _lastAutoPreviewKey = null;

            AppendLog(chkAutoPreview.Checked
                    ? "已开启自动预览：在【分类结果】中选中一行即显示该 PDF。"
                    : "已关闭自动预览：可双击行或点击「预览 PDF」按钮显示。",
                Color.FromArgb(150, 200, 255));
        }

        /// <summary>
        /// 取分类表格当前选中行对应的分类结果（没有选中时返回 null）。
        /// </summary>
        private PdfClassification GetSelectedClassification()
        {
            if (dgvClassify.CurrentRow == null)
            {
                return null;
            }

            return dgvClassify.CurrentRow.Tag as PdfClassification;
        }

        /// <summary>
        /// 在独立预览窗口（PdfPreviewForm）中显示指定 PDF；窗口已存在时复用并切换文件。
        /// </summary>
        /// <param name="item">要预览的分类结果。</param>
        /// <param name="bringToFront">是否把预览窗口提到最前。</param>
        private void ShowPdfPreview(PdfClassification item, bool bringToFront)
        {
            if (item == null || string.IsNullOrEmpty(item.FilePath))
            {
                return;
            }

            if (!File.Exists(item.FilePath))
            {
                SetStatus("PDF 不存在：" + item.FileName, Color.FromArgb(198, 40, 40));
                return;
            }

            _lastAutoPreviewKey = item.FilePath;

            if (_previewForm == null || _previewForm.IsDisposed)
            {
                _previewForm = new PdfPreviewForm();
                _previewForm.Owner = this;
                _previewForm.FormClosed += PreviewForm_FormClosed;
                _previewForm.Show();
            }

            _previewForm.ShowPdf(item.FilePath);

            if (bringToFront)
            {
                _previewForm.BringToFront();
                _previewForm.Activate();
            }
        }

        /// <summary>预览窗口关闭后清空引用，便于下次重新创建。</summary>
        private void PreviewForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            PdfPreviewForm form = sender as PdfPreviewForm;

            if (form != null)
            {
                form.FormClosed -= PreviewForm_FormClosed;
            }

            _previewForm = null;
        }

        /// <summary>主窗口关闭时一并关闭预览窗口，释放 PDF 文件句柄。</summary>
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_previewForm != null && !_previewForm.IsDisposed)
            {
                _previewForm.Close();
                _previewForm.Dispose();
            }

            _previewForm = null;

            base.OnFormClosed(e);
        }

        /// <summary>
        /// 刷新分类统计标签（显示各类数量与待选择数量）。
        /// </summary>
        private void RefreshClassifySummary()
        {
            if (_classifications == null || _classifications.Count == 0)
            {
                lblClassifySummary.Text = "尚未执行分类";
                lblClassifySummary.ForeColor = Color.FromArgb(52, 73, 94);
                return;
            }

            int unknown = PdfClassifier.Count(_classifications, PdfCategory.Unknown);

            lblClassifySummary.Text = PdfClassifier.BuildSummaryText(_classifications) + "\n" +
                (unknown > 0 ? "待选择 " + unknown + " 个：在「分类」列选择后点击「保存分类结果」" : "全部已确定");

            lblClassifySummary.ForeColor = unknown > 0
                ? Color.FromArgb(198, 40, 40)
                : Color.FromArgb(46, 125, 50);
        }

        /// <summary>
        /// 保存（或覆盖）分类汇总 CSV：&lt;PDF 文件夹&gt;\classify\classification.csv。
        /// </summary>
        private void BtnSaveClassify_Click(object sender, EventArgs e)
        {
            if (_classifications == null || _classifications.Count == 0)
            {
                MessageBox.Show(this, "还没有分类结果，请先执行 PDF 分解。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (string.IsNullOrEmpty(_classifyDirectory))
            {
                MessageBox.Show(this, "分类结果目录未知，请先执行 PDF 分解。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                string path = PdfClassifier.SaveSummary(_classifyDirectory, _classifications);

                AppendLog("分类结果已保存：" + path, Color.FromArgb(150, 220, 150));
                SetStatus("分类结果已保存（" + _classifications.Count + " 个 PDF）", Color.FromArgb(46, 125, 50));

                MessageBox.Show(this, "分类结果已保存：\n\n" + path, "完成",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存分类结果失败：\n" + ex.Message, "异常",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 打开分类结果目录（&lt;PDF 文件夹&gt;\classify）。
        /// </summary>
        private void BtnOpenClassifyDir_Click(object sender, EventArgs e)
        {
            string target = _classifyDirectory;

            if (string.IsNullOrEmpty(target) || !Directory.Exists(target))
            {
                string folder = txtFolder.Text.Trim();

                if (folder.Length > 0 && Directory.Exists(folder))
                {
                    target = Path.Combine(folder, PdfClassifier.ClassifyFolderName);
                }
            }

            if (string.IsNullOrEmpty(target) || !Directory.Exists(target))
            {
                MessageBox.Show(this, "分类目录还不存在，请先执行 PDF 分解。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                Process.Start("explorer.exe", "\"" + target + "\"");
                AppendLog("打开目录：" + target, Color.FromArgb(180, 180, 180));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "打开目录失败：\n" + ex.Message, "异常",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnOpenOutput_Click(object sender, EventArgs e)
        {
            string target = null;

            if (!string.IsNullOrEmpty(_lastOutputDir) && Directory.Exists(_lastOutputDir))
            {
                target = _lastOutputDir;
            }
            else
            {
                string folder = txtFolder.Text.Trim();
                if (folder.Length > 0 && Directory.Exists(folder))
                {
                    target = folder;
                }
            }

            if (target == null)
            {
                MessageBox.Show(this, "暂无可打开的输出目录，请先选择 PDF 文件夹并执行分解。",
                    "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                Process.Start("explorer.exe", "\"" + target + "\"");
                AppendLog("打开目录：" + target, Color.FromArgb(180, 180, 180));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "打开目录失败：\n" + ex.Message, "异常",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
