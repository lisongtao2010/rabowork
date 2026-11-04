namespace CadPartSplitter
{
    partial class PdfInfoEditorForm
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
            this.panelTop = new System.Windows.Forms.Panel();
            this.lblListPath = new System.Windows.Forms.Label();
            this.txtListPath = new System.Windows.Forms.TextBox();
            this.btnBrowse = new System.Windows.Forms.Button();
            this.btnReload = new System.Windows.Forms.Button();
            this.lblHint = new System.Windows.Forms.Label();
            this.grid = new System.Windows.Forms.DataGridView();
            this.colNumber = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colFile = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colCategory = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colBasis = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.panelBottom = new System.Windows.Forms.Panel();
            this.lblCategory = new System.Windows.Forms.Label();
            this.cmbCategory = new System.Windows.Forms.ComboBox();
            this.btnApplyCategory = new System.Windows.Forms.Button();
            this.btnAutoSelected = new System.Windows.Forms.Button();
            this.btnAutoEmpty = new System.Windows.Forms.Button();
            this.btnAddRow = new System.Windows.Forms.Button();
            this.btnDeleteRow = new System.Windows.Forms.Button();
            this.btnSave = new System.Windows.Forms.Button();
            this.btnClose = new System.Windows.Forms.Button();
            this.lblStatus = new System.Windows.Forms.Label();
            this.panelTop.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.grid)).BeginInit();
            this.panelBottom.SuspendLayout();
            this.SuspendLayout();
            // 
            // panelTop
            // 
            this.panelTop.Controls.Add(this.lblHint);
            this.panelTop.Controls.Add(this.btnReload);
            this.panelTop.Controls.Add(this.btnBrowse);
            this.panelTop.Controls.Add(this.txtListPath);
            this.panelTop.Controls.Add(this.lblListPath);
            this.panelTop.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelTop.Location = new System.Drawing.Point(0, 0);
            this.panelTop.Name = "panelTop";
            this.panelTop.Size = new System.Drawing.Size(1004, 62);
            this.panelTop.TabIndex = 0;
            // 
            // lblListPath
            // 
            this.lblListPath.Location = new System.Drawing.Point(10, 14);
            this.lblListPath.Name = "lblListPath";
            this.lblListPath.Size = new System.Drawing.Size(72, 17);
            this.lblListPath.TabIndex = 0;
            this.lblListPath.Text = "清单文件：";
            // 
            // txtListPath
            // 
            this.txtListPath.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtListPath.Location = new System.Drawing.Point(86, 11);
            this.txtListPath.Name = "txtListPath";
            this.txtListPath.Size = new System.Drawing.Size(690, 23);
            this.txtListPath.TabIndex = 1;
            // 
            // btnBrowse
            // 
            this.btnBrowse.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnBrowse.Location = new System.Drawing.Point(788, 9);
            this.btnBrowse.Name = "btnBrowse";
            this.btnBrowse.Size = new System.Drawing.Size(96, 27);
            this.btnBrowse.TabIndex = 2;
            this.btnBrowse.Text = "浏览…";
            this.btnBrowse.UseVisualStyleBackColor = true;
            // 
            // btnReload
            // 
            this.btnReload.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnReload.Location = new System.Drawing.Point(892, 9);
            this.btnReload.Name = "btnReload";
            this.btnReload.Size = new System.Drawing.Size(96, 27);
            this.btnReload.TabIndex = 3;
            this.btnReload.Text = "重新载入";
            this.btnReload.UseVisualStyleBackColor = true;
            // 
            // lblHint
            // 
            this.lblHint.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lblHint.AutoEllipsis = true;
            this.lblHint.ForeColor = System.Drawing.Color.DimGray;
            this.lblHint.Location = new System.Drawing.Point(10, 40);
            this.lblHint.Name = "lblHint";
            this.lblHint.Size = new System.Drawing.Size(978, 17);
            this.lblHint.TabIndex = 4;
            this.lblHint.Text = "自动判定判不出来时，在「图面分类」列手工填 / 选一个；手工填过的行（分类依据记作「手工设定」）重跑分割不会被覆盖。";
            //
            // grid
            //
            this.grid.AllowUserToAddRows = false;
            this.grid.AllowUserToDeleteRows = false;
            this.grid.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.grid.BackgroundColor = System.Drawing.SystemColors.Window;
            this.grid.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.grid.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.grid.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.colNumber,
            this.colFile,
            this.colCategory,
            this.colBasis});
            this.grid.Dock = System.Windows.Forms.DockStyle.Fill;
            this.grid.EditMode = System.Windows.Forms.DataGridViewEditMode.EditOnKeystrokeOrF2;
            this.grid.Location = new System.Drawing.Point(0, 62);
            this.grid.MultiSelect = true;
            this.grid.Name = "grid";
            this.grid.RowHeadersWidth = 28;
            this.grid.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.CellSelect;
            this.grid.Size = new System.Drawing.Size(1004, 450);
            this.grid.TabIndex = 1;
            //
            // colNumber
            //
            this.colNumber.FillWeight = 16F;
            this.colNumber.HeaderText = "図番号";
            this.colNumber.Name = "colNumber";
            this.colNumber.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            this.colNumber.ToolTipText = "PDF 文件名去掉扩展名，例如 H310U10004";
            //
            // colFile
            //
            this.colFile.FillWeight = 18F;
            this.colFile.HeaderText = "文件名";
            this.colFile.Name = "colFile";
            this.colFile.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            this.colFile.ToolTipText = "PDF 文件名（带扩展名）；留空保存时自动补成 図番号.pdf";
            //
            // colCategory
            //
            this.colCategory.FillWeight = 14F;
            this.colCategory.HeaderText = "图面分类";
            this.colCategory.Name = "colCategory";
            this.colCategory.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            this.colCategory.ToolTipText = "基本図 / 加工図 / アッセンブリ図 / 要領図；判不出来就留空（下游按未识别处理）";
            //
            // colBasis
            //
            this.colBasis.FillWeight = 52F;
            this.colBasis.HeaderText = "分类依据";
            this.colBasis.Name = "colBasis";
            this.colBasis.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            this.colBasis.ToolTipText = "自动判定命中的表格 / 单元格；手工设定的记作「手工设定」";
            //
            // panelBottom
            //
            this.panelBottom.Controls.Add(this.lblStatus);
            this.panelBottom.Controls.Add(this.btnClose);
            this.panelBottom.Controls.Add(this.btnSave);
            this.panelBottom.Controls.Add(this.btnDeleteRow);
            this.panelBottom.Controls.Add(this.btnAddRow);
            this.panelBottom.Controls.Add(this.btnAutoEmpty);
            this.panelBottom.Controls.Add(this.btnAutoSelected);
            this.panelBottom.Controls.Add(this.btnApplyCategory);
            this.panelBottom.Controls.Add(this.cmbCategory);
            this.panelBottom.Controls.Add(this.lblCategory);
            this.panelBottom.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panelBottom.Location = new System.Drawing.Point(0, 512);
            this.panelBottom.Name = "panelBottom";
            this.panelBottom.Size = new System.Drawing.Size(1004, 92);
            this.panelBottom.TabIndex = 2;
            //
            // lblCategory
            //
            this.lblCategory.Location = new System.Drawing.Point(10, 13);
            this.lblCategory.Name = "lblCategory";
            this.lblCategory.Size = new System.Drawing.Size(44, 17);
            this.lblCategory.TabIndex = 0;
            this.lblCategory.Text = "分类：";
            //
            // cmbCategory
            //
            this.cmbCategory.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDown;
            this.cmbCategory.Location = new System.Drawing.Point(58, 9);
            this.cmbCategory.Name = "cmbCategory";
            this.cmbCategory.Size = new System.Drawing.Size(150, 25);
            this.cmbCategory.TabIndex = 1;
            //
            // btnApplyCategory
            //
            this.btnApplyCategory.Location = new System.Drawing.Point(216, 8);
            this.btnApplyCategory.Name = "btnApplyCategory";
            this.btnApplyCategory.Size = new System.Drawing.Size(112, 27);
            this.btnApplyCategory.TabIndex = 2;
            this.btnApplyCategory.Text = "填入选中行";
            this.btnApplyCategory.UseVisualStyleBackColor = true;
            //
            // btnAutoSelected
            //
            this.btnAutoSelected.Location = new System.Drawing.Point(336, 8);
            this.btnAutoSelected.Name = "btnAutoSelected";
            this.btnAutoSelected.Size = new System.Drawing.Size(112, 27);
            this.btnAutoSelected.TabIndex = 3;
            this.btnAutoSelected.Text = "判定选中行";
            this.btnAutoSelected.UseVisualStyleBackColor = true;
            //
            // btnAutoEmpty
            //
            this.btnAutoEmpty.Location = new System.Drawing.Point(456, 8);
            this.btnAutoEmpty.Name = "btnAutoEmpty";
            this.btnAutoEmpty.Size = new System.Drawing.Size(112, 27);
            this.btnAutoEmpty.TabIndex = 4;
            this.btnAutoEmpty.Text = "判定空白行";
            this.btnAutoEmpty.UseVisualStyleBackColor = true;
            //
            // btnAddRow
            //
            this.btnAddRow.Location = new System.Drawing.Point(576, 8);
            this.btnAddRow.Name = "btnAddRow";
            this.btnAddRow.Size = new System.Drawing.Size(88, 27);
            this.btnAddRow.TabIndex = 5;
            this.btnAddRow.Text = "添加行";
            this.btnAddRow.UseVisualStyleBackColor = true;
            //
            // btnDeleteRow
            //
            this.btnDeleteRow.Location = new System.Drawing.Point(672, 8);
            this.btnDeleteRow.Name = "btnDeleteRow";
            this.btnDeleteRow.Size = new System.Drawing.Size(88, 27);
            this.btnDeleteRow.TabIndex = 6;
            this.btnDeleteRow.Text = "删除行";
            this.btnDeleteRow.UseVisualStyleBackColor = true;
            //
            // btnSave
            //
            this.btnSave.Location = new System.Drawing.Point(10, 48);
            this.btnSave.Name = "btnSave";
            this.btnSave.Size = new System.Drawing.Size(150, 34);
            this.btnSave.TabIndex = 7;
            this.btnSave.Text = "保存";
            this.btnSave.UseVisualStyleBackColor = true;
            //
            // btnClose
            //
            this.btnClose.Location = new System.Drawing.Point(170, 48);
            this.btnClose.Name = "btnClose";
            this.btnClose.Size = new System.Drawing.Size(150, 34);
            this.btnClose.TabIndex = 8;
            this.btnClose.Text = "关闭";
            this.btnClose.UseVisualStyleBackColor = true;
            //
            // lblStatus
            //
            this.lblStatus.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lblStatus.AutoEllipsis = true;
            this.lblStatus.Location = new System.Drawing.Point(334, 56);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(656, 20);
            this.lblStatus.TabIndex = 9;
            this.lblStatus.Text = "就绪";
            this.lblStatus.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // PdfInfoEditorForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.ClientSize = new System.Drawing.Size(1004, 604);
            this.Controls.Add(this.grid);
            this.Controls.Add(this.panelBottom);
            this.Controls.Add(this.panelTop);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.MinimumSize = new System.Drawing.Size(880, 520);
            this.Name = "PdfInfoEditorForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "PDF 信息清单维护（图面分类）";
            this.panelTop.ResumeLayout(false);
            this.panelTop.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.grid)).EndInit();
            this.panelBottom.ResumeLayout(false);
            this.panelBottom.PerformLayout();
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.Panel panelTop;
        private System.Windows.Forms.Label lblListPath;
        private System.Windows.Forms.TextBox txtListPath;
        private System.Windows.Forms.Button btnBrowse;
        private System.Windows.Forms.Button btnReload;
        private System.Windows.Forms.Label lblHint;
        private System.Windows.Forms.DataGridView grid;
        private System.Windows.Forms.DataGridViewTextBoxColumn colNumber;
        private System.Windows.Forms.DataGridViewTextBoxColumn colFile;
        private System.Windows.Forms.DataGridViewTextBoxColumn colCategory;
        private System.Windows.Forms.DataGridViewTextBoxColumn colBasis;
        private System.Windows.Forms.Panel panelBottom;
        private System.Windows.Forms.Label lblCategory;
        private System.Windows.Forms.ComboBox cmbCategory;
        private System.Windows.Forms.Button btnApplyCategory;
        private System.Windows.Forms.Button btnAutoSelected;
        private System.Windows.Forms.Button btnAutoEmpty;
        private System.Windows.Forms.Button btnAddRow;
        private System.Windows.Forms.Button btnDeleteRow;
        private System.Windows.Forms.Button btnSave;
        private System.Windows.Forms.Button btnClose;
        private System.Windows.Forms.Label lblStatus;
    }
}
