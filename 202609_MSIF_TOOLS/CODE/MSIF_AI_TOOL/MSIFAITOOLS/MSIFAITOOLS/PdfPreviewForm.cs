using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using MSIFAITOOLS.Services;
using PdfiumViewer;
using PdfPigDocument = UglyToad.PdfPig.PdfDocument;
using PdfPigPage = UglyToad.PdfPig.Content.Page;

namespace MSIFAITOOLS
{
    /// <summary>
    /// PDF 预览窗口（用 pdfium 渲染成位图）：在【分类结果】列表中选中一行时显示对应 PDF。
    /// 支持翻页、放大 / 缩小、适合宽度、适合页面、实际大小、外部程序打开。
    /// 缩放：Ctrl + 鼠标滚轮（以鼠标位置为中心）；平移：「✋ 拖动」按钮开启后用左键拖动画面，任意模式下按住鼠标中键也可拖动。
    /// 页面上可以像 PDF 阅读器一样拖拽选择文字（双击选词、Ctrl+A 全选、Ctrl+C 复制选中文字），
    /// 文字位置由 PdfPig 的字形坐标提供，与 pdfium 渲染的位图对齐。
    /// 本窗口只做显示，不会修改 PDF 文件。
    /// </summary>
    public partial class PdfPreviewForm : Form
    {
        /// <summary>缩放下限（相对 100% = 96 DPI 的实际大小）。</summary>
        private const float MinZoom = 0.1F;

        /// <summary>缩放上限。</summary>
        private const float MaxZoom = 6F;

        /// <summary>PDF 点（1/72 英寸）到 96 DPI 像素的换算比例，即「100% 显示」。</summary>
        private const float BaseScale = 96F / 72F;

        /// <summary>页面四周留白（像素）。</summary>
        private const int PageMargin = 12;

        /// <summary>单页渲染的最大像素数（防止极端放大占用过多内存）。</summary>
        private const long MaxRenderPixels = 25000000L;

        private PdfDocument _document;

        private string _currentPath;

        private int _pageIndex;

        private float _zoom = BaseScale;

        private PdfScaleMode _scaleMode = PdfScaleMode.FitWidth;

        private bool _isRendering;

        /// <summary>用于「文字可选择」的 PdfPig 文档（打开失败时为 null，预览功能不受影响）。</summary>
        private PdfPigDocument _textDocument;

        /// <summary>PdfPig 文档的只读文件流（共享读写 + 允许删除，预览时其它步骤仍可移动该 PDF）。</summary>
        private FileStream _textStream;

        /// <summary>当前页的文字选择模型（首次需要选择时才解析，避免影响翻页速度）。</summary>
        private PdfTextSelectionModel _selectionModel;

        /// <summary>文字选择模型对应的页索引（-1 表示尚未加载）。</summary>
        private int _selectionModelPage = -1;

        /// <summary>选择的起始插入符位置（0 ~ 字符总数，-1 表示没有选择）。</summary>
        private int _selectionAnchor = -1;

        /// <summary>选择的结束插入符位置。</summary>
        private int _selectionCaret = -1;

        /// <summary>是否正在拖动选择文字。</summary>
        private bool _isSelectingText;

        /// <summary>页面右键菜单（复制 / 全选 / 清除选择）。</summary>
        private ContextMenuStrip _selectionMenu;

        /// <summary>右键菜单项：复制选中文字。</summary>
        private ToolStripMenuItem _miCopySelection;

        /// <summary>右键菜单项：全选本页文字。</summary>
        private ToolStripMenuItem _miSelectAll;

        /// <summary>是否处于「小手拖动」模式（开启后左键拖动只平移画面，不选择文字）。</summary>
        private bool _isPanMode;

        /// <summary>是否正在拖动画面。</summary>
        private bool _isPanning;

        /// <summary>开始拖动时的鼠标屏幕坐标。</summary>
        private Point _panStartScreen;

        /// <summary>开始拖动时画布的滚动偏移（正值，单位像素）。</summary>
        private Point _panStartScroll;

        /// <summary>鼠标滚轮消息过滤器（Ctrl + 滚轮缩放，普通滚轮滚动画布）。</summary>
        private CanvasWheelMessageFilter _wheelFilter;

        public PdfPreviewForm()
        {
            InitializeComponent();
            ApplyButtonStyle();
            InitializeSelection();
            InitializePan();
        }

        /// <summary>缩放方式。</summary>
        private enum PdfScaleMode
        {
            /// <summary>用户指定的固定缩放（放大 / 缩小 / 实际大小）。</summary>
            Custom,

            /// <summary>按窗口宽度适应。</summary>
            FitWidth,

            /// <summary>整页适应窗口。</summary>
            FitPage
        }

        /// <summary>当前显示的 PDF 完整路径（未加载时为 null）。</summary>
        public string CurrentPath
        {
            get { return _currentPath; }
        }

