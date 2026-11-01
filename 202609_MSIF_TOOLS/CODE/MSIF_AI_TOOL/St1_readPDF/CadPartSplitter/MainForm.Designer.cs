namespace CadPartSplitter
{
    partial class MainForm
    {
        /// <summary>设计器所需的组件容器。</summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>清理正在使用的资源。</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// 设计器支持所需的方法 - 不要修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            this.rootLayout = new System.Windows.Forms.TableLayoutPanel();
            this.grpInput = new System.Windows.Forms.GroupBox();
            this.lblInputFile = new System.Windows.Forms.Label();
            this.txtInput = new System.Windows.Forms.TextBox();
            this.btnBrowseInput = new System.Windows.Forms.Button();
            this.lblOutputDir = new System.Windows.Forms.Label();
            this.txtOutput = new System.Windows.Forms.TextBox();
            this.btnBrowseOutput = new System.Windows.Forms.Button();
            this.grpOptions = new System.Windows.Forms.GroupBox();
            this.lblDpi = new System.Windows.Forms.Label();
            this.numDpi = new System.Windows.Forms.NumericUpDown();
            this.lblPage = new System.Windows.Forms.Label();
            this.numPage = new System.Windows.Forms.NumericUpDown();
            this.lblMask = new System.Windows.Forms.Label();
            this.numMask = new System.Windows.Forms.NumericUpDown();
            this.lblArea = new System.Windows.Forms.Label();
            this.numArea = new System.Windows.Forms.NumericUpDown();
            this.lblPad = new System.Windows.Forms.Label();
            this.numPad = new System.Windows.Forms.NumericUpDown();
            this.lblKw = new System.Windows.Forms.Label();
            this.numKw = new System.Windows.Forms.NumericUpDown();
            this.lblKh = new System.Windows.Forms.Label();
            this.numKh = new System.Windows.Forms.NumericUpDown();
            this.chkAutoMask = new System.Windows.Forms.CheckBox();
            this.chkDebug = new System.Windows.Forms.CheckBox();
            this.chkDropTextHeavy = new System.Windows.Forms.CheckBox();
            this.panelActions = new System.Windows.Forms.Panel();
            this.btnStart = new System.Windows.Forms.Button();
            this.btnOpenOutput = new System.Windows.Forms.Button();
            this.progressBar = new System.Windows.Forms.ProgressBar();
            this.lblStatus = new System.Windows.Forms.Label();
            this.grpLog = new System.Windows.Forms.GroupBox();
            this.txtLog = new System.Windows.Forms.RichTextBox();
            this.rootLayout.SuspendLayout();
            this.grpInput.SuspendLayout();
            this.grpOptions.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numDpi)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numPage)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMask)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numArea)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numPad)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numKw)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numKh)).BeginInit();
            this.panelActions.SuspendLayout();
            this.grpLog.SuspendLayout();
            this.SuspendLayout();

            // 
            // rootLayout
            // 
            this.rootLayout.ColumnCount = 1;
            this.rootLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.rootLayout.Controls.Add(this.grpInput, 0, 0);
            this.rootLayout.Controls.Add(this.grpOptions, 0, 1);
            this.rootLayout.Controls.Add(this.panelActions, 0, 2);
            this.rootLayout.Controls.Add(this.grpLog, 0, 3);
            this.rootLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rootLayout.Location = new System.Drawing.Point(0, 0);
            this.rootLayout.Margin = new System.Windows.Forms.Padding(0);
            this.rootLayout.Name = "rootLayout";
            this.rootLayout.RowCount = 4;
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 104F));
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 104F));
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 52F));
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.rootLayout.Size = new System.Drawing.Size(900, 690);
            this.rootLayout.TabIndex = 0;
            // 
            // grpInput
            // 
            this.grpInput.Controls.Add(this.lblInputFile);
            this.grpInput.Controls.Add(this.txtInput);
            this.grpInput.Controls.Add(this.btnBrowseInput);
            this.grpInput.Controls.Add(this.lblOutputDir);
            this.grpInput.Controls.Add(this.txtOutput);
            this.grpInput.Controls.Add(this.btnBrowseOutput);
            this.grpInput.Dock = System.Windows.Forms.DockStyle.Fill;
            this.grpInput.Location = new System.Drawing.Point(8, 6);
            this.grpInput.Margin = new System.Windows.Forms.Padding(8, 6, 8, 0);
            this.grpInput.Name = "grpInput";
            this.grpInput.Size = new System.Drawing.Size(884, 98);
            this.grpInput.TabIndex = 0;
            this.grpInput.TabStop = false;
            this.grpInput.Text = "输入 / 输出";
            // 
            // lblInputFile
            // 
            this.lblInputFile.AutoSize = true;
            this.lblInputFile.Location = new System.Drawing.Point(14, 29);
            this.lblInputFile.Name = "lblInputFile";
            this.lblInputFile.Size = new System.Drawing.Size(68, 17);
            this.lblInputFile.TabIndex = 0;
            this.lblInputFile.Text = "输入文件：";
            // 
            // txtInput
            // 
            this.txtInput.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtInput.BackColor = System.Drawing.Color.White;
            this.txtInput.Location = new System.Drawing.Point(96, 26);
            this.txtInput.Name = "txtInput";
            this.txtInput.ReadOnly = true;
            this.txtInput.Size = new System.Drawing.Size(660, 23);
            this.txtInput.TabIndex = 1;
            // 
            // btnBrowseInput
            // 
            this.btnBrowseInput.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnBrowseInput.Location = new System.Drawing.Point(770, 24);
            this.btnBrowseInput.Name = "btnBrowseInput";
            this.btnBrowseInput.Size = new System.Drawing.Size(100, 26);
            this.btnBrowseInput.TabIndex = 2;
            this.btnBrowseInput.Text = "浏览…";
            this.btnBrowseInput.UseVisualStyleBackColor = true;
            // 
            // lblOutputDir
            // 
            this.lblOutputDir.AutoSize = true;
            this.lblOutputDir.Location = new System.Drawing.Point(14, 59);
            this.lblOutputDir.Name = "lblOutputDir";
            this.lblOutputDir.Size = new System.Drawing.Size(68, 17);
            this.lblOutputDir.TabIndex = 3;
            this.lblOutputDir.Text = "输出目录：";
            // 
            // txtOutput
            // 
            this.txtOutput.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtOutput.Location = new System.Drawing.Point(96, 56);
            this.txtOutput.Name = "txtOutput";
            this.txtOutput.Size = new System.Drawing.Size(660, 23);
            this.txtOutput.TabIndex = 4;
            // 
            // btnBrowseOutput
            // 
            this.btnBrowseOutput.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnBrowseOutput.Location = new System.Drawing.Point(770, 54);
            this.btnBrowseOutput.Name = "btnBrowseOutput";
            this.btnBrowseOutput.Size = new System.Drawing.Size(100, 26);
            this.btnBrowseOutput.TabIndex = 5;
            this.btnBrowseOutput.Text = "浏览…";
            this.btnBrowseOutput.UseVisualStyleBackColor = true;
            // 
            // grpOptions
            // 
            this.grpOptions.Controls.Add(this.lblDpi);
            this.grpOptions.Controls.Add(this.numDpi);
            this.grpOptions.Controls.Add(this.lblPage);
            this.grpOptions.Controls.Add(this.numPage);
            this.grpOptions.Controls.Add(this.lblMask);
            this.grpOptions.Controls.Add(this.numMask);
            this.grpOptions.Controls.Add(this.lblArea);
            this.grpOptions.Controls.Add(this.numArea);
            this.grpOptions.Controls.Add(this.lblPad);
            this.grpOptions.Controls.Add(this.numPad);
            this.grpOptions.Controls.Add(this.lblKw);
            this.grpOptions.Controls.Add(this.numKw);
            this.grpOptions.Controls.Add(this.lblKh);
            this.grpOptions.Controls.Add(this.numKh);
            this.grpOptions.Controls.Add(this.chkAutoMask);
            this.grpOptions.Controls.Add(this.chkDebug);
            this.grpOptions.Controls.Add(this.chkDropTextHeavy);
            this.grpOptions.Dock = System.Windows.Forms.DockStyle.Fill;
            this.grpOptions.Location = new System.Drawing.Point(8, 110);
            this.grpOptions.Margin = new System.Windows.Forms.Padding(8, 6, 8, 0);
            this.grpOptions.Name = "grpOptions";
            this.grpOptions.Size = new System.Drawing.Size(884, 98);
            this.grpOptions.TabIndex = 1;
            this.grpOptions.TabStop = false;
            this.grpOptions.Text = "识别参数";
            // 
            // lblDpi
            // 
            this.lblDpi.AutoSize = true;
            this.lblDpi.Location = new System.Drawing.Point(14, 31);
            this.lblDpi.Name = "lblDpi";
            this.lblDpi.Size = new System.Drawing.Size(68, 17);
            this.lblDpi.TabIndex = 0;
            this.lblDpi.Text = "渲染 DPI：";
            // 
            // numDpi
            // 
            this.numDpi.Location = new System.Drawing.Point(96, 28);
            this.numDpi.Maximum = new decimal(new int[] { 1200, 0, 0, 0 });
            this.numDpi.Minimum = new decimal(new int[] { 72, 0, 0, 0 });
            this.numDpi.Name = "numDpi";
            this.numDpi.Size = new System.Drawing.Size(90, 23);
            this.numDpi.TabIndex = 1;
            this.numDpi.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numDpi.Value = new decimal(new int[] { 300, 0, 0, 0 });
            // 
            // lblPage
            // 
            this.lblPage.AutoSize = true;
            this.lblPage.Location = new System.Drawing.Point(196, 31);
            this.lblPage.Name = "lblPage";
            this.lblPage.Size = new System.Drawing.Size(44, 17);
            this.lblPage.TabIndex = 2;
            this.lblPage.Text = "页码：";
            // 
            // numPage
            // 
            this.numPage.Location = new System.Drawing.Point(268, 28);
            this.numPage.Maximum = new decimal(new int[] { 9999, 0, 0, 0 });
            this.numPage.Name = "numPage";
            this.numPage.Size = new System.Drawing.Size(90, 23);
            this.numPage.TabIndex = 3;
            this.numPage.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            // 
            // lblMask
            // 
            this.lblMask.AutoSize = true;
            this.lblMask.Location = new System.Drawing.Point(368, 31);
            this.lblMask.Name = "lblMask";
            this.lblMask.Size = new System.Drawing.Size(68, 17);
            this.lblMask.TabIndex = 4;
            this.lblMask.Text = "右侧遮蔽：";
            // 
            // numMask
            // 
            this.numMask.DecimalPlaces = 2;
            this.numMask.Increment = new decimal(new int[] { 5, 0, 0, 131072 });
            this.numMask.Location = new System.Drawing.Point(448, 28);
            this.numMask.Maximum = new decimal(new int[] { 99, 0, 0, 131072 });
            this.numMask.Name = "numMask";
            this.numMask.Size = new System.Drawing.Size(90, 23);
            this.numMask.TabIndex = 5;
            this.numMask.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numMask.Value = new decimal(new int[] { 50, 0, 0, 131072 });
            // 
            // lblArea
            // 
            this.lblArea.AutoSize = true;
            this.lblArea.Location = new System.Drawing.Point(548, 31);
            this.lblArea.Name = "lblArea";
            this.lblArea.Size = new System.Drawing.Size(68, 17);
            this.lblArea.TabIndex = 6;
            this.lblArea.Text = "最小面积：";
            // 
            // numArea
            // 
            this.numArea.Increment = new decimal(new int[] { 5000, 0, 0, 0 });
            this.numArea.Location = new System.Drawing.Point(628, 28);
            this.numArea.Maximum = new decimal(new int[] { 5000000, 0, 0, 0 });
            this.numArea.Name = "numArea";
            this.numArea.Size = new System.Drawing.Size(110, 23);
            this.numArea.TabIndex = 7;
            this.numArea.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numArea.Value = new decimal(new int[] { 80000, 0, 0, 0 });

            // 
            // lblPad
            // 
            this.lblPad.AutoSize = true;
            this.lblPad.Location = new System.Drawing.Point(14, 63);
            this.lblPad.Name = "lblPad";
            this.lblPad.Size = new System.Drawing.Size(68, 17);
            this.lblPad.TabIndex = 8;
            this.lblPad.Text = "扩展边距：";
            // 
            // numPad
            // 
            this.numPad.Location = new System.Drawing.Point(96, 60);
            this.numPad.Maximum = new decimal(new int[] { 500, 0, 0, 0 });
            this.numPad.Name = "numPad";
            this.numPad.Size = new System.Drawing.Size(90, 23);
            this.numPad.TabIndex = 9;
            this.numPad.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numPad.Value = new decimal(new int[] { 30, 0, 0, 0 });
            // 
            // lblKw
            // 
            this.lblKw.AutoSize = true;
            this.lblKw.Location = new System.Drawing.Point(196, 63);
            this.lblKw.Name = "lblKw";
            this.lblKw.Size = new System.Drawing.Size(56, 17);
            this.lblKw.TabIndex = 10;
            this.lblKw.Text = "核宽度：";
            // 
            // numKw
            // 
            this.numKw.Location = new System.Drawing.Point(268, 60);
            this.numKw.Maximum = new decimal(new int[] { 500, 0, 0, 0 });
            this.numKw.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numKw.Name = "numKw";
            this.numKw.Size = new System.Drawing.Size(90, 23);
            this.numKw.TabIndex = 11;
            this.numKw.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numKw.Value = new decimal(new int[] { 80, 0, 0, 0 });
            // 
            // lblKh
            // 
            this.lblKh.AutoSize = true;
            this.lblKh.Location = new System.Drawing.Point(368, 63);
            this.lblKh.Name = "lblKh";
            this.lblKh.Size = new System.Drawing.Size(56, 17);
            this.lblKh.TabIndex = 12;
            this.lblKh.Text = "核高度：";
            // 
            // numKh
            // 
            this.numKh.Location = new System.Drawing.Point(448, 60);
            this.numKh.Maximum = new decimal(new int[] { 500, 0, 0, 0 });
            this.numKh.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numKh.Name = "numKh";
            this.numKh.Size = new System.Drawing.Size(90, 23);
            this.numKh.TabIndex = 13;
            this.numKh.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numKh.Value = new decimal(new int[] { 25, 0, 0, 0 });
            // 
            // chkAutoMask
            // 
            this.chkAutoMask.AutoSize = true;
            this.chkAutoMask.Checked = true;
            this.chkAutoMask.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkAutoMask.Location = new System.Drawing.Point(700, 62);
            this.chkAutoMask.Name = "chkAutoMask";
            this.chkAutoMask.Size = new System.Drawing.Size(100, 21);
            this.chkAutoMask.TabIndex = 15;
            this.chkAutoMask.Text = "自动遮蔽";
            this.chkAutoMask.UseVisualStyleBackColor = true;
            // 
            // chkDebug
            // 
            this.chkDebug.AutoSize = true;
            this.chkDebug.Location = new System.Drawing.Point(560, 62);
            this.chkDebug.Name = "chkDebug";
            this.chkDebug.Size = new System.Drawing.Size(92, 21);
            this.chkDebug.TabIndex = 14;
            this.chkDebug.Text = "保存调试图";
            this.chkDebug.UseVisualStyleBackColor = true;
            //
            // chkDropTextHeavy
            //
            this.chkDropTextHeavy.AutoSize = true;
            this.chkDropTextHeavy.Checked = true;
            this.chkDropTextHeavy.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkDropTextHeavy.Location = new System.Drawing.Point(752, 26);
            this.chkDropTextHeavy.Name = "chkDropTextHeavy";
            this.chkDropTextHeavy.Size = new System.Drawing.Size(100, 21);
            this.chkDropTextHeavy.TabIndex = 16;
            this.chkDropTextHeavy.Text = "筛除表格残片";
            this.chkDropTextHeavy.UseVisualStyleBackColor = true;
            // 
            // panelActions
            // 
            this.panelActions.Controls.Add(this.btnStart);
            this.panelActions.Controls.Add(this.btnOpenOutput);
            this.panelActions.Controls.Add(this.progressBar);
            this.panelActions.Controls.Add(this.lblStatus);
            this.panelActions.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelActions.Location = new System.Drawing.Point(8, 214);
            this.panelActions.Margin = new System.Windows.Forms.Padding(8, 6, 8, 0);
            this.panelActions.Name = "panelActions";
            this.panelActions.Size = new System.Drawing.Size(884, 46);
            this.panelActions.TabIndex = 2;
            // 
            // btnStart
            // 
            this.btnStart.Location = new System.Drawing.Point(0, 6);
            this.btnStart.Name = "btnStart";
            this.btnStart.Size = new System.Drawing.Size(150, 34);
            this.btnStart.TabIndex = 0;
            this.btnStart.Text = "开始分割";
            this.btnStart.UseVisualStyleBackColor = true;
            // 
            // btnOpenOutput
            // 
            this.btnOpenOutput.Location = new System.Drawing.Point(160, 6);
            this.btnOpenOutput.Name = "btnOpenOutput";
            this.btnOpenOutput.Size = new System.Drawing.Size(150, 34);
            this.btnOpenOutput.TabIndex = 1;
            this.btnOpenOutput.Text = "打开输出目录";
            this.btnOpenOutput.UseVisualStyleBackColor = true;
            // 
            // progressBar
            // 
            this.progressBar.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.progressBar.Location = new System.Drawing.Point(330, 14);
            this.progressBar.MarqueeAnimationSpeed = 25;
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new System.Drawing.Size(400, 18);
            this.progressBar.Style = System.Windows.Forms.ProgressBarStyle.Marquee;
            this.progressBar.TabIndex = 2;
            this.progressBar.Visible = false;
            // 
            // lblStatus
            // 
            this.lblStatus.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.lblStatus.AutoEllipsis = true;
            this.lblStatus.Location = new System.Drawing.Point(740, 11);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(140, 24);
            this.lblStatus.TabIndex = 3;
            this.lblStatus.Text = "就绪";
            this.lblStatus.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            // 
            // grpLog
            // 
            this.grpLog.Controls.Add(this.txtLog);
            this.grpLog.Dock = System.Windows.Forms.DockStyle.Fill;
            this.grpLog.Location = new System.Drawing.Point(8, 266);
            this.grpLog.Margin = new System.Windows.Forms.Padding(8, 6, 8, 8);
            this.grpLog.Name = "grpLog";
            this.grpLog.Size = new System.Drawing.Size(884, 416);
            this.grpLog.TabIndex = 3;
            this.grpLog.TabStop = false;
            this.grpLog.Text = "处理日志";
            // 
            // txtLog
            // 
            this.txtLog.BackColor = System.Drawing.Color.White;
            this.txtLog.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.txtLog.DetectUrls = false;
            this.txtLog.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtLog.Font = new System.Drawing.Font("Consolas", 9F);
            this.txtLog.HideSelection = false;
            this.txtLog.Location = new System.Drawing.Point(3, 19);
            this.txtLog.Name = "txtLog";
            this.txtLog.ReadOnly = true;
            this.txtLog.Size = new System.Drawing.Size(878, 394);
            this.txtLog.TabIndex = 0;
            this.txtLog.Text = "";
            this.txtLog.WordWrap = false;
            // 
            // MainForm
            // 
            this.AllowDrop = true;
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.ClientSize = new System.Drawing.Size(900, 690);
            this.Controls.Add(this.rootLayout);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.MinimumSize = new System.Drawing.Size(820, 620);
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "CAD 零件分割工具 v1.0";
            this.rootLayout.ResumeLayout(false);
            this.grpInput.ResumeLayout(false);
            this.grpInput.PerformLayout();
            this.grpOptions.ResumeLayout(false);
            this.grpOptions.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numDpi)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numPage)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMask)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numArea)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numPad)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numKw)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numKh)).EndInit();
            this.panelActions.ResumeLayout(false);
            this.grpLog.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel rootLayout;
        private System.Windows.Forms.GroupBox grpInput;
        private System.Windows.Forms.Label lblInputFile;
        private System.Windows.Forms.TextBox txtInput;
        private System.Windows.Forms.Button btnBrowseInput;
        private System.Windows.Forms.Label lblOutputDir;
        private System.Windows.Forms.TextBox txtOutput;
        private System.Windows.Forms.Button btnBrowseOutput;
        private System.Windows.Forms.GroupBox grpOptions;
        private System.Windows.Forms.Label lblDpi;
        private System.Windows.Forms.NumericUpDown numDpi;
        private System.Windows.Forms.Label lblPage;
        private System.Windows.Forms.NumericUpDown numPage;
        private System.Windows.Forms.Label lblMask;
        private System.Windows.Forms.NumericUpDown numMask;
        private System.Windows.Forms.Label lblArea;
        private System.Windows.Forms.NumericUpDown numArea;
        private System.Windows.Forms.Label lblPad;
        private System.Windows.Forms.NumericUpDown numPad;
        private System.Windows.Forms.Label lblKw;
        private System.Windows.Forms.NumericUpDown numKw;
        private System.Windows.Forms.Label lblKh;
        private System.Windows.Forms.NumericUpDown numKh;
        private System.Windows.Forms.CheckBox chkAutoMask;
        private System.Windows.Forms.CheckBox chkDebug;
        private System.Windows.Forms.CheckBox chkDropTextHeavy;
        private System.Windows.Forms.Panel panelActions;
        private System.Windows.Forms.Button btnStart;
        private System.Windows.Forms.Button btnOpenOutput;
        private System.Windows.Forms.ProgressBar progressBar;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.GroupBox grpLog;
        private System.Windows.Forms.RichTextBox txtLog;
    }
}

