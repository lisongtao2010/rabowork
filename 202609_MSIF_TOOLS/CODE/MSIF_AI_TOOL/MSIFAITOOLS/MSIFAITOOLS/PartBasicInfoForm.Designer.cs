namespace MSIFAITOOLS
{
    partial class PartBasicInfoForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }

            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.pnlHeader = new System.Windows.Forms.Panel();
            this.lblDrawingNo = new System.Windows.Forms.Label();
            this.lblPdfPath = new System.Windows.Forms.Label();
            this.pnlSummary = new System.Windows.Forms.Panel();
            this.lblDatabase = new System.Windows.Forms.Label();
            this.lblSummary = new System.Windows.Forms.Label();
            this.lblEmpty = new System.Windows.Forms.Label();
            this.dgvParts = new System.Windows.Forms.DataGridView();
            this.pnlFooter = new System.Windows.Forms.Panel();
            this.btnAddRow = new System.Windows.Forms.Button();
            this.btnDeleteRow = new System.Windows.Forms.Button();
            this.btnSave = new System.Windows.Forms.Button();
            this.btnRefresh = new System.Windows.Forms.Button();
            this.btnPreviewPdf = new System.Windows.Forms.Button();
            this.btnClose = new System.Windows.Forms.Button();
            this.pnlHeader.SuspendLayout();
            this.pnlSummary.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dgvParts)).BeginInit();
            this.pnlFooter.SuspendLayout();
            this.SuspendLayout();
            //
            // pnlHeader
            //
            this.pnlHeader.BackColor = System.Drawing.Color.White;
            this.pnlHeader.Controls.Add(this.lblDrawingNo);
            this.pnlHeader.Controls.Add(this.lblPdfPath);
            this.pnlHeader.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnlHeader.Location = new System.Drawing.Point(0, 0);
            this.pnlHeader.Name = "pnlHeader";
            this.pnlHeader.Size = new System.Drawing.Size(980, 74);
            this.pnlHeader.TabIndex = 4;
            //
            // lblDrawingNo
            //
            this.lblDrawingNo.AutoEllipsis = true;
            this.lblDrawingNo.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Bold,
                System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblDrawingNo.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(44)))), ((int)(((byte)(62)))), ((int)(((byte)(80)))));
            this.lblDrawingNo.Location = new System.Drawing.Point(18, 10);
            this.lblDrawingNo.Name = "lblDrawingNo";
            this.lblDrawingNo.Size = new System.Drawing.Size(944, 28);
            this.lblDrawingNo.TabIndex = 0;
            this.lblDrawingNo.Text = "図番：-";
            //
            // lblPdfPath
            //
            this.lblPdfPath.AutoEllipsis = true;
            this.lblPdfPath.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular,
                System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblPdfPath.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(127)))), ((int)(((byte)(140)))), ((int)(((byte)(141)))));
            this.lblPdfPath.Location = new System.Drawing.Point(20, 42);
            this.lblPdfPath.Name = "lblPdfPath";
            this.lblPdfPath.Size = new System.Drawing.Size(942, 20);
            this.lblPdfPath.TabIndex = 1;
            this.lblPdfPath.Text = "PDF：-";
            //
            // pnlSummary
            //
            this.pnlSummary.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(242)))), ((int)(((byte)(244)))), ((int)(((byte)(247)))));
            this.pnlSummary.Controls.Add(this.lblDatabase);
            this.pnlSummary.Controls.Add(this.lblSummary);
            this.pnlSummary.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnlSummary.Location = new System.Drawing.Point(0, 74);
            this.pnlSummary.Name = "pnlSummary";
            this.pnlSummary.Size = new System.Drawing.Size(980, 42);
            this.pnlSummary.TabIndex = 3;
            //
            // lblDatabase
            //
            this.lblDatabase.AutoEllipsis = true;
            this.lblDatabase.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular,
                System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblDatabase.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.lblDatabase.Location = new System.Drawing.Point(16, 4);
            this.lblDatabase.Name = "lblDatabase";
            this.lblDatabase.Size = new System.Drawing.Size(600, 34);
            this.lblDatabase.TabIndex = 0;
            this.lblDatabase.Text = "数据库：-";
            this.lblDatabase.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // lblSummary
            //
            this.lblSummary.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.lblSummary.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular,
                System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblSummary.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.lblSummary.Location = new System.Drawing.Point(624, 4);
            this.lblSummary.Name = "lblSummary";
            this.lblSummary.Size = new System.Drawing.Size(340, 34);
            this.lblSummary.TabIndex = 1;
            this.lblSummary.Text = "共 0 条";
            this.lblSummary.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // lblEmpty
            //
            this.lblEmpty.BackColor = System.Drawing.Color.White;
            this.lblEmpty.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblEmpty.Font = new System.Drawing.Font("微软雅黑", 10F, System.Drawing.FontStyle.Regular,
                System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.lblEmpty.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(149)))), ((int)(((byte)(165)))), ((int)(((byte)(166)))));
            this.lblEmpty.Location = new System.Drawing.Point(0, 116);
            this.lblEmpty.Name = "lblEmpty";
            this.lblEmpty.Size = new System.Drawing.Size(980, 452);
            this.lblEmpty.TabIndex = 0;
            this.lblEmpty.Text = "该図番尚未登录部品基本信息，请先执行【AI 分析】。";
            this.lblEmpty.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.lblEmpty.Visible = false;
            //
            // dgvParts
            //
            this.dgvParts.AllowUserToAddRows = false;
            this.dgvParts.AllowUserToDeleteRows = false;
            this.dgvParts.AllowUserToResizeRows = false;
            this.dgvParts.BackgroundColor = System.Drawing.Color.White;
            this.dgvParts.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.dgvParts.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            this.dgvParts.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dgvParts.EditMode = System.Windows.Forms.DataGridViewEditMode.EditOnKeystrokeOrF2;
            this.dgvParts.Location = new System.Drawing.Point(0, 116);
            this.dgvParts.MultiSelect = true;
            this.dgvParts.Name = "dgvParts";
            this.dgvParts.ReadOnly = false;
            this.dgvParts.RowHeadersVisible = false;
            this.dgvParts.RowTemplate.Height = 24;
            this.dgvParts.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dgvParts.Size = new System.Drawing.Size(980, 452);
            this.dgvParts.TabIndex = 1;
            this.dgvParts.CellValueChanged += new System.Windows.Forms.DataGridViewCellEventHandler(this.DgvParts_CellValueChanged);
            this.dgvParts.KeyDown += new System.Windows.Forms.KeyEventHandler(this.DgvParts_KeyDown);
            //
            // pnlFooter
            //
            this.pnlFooter.BackColor = System.Drawing.Color.White;
            this.pnlFooter.Controls.Add(this.btnRefresh);
            this.pnlFooter.Controls.Add(this.btnAddRow);
            this.pnlFooter.Controls.Add(this.btnDeleteRow);
            this.pnlFooter.Controls.Add(this.btnSave);
            this.pnlFooter.Controls.Add(this.btnPreviewPdf);
            this.pnlFooter.Controls.Add(this.btnClose);
            this.pnlFooter.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.pnlFooter.Location = new System.Drawing.Point(0, 568);
            this.pnlFooter.Name = "pnlFooter";
            this.pnlFooter.Size = new System.Drawing.Size(980, 52);
            this.pnlFooter.TabIndex = 2;
            //
            // btnRefresh
            //
            this.btnRefresh.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnRefresh.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(232)))), ((int)(((byte)(237)))), ((int)(((byte)(243)))));
            this.btnRefresh.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnRefresh.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(199)))), ((int)(((byte)(208)))), ((int)(((byte)(219)))));
            this.btnRefresh.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(217)))), ((int)(((byte)(226)))), ((int)(((byte)(236)))));
            this.btnRefresh.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnRefresh.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular,
                System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnRefresh.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.btnRefresh.Location = new System.Drawing.Point(384, 11);
            this.btnRefresh.Name = "btnRefresh";
            this.btnRefresh.Size = new System.Drawing.Size(96, 30);
            this.btnRefresh.TabIndex = 0;
            this.btnRefresh.Text = "刷新";
            this.btnRefresh.UseVisualStyleBackColor = false;
            this.btnRefresh.Click += new System.EventHandler(this.BtnRefresh_Click);
            //
            // btnAddRow
            //
            this.btnAddRow.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnAddRow.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(232)))), ((int)(((byte)(237)))), ((int)(((byte)(243)))));
            this.btnAddRow.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnAddRow.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(199)))), ((int)(((byte)(208)))), ((int)(((byte)(219)))));
            this.btnAddRow.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(217)))), ((int)(((byte)(226)))), ((int)(((byte)(236)))));
            this.btnAddRow.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnAddRow.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular,
                System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnAddRow.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.btnAddRow.Location = new System.Drawing.Point(488, 11);
            this.btnAddRow.Name = "btnAddRow";
            this.btnAddRow.Size = new System.Drawing.Size(80, 30);
            this.btnAddRow.TabIndex = 1;
            this.btnAddRow.Text = "追加行";
            this.btnAddRow.UseVisualStyleBackColor = false;
            this.btnAddRow.Click += new System.EventHandler(this.BtnAddRow_Click);
            //
            // btnDeleteRow
            //
            this.btnDeleteRow.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnDeleteRow.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(232)))), ((int)(((byte)(237)))), ((int)(((byte)(243)))));
            this.btnDeleteRow.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnDeleteRow.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(199)))), ((int)(((byte)(208)))), ((int)(((byte)(219)))));
            this.btnDeleteRow.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(217)))), ((int)(((byte)(226)))), ((int)(((byte)(236)))));
            this.btnDeleteRow.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnDeleteRow.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular,
                System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnDeleteRow.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.btnDeleteRow.Location = new System.Drawing.Point(576, 11);
            this.btnDeleteRow.Name = "btnDeleteRow";
            this.btnDeleteRow.Size = new System.Drawing.Size(80, 30);
            this.btnDeleteRow.TabIndex = 2;
            this.btnDeleteRow.Text = "删除行";
            this.btnDeleteRow.UseVisualStyleBackColor = false;
            this.btnDeleteRow.Click += new System.EventHandler(this.BtnDeleteRow_Click);
            //
            // btnSave
            //
            this.btnSave.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnSave.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(46)))), ((int)(((byte)(125)))), ((int)(((byte)(50)))));
            this.btnSave.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnSave.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(46)))), ((int)(((byte)(125)))), ((int)(((byte)(50)))));
            this.btnSave.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(67)))), ((int)(((byte)(160)))), ((int)(((byte)(71)))));
            this.btnSave.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnSave.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Bold,
                System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnSave.ForeColor = System.Drawing.Color.White;
            this.btnSave.Location = new System.Drawing.Point(664, 11);
            this.btnSave.Name = "btnSave";
            this.btnSave.Size = new System.Drawing.Size(88, 30);
            this.btnSave.TabIndex = 3;
            this.btnSave.Text = "保存";
            this.btnSave.UseVisualStyleBackColor = false;
            this.btnSave.Click += new System.EventHandler(this.BtnSave_Click);
            //
            // btnPreviewPdf
            //
            this.btnPreviewPdf.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnPreviewPdf.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(232)))), ((int)(((byte)(237)))), ((int)(((byte)(243)))));
            this.btnPreviewPdf.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnPreviewPdf.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(199)))), ((int)(((byte)(208)))), ((int)(((byte)(219)))));
            this.btnPreviewPdf.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(217)))), ((int)(((byte)(226)))), ((int)(((byte)(236)))));
            this.btnPreviewPdf.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnPreviewPdf.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular,
                System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnPreviewPdf.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.btnPreviewPdf.Location = new System.Drawing.Point(760, 11);
            this.btnPreviewPdf.Name = "btnPreviewPdf";
            this.btnPreviewPdf.Size = new System.Drawing.Size(100, 30);
            this.btnPreviewPdf.TabIndex = 4;
            this.btnPreviewPdf.Text = "预览 PDF";
            this.btnPreviewPdf.UseVisualStyleBackColor = false;
            this.btnPreviewPdf.Click += new System.EventHandler(this.BtnPreviewPdf_Click);
            //
            // btnClose
            //
            this.btnClose.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnClose.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(232)))), ((int)(((byte)(237)))), ((int)(((byte)(243)))));
            this.btnClose.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnClose.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnClose.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(199)))), ((int)(((byte)(208)))), ((int)(((byte)(219)))));
            this.btnClose.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(217)))), ((int)(((byte)(226)))), ((int)(((byte)(236)))));
            this.btnClose.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnClose.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular,
                System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.btnClose.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(52)))), ((int)(((byte)(73)))), ((int)(((byte)(94)))));
            this.btnClose.Location = new System.Drawing.Point(868, 11);
            this.btnClose.Name = "btnClose";
            this.btnClose.Size = new System.Drawing.Size(96, 30);
            this.btnClose.TabIndex = 5;
            this.btnClose.Text = "关闭";
            this.btnClose.UseVisualStyleBackColor = false;
            this.btnClose.Click += new System.EventHandler(this.BtnClose_Click);
            //
            // PartBasicInfoForm
            //
            this.BackColor = System.Drawing.Color.White;
            this.CancelButton = this.btnClose;
            this.ClientSize = new System.Drawing.Size(980, 620);
            this.Controls.Add(this.lblEmpty);
            this.Controls.Add(this.dgvParts);
            this.Controls.Add(this.pnlSummary);
            this.Controls.Add(this.pnlHeader);
            this.Controls.Add(this.pnlFooter);
            this.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular,
                System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.KeyPreview = true;
            this.MinimumSize = new System.Drawing.Size(860, 520);
            this.Name = "PartBasicInfoForm";
            this.ShowIcon = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "部品基本信息";
            this.KeyDown += new System.Windows.Forms.KeyEventHandler(this.PartBasicInfoForm_KeyDown);
            this.pnlHeader.ResumeLayout(false);
            this.pnlSummary.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.dgvParts)).EndInit();
            this.pnlFooter.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.Panel pnlHeader;
        private System.Windows.Forms.Label lblDrawingNo;
        private System.Windows.Forms.Label lblPdfPath;
        private System.Windows.Forms.Panel pnlSummary;
        private System.Windows.Forms.Label lblDatabase;
        private System.Windows.Forms.Label lblSummary;
        private System.Windows.Forms.Label lblEmpty;
        private System.Windows.Forms.DataGridView dgvParts;
        private System.Windows.Forms.Panel pnlFooter;
        private System.Windows.Forms.Button btnAddRow;
        private System.Windows.Forms.Button btnDeleteRow;
        private System.Windows.Forms.Button btnSave;
        private System.Windows.Forms.Button btnRefresh;
        private System.Windows.Forms.Button btnPreviewPdf;
        private System.Windows.Forms.Button btnClose;
    }
}