        /// <summary>
        /// 显示指定的 PDF。若与当前显示的已是同一文件，则只把窗口前置并保持当前页 / 缩放。
        /// 文件不存在或损坏时在状态栏与弹窗中提示，不抛出异常。
        /// </summary>
        /// <param name="pdfFilePath">PDF 完整路径。</param>
        public void ShowPdf(string pdfFilePath)
        {
            if (string.IsNullOrWhiteSpace(pdfFilePath))
            {
                return;
            }

            if (!File.Exists(pdfFilePath))
            {
                ShowError("文件不存在：" + pdfFilePath);
                return;
            }

            if (_document != null && string.Equals(_currentPath, pdfFilePath, StringComparison.OrdinalIgnoreCase))
            {
                if (WindowState == FormWindowState.Minimized)
                {
                    WindowState = FormWindowState.Normal;
                }

                Activate();
                return;
            }

            LoadDocument(pdfFilePath);
        }

        /// <summary>加载 PDF（失败时关闭旧文档并给出提示）。</summary>
        private void LoadDocument(string pdfFilePath)
        {
            Cursor previousCursor = Cursor;

            Cursor = Cursors.WaitCursor;

            try
            {
                CloseDocument();

                _document = PdfDocument.Load(pdfFilePath);
                _currentPath = pdfFilePath;
                _pageIndex = 0;
                _scaleMode = PdfScaleMode.FitWidth;

                // 另外用 PdfPig 打开一份（只读）用于文字选择；失败时只影响“能否选字”，不影响预览
                OpenTextDocument(pdfFilePath);

                Text = "PDF 预览 - " + Path.GetFileName(pdfFilePath);

                RenderPage();
            }
            catch (Exception ex)
            {
                CloseDocument();
                _currentPath = null;
                Text = "PDF 预览 - 读取失败";
                ShowError("无法显示该 PDF：" + ex.Message);

                MessageBox.Show(this,
                    "无法显示该 PDF：\n" + pdfFilePath + "\n\n" + ex.Message,
                    "PDF 预览", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                Cursor = previousCursor;
            }
        }

        /// <summary>释放当前 PDF 与页面位图。</summary>
        private void CloseDocument()
        {
            Image image = picPage.Image;

            picPage.Image = null;

            if (image != null)
            {
                image.Dispose();
            }

            if (_document != null)
            {
                _document.Dispose();
                _document = null;
            }

            CloseTextDocument();
        }

        /// <summary>
        /// 打开 PdfPig 文档用于文字选择。用 FileShare.ReadWrite | Delete 打开文件流，
        /// 保证预览期间其它步骤（分类整理）仍能移动 / 覆盖该 PDF。
        /// </summary>
        private void OpenTextDocument(string pdfFilePath)
        {
            CloseTextDocument();

            try
            {
                _textStream = new FileStream(pdfFilePath, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                _textDocument = PdfPigDocument.Open(_textStream);
            }
            catch (Exception)
            {
                // PDF 加密 / 损坏等情况下 PdfPig 可能打不开：此时禁用选字，预览照常可用
                CloseTextDocument();
            }
        }

        /// <summary>释放 PdfPig 文档、文件流与文字选择模型。</summary>
        private void CloseTextDocument()
        {
            ResetSelection();

            if (_textDocument != null)
            {
                try
                {
                    _textDocument.Dispose();
                }
                catch (Exception)
                {
                    // 释放失败不影响窗口关闭
                }

                _textDocument = null;
            }

            if (_textStream != null)
            {
                try
                {
                    _textStream.Dispose();
                }
                catch (Exception)
                {
                    // 释放失败不影响窗口关闭
                }

                _textStream = null;
            }

            _selectionModel = null;
            _selectionModelPage = -1;
        }

        /// <summary>按当前页 / 缩放方式渲染页面位图并显示。</summary>
        private void RenderPage()
        {
            if (_document == null || _isRendering)
            {
                return;
            }

            int pageCount = _document.PageCount;

            if (pageCount <= 0)
            {
                ShowError("该 PDF 没有可显示的页面。");
                return;
            }

            if (_pageIndex < 0)
            {
                _pageIndex = 0;
            }

            if (_pageIndex >= pageCount)
            {
                _pageIndex = pageCount - 1;
            }

            SizeF pageSize;

            try
            {
                pageSize = _document.PageSizes[_pageIndex];
            }
            catch (Exception ex)
            {
                ShowError("读取页面尺寸失败：" + ex.Message);
                return;
            }

            if (pageSize.Width <= 0F || pageSize.Height <= 0F)
            {
                ShowError("页面尺寸无效。");
                return;
            }

            int viewWidth = Math.Max(120, pnlCanvas.ClientSize.Width - PageMargin * 2);
            int viewHeight = Math.Max(120, pnlCanvas.ClientSize.Height - PageMargin * 2);
            float scale;

            switch (_scaleMode)
            {
                case PdfScaleMode.FitWidth:
                    scale = viewWidth / pageSize.Width;
                    break;

                case PdfScaleMode.FitPage:
                    scale = Math.Min(viewWidth / pageSize.Width, viewHeight / pageSize.Height);
                    break;

                default:
                    scale = _zoom;
                    break;
            }

            scale = Clamp(scale, MinZoom, MaxZoom);

            int width = Math.Max(1, (int)Math.Round(pageSize.Width * scale));
            int height = Math.Max(1, (int)Math.Round(pageSize.Height * scale));
            long pixels = (long)width * height;

            if (pixels > MaxRenderPixels)
            {
                // 极端放大时按比例收缩，避免一次申请过大的位图
                double factor = Math.Sqrt((double)MaxRenderPixels / pixels);

                width = Math.Max(1, (int)(width * factor));
                height = Math.Max(1, (int)(height * factor));
                scale = width / pageSize.Width;
            }

            _zoom = scale;
            _isRendering = true;

            // 页码变化时清空选择；缩放只改变像素比例，PDF 坐标不变，选择可以保留
            if (_selectionModelPage != _pageIndex)
            {
                ResetSelection();
            }

            try
            {
                Image image = _document.Render(_pageIndex, width, height, 96F, 96F, false);
                Image previous = picPage.Image;

                picPage.Size = new Size(width, height);
                picPage.Image = image;

                if (previous != null)
                {
                    previous.Dispose();
                }

                LayoutPage();
                UpdateStatus();
            }
            catch (Exception ex)
            {
                ShowError("渲染失败：" + ex.Message);
            }
            finally
            {
                _isRendering = false;
            }
        }

        /// <summary>把页面位图居中放在画布中（比可视区大时靠左上并出现滚动条）。</summary>
        private void LayoutPage()
        {
            int left = Math.Max(PageMargin, (pnlCanvas.ClientSize.Width - picPage.Width) / 2);

            picPage.Location = new Point(left, PageMargin);
        }

        /// <summary>刷新状态栏（文件名 / 页码 / 缩放比例）。</summary>
        private void UpdateStatus()
        {
            lblStatus.ForeColor = Color.FromArgb(52, 73, 94);

            if (_document == null)
            {
                lblStatus.Text = "尚未选择 PDF";
                lblPageInfo.Text = "- / -";
                lblZoom.Text = "-";
                return;
            }

            int percent = (int)Math.Round(_zoom / BaseScale * 100);

            lblPageInfo.Text = (_pageIndex + 1) + " / " + _document.PageCount;
            lblZoom.Text = percent + "%";
            lblStatus.Text = string.Format("{0}　第 {1} / {2} 页　缩放 {3}%　（← → 翻页，Ctrl+滚轮 / +- 缩放，滚轮滚动，拖拽选字后 Ctrl+C 复制）",
                Path.GetFileName(_currentPath ?? string.Empty),
                _pageIndex + 1,
                _document.PageCount,
                percent);

            if (_isPanMode)
            {
                lblStatus.Text += "　拖动模式：按住左键拖动画布";
            }

            PdfTextRange selection = GetSelectionRange();

            if (!selection.IsEmpty)
            {
                lblStatus.Text += "　已选 " + selection.Length + " 个字符";
            }
        }

        /// <summary>在状态栏显示红色错误信息。</summary>
        private void ShowError(string message)
        {
            lblStatus.Text = message;
            lblStatus.ForeColor = Color.FromArgb(198, 40, 40);
        }

        /// <summary>把数值限制在 [min, max] 区间内。</summary>
        private static float Clamp(float value, float min, float max)
        {
            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }

        /// <summary>上一页。</summary>
        private void BtnPrevPage_Click(object sender, EventArgs e)
        {
            GoToPage(_pageIndex - 1);
        }

        /// <summary>下一页。</summary>
        private void BtnNextPage_Click(object sender, EventArgs e)
        {
            GoToPage(_pageIndex + 1);
        }

        /// <summary>跳到指定页（自动限制在有效范围内）。</summary>
        private void GoToPage(int pageIndex)
        {
            if (_document == null)
            {
                return;
            }

            int target = pageIndex;

            if (target < 0)
            {
                target = 0;
            }

            if (target > _document.PageCount - 1)
            {
                target = _document.PageCount - 1;
            }

            if (target == _pageIndex)
            {
                return;
            }

            _pageIndex = target;
            RenderPage();
            pnlCanvas.AutoScrollPosition = new Point(0, 0);
        }

        /// <summary>放大一档。</summary>
        private void BtnZoomIn_Click(object sender, EventArgs e)
        {
            ApplyZoom(_zoom * 1.25F);
        }

        /// <summary>缩小一档。</summary>
        private void BtnZoomOut_Click(object sender, EventArgs e)
        {
            ApplyZoom(_zoom / 1.25F);
        }

        /// <summary>按固定比例缩放（100% = 96 DPI 的实际大小）。</summary>
        private void ApplyZoom(float zoom)
        {
            if (_document == null)
            {
                return;
            }

            _scaleMode = PdfScaleMode.Custom;
            _zoom = Clamp(zoom, MinZoom, MaxZoom);
            RenderPage();
        }

        /// <summary>适合窗口宽度。</summary>
        private void BtnFitWidth_Click(object sender, EventArgs e)
        {
            _scaleMode = PdfScaleMode.FitWidth;
            RenderPage();
        }

        /// <summary>整页适应窗口。</summary>
        private void BtnFitPage_Click(object sender, EventArgs e)
        {
            _scaleMode = PdfScaleMode.FitPage;
            RenderPage();
        }

        /// <summary>恢复到实际大小（100%）。</summary>
        private void BtnActualSize_Click(object sender, EventArgs e)
        {
            ApplyZoom(BaseScale);
        }

        /// <summary>用系统默认程序打开当前 PDF（例如 Adobe Reader，便于打印或批注）。</summary>
        private void BtnOpenExternal_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_currentPath) || !File.Exists(_currentPath))
            {
                ShowError("当前没有可打开的 PDF。");
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(_currentPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ShowError("无法调用外部程序打开：" + ex.Message);
            }
        }

