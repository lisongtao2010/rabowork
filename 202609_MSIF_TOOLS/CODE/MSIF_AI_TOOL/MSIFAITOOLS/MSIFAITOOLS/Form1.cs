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
        /// <summary>最近一次分解的输出根目录（&lt;PDF 文件夹&gt;\AiAppData，其下每个 PDF 一个「図番号」子文件夹）。</summary>
        private string _lastOutputDir;

        private string _classifyDirectory;

        private IList<PdfClassification> _classifications;

        private bool _isRunning;

        private string _startButtonText;

        /// <summary>【AI 分析】按钮的初始文字（运行中显示“分析中...”）。</summary>
        private string _aiButtonText;

        /// <summary>【CMD 分析】按钮的初始文字（运行中显示“分析中...”）。</summary>
        private string _cmdButtonText;

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
            _cmdButtonText = btnCmdAnalyze.Text;
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
            btnCmdAnalyze.Enabled = !running;
            btnAiSettings.Enabled = !running;
            btnBrowse.Enabled = !running;
            txtFolder.Enabled = !running;
            chkCadSplit.Enabled = !running;
            btnStart.Text = running ? "分解中..." : _startButtonText;
            btnAiAnalyze.Text = running ? "分析中..." : _aiButtonText;
            btnCmdAnalyze.Text = running ? "分析中..." : _cmdButtonText;

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
        /// 第 1 步：调用外部程序 CadPartSplitter（x64）批量分割本文件夹内的 PDF，
        /// 结果写入 &lt;PDF 文件夹&gt;\AiAppData\&lt;図番号&gt;\（Part_NN.png / parts_info.json / parts_text.txt 等），
        /// 与本工具随后生成的 xy.txt / table.txt / plu_*.txt 位于同一子文件夹、互不覆盖。
        ///
        /// 未勾选、找不到程序或分割失败时只记录警告，不阻断第 2 步（本工具自身的分解）。
        /// 注意：本方法在后台线程执行（由 BtnStart_Click 的 Task.Run 调用），
        /// 界面刷新走 AppendLog / SetStatus / RunOnUi，它们内部已处理线程切换。
        /// </summary>
        /// <param name="folder">PDF 所在文件夹。</param>
        private void RunCadPartSplitterStep(string folder)
        {
            AppendLog("--------------------------------------------------", Color.FromArgb(90, 90, 90));

            if (chkCadSplit != null && !chkCadSplit.Checked)
            {
                AppendLog("已按设置跳过 CadPartSplitter 零件分割。", Color.FromArgb(255, 200, 120));
                return;
            }

            AppendLog("第 1 步：调用 CadPartSplitter 做零件分割", Color.FromArgb(120, 200, 255));
            SetStatus("正在执行 CadPartSplitter 零件分割...", Color.FromArgb(230, 126, 34));
            RunOnUi(delegate { lblProgress.Text = "CadPartSplitter 分割中..."; });

            CadPartSplitResult cadResult = CadPartSplitterRunner.RunBatch(folder, AppendCadSplitLog);

            if (!cadResult.ExeFound)
            {
                AppendLog("已跳过 CadPartSplitter 零件分割：" + cadResult.ErrorMessage,
                    Color.FromArgb(255, 200, 120));
                return;
            }

            AppendLog("调用程序：" + cadResult.ExePath, Color.FromArgb(180, 180, 180));
            AppendLog("CadPartSplitter 耗时 " + (cadResult.ElapsedMilliseconds / 1000.0).ToString("0.0") + " 秒",
                Color.FromArgb(180, 180, 180));

            if (cadResult.Success)
            {
                AppendLog(string.IsNullOrEmpty(cadResult.Summary)
                        ? "CadPartSplitter 零件分割完成。"
                        : "CadPartSplitter 零件分割完成：" + cadResult.Summary,
                    Color.FromArgb(150, 220, 150));
                return;
            }

            AppendLog("CadPartSplitter 未全部成功：" + cadResult.ErrorMessage, Color.FromArgb(255, 200, 120));

            if (!string.IsNullOrEmpty(cadResult.OutputTail))
            {
                AppendLog("CadPartSplitter 输出末尾：" + Environment.NewLine + cadResult.OutputTail,
                    Color.FromArgb(255, 200, 120));
            }

            AppendLog("继续执行第 2 步（本工具的 PDF 分解）。", Color.FromArgb(255, 200, 120));
        }

        /// <summary>
        /// CadPartSplitter 的逐行输出回调（来自后台线程），统一加上 [CAD] 前缀以便与本工具日志区分。
        /// </summary>
        private void AppendCadSplitLog(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return;
            }

            AppendLog("[CAD] " + line, Color.FromArgb(170, 190, 210));
        }

        /// <summary>
        /// 第 2 步开始前刷新界面：把进度显示从 CadPartSplitter 阶段切回本工具的分解。
        /// </summary>
        /// <param name="pdfCount">待处理的 PDF 数量。</param>
        private void ReportLocalSplitStart(int pdfCount)
        {
            AppendLog("第 2 步：执行 MSIF AI TOOLS 自身的 PDF 分解（" + pdfCount + " 个 PDF）",
                Color.FromArgb(120, 200, 255));
            SetStatus("正在分解（共 " + pdfCount + " 个 PDF）...", Color.FromArgb(230, 126, 34));
            RunOnUi(delegate { lblProgress.Text = "正在分解，请稍候..."; });
        }

        /// <summary>
        /// 开始分解：先调用 CadPartSplitter 做零件分割，再调用内置 .NET PDF 引擎（PdfPig）批量处理文件夹内所有 PDF。
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
                    // 第 1 步：先调用 CadPartSplitter 做零件分割（Part_NN.png / parts_info.json 等），
                    // 结果落在同一个 <PDF 文件夹>\AiAppData\<図番号> 下，与本工具的 txt 结果互不覆盖。
                    // 找不到程序或分割失败只记警告，不阻断第 2 步。
                    RunCadPartSplitterStep(folder);

                    // 第 2 步：本工具自身的分解（xy / table / plu 等文本结果）
                    ReportLocalSplitStart(pdfFiles.Length);

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

            string outputDir = result.OutputDirectory;
            _lastOutputDir = outputDir;

            // 分类结果：分解后由 PdfSplitService 读取 classify\PdfInfoList.txt，
            // 清单里没有的 PDF 记为未知，在【分类结果】页标黄提示（需在上游工具的分类清单里补充）
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
                AppendLog("输出根目录：" + outputDir, Color.FromArgb(180, 180, 180));
                AppendLog("每个 PDF 的子目录：" + outputDir + Path.DirectorySeparatorChar + "<図番号>",
                    Color.FromArgb(180, 180, 180));
                AppendLog("分类清单目录：" + _classifyDirectory, Color.FromArgb(180, 180, 180));
                AppendLog("分类统计：" + PdfClassifier.BuildSummaryText(result.Classifications),
                    unknownCount > 0 ? Color.FromArgb(255, 200, 120) : Color.FromArgb(150, 220, 150));

                SetStatus("分解完成（" + result.Total + " 个 PDF）", Color.FromArgb(46, 125, 50));
                lblProgress.Text = "分解完成";
                progressBar.Style = ProgressBarStyle.Continuous;
                progressBar.Value = progressBar.Maximum;

                MessageBox.Show(this,
                    "批量分解成功！\n\n共处理 " + result.Total + " 个 PDF\n\n" +
                    "输出根目录（每个 PDF 一个以図番号命名的子文件夹）：\n" + outputDir + "\n\n" +
                    "各 PDF 子文件夹中的文件：\n" +
                    "xy.txt / xy_imgtext.txt / table.txt /\n" +
                    "plu_text.txt / plu_layout.txt / plu_table.txt / plu_image.txt / plu_all.txt\n\n" +
                    "CadPartSplitter 生成的部品表（CMD 分析用）：\n" +
                    CmdPartAnalyzer.LtStep1FileName + "（アッセンブリ図）/ " +
                    CmdInstructionPartCollector.YoryoListFileName + "（要領図一覧）/ " +
                    CmdInstructionPartCollector.YoryoPartFileName + "（要領図）\n\n" +
                    "分类清单（PdfInfoList.txt）：\n" + _classifyDirectory + "\n\n" +
                    PdfClassifier.BuildSummaryText(result.Classifications) +
                    (unknownCount > 0
                        ? "\n\n有 " + unknownCount + " 个 PDF 在分类清单里没有记录，请在上游工具的分类清单里补充。"
                        : "\n\n分类已全部确定。"),
                    "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                AppendLog("成功 " + result.Succeeded + " 个，失败 " + result.Failed +
                    " 个，详见上方 ERROR 信息。", Color.FromArgb(255, 140, 140));

                if (unknownCount > 0)
                {
                    AppendLog("有 " + unknownCount + " 个 PDF 在分类清单里没有记录，请在上游工具的分类清单里补充。",
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
            string outputRoot = PdfSplitService.GetOutputRoot(folder);
            string classifyDir = PdfSplitService.GetClassifyDirectory(outputRoot);

            _lastOutputDir = outputRoot;
            _classifyDirectory = classifyDir;

            lblPdfInfo.Text = "PDF：" + pdfFiles.Length + " 个";

            // 分类读自 <输出根目录>\classify\PdfInfoList.txt（外部生成，本工具只读）：
            // 清单里没有的 PDF 记为未知，在画面上标黄提示
            IList<PdfClassification> items = PdfClassifier.LoadInfoList(folder, pdfFiles, outputRoot);

            PopulateClassification(items);

            int unknownCount = PdfClassifier.Count(items, PdfCategory.Unknown);

            if (unknownCount > 0)
            {
                tabMain.SelectedTab = tabPageClassify;
            }

            AppendLog("--------------------------------------------------", Color.FromArgb(90, 90, 90));
            AppendLog("已跳过重新分解：直接使用现有分解结果（" + pdfFiles.Length + " 个 PDF）",
                Color.FromArgb(150, 220, 150));
            AppendLog("输出根目录：" + outputRoot, Color.FromArgb(180, 180, 180));

            string infoListPath = PdfClassifier.GetInfoListPath(outputRoot);

            AppendLog("分类清单：" + infoListPath + (File.Exists(infoListPath) ? string.Empty : "（不存在）"),
                Color.FromArgb(180, 180, 180));
            AppendLog("分类统计：" + PdfClassifier.BuildSummaryText(items),
                unknownCount > 0 ? Color.FromArgb(255, 200, 120) : Color.FromArgb(150, 220, 150));

            if (unknownCount > 0)
            {
                AppendLog("有 " + unknownCount + " 个 PDF 在分类清单里没有记录，请在上游工具的分类清单里补充。",
                    Color.FromArgb(255, 200, 120));
            }

            AppendLog("现有结果已就绪，可直接点击【AI 分析】。", Color.FromArgb(150, 220, 150));

            SetStatus("已使用现有分解结果（" + pdfFiles.Length + " 个 PDF）", Color.FromArgb(46, 125, 50));
            lblProgress.Text = "使用现有分解结果";
            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.Value = 0;
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
        /// 用 AI 解析 plu_table.txt（&lt;输出根目录&gt;\&lt;図番号&gt;\）提取【部品基本信息列表】，
        /// 以 xy.txt 校验后登录到 SQLite 的部品基本信息表。
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

            string outputDir = _lastOutputDir;

            if (string.IsNullOrEmpty(outputDir) || !Directory.Exists(outputDir))
            {
                MessageBox.Show(this,
                    "找不到分解输出目录，请先执行【开始分解】。\n\n" + (outputDir ?? string.Empty),
                    "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetStatus("找不到分解输出目录", Color.FromArgb(239, 108, 0));
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
                    "请在【分类结果】页确认分类（分类由上游工具写入 PdfInfoList.txt）后再执行。",
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
            AppendLog("分解输出目录：" + outputDir, Color.FromArgb(180, 180, 180));

            if (PartAnalysisService.IsDebugSingleFileMode)
            {
                AppendLog("【调试模式】只分析指定文件：" + PartAnalysisService.DebugSingleFileName,
                    Color.FromArgb(255, 200, 120));
            }

            SetStatus("正在 AI 分析（共 " + snapshot.Count + " 个 PDF）...", Color.FromArgb(230, 126, 34));
            lblProgress.Text = "AI 分析中...";

            PartAnalysisSummary summary = null;
            Exception failure = null;

            try
            {
                PartAnalysisService service = new PartAnalysisService(options,
                    PartBasicInfoRepository.ResolveDatabasePath());

                // 同上：用匿名方法（delegate）代替 Lambda 表达式，日志回调复用 OnServiceLog
                Func<PartAnalysisSummary> analyzeWork = delegate
                {
                    return service.AnalyzeAndSave(snapshot, outputDir, OnServiceLog);
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
        /// 【CMD 分析】遍历【分类结果】中「アッセンブリ図」的 PDF，
        /// 用本工具自己的逻辑（<b>不调用 AI</b>）读取 CadPartSplitter 生成的
        /// 01_アッセンブリ図_部品機能情報.csv（&lt;输出根目录&gt;\&lt;図番号&gt;\…，旧名 LT_STEP1.csv），
        /// 以 xy.txt 校验后登录到与【AI 分析】相同的 SQLite 表。
        ///
        /// 追加步骤：再按 PdfInfoList.txt 的「分类依据」，顺着
        /// 01_アッセンブリ図_要領図.csv → 各要領図的 02_要領図_部品機能情報.csv，
        /// 把「品種命中分类依据」的部品行也登录到同一张表。
        /// 登录后可在【分类结果】选中 PDF 行 →【部品信息】查看明细。
        /// </summary>
        private async void BtnCmdAnalyze_Click(object sender, EventArgs e)
        {
            if (_isRunning)
            {
                return;
            }

            if (_classifications == null || _classifications.Count == 0)
            {
                MessageBox.Show(this, "请先点击【开始分解】，并在【分类结果】页确认分类后再执行 CMD 分析。",
                    "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetStatus("尚未分解，无法 CMD 分析", Color.FromArgb(239, 108, 0));
                return;
            }

            string outputDir = _lastOutputDir;

            if (string.IsNullOrEmpty(outputDir) || !Directory.Exists(outputDir))
            {
                MessageBox.Show(this,
                    "找不到分解输出目录，请先执行【开始分解】。\n\n" + (outputDir ?? string.Empty),
                    "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetStatus("找不到分解输出目录", Color.FromArgb(239, 108, 0));
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
                    "请在【分类结果】页确认分类（分类由上游工具写入 PdfInfoList.txt）后再执行。",
                    "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetStatus("没有「" + PdfCategoryNames.Assembly + "」，无需分析", Color.FromArgb(239, 108, 0));
                return;
            }

            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.Value = 0;

            SetRunning(true);
            tabMain.SelectedTab = tabPageLog;

            AppendLog("==================================================", Color.FromArgb(90, 90, 90));
            AppendLog(string.Format("CMD 分析开始：{0} 个「{1}」PDF（不调用 AI：{2} + 要領図の部品情報）",
                snapshot.Count, PdfCategoryNames.Assembly, CmdPartAnalyzer.LtStep1FileName),
                Color.FromArgb(120, 200, 255));
            AppendLog("分解输出目录：" + outputDir, Color.FromArgb(180, 180, 180));
            AppendLog("部品基本信息数据库：" + PartBasicInfoRepository.ResolveDatabasePath(),
                Color.FromArgb(180, 180, 180));

            SetStatus("正在 CMD 分析（共 " + snapshot.Count + " 个 PDF）...", Color.FromArgb(230, 126, 34));
            lblProgress.Text = "CMD 分析中...";

            PartAnalysisSummary summary = null;
            Exception failure = null;

            try
            {
                CmdPartAnalysisService service =
                    new CmdPartAnalysisService(PartBasicInfoRepository.ResolveDatabasePath());

                // 后台线程执行：用匿名方法（delegate）代替 Lambda 表达式
                Func<PartAnalysisSummary> analyzeWork = delegate
                {
                    return service.AnalyzeAndSave(snapshot, outputDir, OnServiceLog);
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
                AppendLog("CMD 分析失败：" + failure.Message, Color.FromArgb(255, 140, 140));
                SetStatus("CMD 分析异常", Color.FromArgb(198, 40, 40));
                lblProgress.Text = "CMD 分析异常";
                MessageBox.Show(this, "CMD 分析失败：\n" + failure.Message, "异常",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (summary == null)
            {
                SetStatus("CMD 分析未返回结果", Color.FromArgb(198, 40, 40));
                lblProgress.Text = "CMD 分析未返回结果";
                return;
            }

            string summaryText = CmdPartAnalysisService.BuildSummaryText(summary);

            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.Value = summary.SavedRowCount > 0 ? progressBar.Maximum : 0;
            lblProgress.Text = "CMD 分析完成";

            AppendLog("--------------------------------------------------", Color.FromArgb(90, 90, 90));
            AppendLog(summaryText, summary.SavedRowCount > 0
                ? Color.FromArgb(150, 220, 150)
                : Color.FromArgb(255, 200, 120));
            AppendLog("部品基本信息数据库：" + summary.DatabasePath, Color.FromArgb(180, 180, 180));

            SetStatus(summaryText, summary.FailedFileCount > 0
                ? Color.FromArgb(255, 140, 140)
                : Color.FromArgb(46, 125, 50));

            MessageBox.Show(this,
                "CMD 分析完成！\n\n" + summaryText + "\n\n" +
                "部品基本信息数据库：\n" + summary.DatabasePath + "\n\n" +
                "可在【分类结果】选中 PDF 行后点击【部品信息】查看登录的部品基本信息。",
                "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>
        /// 初始化分类结果表格的列（序号 / 文件名 / 分类 / 判定依据），全部只读显示。
        /// 分类由外部工具生成的 PdfInfoList.txt 决定，本画面只显示、不改判。
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

            DataGridViewTextBoxColumn colCategory = new DataGridViewTextBoxColumn();
            colCategory.Name = "colCategory";
            colCategory.HeaderText = "分类";
            colCategory.Width = 170;
            colCategory.ReadOnly = true;
            colCategory.SortMode = DataGridViewColumnSortMode.NotSortable;

            DataGridViewTextBoxColumn colEvidence = new DataGridViewTextBoxColumn();
            colEvidence.Name = "colEvidence";
            colEvidence.HeaderText = "判定依据";
            colEvidence.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            colEvidence.ReadOnly = true;
            colEvidence.SortMode = DataGridViewColumnSortMode.NotSortable;

            dgvClassify.Columns.AddRange(new DataGridViewColumn[] { colNo, colFile, colCategory, colEvidence });
        }

        /// <summary>
        /// 用分解结果刷新分类表格（必须调用：清空旧行 → 逐行填充 → 刷新统计）。
        /// </summary>
        private void PopulateClassification(IList<PdfClassification> items)
        {
            _classifications = items ?? new List<PdfClassification>();

            _lastAutoPreviewKey = null;
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
                _suppressAutoPreview = false;
            }

            RefreshClassifySummary();
        }
        /// <summary>
        /// 按分类状态给行着色：未知（分类清单里没有该图的记录）为淡黄色，其余为白底。
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
        /// 表格取值异常时静默处理，避免弹出系统错误对话框。
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
        /// </summary>
        private void DgvClassify_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
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
        /// 分类结果表格的快捷键：按 Enter 打开当前行的部品基本信息。
        /// </summary>
        private void DgvClassify_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
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
        /// 刷新分类统计标签（显示各类数量与未知数量）。
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
                (unknown > 0 ? "未知 " + unknown + " 个：分类清单（PdfInfoList.txt）里没有记录" : "全部已确定");

            lblClassifySummary.ForeColor = unknown > 0
                ? Color.FromArgb(198, 40, 40)
                : Color.FromArgb(46, 125, 50);
        }

        /// <summary>
        /// 打开分类清单目录（&lt;PDF 文件夹&gt;\AiAppData\classify），
        /// 便于查看 / 由外部工具维护其中的 PdfInfoList.txt。
        /// </summary>
        private void BtnOpenClassifyDir_Click(object sender, EventArgs e)
        {
            string target = _classifyDirectory;

            if (string.IsNullOrEmpty(target) || !Directory.Exists(target))
            {
                string folder = txtFolder.Text.Trim();

                if (folder.Length > 0 && Directory.Exists(folder))
                {
                    target = PdfSplitService.GetClassifyDirectory(PdfSplitService.GetOutputRoot(folder));
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
                    // 优先打开分解输出的根目录（<PDF 文件夹>\AiAppData）
                    string outputRoot = PdfSplitService.GetOutputRoot(folder);
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
