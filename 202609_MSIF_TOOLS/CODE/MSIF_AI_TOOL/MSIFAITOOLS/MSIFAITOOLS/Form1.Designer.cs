namespace MSIFAITOOLS
{
    partial class Form1
    {
        /// <summary>
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码

        /// <summary>
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.pnlHeader = new System.Windows.Forms.Panel();
            this.lblVersion = new System.Windows.Forms.Label();
            this.lblAppSub = new System.Windows.Forms.Label();
            this.lblAppTitle = new System.Windows.Forms.Label();
            this.pnlHeaderAccent = new System.Windows.Forms.Panel();
            this.pnlContent = new System.Windows.Forms.Panel();
            this.grpLog = new System.Windows.Forms.GroupBox();
            this.tabMain = new System.Windows.Forms.TabControl();
            this.tabPageLog = new System.Windows.Forms.TabPage();
            this.rtbLog = new System.Windows.Forms.RichTextBox();
            this.tabPageClassify = new System.Windows.Forms.TabPage();
            this.dgvClassify = new System.Windows.Forms.DataGridView();
            this.pnlClassifyBar = new System.Windows.Forms.Panel();
            this.lblClassifySummary = new System.Windows.Forms.Label();
            this.btnOpenClassifyDir = new System.Windows.Forms.Button();
            this.btnPreviewPdf = new System.Windows.Forms.Button();
            this.btnPartInfo = new System.Windows.Forms.Button();
            this.cmsClassify = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.mnuShowPartInfo = new System.Windows.Forms.ToolStripMenuItem();
            this.mnuPreviewPdf = new System.Windows.Forms.ToolStripMenuItem();
            this.btnSaveClassify = new System.Windows.Forms.Button();
            this.chkAutoPreview = new System.Windows.Forms.CheckBox();
            this.grpAction = new System.Windows.Forms.GroupBox();
            this.progressBar = new System.Windows.Forms.ProgressBar();
            this.lblProgress = new System.Windows.Forms.Label();
            this.btnOpenOutput = new System.Windows.Forms.Button();
            this.btnStart = new System.Windows.Forms.Button();
            this.btnAiAnalyze = new System.Windows.Forms.Button();
            this.btnAiSettings = new System.Windows.Forms.Button();
            this.grpInput = new System.Windows.Forms.GroupBox();
            this.btnBrowse = new System.Windows.Forms.Button();
            this.txtFolder = new System.Windows.Forms.TextBox();
            this.lblFolder = new System.Windows.Forms.Label();
            this.statusStrip = new System.Windows.Forms.StatusStrip();
            this.lblStatus = new System.Windows.Forms.ToolStripStatusLabel();
            this.lblSpring = new System.Windows.Forms.ToolStripStatusLabel();
            this.lblPdfInfo = new System.Windows.Forms.ToolStripStatusLabel();
            this.lblEngine = new System.Windows.Forms.ToolStripStatusLabel();
            this.pnlHeader.SuspendLayout();
            this.pnlContent.SuspendLayout();
            this.grpLog.SuspendLayout();
            this.tabMain.SuspendLayout();
            this.tabPageLog.SuspendLayout();
            this.tabPageClassify.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dgvClassify)).BeginInit();
            this.pnlClassifyBar.SuspendLayout();
            this.grpAction.SuspendLayout();
            this.grpInput.SuspendLayout();
            this.statusStrip.SuspendLayout();
            this.SuspendLayout();
            // 
            // pnlHeader
            // 
            this.pnlHeader.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(27)))), ((int)(((byte)(58)))), ((int)(((byte)(95)))));
            this.pnlHeader.Controls.Add(this.lblVersion);
            this.pnlHeader.Controls.Add(this.lblAppSub);
            this.pnlHeader.Controls.Add(this.lblAppTitle);
            this.pnlHeader.Controls.Add(this.pnlHeaderAccent);
            this.pnlHeader.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnlHeader.Location = new System.Drawing.Point(0, 0);
            this.pnlHeader.Name = "pnlHeader";
            this.pnlHeader.Size = new System.Drawing.Size(900, 76);
            this.pnlHeader.TabIndex = 0;
            // 
            // lblAppTitle
            // 
            this.lblAppTitle.AutoSize = true;
            this.lblAppTitle.BackColor = System.Drawing.Color.Transparent;
            this.lblAppTitle.Font = new System.Drawing.Font("微软雅黑", 18F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblAppTitle.ForeColor = System.Drawing.Color.White;
            this.lblAppTitle.Location = new System.Drawing.Point(22, 12);
            this.lblAppTitle.Name = "lblAppTitle";
            this.lblAppTitle.Size = new System.Drawing.Size(191, 31);
            this.lblAppTitle.TabIndex = 0;
            this.lblAppTitle.Text = "MSIF AI TOOLS";
            // 
            // lblAppSub
            // 
            this.lblAppSub.AutoSize = true;
            this.lblAppSub.BackColor = System.Drawing.Color.Transparent;
            this.lblAppSub.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblAppSub.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(169)))), ((int)(((byte)(196)))), ((int)(((byte)(224)))));
            this.lblAppSub.Location = new System.Drawing.Point(24, 46);
            this.lblAppSub.Name = "lblAppSub";
            this.lblAppSub.Size = new System.Drawing.Size(263, 17);
            this.lblAppSub.TabIndex = 1;
            this.lblAppSub.Text = "PDF 分解工具 · 坐标 / 文本 / plu（pdfplumber 风格）";
            // 
            // lblVersion
            // 
            this.lblVersion.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.lblVersion.AutoSize = true;
            this.lblVersion.BackColor = System.Drawing.Color.Transparent;
            this.lblVersion.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblVersion.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(140)))), ((int)(((byte)(170)))), ((int)(((byte)(200)))));
            this.lblVersion.Location = new System.Drawing.Point(824, 20);
            this.lblVersion.Name = "lblVersion";
            this.lblVersion.Size = new System.Drawing.Size(44, 17);
            this.lblVersion.TabIndex = 2;
            this.lblVersion.Text = "v1.0.0";
            // 
            // pnlHeaderAccent
            // 
            this.pnlHeaderAccent.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(230)))), ((int)(((byte)(126)))), ((int)(((byte)(34)))));
            this.pnlHeaderAccent.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.pnlHeaderAccent.Location = new System.Drawing.Point(0, 73);
            this.pnlHeaderAccent.Name = "pnlHeaderAccent";
            this.pnlHeaderAccent.Size = new System.Drawing.Size(900, 3);
            this.pnlHeaderAccent.TabIndex = 3;
            // 
            // pnlContent
            // 
            this.pnlContent.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(242)))), ((int)(((byte)(244)))), ((int)(((byte)(247)))));
            this.pnlContent.Controls.Add(this.grpLog);
            this.pnlContent.Controls.Add(this.grpAction);
            this.pnlContent.Controls.Add(this.grpInput);
            this.pnlContent.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlContent.Location = new System.Drawing.Point(0, 76);
            this.pnlContent.Name = "pnlContent";
            this.pnlContent.Size = new System.Drawing.Size(900, 602);
            this.pnlContent.TabIndex = 1;
            // 
            // grpInput
            // 
            this.grpInput.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grpInput.BackColor = System.Drawing.Color.White;
            this.grpInput.Controls.Add(this.btnBrowse);
            this.grpInput.Controls.Add(this.txtFolder);
            this.grpInput.Controls.Add(this.lblFolder);
            this.grpInput.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.grpInput.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.grpInput.Location = new System.Drawing.Point(20, 16);
            this.grpInput.Name = "grpInput";
            this.grpInput.Size = new System.Drawing.Size(860, 96);
            this.grpInput.TabIndex = 0;
            this.grpInput.TabStop = false;
            this.grpInput.Text = "①  输入设置";
            // 
            // lblFolder
            // 
            this.lblFolder.AutoSize = true;
            this.lblFolder.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblFolder.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.lblFolder.Location = new System.Drawing.Point(18, 44);
            this.lblFolder.Name = "lblFolder";
            this.lblFolder.Size = new System.Drawing.Size(80, 17);
            this.lblFolder.TabIndex = 0;
            this.lblFolder.Text = "PDF 文件夹：";
            // 
            // txtFolder
            // 
            this.txtFolder.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtFolder.BackColor = System.Drawing.Color.White;
            this.txtFolder.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.txtFolder.Font = new System.Drawing.Font("微软雅黑", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.txtFolder.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(33)))), ((int)(((byte)(37)))), ((int)(((byte)(41)))));
            this.txtFolder.Location = new System.Drawing.Point(112, 38);
            this.txtFolder.Name = "txtFolder";
            this.txtFolder.Size = new System.Drawing.Size(608, 25);
            this.txtFolder.TabIndex = 1;
            // 
            // btnBrowse
            // 
            this.btnBrowse.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnBrowse.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(232)))), ((int)(((byte)(237)))), ((int)(((byte)(243)))));
            this.btnBrowse.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnBrowse.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(199)))), ((int)(((byte)(208)))), ((int)(((byte)(219)))));
            this.btnBrowse.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(217)))), ((int)(((byte)(226)))), ((int)(((byte)(236)))));
            this.btnBrowse.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnBrowse.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnBrowse.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.btnBrowse.Location = new System.Drawing.Point(732, 36);
            this.btnBrowse.Name = "btnBrowse";
            this.btnBrowse.Size = new System.Drawing.Size(110, 32);
            this.btnBrowse.TabIndex = 2;
            this.btnBrowse.Text = "浏览...";
            this.btnBrowse.UseVisualStyleBackColor = false;
            this.btnBrowse.Click += new System.EventHandler(this.BtnBrowseFolder_Click);
            // 
            // grpAction
            // 
            this.grpAction.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grpAction.BackColor = System.Drawing.Color.White;
            this.grpAction.Controls.Add(this.progressBar);
            this.grpAction.Controls.Add(this.lblProgress);
            this.grpAction.Controls.Add(this.btnOpenOutput);
            this.grpAction.Controls.Add(this.btnStart);
            this.grpAction.Controls.Add(this.btnAiAnalyze);
            this.grpAction.Controls.Add(this.btnAiSettings);
            this.grpAction.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.grpAction.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.grpAction.Location = new System.Drawing.Point(20, 124);
            this.grpAction.Name = "grpAction";
            this.grpAction.Size = new System.Drawing.Size(860, 104);
            this.grpAction.TabIndex = 1;
            this.grpAction.TabStop = false;
            this.grpAction.Text = "②  执行";
            // 
            // btnStart
            // 
            this.btnStart.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(21)))), ((int)(((byte)(101)))), ((int)(((byte)(192)))));
            this.btnStart.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnStart.FlatAppearance.BorderSize = 0;
            this.btnStart.FlatAppearance.MouseDownBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(13)))), ((int)(((byte)(71)))), ((int)(((byte)(161)))));
            this.btnStart.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(136)))), ((int)(((byte)(229)))));
            this.btnStart.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnStart.Font = new System.Drawing.Font("微软雅黑", 11F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnStart.ForeColor = System.Drawing.Color.White;
            this.btnStart.Location = new System.Drawing.Point(18, 34);
            this.btnStart.Name = "btnStart";
            this.btnStart.Size = new System.Drawing.Size(210, 46);
            this.btnStart.TabIndex = 0;
            this.btnStart.Text = "开始分解";
            this.btnStart.UseVisualStyleBackColor = false;
            this.btnStart.Click += new System.EventHandler(this.BtnStart_Click);
            // 
            // btnAiAnalyze
            // 
            this.btnAiAnalyze.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(46)))), ((int)(((byte)(125)))), ((int)(((byte)(50)))));
            this.btnAiAnalyze.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnAiAnalyze.FlatAppearance.BorderSize = 0;
            this.btnAiAnalyze.FlatAppearance.MouseDownBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(27)))), ((int)(((byte)(94)))), ((int)(((byte)(32)))));
            this.btnAiAnalyze.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(76)))), ((int)(((byte)(175)))), ((int)(((byte)(80)))));
            this.btnAiAnalyze.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnAiAnalyze.Font = new System.Drawing.Font("微软雅黑", 11F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnAiAnalyze.ForeColor = System.Drawing.Color.White;
            this.btnAiAnalyze.Location = new System.Drawing.Point(240, 34);
            this.btnAiAnalyze.Name = "btnAiAnalyze";
            this.btnAiAnalyze.Size = new System.Drawing.Size(150, 46);
            this.btnAiAnalyze.TabIndex = 1;
            this.btnAiAnalyze.Text = "AI 分析";
            this.btnAiAnalyze.UseVisualStyleBackColor = false;
            this.btnAiAnalyze.Click += new System.EventHandler(this.BtnAiAnalyze_Click);
            // 
            // btnOpenOutput
            // 
            this.btnOpenOutput.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(240)))), ((int)(((byte)(242)))), ((int)(((byte)(245)))));
            this.btnOpenOutput.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnOpenOutput.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(199)))), ((int)(((byte)(208)))), ((int)(((byte)(219)))));
            this.btnOpenOutput.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(226)))), ((int)(((byte)(232)))), ((int)(((byte)(240)))));
            this.btnOpenOutput.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnOpenOutput.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnOpenOutput.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.btnOpenOutput.Location = new System.Drawing.Point(400, 34);
            this.btnOpenOutput.Name = "btnOpenOutput";
            this.btnOpenOutput.Size = new System.Drawing.Size(140, 46);
            this.btnOpenOutput.TabIndex = 2;
            this.btnOpenOutput.Text = "打开输出目录";
            this.btnOpenOutput.UseVisualStyleBackColor = false;
            this.btnOpenOutput.Click += new System.EventHandler(this.BtnOpenOutput_Click);
            // 
            // btnAiSettings
            // 
            this.btnAiSettings.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(240)))), ((int)(((byte)(242)))), ((int)(((byte)(245)))));
            this.btnAiSettings.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnAiSettings.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(199)))), ((int)(((byte)(208)))), ((int)(((byte)(219)))));
            this.btnAiSettings.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(226)))), ((int)(((byte)(232)))), ((int)(((byte)(240)))));
            this.btnAiSettings.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnAiSettings.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnAiSettings.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.btnAiSettings.Location = new System.Drawing.Point(550, 34);
            this.btnAiSettings.Name = "btnAiSettings";
            this.btnAiSettings.Size = new System.Drawing.Size(110, 46);
            this.btnAiSettings.TabIndex = 3;
            this.btnAiSettings.Text = "AI 设置";
            this.btnAiSettings.UseVisualStyleBackColor = false;
            this.btnAiSettings.Click += new System.EventHandler(this.BtnAiSettings_Click);
            // 
            // lblProgress
            // 
            this.lblProgress.AutoSize = true;
            this.lblProgress.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblProgress.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(108)))), ((int)(((byte)(117)))), ((int)(((byte)(125)))));
            this.lblProgress.Location = new System.Drawing.Point(668, 34);
            this.lblProgress.Name = "lblProgress";
            this.lblProgress.Size = new System.Drawing.Size(56, 17);
            this.lblProgress.TabIndex = 3;
            this.lblProgress.Text = "等待开始";
            // 
            // progressBar
            // 
            this.progressBar.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.progressBar.Location = new System.Drawing.Point(668, 56);
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new System.Drawing.Size(184, 18);
            this.progressBar.TabIndex = 4;
            // 
            // grpLog
            // 
            this.grpLog.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grpLog.BackColor = System.Drawing.Color.White;
            this.grpLog.Controls.Add(this.tabMain);
            this.grpLog.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.grpLog.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.grpLog.Location = new System.Drawing.Point(20, 240);
            this.grpLog.Name = "grpLog";
            this.grpLog.Size = new System.Drawing.Size(860, 340);
            this.grpLog.TabIndex = 2;
            this.grpLog.TabStop = false;
            this.grpLog.Text = "③  运行日志 / 分类结果";
            //
            // tabMain
            //
            this.tabMain.Controls.Add(this.tabPageLog);
            this.tabMain.Controls.Add(this.tabPageClassify);
            this.tabMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabMain.Location = new System.Drawing.Point(3, 19);
            this.tabMain.Name = "tabMain";
            this.tabMain.SelectedIndex = 0;
            this.tabMain.Size = new System.Drawing.Size(854, 318);
            this.tabMain.TabIndex = 0;
            //
            // tabPageLog
            //
            this.tabPageLog.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(30)))), ((int)(((byte)(30)))));
            this.tabPageLog.Controls.Add(this.rtbLog);
            this.tabPageLog.Location = new System.Drawing.Point(4, 24);
            this.tabPageLog.Name = "tabPageLog";
            this.tabPageLog.Padding = new System.Windows.Forms.Padding(3);
            this.tabPageLog.Size = new System.Drawing.Size(846, 290);
            this.tabPageLog.TabIndex = 0;
            this.tabPageLog.Text = "运行日志";
            //
            // tabPageClassify
            //
            this.tabPageClassify.BackColor = System.Drawing.Color.White;
            this.tabPageClassify.Controls.Add(this.dgvClassify);
            this.tabPageClassify.Controls.Add(this.pnlClassifyBar);
            this.tabPageClassify.Location = new System.Drawing.Point(4, 24);
            this.tabPageClassify.Name = "tabPageClassify";
            this.tabPageClassify.Padding = new System.Windows.Forms.Padding(3);
            this.tabPageClassify.Size = new System.Drawing.Size(846, 290);
            this.tabPageClassify.TabIndex = 1;
            this.tabPageClassify.Text = "分类结果";
            //
            // pnlClassifyBar
            //
            this.pnlClassifyBar.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(242)))), ((int)(((byte)(244)))), ((int)(((byte)(247)))));
            this.pnlClassifyBar.Controls.Add(this.lblClassifySummary);
            this.pnlClassifyBar.Controls.Add(this.chkAutoPreview);
            this.pnlClassifyBar.Controls.Add(this.btnPartInfo);
            this.pnlClassifyBar.Controls.Add(this.btnOpenClassifyDir);
            this.pnlClassifyBar.Controls.Add(this.btnPreviewPdf);
            this.pnlClassifyBar.Controls.Add(this.btnSaveClassify);
            this.pnlClassifyBar.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.pnlClassifyBar.Location = new System.Drawing.Point(3, 243);
            this.pnlClassifyBar.Name = "pnlClassifyBar";
            this.pnlClassifyBar.Size = new System.Drawing.Size(840, 44);
            this.pnlClassifyBar.TabIndex = 1;
            //
            // lblClassifySummary
            //
            this.lblClassifySummary.AutoSize = false;
            this.lblClassifySummary.AutoEllipsis = true;
            this.lblClassifySummary.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblClassifySummary.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.lblClassifySummary.Location = new System.Drawing.Point(6, 5);
            this.lblClassifySummary.Name = "lblClassifySummary";
            this.lblClassifySummary.Size = new System.Drawing.Size(294, 34);
            this.lblClassifySummary.TabIndex = 0;
            this.lblClassifySummary.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.lblClassifySummary.Text = "尚未执行分类";
            //
            // btnPartInfo
            //
            this.btnPartInfo.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnPartInfo.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(219)))), ((int)(((byte)(233)))), ((int)(((byte)(248)))));
            this.btnPartInfo.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnPartInfo.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(152)))), ((int)(((byte)(184)))), ((int)(((byte)(214)))));
            this.btnPartInfo.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(203)))), ((int)(((byte)(222)))), ((int)(((byte)(242)))));
            this.btnPartInfo.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnPartInfo.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnPartInfo.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(31)))), ((int)(((byte)(78)))), ((int)(((byte)(121)))));
            this.btnPartInfo.Location = new System.Drawing.Point(408, 8);
            this.btnPartInfo.Name = "btnPartInfo";
            this.btnPartInfo.Size = new System.Drawing.Size(90, 28);
            this.btnPartInfo.TabIndex = 1;
            this.btnPartInfo.Text = "部品信息";
            this.btnPartInfo.UseVisualStyleBackColor = false;
            this.btnPartInfo.Click += new System.EventHandler(this.BtnPartInfo_Click);
            //
            // btnPreviewPdf
            //
            this.btnPreviewPdf.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnPreviewPdf.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(232)))), ((int)(((byte)(237)))), ((int)(((byte)(243)))));
            this.btnPreviewPdf.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnPreviewPdf.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(199)))), ((int)(((byte)(208)))), ((int)(((byte)(219)))));
            this.btnPreviewPdf.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(217)))), ((int)(((byte)(226)))), ((int)(((byte)(236)))));
            this.btnPreviewPdf.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnPreviewPdf.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnPreviewPdf.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.btnPreviewPdf.Location = new System.Drawing.Point(504, 8);
            this.btnPreviewPdf.Name = "btnPreviewPdf";
            this.btnPreviewPdf.Size = new System.Drawing.Size(90, 28);
            this.btnPreviewPdf.TabIndex = 1;
            this.btnPreviewPdf.Text = "预览 PDF";
            this.btnPreviewPdf.UseVisualStyleBackColor = false;
            this.btnPreviewPdf.Click += new System.EventHandler(this.BtnPreviewPdf_Click);
            //
            // chkAutoPreview
            //
            this.chkAutoPreview.AutoSize = true;
            this.chkAutoPreview.Checked = true;
            this.chkAutoPreview.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkAutoPreview.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.chkAutoPreview.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.chkAutoPreview.Location = new System.Drawing.Point(306, 13);
            this.chkAutoPreview.Name = "chkAutoPreview";
            this.chkAutoPreview.Size = new System.Drawing.Size(102, 21);
            this.chkAutoPreview.TabIndex = 0;
            this.chkAutoPreview.Text = "选中即预览";
            this.chkAutoPreview.UseVisualStyleBackColor = true;
            this.chkAutoPreview.CheckedChanged += new System.EventHandler(this.ChkAutoPreview_CheckedChanged);
            //
            // btnSaveClassify
            //
            this.btnSaveClassify.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnSaveClassify.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(232)))), ((int)(((byte)(237)))), ((int)(((byte)(243)))));
            this.btnSaveClassify.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnSaveClassify.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(199)))), ((int)(((byte)(208)))), ((int)(((byte)(219)))));
            this.btnSaveClassify.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(217)))), ((int)(((byte)(226)))), ((int)(((byte)(236)))));
            this.btnSaveClassify.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnSaveClassify.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnSaveClassify.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.btnSaveClassify.Location = new System.Drawing.Point(600, 8);
            this.btnSaveClassify.Name = "btnSaveClassify";
            this.btnSaveClassify.Size = new System.Drawing.Size(110, 28);
            this.btnSaveClassify.TabIndex = 2;
            this.btnSaveClassify.Text = "保存分类结果";
            this.btnSaveClassify.UseVisualStyleBackColor = false;
            this.btnSaveClassify.Click += new System.EventHandler(this.BtnSaveClassify_Click);
            //
            // btnOpenClassifyDir
            //
            this.btnOpenClassifyDir.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnOpenClassifyDir.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(232)))), ((int)(((byte)(237)))), ((int)(((byte)(243)))));
            this.btnOpenClassifyDir.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnOpenClassifyDir.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(199)))), ((int)(((byte)(208)))), ((int)(((byte)(219)))));
            this.btnOpenClassifyDir.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(217)))), ((int)(((byte)(226)))), ((int)(((byte)(236)))));
            this.btnOpenClassifyDir.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnOpenClassifyDir.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnOpenClassifyDir.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.btnOpenClassifyDir.Location = new System.Drawing.Point(714, 8);
            this.btnOpenClassifyDir.Name = "btnOpenClassifyDir";
            this.btnOpenClassifyDir.Size = new System.Drawing.Size(110, 28);
            this.btnOpenClassifyDir.TabIndex = 3;
            this.btnOpenClassifyDir.Text = "打开分类目录";
            this.btnOpenClassifyDir.UseVisualStyleBackColor = false;
            this.btnOpenClassifyDir.Click += new System.EventHandler(this.BtnOpenClassifyDir_Click);
            //
            // cmsClassify
            //
            this.cmsClassify.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.mnuShowPartInfo,
            this.mnuPreviewPdf});
            this.cmsClassify.Name = "cmsClassify";
            this.cmsClassify.Size = new System.Drawing.Size(173, 48);
            this.cmsClassify.Opening += new System.ComponentModel.CancelEventHandler(this.CmsClassify_Opening);
            //
            // mnuShowPartInfo
            //
            this.mnuShowPartInfo.Name = "mnuShowPartInfo";
            this.mnuShowPartInfo.ShortcutKeyDisplayString = "Enter";
            this.mnuShowPartInfo.Size = new System.Drawing.Size(172, 22);
            this.mnuShowPartInfo.Text = "查看部品基本信息";
            this.mnuShowPartInfo.Click += new System.EventHandler(this.MnuShowPartInfo_Click);
            //
            // mnuPreviewPdf
            //
            this.mnuPreviewPdf.Name = "mnuPreviewPdf";
            this.mnuPreviewPdf.Size = new System.Drawing.Size(172, 22);
            this.mnuPreviewPdf.Text = "预览 PDF";
            this.mnuPreviewPdf.Click += new System.EventHandler(this.MnuPreviewPdf_Click);
            //
            // dgvClassify
            //
            this.dgvClassify.AllowUserToAddRows = false;
            this.dgvClassify.AllowUserToDeleteRows = false;
            this.dgvClassify.AllowUserToResizeRows = false;
            this.dgvClassify.BackgroundColor = System.Drawing.Color.White;
            this.dgvClassify.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.dgvClassify.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            this.dgvClassify.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dgvClassify.Location = new System.Drawing.Point(3, 3);
            this.dgvClassify.MultiSelect = false;
            this.dgvClassify.Name = "dgvClassify";
            this.dgvClassify.RowHeadersVisible = false;
            this.dgvClassify.RowTemplate.Height = 24;
            this.dgvClassify.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dgvClassify.Size = new System.Drawing.Size(840, 240);
            this.dgvClassify.TabIndex = 0;
            this.dgvClassify.CellValueChanged += new System.Windows.Forms.DataGridViewCellEventHandler(this.DgvClassify_CellValueChanged);
            this.dgvClassify.ContextMenuStrip = this.cmsClassify;
            this.dgvClassify.KeyDown += new System.Windows.Forms.KeyEventHandler(this.DgvClassify_KeyDown);
            this.dgvClassify.CellDoubleClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.DgvClassify_CellDoubleClick);
            this.dgvClassify.CurrentCellChanged += new System.EventHandler(this.DgvClassify_CurrentCellChanged);
            this.dgvClassify.DataError += new System.Windows.Forms.DataGridViewDataErrorEventHandler(this.DgvClassify_DataError);
            // 
            // rtbLog
            // 
            this.rtbLog.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.rtbLog.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(30)))), ((int)(((byte)(30)))));
            this.rtbLog.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.rtbLog.DetectUrls = false;
            this.rtbLog.Font = new System.Drawing.Font("Consolas", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.rtbLog.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(212)))), ((int)(((byte)(212)))), ((int)(((byte)(212)))));
            this.rtbLog.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rtbLog.Location = new System.Drawing.Point(16, 28);
            this.rtbLog.Name = "rtbLog";
            this.rtbLog.ReadOnly = true;
            this.rtbLog.Size = new System.Drawing.Size(828, 296);
            this.rtbLog.TabIndex = 0;
            this.rtbLog.Text = "";
            this.rtbLog.WordWrap = false;
            // 
            // statusStrip
            // 
            this.statusStrip.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(232)))), ((int)(((byte)(237)))), ((int)(((byte)(243)))));
            this.statusStrip.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.statusStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.lblStatus,
            this.lblSpring,
            this.lblPdfInfo,
            this.lblEngine});
            this.statusStrip.Location = new System.Drawing.Point(0, 678);
            this.statusStrip.Name = "statusStrip";
            this.statusStrip.Size = new System.Drawing.Size(900, 22);
            this.statusStrip.SizingGrip = false;
            this.statusStrip.TabIndex = 2;
            // 
            // lblStatus
            // 
            this.lblStatus.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(32, 17);
            this.lblStatus.Text = "就绪";
            // 
            // lblSpring
            // 
            this.lblSpring.Name = "lblSpring";
            this.lblSpring.Size = new System.Drawing.Size(0, 17);
            this.lblSpring.Spring = true;
            // 
            // lblPdfInfo
            // 
            this.lblPdfInfo.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.lblPdfInfo.Name = "lblPdfInfo";
            this.lblPdfInfo.Size = new System.Drawing.Size(62, 17);
            this.lblPdfInfo.Text = "PDF：0 个";
            // 
            // lblEngine
            // 
            this.lblEngine.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(108)))), ((int)(((byte)(117)))), ((int)(((byte)(125)))));
            this.lblEngine.Name = "lblEngine";
            this.lblEngine.Size = new System.Drawing.Size(111, 17);
            this.lblEngine.Text = "引擎：加载中...";
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(242)))), ((int)(((byte)(244)))), ((int)(((byte)(247)))));
            this.ClientSize = new System.Drawing.Size(900, 700);
            this.Controls.Add(this.pnlContent);
            this.Controls.Add(this.pnlHeader);
            this.Controls.Add(this.statusStrip);
            this.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.MinimumSize = new System.Drawing.Size(900, 700);
            this.Name = "Form1";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "MSIF AI TOOLS  -  PDF 分解工具（坐标 + 文本 + 分类）";
            this.Shown += new System.EventHandler(this.Form1_Shown);
            this.pnlHeader.ResumeLayout(false);
            this.pnlHeader.PerformLayout();
            this.pnlContent.ResumeLayout(false);
            this.grpLog.ResumeLayout(false);
            this.tabMain.ResumeLayout(false);
            this.tabPageLog.ResumeLayout(false);
            this.tabPageClassify.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.dgvClassify)).EndInit();
            this.pnlClassifyBar.ResumeLayout(false);
            this.pnlClassifyBar.PerformLayout();
            this.grpAction.ResumeLayout(false);
            this.grpAction.PerformLayout();
            this.grpInput.ResumeLayout(false);
            this.grpInput.PerformLayout();
            this.statusStrip.ResumeLayout(false);
            this.statusStrip.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Panel pnlHeader;
        private System.Windows.Forms.Label lblAppTitle;
        private System.Windows.Forms.Label lblAppSub;
        private System.Windows.Forms.Label lblVersion;
        private System.Windows.Forms.Panel pnlHeaderAccent;
        private System.Windows.Forms.Panel pnlContent;
        private System.Windows.Forms.GroupBox grpInput;
        private System.Windows.Forms.Label lblFolder;
        private System.Windows.Forms.TextBox txtFolder;
        private System.Windows.Forms.Button btnBrowse;
        private System.Windows.Forms.GroupBox grpAction;
        private System.Windows.Forms.Button btnStart;
        private System.Windows.Forms.Button btnAiAnalyze;
        private System.Windows.Forms.Button btnAiSettings;
        private System.Windows.Forms.Button btnOpenOutput;
        private System.Windows.Forms.Label lblProgress;
        private System.Windows.Forms.ProgressBar progressBar;
        private System.Windows.Forms.GroupBox grpLog;
        private System.Windows.Forms.TabControl tabMain;
        private System.Windows.Forms.TabPage tabPageLog;
        private System.Windows.Forms.RichTextBox rtbLog;
        private System.Windows.Forms.TabPage tabPageClassify;
        private System.Windows.Forms.DataGridView dgvClassify;
        private System.Windows.Forms.Panel pnlClassifyBar;
        private System.Windows.Forms.Label lblClassifySummary;
        private System.Windows.Forms.Button btnOpenClassifyDir;
        private System.Windows.Forms.Button btnPreviewPdf;
        private System.Windows.Forms.Button btnPartInfo;
        private System.Windows.Forms.ContextMenuStrip cmsClassify;
        private System.Windows.Forms.ToolStripMenuItem mnuShowPartInfo;
        private System.Windows.Forms.ToolStripMenuItem mnuPreviewPdf;
        private System.Windows.Forms.Button btnSaveClassify;
        private System.Windows.Forms.CheckBox chkAutoPreview;
        private System.Windows.Forms.StatusStrip statusStrip;
        private System.Windows.Forms.ToolStripStatusLabel lblStatus;
        private System.Windows.Forms.ToolStripStatusLabel lblSpring;
        private System.Windows.Forms.ToolStripStatusLabel lblPdfInfo;
        private System.Windows.Forms.ToolStripStatusLabel lblEngine;
    }
}