        /// <summary>关闭预览窗口。</summary>
        private void BtnClose_Click(object sender, EventArgs e)
        {
            Close();
        }

        /// <summary>画布尺寸变化：延迟重新渲染，避免拖动窗口时反复渲染。</summary>
        private void PnlCanvas_Resize(object sender, EventArgs e)
        {
            if (_document == null)
            {
                return;
            }

            resizeTimer.Stop();
            resizeTimer.Start();
        }

        /// <summary>尺寸稳定后按「适合宽度 / 适合页面」重新渲染。</summary>
        private void ResizeTimer_Tick(object sender, EventArgs e)
        {
            resizeTimer.Stop();

            if (_document == null || _scaleMode == PdfScaleMode.Custom)
            {
                return;
            }

            RenderPage();
        }

        /// <summary>键盘操作：← / PageUp 上一页，→ / PageDown 下一页，+ / - 缩放，Ctrl+C 复制选中文字，Ctrl+A 全选，Esc 关闭。</summary>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.Control && e.KeyCode == Keys.C)
            {
                CopySelection();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            if (e.Control && e.KeyCode == Keys.A)
            {
                SelectAllText();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            switch (e.KeyCode)
            {
                case Keys.Left:
                case Keys.PageUp:
                    GoToPage(_pageIndex - 1);
                    e.Handled = true;
                    break;

                case Keys.Right:
                case Keys.PageDown:
                    GoToPage(_pageIndex + 1);
                    e.Handled = true;
                    break;

                case Keys.Add:
                case Keys.Oemplus:
                    ApplyZoom(_zoom * 1.25F);
                    e.Handled = true;
                    break;

                case Keys.Subtract:
                case Keys.OemMinus:
                    ApplyZoom(_zoom / 1.25F);
                    e.Handled = true;
                    break;

                case Keys.Escape:
                    if (!GetSelectionRange().IsEmpty)
                    {
                        ResetSelection();
                        picPage.Invalidate();
                        UpdateStatus();
                    }
                    else
                    {
                        Close();
                    }

                    break;
            }
        }

        /// <summary>
        /// 初始化文字选择：把鼠标 / 绘制 / 右键菜单事件挂到页面位图上。
        /// 高亮直接画在 PictureBox 的 Paint 事件里，保证与页面位图像素坐标一一对应。
        /// </summary>
        private void InitializeSelection()
        {
            _miCopySelection = new ToolStripMenuItem("复制选中文字(&C)", null, MiCopySelection_Click);
            _miSelectAll = new ToolStripMenuItem("全选本页文字(&A)", null, MiSelectAll_Click);
            ToolStripMenuItem clearItem = new ToolStripMenuItem("清除选择(&E)", null, MiClearSelection_Click);

            _selectionMenu = new ContextMenuStrip();
            _selectionMenu.Items.Add(_miCopySelection);
            _selectionMenu.Items.Add(_miSelectAll);
            _selectionMenu.Items.Add(new ToolStripSeparator());
            _selectionMenu.Items.Add(clearItem);
            _selectionMenu.Opening += SelectionMenu_Opening;

            picPage.ContextMenuStrip = _selectionMenu;
            picPage.Paint += PicPage_Paint;
            picPage.MouseDown += PicPage_MouseDown;
            picPage.MouseMove += PicPage_MouseMove;
            picPage.MouseUp += PicPage_MouseUp;
            picPage.MouseLeave += PicPage_MouseLeave;
        }

        /// <summary>
        /// 初始化平移（小手）与滚轮支持：按钮提示、进程级滚轮过滤器。
        /// 滚轮用消息过滤器处理，因为 WM_MOUSEWHEEL 只发给有焦点的控件，
        /// 若只在 Form.WndProc 里处理，点过工具栏按钮后（焦点在按钮上）就会失效。
        /// </summary>
        private void InitializePan()
        {
            ToolTip tip = new ToolTip(components);

            tip.SetToolTip(btnPan, "开启后用左键按住页面拖动画面（任何模式下按住鼠标中键也可拖动）\nCtrl + 鼠标滚轮：以鼠标位置为中心缩放");
            tip.SetToolTip(pnlCanvas, "Ctrl + 鼠标滚轮缩放，滚轮上下滚动画面");

            _wheelFilter = new CanvasWheelMessageFilter(this);
            Application.AddMessageFilter(_wheelFilter);

            UpdatePanButton();
        }

        /// <summary>工具栏：切换「✋ 拖动」模式。</summary>
        private void BtnPan_Click(object sender, EventArgs e)
        {
            SetPanMode(!_isPanMode);
        }

        /// <summary>开启 / 关闭拖动模式（开启后左键拖动平移画面，不再选择文字）。</summary>
        private void SetPanMode(bool enabled)
        {
            if (_isPanMode == enabled)
            {
                return;
            }

            _isPanMode = enabled;

            if (!_isPanMode)
            {
                EndPan();
            }

            UpdatePanButton();
            UpdateStatus();
        }

        /// <summary>按当前拖动模式刷新「✋ 拖动」按钮与页面光标。</summary>
        private void UpdatePanButton()
        {
            if (_isPanMode)
            {
                btnPan.BackColor = Color.FromArgb(204, 226, 247);
                btnPan.ForeColor = Color.FromArgb(0, 80, 160);
                btnPan.FlatAppearance.BorderColor = Color.FromArgb(0, 120, 215);
                picPage.Cursor = Cursors.Hand;
            }
            else
            {
                btnPan.BackColor = Color.FromArgb(232, 237, 243);
                btnPan.ForeColor = Color.FromArgb(52, 73, 94);
                btnPan.FlatAppearance.BorderColor = Color.FromArgb(199, 208, 219);
                picPage.Cursor = Cursors.Default;
            }
        }

        /// <summary>开始拖动画面（记住起始鼠标位置与滚动偏移）。</summary>
        /// <param name="screenPoint">鼠标当前屏幕坐标。</param>
        private void BeginPan(Point screenPoint)
        {
            if (_document == null)
            {
                return;
            }

            _isPanning = true;
            _panStartScreen = screenPoint;
            _panStartScroll = GetCanvasScrollOffset();
            picPage.Capture = true;
            picPage.Cursor = Cursors.Hand;
        }

        /// <summary>拖动画面：鼠标移动多少，内容就跟着移动多少（抓手手感）。</summary>
        /// <param name="screenPoint">鼠标当前屏幕坐标。</param>
        private void UpdatePanTo(Point screenPoint)
        {
            int offsetX = _panStartScroll.X - (screenPoint.X - _panStartScreen.X);
            int offsetY = _panStartScroll.Y - (screenPoint.Y - _panStartScreen.Y);

            pnlCanvas.AutoScrollPosition = new Point(Math.Max(0, offsetX), Math.Max(0, offsetY));
        }

        /// <summary>结束拖动画面。</summary>
        private void EndPan()
        {
            if (!_isPanning)
            {
                return;
            }

            _isPanning = false;

            if (picPage.Capture)
            {
                picPage.Capture = false;
            }

            picPage.Cursor = _isPanMode ? Cursors.Hand : Cursors.Default;
        }

        /// <summary>当前画布的滚动偏移（正值，单位像素）。</summary>
        private Point GetCanvasScrollOffset()
        {
            Point position = pnlCanvas.AutoScrollPosition;

            return new Point(-position.X, -position.Y);
        }

        /// <summary>按像素滚动画布（offsetY 为正表示向下滚动、看到更下面的内容）。</summary>
        private void ScrollCanvasBy(int offsetY)
        {
            Point scroll = GetCanvasScrollOffset();

            pnlCanvas.AutoScrollPosition = new Point(scroll.X, Math.Max(0, scroll.Y + offsetY));
        }

        /// <summary>按像素横向滚动画布（Shift + 滚轮时使用）。</summary>
        private void ScrollCanvasHorizontal(int offsetX)
        {
            Point scroll = GetCanvasScrollOffset();

            pnlCanvas.AutoScrollPosition = new Point(Math.Max(0, scroll.X + offsetX), scroll.Y);
        }

        /// <summary>以画布上的指定点为中心缩放（缩放后该点下的内容保持不动）。</summary>
        private void ZoomAtPoint(float factor, Point canvasPoint)
        {
            if (_document == null || picPage.Image == null || _zoom <= 0F)
            {
                return;
            }

            float oldZoom = _zoom;
            float newZoom = Clamp(oldZoom * factor, MinZoom, MaxZoom);

            if (Math.Abs(newZoom - oldZoom) < 0.0001F)
            {
                return;
            }

            // 鼠标位置下的 PDF 点（相对页面左上角，单位 PDF 点）。
            // picPage.Left / Top 已经是「页面在画布上的显示位置」（AutoScroll 会把子控件移到负坐标），
            // 所以这里不能再加滚动偏移。
            double pdfX = (canvasPoint.X - picPage.Left) / (double)oldZoom;
            double pdfY = (canvasPoint.Y - picPage.Top) / (double)oldZoom;

            ApplyZoom(newZoom);

            // 缩放后位图已重新渲染并重新居中，把滚动位置调到让同一个 PDF 点回到鼠标处。
            // 设置滚动条可能改变可视区宽度（滚动条出现 / 消失），所以校正两次。
            for (int attempt = 0; attempt < 2; attempt++)
            {
                Point scroll = GetCanvasScrollOffset();

                // 页面左上角在「内容坐标」中的位置 = 显示位置 + 滚动偏移
                int contentLeft = picPage.Left + scroll.X;
                int contentTop = picPage.Top + scroll.Y;

                Point next = new Point(
                    Math.Max(0, (int)Math.Round(contentLeft + pdfX * _zoom - canvasPoint.X)),
                    Math.Max(0, (int)Math.Round(contentTop + pdfY * _zoom - canvasPoint.Y)));

                if (next == scroll)
                {
                    break;
                }

                pnlCanvas.AutoScrollPosition = next;
            }
        }

        /// <summary>
        /// 处理鼠标滚轮消息。返回 true 表示已处理（不再交给焦点控件）。
        /// Ctrl + 滚轮：以鼠标位置为中心缩放；普通滚轮：滚动画面。
        /// </summary>
        private bool HandleCanvasWheel(ref Message m)
        {
            if (IsDisposed || !Visible || !ContainsFocus)
            {
                return false;
            }

            Point screenPoint = Cursor.Position;
            bool inCanvas = pnlCanvas.RectangleToScreen(pnlCanvas.ClientRectangle).Contains(screenPoint);
            bool control = (ModifierKeys & Keys.Control) == Keys.Control;

            if (!inCanvas && !control)
            {
                return false;
            }

            // WParam 高 16 位是有符号的滚轮增量（+120 向前 / -120 向后，高精度滚轮可能是其它倍数）
            int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);

            if (delta == 0 || _document == null)
            {
                return false;
            }

            if (control)
            {
                Point center = inCanvas
                    ? pnlCanvas.PointToClient(screenPoint)
                    : new Point(pnlCanvas.ClientSize.Width / 2, pnlCanvas.ClientSize.Height / 2);

                ZoomAtPoint(delta > 0 ? 1.15F : 1F / 1.15F, center);
                return true;
            }

            int lines = SystemInformation.MouseWheelScrollLines;

            if (lines < 0)
            {
                // 系统设置为「滚轮不滚动」
                return true;
            }

            int step = lines == 0
                ? Math.Max(40, pnlCanvas.ClientSize.Height - 20)
                : Math.Max(20, lines * 20);

            if ((ModifierKeys & Keys.Shift) == Keys.Shift)
            {
                // Shift + 滚轮：横向滚动
                ScrollCanvasHorizontal(-delta * step / 120);
            }
            else
            {
                ScrollCanvasBy(-delta * step / 120);
            }

            return true;
        }

