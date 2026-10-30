namespace MSIFAITOOLS
{
    partial class PdfPreviewForm
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
            this.pnlToolbar = new System.Windows.Forms.Panel();
            this.btnPrev = new System.Windows.Forms.Button();
            this.btnNext = new System.Windows.Forms.Button();
            this.lblPageInfo = new System.Windows.Forms.Label();
            this.btnZoomOut = new System.Windows.Forms.Button();
            this.btnZoomIn = new System.Windows.Forms.Button();
            this.lblZoom = new System.Windows.Forms.Label();
            this.btnFitWidth = new System.Windows.Forms.Button();
            this.btnFitPage = new System.Windows.Forms.Button();
            this.btnActualSize = new System.Windows.Forms.Button();
            this.btnPan = new System.Windows.Forms.Button();
            this.btnOpenExternal = new System.Windows.Forms.Button();
            this.btnClose = new System.Windows.Forms.Button();
            this.lblStatus = new System.Windows.Forms.Label();
            this.pnlCanvas = new System.Windows.Forms.Panel();
            this.picPage = new System.Windows.Forms.PictureBox();
            this.resizeTimer = new System.Windows.Forms.Timer(this.components);
            this.pnlToolbar.SuspendLayout();
            this.pnlCanvas.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.picPage)).BeginInit();
            this.SuspendLayout();
            // 
            // pnlToolbar
            // 
            this.pnlToolbar.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(242)))), ((int)(((byte)(244)))), ((int)(((byte)(247)))));
            this.pnlToolbar.Controls.Add(this.btnPrev);
            this.pnlToolbar.Controls.Add(this.btnNext);
            this.pnlToolbar.Controls.Add(this.lblPageInfo);
            this.pnlToolbar.Controls.Add(this.btnZoomOut);
            this.pnlToolbar.Controls.Add(this.btnZoomIn);
            this.pnlToolbar.Controls.Add(this.lblZoom);
            this.pnlToolbar.Controls.Add(this.btnFitWidth);
            this.pnlToolbar.Controls.Add(this.btnFitPage);
            this.pnlToolbar.Controls.Add(this.btnActualSize);
            this.pnlToolbar.Controls.Add(this.btnPan);
            this.pnlToolbar.Controls.Add(this.btnOpenExternal);
            this.pnlToolbar.Controls.Add(this.btnClose);
            this.pnlToolbar.Dock = System.Windows.Forms.DockStyle.Top;
            this.pnlToolbar.Location = new System.Drawing.Point(0, 0);
            this.pnlToolbar.Name = "pnlToolbar";
            this.pnlToolbar.Size = new System.Drawing.Size(1084, 46);
            this.pnlToolbar.TabIndex = 0;
            // 
            // btnPrev
            // 
            this.btnPrev.Location = new System.Drawing.Point(8, 9);
            this.btnPrev.Name = "btnPrev";
            this.btnPrev.Size = new System.Drawing.Size(90, 28);
            this.btnPrev.TabIndex = 0;
            this.btnPrev.Text = "◀ 上一页";
            this.btnPrev.Click += new System.EventHandler(this.BtnPrevPage_Click);
            // 
            // btnNext
            // 
            this.btnNext.Location = new System.Drawing.Point(104, 9);
            this.btnNext.Name = "btnNext";
            this.btnNext.Size = new System.Drawing.Size(90, 28);
            this.btnNext.TabIndex = 1;
            this.btnNext.Text = "下一页 ▶";
            this.btnNext.Click += new System.EventHandler(this.BtnNextPage_Click);
            // 
            // lblPageInfo
            // 
            this.lblPageInfo.Location = new System.Drawing.Point(200, 15);
            this.lblPageInfo.Name = "lblPageInfo";
            this.lblPageInfo.Size = new System.Drawing.Size(90, 17);
            this.lblPageInfo.TabIndex = 2;
            this.lblPageInfo.Text = "1 / 1";
            this.lblPageInfo.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // btnZoomOut
            // 
            this.btnZoomOut.Location = new System.Drawing.Point(296, 9);
            this.btnZoomOut.Name = "btnZoomOut";
            this.btnZoomOut.Size = new System.Drawing.Size(70, 28);
            this.btnZoomOut.TabIndex = 3;
            this.btnZoomOut.Text = "缩小 -";
            this.btnZoomOut.Click += new System.EventHandler(this.BtnZoomOut_Click);
            // 
            // btnZoomIn
            // 
            this.btnZoomIn.Location = new System.Drawing.Point(372, 9);
            this.btnZoomIn.Name = "btnZoomIn";
            this.btnZoomIn.Size = new System.Drawing.Size(70, 28);
            this.btnZoomIn.TabIndex = 4;
            this.btnZoomIn.Text = "放大 +";
            this.btnZoomIn.Click += new System.EventHandler(this.BtnZoomIn_Click);
            // 
            // lblZoom
            // 
            this.lblZoom.Location = new System.Drawing.Point(448, 15);
            this.lblZoom.Name = "lblZoom";
            this.lblZoom.Size = new System.Drawing.Size(56, 17);
            this.lblZoom.TabIndex = 5;
            this.lblZoom.Text = "100%";
            this.lblZoom.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // btnFitWidth
            // 
            this.btnFitWidth.Location = new System.Drawing.Point(508, 9);
            this.btnFitWidth.Name = "btnFitWidth";
            this.btnFitWidth.Size = new System.Drawing.Size(64, 28);
            this.btnFitWidth.TabIndex = 6;
            this.btnFitWidth.Text = "适合宽度";
            this.btnFitWidth.Click += new System.EventHandler(this.BtnFitWidth_Click);
            // 
            // btnFitPage
            // 
            this.btnFitPage.Location = new System.Drawing.Point(578, 9);
            this.btnFitPage.Name = "btnFitPage";
            this.btnFitPage.Size = new System.Drawing.Size(64, 28);
            this.btnFitPage.TabIndex = 7;
            this.btnFitPage.Text = "适合页面";
            this.btnFitPage.Click += new System.EventHandler(this.BtnFitPage_Click);
            // 
            // btnActualSize
            // 
            this.btnActualSize.Location = new System.Drawing.Point(648, 9);
            this.btnActualSize.Name = "btnActualSize";
            this.btnActualSize.Size = new System.Drawing.Size(64, 28);
            this.btnActualSize.TabIndex = 8;
            this.btnActualSize.Text = "实际大小";
            this.btnActualSize.Click += new System.EventHandler(this.BtnActualSize_Click);
            //
            // btnPan
            //
            this.btnPan.Location = new System.Drawing.Point(718, 9);
            this.btnPan.Name = "btnPan";
            this.btnPan.Size = new System.Drawing.Size(84, 28);
            this.btnPan.TabIndex = 9;
            this.btnPan.Text = "✋ 拖动";
            this.btnPan.Click += new System.EventHandler(this.BtnPan_Click);
            // 
            // btnOpenExternal
            // 
            this.btnOpenExternal.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnOpenExternal.Location = new System.Drawing.Point(840, 9);
            this.btnOpenExternal.Name = "btnOpenExternal";
            this.btnOpenExternal.Size = new System.Drawing.Size(110, 28);
            this.btnOpenExternal.TabIndex = 10;
            this.btnOpenExternal.Text = "外部程序打开";
            this.btnOpenExternal.Click += new System.EventHandler(this.BtnOpenExternal_Click);
            // 
            // btnClose
            // 
            this.btnClose.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnClose.Location = new System.Drawing.Point(956, 9);
            this.btnClose.Name = "btnClose";
            this.btnClose.Size = new System.Drawing.Size(100, 28);
            this.btnClose.TabIndex = 11;
            this.btnClose.Text = "关闭";
            this.btnClose.Click += new System.EventHandler(this.BtnClose_Click);
            // 
            // lblStatus
            // 
            this.lblStatus.BackColor = System.Drawing.Color.White;
            this.lblStatus.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.lblStatus.Location = new System.Drawing.Point(0, 662);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Padding = new System.Windows.Forms.Padding(10, 0, 0, 0);
            this.lblStatus.Size = new System.Drawing.Size(1084, 26);
            this.lblStatus.TabIndex = 2;
            this.lblStatus.Text = "尚未选择 PDF";
            this.lblStatus.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // pnlCanvas
            // 
            this.pnlCanvas.AutoScroll = true;
            this.pnlCanvas.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(228)))), ((int)(((byte)(232)))), ((int)(((byte)(237)))));
            this.pnlCanvas.Controls.Add(this.picPage);
            this.pnlCanvas.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pnlCanvas.Location = new System.Drawing.Point(0, 46);
            this.pnlCanvas.Name = "pnlCanvas";
            this.pnlCanvas.Size = new System.Drawing.Size(1084, 616);
            this.pnlCanvas.TabIndex = 1;
            this.pnlCanvas.Resize += new System.EventHandler(this.PnlCanvas_Resize);
            // 
            // picPage
            // 
            this.picPage.BackColor = System.Drawing.Color.White;
            this.picPage.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.picPage.Location = new System.Drawing.Point(12, 12);
            this.picPage.Name = "picPage";
            this.picPage.Size = new System.Drawing.Size(600, 800);
            this.picPage.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Normal;
            this.picPage.TabIndex = 0;
            this.picPage.TabStop = false;
            // 
            // resizeTimer
            // 
            this.resizeTimer.Interval = 180;
            this.resizeTimer.Tick += new System.EventHandler(this.ResizeTimer_Tick);
            // 
            // PdfPreviewForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(242)))), ((int)(((byte)(244)))), ((int)(((byte)(247)))));
            this.ClientSize = new System.Drawing.Size(1084, 688);
            this.Controls.Add(this.pnlCanvas);
            this.Controls.Add(this.pnlToolbar);
            this.Controls.Add(this.lblStatus);
            this.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.KeyPreview = true;
            this.MinimumSize = new System.Drawing.Size(760, 560);
            this.Name = "PdfPreviewForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "PDF 预览";
            this.pnlToolbar.ResumeLayout(false);
            this.pnlCanvas.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.picPage)).EndInit();
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.Panel pnlToolbar;
        private System.Windows.Forms.Button btnPrev;
        private System.Windows.Forms.Button btnNext;
        private System.Windows.Forms.Label lblPageInfo;
        private System.Windows.Forms.Button btnZoomOut;
        private System.Windows.Forms.Button btnZoomIn;
        private System.Windows.Forms.Label lblZoom;
        private System.Windows.Forms.Button btnFitWidth;
        private System.Windows.Forms.Button btnFitPage;
        private System.Windows.Forms.Button btnActualSize;
        private System.Windows.Forms.Button btnPan;
        private System.Windows.Forms.Button btnOpenExternal;
        private System.Windows.Forms.Button btnClose;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.Panel pnlCanvas;
        private System.Windows.Forms.PictureBox picPage;
        private System.Windows.Forms.Timer resizeTimer;
    }
}
