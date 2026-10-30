using System;
using System.Collections.Generic;
using System.ComponentModel;
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

        /// <summary>【AI 分析】按钮的初始文字（运行中显示“分析中...”）。</summary>
        private string _aiButtonText;

        /// <summary>最近一次分解的坐标目录（&lt;PDF 文件夹&gt;\xy）。</summary>
        private string _lastXyDirectory;

        /// <summary>最近一次分解的 plu 目录（&lt;PDF 文件夹&gt;\plu，含 *_plu_table.txt）。</summary>
        private string _lastPluDirectory;

        /// <summary>PDF 预览窗口（非模态，选中分类列表行时复用同一个窗口）。</summary>
        private PdfPreviewForm _previewForm;

        /// <summary>部品基本信息窗口（非模态，选中分类列表行后复用同一个窗口）。</summary>
        private PartBasicInfoForm _partInfoForm;

        /// <summary>填充分类表格期间抑制「选中即预览」，避免批量刷新时弹出预览窗口。</summary>
        private bool _suppressAutoPreview;

        /// <summary>上一次自动预览过的 PDF 路径，用于避免同一行被重复预览（例如预览窗口关闭后焦点变化）。</summary>
        private string _lastAutoPreviewKey;

        public Form1()
        {
            InitializeComponent();
            InitializeClassifyGrid();
            _startButtonText = btnStart.Text;
            _aiButtonText = btnAiAnalyze.Text;
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
            btnAiAnalyze.Enabled = !running;
            btnAiSettings.Enabled = !running;
            btnBrowse.Enabled = !running;
            txtFolder.Enabled = !running;
            btnStart.Text = running ? "分解中..." : _startButtonText;
            btnAiAnalyze.Text = running ? "分析中..." : _aiButtonText;

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
        /// 分解进度回调（普通方法，供 PdfSplitService 以委托方式调用）：
        /// 先切换到 UI 线程，再刷新进度显示。
        /// </summary>
        private void OnSplitProgress(PdfSplitProgress progress)
        {
            RunOnUi(delegate
            {
                UpdateProgress(progress);
            });
        }

        /// <summary>
        /// 分解与 AI 分析共用的日志回调（普通方法，供服务层以委托方式调用）。
        /// </summary>
        private void OnServiceLog(LogKind kind, string message)
        {
            AppendLog(message, GetLogColor(kind));
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

            // 已存在全部分解结果时，先询问是否重新分解（开发调试时避免每次都重新解析 PDF）
            if (PdfSplitService.CountCompletedOutputs(folder, pdfFiles, null) == pdfFiles.Length)
            {
                DialogResult answer = MessageBox.Show(this,
                    "该文件夹中已存在全部分解结果（" + pdfFiles.Length + " 个 PDF 的 xy / table / plu txt 文件齐全）。\n\n" +
                    "是否重新分解？\n\n" +
                    "「是」= 重新分解并覆盖现有结果\n" +
                    "「否」= 直接使用现有结果（可接着点击【AI 分析】）",
                    "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);

                if (answer != DialogResult.Yes)
                {
                    UseExistingSplitResult(folder, pdfFiles);
                    return;
                }
            }

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

                // 用匿名方法（delegate）代替 Lambda 表达式：Task.Run 需要一个无参委托。
                // 进度与日志回调都改用本窗体的普通方法，便于单步跟踪。
                Func<PdfSplitResult> splitWork = delegate
                {
                    return service.Split(folder, OnSplitProgress, OnServiceLog);
                };

                result = await Task.Run(splitWork);
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
            _lastXyDirectory = xyDir;
            _lastPluDirectory = pluDir;

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
                AppendLog("表格输出：" + tableDir, Color.FromArgb(180, 180, 180));
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
                    "坐标文件（*_xy.txt / *_xy_imgtext.txt）：\n" + xyDir + "\n\n" +
                    "表格文件（*_table.txt）：\n" + tableDir + "\n\n" +
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
        /// 复用已有的分解结果（用户对「是否重新分解」选择「否」时调用）：
        /// 不重新分解，直接还原输出目录与分类列表，使用户可以接着执行【AI 分析】。
        /// </summary>
        /// <param name="folder">PDF 所在文件夹。</param>
        /// <param name="pdfFiles">该文件夹中的 PDF 文件名（含扩展名）。</param>
        private void UseExistingSplitResult(string folder, string[] pdfFiles)
        {
            string xyDir;
            string tableDir;
            string pluDir;
            string classifyDir;
            PdfSplitService.GetOutputDirectories(folder, out xyDir, out tableDir, out pluDir,
                out classifyDir);

            _lastOutputDir = xyDir;
            _lastXyDirectory = xyDir;
            _lastPluDirectory = pluDir;
            _classifyDirectory = classifyDir;

            lblPdfInfo.Text = "PDF：" + pdfFiles.Length + " 个";

            // 优先复用 classification.csv（保留用户手工改判）；缺失或与当前 PDF 集合不一致时按规则重新判定
            IList<PdfClassification> items = PdfClassifier.LoadSummary(folder, classifyDir);

            if (!MatchesPdfFiles(items, pdfFiles))
            {
                items = PdfClassifier.ClassifyFiles(folder, pdfFiles, pluDir);
            }

            PopulateClassification(items);

            int unknownCount = PdfClassifier.Count(items, PdfCategory.Unknown);

            if (unknownCount > 0)
            {
                tabMain.SelectedTab = tabPageClassify;
            }

            AppendLog("--------------------------------------------------", Color.FromArgb(90, 90, 90));
            AppendLog("已跳过重新分解：直接使用现有分解结果（" + pdfFiles.Length + " 个 PDF）",
                Color.FromArgb(150, 220, 150));
            AppendLog("坐标目录：" + xyDir, Color.FromArgb(180, 180, 180));
            AppendLog("表格目录：" + tableDir, Color.FromArgb(180, 180, 180));
            AppendLog("plu 目录：" + pluDir, Color.FromArgb(180, 180, 180));
            AppendLog("分类结果：" + Path.Combine(classifyDir, PdfClassifier.SummaryFileName),
                Color.FromArgb(180, 180, 180));
            AppendLog("分类统计：" + PdfClassifier.BuildSummaryText(items),
                unknownCount > 0 ? Color.FromArgb(255, 200, 120) : Color.FromArgb(150, 220, 150));

            if (unknownCount > 0)
            {
                AppendLog("有 " + unknownCount + " 个 PDF 无法自动判定分类，请在【分类结果】页选择。",
                    Color.FromArgb(255, 200, 120));
            }

            AppendLog("现有结果已就绪，可直接点击【AI 分析】。", Color.FromArgb(150, 220, 150));

            SetStatus("已使用现有分解结果（" + pdfFiles.Length + " 个 PDF）", Color.FromArgb(46, 125, 50));
            lblProgress.Text = "使用现有分解结果";
            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.Value = 0;
        }

        /// <summary>
        /// 判断已加载的分类列表是否与当前 PDF 集合一一对应（数量相同且文件名全部命中）。
        /// 用于决定是否可以安全复用 classification.csv。
        /// </summary>
        /// <param name="items">从 CSV 读出的分类列表（可为 null）。</param>
        /// <param name="pdfFiles">当前文件夹中的 PDF 文件名。</param>
        private static bool MatchesPdfFiles(IList<PdfClassification> items, string[] pdfFiles)
        {
            if (items == null || items.Count == 0 || pdfFiles == null || items.Count != pdfFiles.Length)
            {
                return false;
            }

            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string name in pdfFiles)
            {
                names.Add(name);
            }

            foreach (PdfClassification item in items)
            {
                if (item == null || !names.Remove(item.FileName))
                {
                    return false;
                }
            }

            return names.Count == 0;
        }

        /// <summary>
        /// 打开独立的 AI 接口设置画面（服务端点 / 模型下拉、API Key、超时）。
        /// 保存后写入用户配置文件并立即生效，无需重启程序。
        /// </summary>
        private void BtnAiSettings_Click(object sender, EventArgs e)
        {
            using (AiSettingsForm form = new AiSettingsForm())
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
            }

            AiApiOptions options = AiApiOptions.FromConfiguration();

            AppendLog("AI 设置已保存：" + options.Describe(), Color.FromArgb(180, 220, 255));

            if (!string.IsNullOrEmpty(AiApiOptions.LastSettingsError))
            {
                AppendLog("用户配置文件读取失败：" + AiApiOptions.LastSettingsError,
                    Color.FromArgb(255, 200, 120));
                SetStatus("AI 设置已保存（读取异常）", Color.FromArgb(239, 108, 0));
                return;
            }

            SetStatus("AI 设置已保存", Color.FromArgb(46, 125, 50));
        }

        /// <summary>
        /// AI 分析：遍历【分类结果】中「アッセンブリ図」的 PDF，
        /// 用 AI 解析 *_plu_table.txt 提取【部品基本信息列表】，
        /// 以 *_xy.txt 校验后登录到 SQLite 的部品基本信息表。
        /// </summary>
        private async void BtnAiAnalyze_Click(object sender, EventArgs e)
        {
            if (_isRunning)
            {
                return;
            }

            if (_classifications == null || _classifications.Count == 0)
            {
                MessageBox.Show(this, "请先点击【开始分解】，并在【分类结果】页确认分类后再执行 AI 分析。",
                    "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetStatus("尚未分解，无法 AI 分析", Color.FromArgb(239, 108, 0));
                return;
            }

            string pluDir = _lastPluDirectory;

            if (string.IsNullOrEmpty(pluDir) || !Directory.Exists(pluDir))
            {
                MessageBox.Show(this,
                    "找不到 plu 输出目录，请先执行【开始分解】。\n\n" + (pluDir ?? string.Empty),
                    "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetStatus("找不到 plu 输出目录", Color.FromArgb(239, 108, 0));
                return;
            }

            // 后台线程只读快照，避免分析期间界面修改分类造成并发问题
            List<PdfClassification> snapshot = new List<PdfClassification>();

            foreach (PdfClassification item in _classifications)
            {
                if (item != null && item.Category == PdfCategory.Assembly)
                {
                    snapshot.Add(new PdfClassification(item.FilePath, item.Category, item.Evidence,
                        item.IsAutomatic));
                }
            }

            if (snapshot.Count == 0)
            {
                MessageBox.Show(this,
                    "【分类结果】中没有「" + PdfCategoryNames.Assembly + "」的 PDF。\n\n" +
                    "请在【分类结果】页确认分类（未知项可在下拉框中选择）后再执行。",
                    "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetStatus("没有「" + PdfCategoryNames.Assembly + "」，无需分析", Color.FromArgb(239, 108, 0));
                return;
            }

            // 【开发调试】PartAnalysisService.DebugSingleFileOnly = true 时只分析指定 PDF
            if (PartAnalysisService.IsDebugSingleFileMode)
            {
                List<PdfClassification> debugTargets = PartAnalysisService.ApplyDebugFilter(snapshot);

                if (debugTargets.Count == 0)
                {
                    MessageBox.Show(this,
                        "【调试模式】只分析「" + PartAnalysisService.DebugSingleFileName + "」，" +
                        "但【分类结果】中没有这个文件。\n\n" +
                        "请确认该 PDF 已分类为「" + PdfCategoryNames.Assembly + "」，" +
                        "或把 PartAnalysisService.DebugSingleFileOnly 改为 false。",
                        "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    SetStatus("【调试模式】未找到 " + PartAnalysisService.DebugSingleFileName,
                        Color.FromArgb(239, 108, 0));
                    return;
                }

                snapshot = debugTargets;
            }

            AiApiOptions options = AiApiOptions.FromConfiguration();

            if (!options.IsConfigured)
            {
                string configError = AiApiOptions.LastConfigurationError;
                string settingsError = AiApiOptions.LastSettingsError;
                string errorNote = string.Empty;

                if (!string.IsNullOrEmpty(configError))
                {
                    errorNote += "\n\nApp.config 读取失败：\n" + configError;
                }

                if (!string.IsNullOrEmpty(settingsError))
                {
                    errorNote += "\n\n用户设置文件读取失败：\n" + settingsError;
                }

                MessageBox.Show(this,
                    "尚未配置 AI 接口密钥。\n\n" +
                    "请点击【AI 设置】按钮填写 ApiKey（保存后立即生效），" +
                    "或在 App.config 的 <appSettings> 中填写 AiApiKey，" +
                    "或设置环境变量 " + AiApiOptions.ApiKeyEnvironmentKey + "。" + errorNote,
                    "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                AppendLog("AI 分析已取消：未配置 AiApiKey（可在【AI 设置】中填写）/ " +
                    AiApiOptions.ApiKeyEnvironmentKey +
                    (string.IsNullOrEmpty(configError) ? string.Empty : "（App.config 读取失败：" + configError + "）") +
                    (string.IsNullOrEmpty(settingsError) ? string.Empty : "（用户设置读取失败：" + settingsError + "）"),
                    Color.FromArgb(255, 200, 120));
                SetStatus("未配置 AI 密钥", Color.FromArgb(239, 108, 0));
                return;
            }

            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.Value = 0;

            SetRunning(true);
            tabMain.SelectedTab = tabPageLog;

            AppendLog("==================================================", Color.FromArgb(90, 90, 90));
            AppendLog(string.Format("AI 分析开始：{0} 个「{1}」PDF", snapshot.Count,
                PdfCategoryNames.Assembly), Color.FromArgb(120, 200, 255));
            AppendLog("AI 配置：" + options.Describe(), Color.FromArgb(180, 180, 180));
            AppendLog("表格目录：" + pluDir, Color.FromArgb(180, 180, 180));
            AppendLog("坐标目录：" + (string.IsNullOrEmpty(_lastXyDirectory) ? "（无）" : _lastXyDirectory),
                Color.FromArgb(180, 180, 180));

            if (PartAnalysisService.IsDebugSingleFileMode)
            {
                AppendLog("【调试模式】只分析指定文件：" + PartAnalysisService.DebugSingleFileName,
                    Color.FromArgb(255, 200, 120));
            }

            SetStatus("正在 AI 分析（共 " + snapshot.Count + " 个 PDF）...", Color.FromArgb(230, 126, 34));
            lblProgress.Text = "AI 分析中...";

            PartAnalysisSummary summary = null;
            Exception failure = null;
            string xyDir = _lastXyDirectory;

            try
            {
                PartAnalysisService service = new PartAnalysisService(options,
                    PartBasicInfoRepository.ResolveDatabasePath());

                // 同上：用匿名方法（delegate）代替 Lambda 表达式，日志回调复用 OnServiceLog
                Func<PartAnalysisSummary> analyzeWork = delegate
                {
                    return service.AnalyzeAndSave(snapshot, xyDir, pluDir, OnServiceLog);
                };

                summary = await Task.Run(analyzeWork);
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            SetRunning(false);

            if (failure != null)
            {
                AppendLog("AI 分析失败：" + failure.Message, Color.FromArgb(255, 140, 140));
                SetStatus("AI 分析异常", Color.FromArgb(198, 40, 40));
                lblProgress.Text = "AI 分析异常";
                MessageBox.Show(this, "AI 分析失败：\n" + failure.Message, "异常",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (summary == null)
            {
                SetStatus("AI 分析未返回结果", Color.FromArgb(198, 40, 40));
                lblProgress.Text = "AI 分析未返回结果";
                return;
            }

            string summaryText = PartAnalysisService.BuildSummaryText(summary);

            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.Value = summary.SavedRowCount > 0 ? progressBar.Maximum : 0;
            lblProgress.Text = "AI 分析完成";

            AppendLog("--------------------------------------------------", Color.FromArgb(90, 90, 90));
            AppendLog(summaryText, summary.SavedRowCount > 0
                ? Color.FromArgb(150, 220, 150)
                : Color.FromArgb(255, 200, 120));
            AppendLog("部品基本信息数据库：" + summary.DatabasePath, Color.FromArgb(180, 180, 180));

            SetStatus(summaryText, summary.FailedFileCount > 0
                ? Color.FromArgb(255, 140, 140)
                : Color.FromArgb(46, 125, 50));

            MessageBox.Show(this,
                "AI 分析完成！\n\n" + summaryText + "\n\n" +
                "部品基本信息数据库：\n" + summary.DatabasePath + "\n\n" +
                "可在【分类结果】选中 PDF 行后点击【部品信息】查看登录的部品基本信息。",
                "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

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
        /// 点击「部品信息」按钮：在独立窗口中显示当前选中行对应 PDF 的部品基本信息。
        /// </summary>
        private void BtnPartInfo_Click(object sender, EventArgs e)
        {
            ShowPartInfo(GetSelectedClassification(), true);
        }

        /// <summary>
        /// 分类结果表格的快捷键：按 Enter 打开当前行的部品基本信息（编辑下拉框时不处理）。
        /// </summary>
        private void DgvClassify_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter || dgvClassify.IsCurrentCellInEditMode)
            {
                return;
            }

            e.Handled = true;
            e.SuppressKeyPress = true;
            ShowPartInfo(GetSelectedClassification(), true);
        }

        /// <summary>
        /// 右键菜单弹出前，把鼠标所在行设为当前行（否则菜单会作用在上次选中的行上）。
        /// </summary>
        private void CmsClassify_Opening(object sender, CancelEventArgs e)
        {
            Point location = dgvClassify.PointToClient(Cursor.Position);
            DataGridView.HitTestInfo hit = dgvClassify.HitTest(location.X, location.Y);

            if (hit.RowIndex >= 0 && hit.RowIndex < dgvClassify.Rows.Count)
            {
                dgvClassify.CurrentCell = dgvClassify.Rows[hit.RowIndex].Cells[0];
            }

            PdfClassification item = GetSelectedClassification();
            bool hasItem = item != null && !string.IsNullOrEmpty(item.FilePath);

            mnuShowPartInfo.Enabled = hasItem;
            mnuPreviewPdf.Enabled = hasItem;
        }

        /// <summary>右键菜单「查看部品基本信息」。</summary>
        private void MnuShowPartInfo_Click(object sender, EventArgs e)
        {
            ShowPartInfo(GetSelectedClassification(), true);
        }

        /// <summary>右键菜单「预览 PDF」。</summary>
        private void MnuPreviewPdf_Click(object sender, EventArgs e)
        {
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

            // 部品基本信息画面正在显示同一份 PDF 时，直接复用它的预览窗口（避免打开两个预览窗口）
            if (TryShowPreviewInPartInfoForm(item, bringToFront))
            {
                return;
            }

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

        /// <summary>
        /// 部品基本信息画面已打开且显示同一份 PDF 时，使用它自己的预览窗口显示 / 前置预览。
        /// （两个画面成对显示：预览在屏幕 1、部品基本信息在屏幕 2）
        /// </summary>
        /// <param name="item">要预览的分类结果。</param>
        /// <param name="bringToFront">是否把预览窗口提到最前。</param>
        /// <returns>已由部品基本信息画面的预览窗口处理时返回 true。</returns>
        private bool TryShowPreviewInPartInfoForm(PdfClassification item, bool bringToFront)
        {
            if (item == null || _partInfoForm == null || _partInfoForm.IsDisposed || !_partInfoForm.Visible)
            {
                return false;
            }

            if (!string.Equals(_partInfoForm.PdfPath, item.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            _partInfoForm.ShowPreview(bringToFront);

            return true;
        }

        /// <summary>
        /// 把主画面已打开的 PDF 预览窗口移交给部品基本信息画面，
        /// 避免同时存在两个预览窗口（部品基本信息关闭时由它一并关闭预览）。
        /// </summary>
        private void TransferPreviewFormToPartInfo()
        {
            if (_partInfoForm == null || _partInfoForm.IsDisposed)
            {
                return;
            }

            PdfPreviewForm preview = _previewForm;
            _previewForm = null;

            if (preview == null || preview.IsDisposed)
            {
                return;
            }

            preview.FormClosed -= PreviewForm_FormClosed;
            _partInfoForm.AttachPreviewForm(preview);
        }

        /// <summary>
        /// 在独立窗口（PartBasicInfoForm）中显示指定 PDF（図番）已登录到 SQLite 的部品基本信息；
        /// 窗口已存在时复用并切换到该 PDF。PDF 预览画面与本画面同时打开：
        /// 预览窗口在屏幕 1、部品基本信息在屏幕 2（只有 1 块屏幕时左右并排）。
        /// </summary>
        /// <param name="item">要查看的分类结果。</param>
        /// <param name="bringToFront">是否把窗口提到最前。</param>
        private void ShowPartInfo(PdfClassification item, bool bringToFront)
        {
            if (item == null || string.IsNullOrEmpty(item.FilePath))
            {
                SetStatus("请先在分类结果列表中选择一行", Color.FromArgb(198, 40, 40));
                return;
            }

            bool created = _partInfoForm == null || _partInfoForm.IsDisposed;

            if (created)
            {
                _partInfoForm = new PartBasicInfoForm();
                _partInfoForm.Owner = this;
                _partInfoForm.FormClosed += PartInfoForm_FormClosed;

                // 主画面已打开的预览窗口移交给部品基本信息画面（两个画面成对显示）
                TransferPreviewFormToPartInfo();
            }

            _partInfoForm.ShowDrawing(item.FilePath);

            if (created || !_partInfoForm.Visible)
            {
                _partInfoForm.Show();
            }

            if (bringToFront)
            {
                _partInfoForm.BringToFront();
                _partInfoForm.Activate();
            }

            SetStatus("部品基本信息：" + PartBasicInfoRepository.ResolveDrawingNo(item.FilePath),
                Color.FromArgb(46, 125, 50));
        }

        /// <summary>部品基本信息窗口关闭后清空引用，便于下次重新创建。</summary>
        private void PartInfoForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            PartBasicInfoForm form = sender as PartBasicInfoForm;

            if (form != null)
            {
                form.FormClosed -= PartInfoForm_FormClosed;
            }

            _partInfoForm = null;
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
            // Close() 会触发 FormClosed（事件里会清空字段），所以先取到局部变量
            PdfPreviewForm preview = _previewForm;
            PartBasicInfoForm partInfo = _partInfoForm;

            _previewForm = null;
            _partInfoForm = null;

            if (preview != null && !preview.IsDisposed)
            {
                preview.Close();
                preview.Dispose();
            }

            if (partInfo != null && !partInfo.IsDisposed)
            {
                partInfo.Close();
                partInfo.Dispose();
            }

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
        /// 打开分类结果目录（&lt;PDF 文件夹&gt;\AppData\classify）。
        /// </summary>
        private void BtnOpenClassifyDir_Click(object sender, EventArgs e)
        {
            string target = _classifyDirectory;

            if (string.IsNullOrEmpty(target) || !Directory.Exists(target))
            {
                string folder = txtFolder.Text.Trim();

                if (folder.Length > 0 && Directory.Exists(folder))
                {
                    target = Path.Combine(folder, PdfSplitService.OutputRootFolderName,
                        PdfClassifier.ClassifyFolderName);
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
                    // 优先打开分解输出的根目录（<PDF 文件夹>\AppData）
                    string outputRoot = Path.Combine(folder, PdfSplitService.OutputRootFolderName);
                    target = Directory.Exists(outputRoot) ? outputRoot : folder;
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