        /// <summary>菜单弹出时按当前状态启用 / 禁用菜单项。</summary>
        private void SelectionMenu_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _miCopySelection.Enabled = !GetSelectionRange().IsEmpty;
            _miSelectAll.Enabled = _textDocument != null || _selectionModel != null;
        }

        /// <summary>在页面位图之上绘制选择高亮与插入符竖线。</summary>
        private void PicPage_Paint(object sender, PaintEventArgs e)
        {
            if (_selectionModel == null)
            {
                return;
            }

            float scale = _zoom;
            PdfTextRange range = GetSelectionRange();

            if (!range.IsEmpty)
            {
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(70, 0, 120, 215)))
                {
                    foreach (PdfSelectionRectangle item in _selectionModel.GetRectangles(range))
                    {
                        e.Graphics.FillRectangle(brush, ToPixelRectangle(item, scale));
                    }
                }
            }

            if (_isSelectingText && _selectionCaret >= 0)
            {
                RectangleF caret = ToPixelRectangle(_selectionModel.GetCaretBar(_selectionCaret), scale);

                if (caret.Width >= caret.Height)
                {
                    // 竖排文字的插入符是一条横线
                    caret.Height = 1F;
                }
                else
                {
                    caret.Width = 1F;
                }

                using (SolidBrush caretBrush = new SolidBrush(Color.FromArgb(210, 0, 90, 180)))
                {
                    e.Graphics.FillRectangle(caretBrush, caret);
                }
            }
        }

        /// <summary>把 PDF 点坐标的矩形换算成位图像素坐标的矩形。</summary>
        private static RectangleF ToPixelRectangle(PdfSelectionRectangle rect, float scale)
        {
            return new RectangleF(
                (float)(rect.Left * scale),
                (float)(rect.Top * scale),
                Math.Max(1F, (float)(rect.Width * scale)),
                Math.Max(1F, (float)(rect.Height * scale)));
        }

        /// <summary>按下鼠标：拖动模式下（或按中键）平移画面，否则开始新的选择（双击选词、三击选行）。</summary>
        private void PicPage_MouseDown(object sender, MouseEventArgs e)
        {
            // 中键：任何模式下都可以拖动画布
            if (e.Button == MouseButtons.Middle)
            {
                BeginPan(Cursor.Position);
                return;
            }

            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            // 「✋ 拖动」模式下左键只平移画面，不选择文字
            if (_isPanMode)
            {
                BeginPan(Cursor.Position);
                return;
            }

            PdfTextSelectionModel model = EnsureSelectionModel();

            if (model == null)
            {
                return;
            }

            double x;
            double y;

            if (!TryGetPdfPoint(e, out x, out y))
            {
                return;
            }

            int caret = model.HitTestCaret(x, y);

            if (caret < 0)
            {
                ResetSelection();
                picPage.Invalidate();
                UpdateStatus();
                return;
            }

            if (e.Clicks >= 3)
            {
                ApplySelection(model.SelectLine(caret));
                return;
            }

            if (e.Clicks == 2)
            {
                ApplySelection(model.SelectWord(caret));
                return;
            }

            _selectionAnchor = caret;
            _selectionCaret = caret;
            _isSelectingText = true;
            picPage.Capture = true;
            picPage.Invalidate();
            UpdateStatus();
        }

        /// <summary>移动鼠标：拖动时平移画面；拖动模式下固定手形光标；否则扩展选择 / 切换 I 形光标。</summary>
        private void PicPage_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanning)
            {
                UpdatePanTo(Cursor.Position);
                return;
            }

            if (_isPanMode)
            {
                picPage.Cursor = Cursors.Hand;
                return;
            }

            if (_selectionModel == null)
            {
                return;
            }

            double x;
            double y;

            if (!TryGetPdfPoint(e, out x, out y))
            {
                return;
            }

            if (!_isSelectingText)
            {
                picPage.Cursor = _selectionModel.HitTestCaret(x, y) >= 0 ? Cursors.IBeam : Cursors.Default;
                return;
            }

            int caret = _selectionModel.HitTestCaret(x, y);

            if (caret < 0 || caret == _selectionCaret)
            {
                return;
            }

            _selectionCaret = caret;
            picPage.Invalidate();
            UpdateStatus();
        }

        /// <summary>松开鼠标：结束拖动或结束拖动选择。</summary>
        private void PicPage_MouseUp(object sender, MouseEventArgs e)
        {
            if (_isPanning)
            {
                EndPan();
                return;
            }

            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            _isSelectingText = false;

            if (picPage.Capture)
            {
                picPage.Capture = false;
            }

            picPage.Invalidate();
            UpdateStatus();
        }

        /// <summary>鼠标离开页面时恢复默认光标（拖动中或拖动模式下保持手形）。</summary>
        private void PicPage_MouseLeave(object sender, EventArgs e)
        {
            if (_isSelectingText || _isPanning || _isPanMode)
            {
                return;
            }

            picPage.Cursor = Cursors.Default;
        }

        /// <summary>当前选择的字符范围（没有选择时为空范围）。</summary>
        private PdfTextRange GetSelectionRange()
        {
            if (_selectionModel == null || _selectionAnchor < 0 || _selectionCaret < 0)
            {
                return PdfTextRange.Empty;
            }

            int start = Math.Min(_selectionAnchor, _selectionCaret);
            int end = Math.Max(_selectionAnchor, _selectionCaret) - 1;

            return start <= end ? new PdfTextRange(start, end) : PdfTextRange.Empty;
        }

        /// <summary>清除当前选择。</summary>
        private void ResetSelection()
        {
            _selectionAnchor = -1;
            _selectionCaret = -1;
            _isSelectingText = false;

            if (picPage.Capture)
            {
                picPage.Capture = false;
            }
        }

        /// <summary>把字符范围设置成当前选择并刷新显示。</summary>
        private void ApplySelection(PdfTextRange range)
        {
            if (range.IsEmpty)
            {
                ResetSelection();
            }
            else
            {
                _selectionAnchor = range.Start;
                _selectionCaret = range.End + 1;
                _isSelectingText = false;
            }

            picPage.Invalidate();
            UpdateStatus();
        }

        /// <summary>取当前页的文字选择模型（首次使用时才解析 PdfPig 内容，失败返回 null）。</summary>
        private PdfTextSelectionModel EnsureSelectionModel()
        {
            if (_selectionModel != null && _selectionModelPage == _pageIndex)
            {
                return _selectionModel;
            }

            if (_textDocument == null)
            {
                ShowError("本页文字无法选择：该 PDF 无法用 PdfPig 解析（可能是加密文件）。");
                return null;
            }

            Cursor previousCursor = Cursor;

            Cursor = Cursors.WaitCursor;

            try
            {
                PdfPigPage page = _textDocument.GetPage(_pageIndex + 1);

                _selectionModel = PdfTextSelectionModel.Create(page);
                _selectionModelPage = _pageIndex;
            }
            catch (Exception ex)
            {
                _selectionModel = null;
                _selectionModelPage = -1;
                ShowError("提取本页文字失败：" + ex.Message);
                return null;
            }
            finally
            {
                Cursor = previousCursor;
            }

            if (_selectionModel.Chars.Count == 0)
            {
                ShowError("本页没有可选择的文字（可能是扫描图片或纯矢量图）。");
                return null;
            }

            // 之前可能显示了错误提示，这里恢复正常状态栏
            UpdateStatus();

            return _selectionModel;
        }

        /// <summary>把鼠标位置换算成页面 PDF 坐标（左上角原点、单位 PDF 点）。</summary>
        private bool TryGetPdfPoint(MouseEventArgs e, out double x, out double y)
        {
            x = 0.0;
            y = 0.0;

            if (_zoom <= 0F || picPage.Image == null)
            {
                return false;
            }

            x = e.X / (double)_zoom;
            y = e.Y / (double)_zoom;

            return true;
        }

        /// <summary>复制选中文字到剪贴板（Ctrl+C / 右键菜单）。</summary>
        private void CopySelection()
        {
            string text = _selectionModel == null ? string.Empty : _selectionModel.GetText(GetSelectionRange());

            if (string.IsNullOrEmpty(text))
            {
                ShowError("没有选中文字：请先在页面上拖拽选择一段文字。");
                return;
            }

            try
            {
                Clipboard.SetText(text);
                lblStatus.ForeColor = Color.FromArgb(52, 73, 94);
                lblStatus.Text = "已复制 " + text.Length + " 个字符到剪贴板。";
            }
            catch (Exception ex)
            {
                ShowError("复制到剪贴板失败：" + ex.Message);
            }
        }

        /// <summary>全选本页文字（Ctrl+A）。</summary>
        private void SelectAllText()
        {
            PdfTextSelectionModel model = EnsureSelectionModel();

            if (model == null)
            {
                return;
            }

            ApplySelection(model.SelectAll());
        }

        /// <summary>右键菜单：复制选中文字。</summary>
        private void MiCopySelection_Click(object sender, EventArgs e)
        {
            CopySelection();
        }

        /// <summary>右键菜单：全选本页文字。</summary>
        private void MiSelectAll_Click(object sender, EventArgs e)
        {
            SelectAllText();
        }

        /// <summary>右键菜单：清除选择。</summary>
        private void MiClearSelection_Click(object sender, EventArgs e)
        {
            ResetSelection();
            picPage.Invalidate();
            UpdateStatus();
        }

        /// <summary>统一工具栏按钮外观（与主窗口风格保持一致）。</summary>
        private void ApplyButtonStyle()
        {
            foreach (Control control in pnlToolbar.Controls)
            {
                Button button = control as Button;

                if (button == null)
                {
                    continue;
                }

                button.BackColor = Color.FromArgb(232, 237, 243);
                button.ForeColor = Color.FromArgb(52, 73, 94);
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = Color.FromArgb(199, 208, 219);
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(217, 226, 236);
                button.Cursor = Cursors.Hand;
                button.UseVisualStyleBackColor = false;
            }
        }

        /// <summary>Windows 消息：鼠标滚轮（Ctrl + 滚轮缩放、滚轮滚动画布）。</summary>
        private const int WmMouseWheel = 0x020A;

        /// <summary>
        /// 兜底处理发给本窗体的滚轮消息（正常情况下已由 <see cref="CanvasWheelMessageFilter"/> 先处理）。
        /// </summary>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmMouseWheel && HandleCanvasWheel(ref m))
            {
                return;
            }

            base.WndProc(ref m);
        }

        /// <summary>
        /// 进程级鼠标滚轮过滤器：WM_MOUSEWHEEL 只会发给有焦点的控件，
        /// 用过滤器才能保证「鼠标在预览画面上时」Ctrl + 滚轮缩放与滚轮滚动始终生效。
        /// </summary>
        private sealed class CanvasWheelMessageFilter : IMessageFilter
        {
            private readonly PdfPreviewForm _owner;

            public CanvasWheelMessageFilter(PdfPreviewForm owner)
            {
                _owner = owner;
            }

            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg != WmMouseWheel)
                {
                    return false;
                }

                return _owner.HandleCanvasWheel(ref m);
            }
        }

        /// <summary>窗口关闭时释放 pdfium 文档、PdfPig 文档与位图，避免文件被占用。</summary>
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            resizeTimer.Stop();

            if (_wheelFilter != null)
            {
                Application.RemoveMessageFilter(_wheelFilter);
                _wheelFilter = null;
            }

            CloseDocument();

            if (_selectionMenu != null)
            {
                _selectionMenu.Dispose();
                _selectionMenu = null;
            }

            base.OnFormClosed(e);
        }
    }
}
