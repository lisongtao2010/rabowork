namespace MSIFAITOOLS
{
    partial class AiSettingsForm
    {
        /// <summary>必需的设计器变量。</summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>清理所有正在使用的资源。</summary>
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

        /// <summary>设计器支持所需的方法。</summary>
        private void InitializeComponent()
        {
            this.lblTitle = new System.Windows.Forms.Label();
            this.lblSettingsPath = new System.Windows.Forms.Label();
            this.grpConnection = new System.Windows.Forms.GroupBox();
            this.lblEndpointPreset = new System.Windows.Forms.Label();
            this.cboEndpointPreset = new System.Windows.Forms.ComboBox();
            this.lblBaseUrl = new System.Windows.Forms.Label();
            this.txtBaseUrl = new System.Windows.Forms.TextBox();
            this.lblModel = new System.Windows.Forms.Label();
            this.cboModel = new System.Windows.Forms.ComboBox();
            this.lblApiKey = new System.Windows.Forms.Label();
            this.txtApiKey = new System.Windows.Forms.TextBox();
            this.chkShowKey = new System.Windows.Forms.CheckBox();
            this.lblTimeout = new System.Windows.Forms.Label();
            this.numTimeout = new System.Windows.Forms.NumericUpDown();
            this.lblNote = new System.Windows.Forms.Label();
            this.lblStatus = new System.Windows.Forms.Label();
            this.btnTest = new System.Windows.Forms.Button();
            this.btnOpenFolder = new System.Windows.Forms.Button();
            this.btnSave = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.grpConnection.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numTimeout)).BeginInit();
            this.SuspendLayout();
            // 
            // lblTitle
            // 
            this.lblTitle.AutoSize = true;
            this.lblTitle.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblTitle.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(44)))), ((int)(((byte)(62)))), ((int)(((byte)(80)))));
            this.lblTitle.Location = new System.Drawing.Point(20, 14);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(240, 22);
            this.lblTitle.TabIndex = 0;
            this.lblTitle.Text = "AI 接口设置（OpenAI 兼容）";
            // 
            // lblSettingsPath
            // 
            this.lblSettingsPath.AutoSize = true;
            this.lblSettingsPath.Font = new System.Drawing.Font("微软雅黑", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblSettingsPath.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(108)))), ((int)(((byte)(117)))), ((int)(((byte)(125)))));
            this.lblSettingsPath.Location = new System.Drawing.Point(22, 44);
            this.lblSettingsPath.Name = "lblSettingsPath";
            this.lblSettingsPath.Size = new System.Drawing.Size(560, 16);
            this.lblSettingsPath.TabIndex = 1;
            this.lblSettingsPath.Text = "保存位置：%APPDATA%\\MSIFAITOOLS\\ai-settings.json";
            // 
            // grpConnection
            // 
            this.grpConnection.BackColor = System.Drawing.Color.White;
            this.grpConnection.Controls.Add(this.lblNote);
            this.grpConnection.Controls.Add(this.numTimeout);
            this.grpConnection.Controls.Add(this.lblTimeout);
            this.grpConnection.Controls.Add(this.chkShowKey);
            this.grpConnection.Controls.Add(this.txtApiKey);
            this.grpConnection.Controls.Add(this.lblApiKey);
            this.grpConnection.Controls.Add(this.cboModel);
            this.grpConnection.Controls.Add(this.lblModel);
            this.grpConnection.Controls.Add(this.txtBaseUrl);
            this.grpConnection.Controls.Add(this.lblBaseUrl);
            this.grpConnection.Controls.Add(this.cboEndpointPreset);
            this.grpConnection.Controls.Add(this.lblEndpointPreset);
            this.grpConnection.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.grpConnection.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.grpConnection.Location = new System.Drawing.Point(18, 70);
            this.grpConnection.Name = "grpConnection";
            this.grpConnection.Size = new System.Drawing.Size(624, 258);
            this.grpConnection.TabIndex = 2;
            this.grpConnection.TabStop = false;
            this.grpConnection.Text = "连接参数";
            // 
            // lblEndpointPreset
            // 
            this.lblEndpointPreset.AutoSize = true;
            this.lblEndpointPreset.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblEndpointPreset.Location = new System.Drawing.Point(16, 33);
            this.lblEndpointPreset.Name = "lblEndpointPreset";
            this.lblEndpointPreset.Size = new System.Drawing.Size(60, 17);
            this.lblEndpointPreset.TabIndex = 0;
            this.lblEndpointPreset.Text = "服务端点";
            // 
            // cboEndpointPreset
            // 
            this.cboEndpointPreset.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cboEndpointPreset.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.cboEndpointPreset.FormattingEnabled = true;
            this.cboEndpointPreset.Location = new System.Drawing.Point(110, 29);
            this.cboEndpointPreset.Name = "cboEndpointPreset";
            this.cboEndpointPreset.Size = new System.Drawing.Size(260, 25);
            this.cboEndpointPreset.TabIndex = 1;
            this.cboEndpointPreset.SelectedIndexChanged += new System.EventHandler(this.CboEndpointPreset_SelectedIndexChanged);
            // 
            // lblBaseUrl
            // 
            this.lblBaseUrl.AutoSize = true;
            this.lblBaseUrl.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblBaseUrl.Location = new System.Drawing.Point(16, 71);
            this.lblBaseUrl.Name = "lblBaseUrl";
            this.lblBaseUrl.Size = new System.Drawing.Size(60, 17);
            this.lblBaseUrl.TabIndex = 2;
            this.lblBaseUrl.Text = "接口地址";
            // 
            // txtBaseUrl
            // 
            this.txtBaseUrl.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.txtBaseUrl.Font = new System.Drawing.Font("Consolas", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.txtBaseUrl.Location = new System.Drawing.Point(110, 67);
            this.txtBaseUrl.Name = "txtBaseUrl";
            this.txtBaseUrl.Size = new System.Drawing.Size(496, 22);
            this.txtBaseUrl.TabIndex = 3;
            // 
            // lblModel
            // 
            this.lblModel.AutoSize = true;
            this.lblModel.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblModel.Location = new System.Drawing.Point(16, 109);
            this.lblModel.Name = "lblModel";
            this.lblModel.Size = new System.Drawing.Size(32, 17);
            this.lblModel.TabIndex = 4;
            this.lblModel.Text = "模型";
            // 
            // cboModel
            // 
            this.cboModel.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.cboModel.FormattingEnabled = true;
            this.cboModel.Location = new System.Drawing.Point(110, 105);
            this.cboModel.Name = "cboModel";
            this.cboModel.Size = new System.Drawing.Size(340, 25);
            this.cboModel.TabIndex = 5;
            // 
            // lblApiKey
            // 
            this.lblApiKey.AutoSize = true;
            this.lblApiKey.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblApiKey.Location = new System.Drawing.Point(16, 147);
            this.lblApiKey.Name = "lblApiKey";
            this.lblApiKey.Size = new System.Drawing.Size(54, 17);
            this.lblApiKey.TabIndex = 6;
            this.lblApiKey.Text = "API Key";
            // 
            // txtApiKey
            // 
            this.txtApiKey.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.txtApiKey.Font = new System.Drawing.Font("Consolas", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.txtApiKey.Location = new System.Drawing.Point(110, 143);
            this.txtApiKey.Name = "txtApiKey";
            this.txtApiKey.Size = new System.Drawing.Size(410, 22);
            this.txtApiKey.TabIndex = 7;
            this.txtApiKey.UseSystemPasswordChar = true;
            // 
            // chkShowKey
            // 
            this.chkShowKey.AutoSize = true;
            this.chkShowKey.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.chkShowKey.Location = new System.Drawing.Point(530, 144);
            this.chkShowKey.Name = "chkShowKey";
            this.chkShowKey.Size = new System.Drawing.Size(51, 21);
            this.chkShowKey.TabIndex = 8;
            this.chkShowKey.Text = "显示";
            this.chkShowKey.UseVisualStyleBackColor = true;
            this.chkShowKey.CheckedChanged += new System.EventHandler(this.ChkShowKey_CheckedChanged);
            // 
            // lblTimeout
            // 
            this.lblTimeout.AutoSize = true;
            this.lblTimeout.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblTimeout.Location = new System.Drawing.Point(16, 185);
            this.lblTimeout.Name = "lblTimeout";
            this.lblTimeout.Size = new System.Drawing.Size(62, 17);
            this.lblTimeout.TabIndex = 9;
            this.lblTimeout.Text = "超时（秒）";
            // 
            // numTimeout
            // 
            this.numTimeout.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.numTimeout.Location = new System.Drawing.Point(110, 181);
            this.numTimeout.Maximum = new decimal(new int[] { 1800, 0, 0, 0 });
            this.numTimeout.Minimum = new decimal(new int[] { 10, 0, 0, 0 });
            this.numTimeout.Name = "numTimeout";
            this.numTimeout.Size = new System.Drawing.Size(100, 23);
            this.numTimeout.TabIndex = 10;
            this.numTimeout.Value = new decimal(new int[] { 180, 0, 0, 0 });
            // 
            // lblNote
            // 
            this.lblNote.AutoSize = false;
            this.lblNote.Font = new System.Drawing.Font("微软雅黑", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblNote.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(108)))), ((int)(((byte)(117)))), ((int)(((byte)(125)))));
            this.lblNote.Location = new System.Drawing.Point(18, 212);
            this.lblNote.Name = "lblNote";
            this.lblNote.Size = new System.Drawing.Size(588, 36);
            this.lblNote.TabIndex = 11;
            this.lblNote.Text = "提示：保存后立即生效，无需重启程序。环境变量 MSIF_AI_API_KEY / MSIF_AI_BASE_URL / MSIF_AI_MODEL / MSIF_AI_TIMEOUT_SECONDS 的优先级高于本画面。";
            // 
            // lblStatus
            // 
            this.lblStatus.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblStatus.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.lblStatus.Location = new System.Drawing.Point(20, 336);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(624, 20);
            this.lblStatus.TabIndex = 3;
            this.lblStatus.Text = "就绪";
            // 
            // btnTest
            // 
            this.btnTest.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(232)))), ((int)(((byte)(237)))), ((int)(((byte)(243)))));
            this.btnTest.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnTest.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(199)))), ((int)(((byte)(208)))), ((int)(((byte)(219)))));
            this.btnTest.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(217)))), ((int)(((byte)(226)))), ((int)(((byte)(236)))));
            this.btnTest.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnTest.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnTest.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.btnTest.Location = new System.Drawing.Point(20, 366);
            this.btnTest.Name = "btnTest";
            this.btnTest.Size = new System.Drawing.Size(120, 38);
            this.btnTest.TabIndex = 4;
            this.btnTest.Text = "测试连接";
            this.btnTest.UseVisualStyleBackColor = false;
            this.btnTest.Click += new System.EventHandler(this.BtnTest_Click);
            // 
            // btnOpenFolder
            // 
            this.btnOpenFolder.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(232)))), ((int)(((byte)(237)))), ((int)(((byte)(243)))));
            this.btnOpenFolder.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnOpenFolder.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(199)))), ((int)(((byte)(208)))), ((int)(((byte)(219)))));
            this.btnOpenFolder.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(217)))), ((int)(((byte)(226)))), ((int)(((byte)(236)))));
            this.btnOpenFolder.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnOpenFolder.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnOpenFolder.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.btnOpenFolder.Location = new System.Drawing.Point(150, 366);
            this.btnOpenFolder.Name = "btnOpenFolder";
            this.btnOpenFolder.Size = new System.Drawing.Size(140, 38);
            this.btnOpenFolder.TabIndex = 5;
            this.btnOpenFolder.Text = "打开配置目录";
            this.btnOpenFolder.UseVisualStyleBackColor = false;
            this.btnOpenFolder.Click += new System.EventHandler(this.BtnOpenFolder_Click);
            // 
            // btnSave
            // 
            this.btnSave.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(21)))), ((int)(((byte)(101)))), ((int)(((byte)(192)))));
            this.btnSave.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnSave.FlatAppearance.BorderSize = 0;
            this.btnSave.FlatAppearance.MouseDownBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(13)))), ((int)(((byte)(71)))), ((int)(((byte)(161)))));
            this.btnSave.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(136)))), ((int)(((byte)(229)))));
            this.btnSave.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnSave.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnSave.ForeColor = System.Drawing.Color.White;
            this.btnSave.Location = new System.Drawing.Point(410, 366);
            this.btnSave.Name = "btnSave";
            this.btnSave.Size = new System.Drawing.Size(110, 38);
            this.btnSave.TabIndex = 6;
            this.btnSave.Text = "保存";
            this.btnSave.UseVisualStyleBackColor = false;
            this.btnSave.Click += new System.EventHandler(this.BtnSave_Click);
            // 
            // btnCancel
            // 
            this.btnCancel.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(240)))), ((int)(((byte)(242)))), ((int)(((byte)(245)))));
            this.btnCancel.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(199)))), ((int)(((byte)(208)))), ((int)(((byte)(219)))));
            this.btnCancel.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(226)))), ((int)(((byte)(232)))), ((int)(((byte)(240)))));
            this.btnCancel.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnCancel.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnCancel.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.btnCancel.Location = new System.Drawing.Point(530, 366);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(110, 38);
            this.btnCancel.TabIndex = 7;
            this.btnCancel.Text = "取消";
            this.btnCancel.UseVisualStyleBackColor = false;
            this.btnCancel.Click += new System.EventHandler(this.BtnCancel_Click);
            // 
            // AiSettingsForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(242)))), ((int)(((byte)(244)))), ((int)(((byte)(247)))));
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(660, 424);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnSave);
            this.Controls.Add(this.btnOpenFolder);
            this.Controls.Add(this.btnTest);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.grpConnection);
            this.Controls.Add(this.lblSettingsPath);
            this.Controls.Add(this.lblTitle);
            this.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "AiSettingsForm";
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "AI 接口设置";
            this.grpConnection.ResumeLayout(false);
            this.grpConnection.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numTimeout)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.Label lblSettingsPath;
        private System.Windows.Forms.GroupBox grpConnection;
        private System.Windows.Forms.Label lblEndpointPreset;
        private System.Windows.Forms.ComboBox cboEndpointPreset;
        private System.Windows.Forms.Label lblBaseUrl;
        private System.Windows.Forms.TextBox txtBaseUrl;
        private System.Windows.Forms.Label lblModel;
        private System.Windows.Forms.ComboBox cboModel;
        private System.Windows.Forms.Label lblApiKey;
        private System.Windows.Forms.TextBox txtApiKey;
        private System.Windows.Forms.CheckBox chkShowKey;
        private System.Windows.Forms.Label lblTimeout;
        private System.Windows.Forms.NumericUpDown numTimeout;
        private System.Windows.Forms.Label lblNote;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.Button btnTest;
        private System.Windows.Forms.Button btnOpenFolder;
        private System.Windows.Forms.Button btnSave;
        private System.Windows.Forms.Button btnCancel;
    }
}
