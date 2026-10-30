using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using MSIFAITOOLS.Services;

namespace MSIFAITOOLS
{
    /// <summary>
    /// 部品基本信息画面：显示指定 PDF（図番）已登录到 SQLite 的部品明细，
    /// 并支持在画面上直接编辑、追加行、删除行，保存后回写数据库。
    /// 由【分类结果】选中 PDF 行后点击【部品信息】/右键菜单/Enter 打开；
    /// 窗口可复用（<see cref="ShowDrawing"/> 切换显示内容）。
    /// </summary>
    internal sealed partial class PartBasicInfoForm : Form
    {
        /// <summary>本画面打开的 PDF 预览窗口（关闭本画面时一并关闭）。</summary>
        private PdfPreviewForm _previewForm;

        /// <summary>当前显示的図番（PDF 文件名去扩展名）。</summary>
        private string _drawingNo = string.Empty;

        /// <summary>当前显示的 PDF 完整路径。</summary>
        private string _pdfPath = string.Empty;

        /// <summary>指定的数据库路径（为空时使用 <see cref="PartBasicInfoRepository.ResolveDatabasePath"/>）。</summary>
        private readonly string _databasePath;

        /// <summary>填表期间抑制「单元格变更」引起的脏标记。</summary>
        private bool _suppressGridEvents;

        /// <summary>画面上是否存在尚未保存到数据库的修改。</summary>
        private bool _isDirty;

        /// <summary>窗口尚未显示，需要在 <see cref="OnShown"/> 里打开 PDF 预览窗口。</summary>
        private bool _openPreviewOnShown;

        /// <summary>部品基本信息窗口是否已按屏幕摆放（只摆一次，之后保留用户拖动的位置）。</summary>
        private bool _editorPlaced;

        public PartBasicInfoForm()
            : this(null)
        {
        }

        /// <summary>
        /// 创建部品基本信息画面。
        /// </summary>
        /// <param name="databasePath">数据库文件路径；为空时按 App.config / 程序目录解析。</param>
        public PartBasicInfoForm(string databasePath)
        {
            InitializeComponent();
            InitializePartsGrid();
            _databasePath = string.IsNullOrWhiteSpace(databasePath) ? null : databasePath.Trim();
        }

        /// <summary>当前显示的図番。</summary>
        public string DrawingNo
        {
            get { return _drawingNo; }
        }

        /// <summary>当前显示的 PDF 完整路径。</summary>
        public string PdfPath
        {
            get { return _pdfPath; }
        }

        /// <summary>
        /// 切换到指定 PDF 对应的部品基本信息（重新从数据库读取），
        /// 并让 PDF 预览窗口同时显示该 PDF（两个画面成对显示）。
        /// 若有未保存的修改，先询问是否保存。
        /// </summary>
        /// <param name="pdfFilePath">PDF 完整路径（図番 = 文件名去扩展名）。</param>
        public void ShowDrawing(string pdfFilePath)
        {
            if (!ConfirmUnsavedChanges("切换图番"))
            {
                return;
            }

            string newPath = pdfFilePath == null ? string.Empty : pdfFilePath.Trim();
            bool drawingChanged = !string.Equals(_pdfPath, newPath, StringComparison.OrdinalIgnoreCase);

            _pdfPath = newPath;
            _drawingNo = PartBasicInfoRepository.ResolveDrawingNo(_pdfPath);

            lblDrawingNo.Text = "図番：" + (_drawingNo.Length == 0 ? "-" : _drawingNo);
            lblPdfPath.Text = "PDF：" + (_pdfPath.Length == 0 ? "-" : _pdfPath);
            UpdateTitle();

            LoadParts();

            if (drawingChanged)
            {
                // 与部品基本信息画面同时打开 PDF 预览（窗口未显示时延后到 OnShown）
                ShowPreviewWindow(false);
            }
            else
            {
                // 同一图番刷新：预览已打开时只同步文件，不抢焦点
                SyncPreviewDocument();
            }
        }

        /// <summary>
        /// 从 SQLite 读取当前図番的部品基本信息并填充表格（异常不会抛出，只显示在画面上）。
        /// </summary>
        private void LoadParts()
        {
            IList<PartBasicInfo> parts = new List<PartBasicInfo>();
            string databasePath = _databasePath ?? PartBasicInfoRepository.ResolveDatabasePath();
            string error = null;

            if (_drawingNo.Length == 0)
            {
                error = "无法从 PDF 文件名解析出図番，请确认文件路径。";
            }
            else if (!File.Exists(databasePath))
            {
                error = "数据库尚未创建（请先执行【AI 分析】）：\n" + databasePath;
            }
            else
            {
                try
                {
                    using (PartBasicInfoRepository repository = new PartBasicInfoRepository(databasePath))
                    {
                        parts = repository.Query(_drawingNo);
                    }
                }
                catch (Exception ex)
                {
                    error = "读取数据库失败：" + ex.Message;
                }
            }

            lblDatabase.Text = "数据库：" + databasePath;

            SetDirty(false);
            PopulateParts(parts);

            if (error != null)
            {
                lblSummary.Text = "读取失败";
                lblSummary.ForeColor = Color.FromArgb(198, 40, 40);
                lblEmpty.Text = error;
            }
            else
            {
                lblSummary.ForeColor = Color.FromArgb(52, 73, 94);
                lblEmpty.Text = "该図番尚未登录部品基本信息。\n可点击【追加行】手工录入，或先对该 PDF 执行【AI 分析】。";
            }

            lblEmpty.Visible = parts.Count == 0;
            dgvParts.Visible = parts.Count > 0;
        }

        /// <summary>把部品明细填入表格（填表期间不产生脏标记）。</summary>
        private void PopulateParts(IList<PartBasicInfo> parts)
        {
            _suppressGridEvents = true;

            try
            {
                dgvParts.Rows.Clear();

                for (int index = 0; index < parts.Count; index++)
                {
                    PartBasicInfo part = parts[index];
                    string status = IsKnownVerifyStatus(part.VerifyStatus)
                        ? part.VerifyStatus
                        : PartVerifyStatus.NotChecked;

                    int rowIndex = dgvParts.Rows.Add(
                        (index + 1).ToString(),
                        part.Page.ToString(),
                        part.Number.ToString(),
                        part.PartName,
                        part.PartDrawingNo,
                        part.FrameKind,
                        part.Quantity,
                        part.BasicSize,
                        part.CutSize,
                        part.PartCode,
                        status,
                        part.Remark,
                        part.LoginTime);

                    DataGridViewRow row = dgvParts.Rows[rowIndex];
                    row.Tag = part;
                    ApplyRowStyle(row, status);
                }
            }
            finally
            {
                _suppressGridEvents = false;
            }

            RefreshGridSummary();
        }
        /// <summary>
        /// 初始化部品明细表格的列（与 <see cref="PopulateParts"/> 的填充顺序一致）。
        /// 除「No」「登录时间」以外均可编辑；「校验状态」为下拉选择。
        /// </summary>
        private void InitializePartsGrid()
        {
            dgvParts.AutoGenerateColumns = false;
            dgvParts.Columns.Clear();

            AddColumn("colNo", "No", 40, DataGridViewContentAlignment.MiddleCenter);
            AddColumn("colPage", "页码", 46, DataGridViewContentAlignment.MiddleCenter, false);
            AddColumn("colNumber", "番号", 46, DataGridViewContentAlignment.MiddleCenter, false);
            AddColumn("colPartName", "部品名", 170, DataGridViewContentAlignment.MiddleLeft, false);
            AddColumn("colPartDrawingNo", "部品図番", 118, DataGridViewContentAlignment.MiddleLeft, false);
            AddColumn("colFrameKind", "枠区分", 66, DataGridViewContentAlignment.MiddleCenter, false);
            AddColumn("colQuantity", "員数", 56, DataGridViewContentAlignment.MiddleCenter, false);
            AddColumn("colBasicSize", "基本寸法", 100, DataGridViewContentAlignment.MiddleLeft, false);
            AddColumn("colCutSize", "切断寸法", 100, DataGridViewContentAlignment.MiddleLeft, false);
            AddColumn("colPartCode", "部品コード", 102, DataGridViewContentAlignment.MiddleLeft, false);
            AddVerifyStatusColumn("colVerifyStatus", "校验状态", 88);
            AddColumn("colRemark", "备注", 170, DataGridViewContentAlignment.MiddleLeft, false);
            AddColumn("colLoginTime", "登录时间", 130, DataGridViewContentAlignment.MiddleCenter);
        }

        /// <summary>添加一个文本列。</summary>
        /// <param name="readOnly">true = 只读（No、登录时间等由程序维护的列）。</param>
        private void AddColumn(string name, string headerText, int width,
            DataGridViewContentAlignment alignment, bool readOnly = true)
        {
            DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
            column.Name = name;
            column.HeaderText = headerText;
            column.Width = width;
            column.ReadOnly = readOnly;
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            column.DefaultCellStyle.Alignment = alignment;
            column.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dgvParts.Columns.Add(column);
        }

        /// <summary>添加「校验状态」下拉列（候选项 = <see cref="PartVerifyStatus.All"/>）。</summary>
        private void AddVerifyStatusColumn(string name, string headerText, int width)
        {
            DataGridViewComboBoxColumn column = new DataGridViewComboBoxColumn();
            column.Name = name;
            column.HeaderText = headerText;
            column.Width = width;
            column.ReadOnly = false;
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            column.FlatStyle = FlatStyle.Flat;
            column.DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton;
            column.Items.AddRange(PartVerifyStatus.All);
            column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dgvParts.Columns.Add(column);
        }

        /// <summary>「追加行」：在表格末尾追加一行（页码沿用最后一行，番号取其下一个）。</summary>
        private void BtnAddRow_Click(object sender, EventArgs e)
        {
            if (_drawingNo.Length == 0)
            {
                MessageBox.Show(this, "无法从 PDF 文件名解析出図番，不能追加部品信息。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            dgvParts.EndEdit();

            int page = 1;
            int number = 1;

            if (dgvParts.Rows.Count > 0)
            {
                DataGridViewRow last = dgvParts.Rows[dgvParts.Rows.Count - 1];
                int lastPage;
                int lastNumber;

                if (int.TryParse(GetCellText(last, "colPage"), out lastPage) && lastPage > 0)
                {
                    page = lastPage;
                }

                if (int.TryParse(GetCellText(last, "colNumber"), out lastNumber) && lastNumber > 0)
                {
                    number = lastNumber + 1;
                }
            }

            _suppressGridEvents = true;
            DataGridViewRow row;

            try
            {
                dgvParts.Visible = true;
                lblEmpty.Visible = false;

                int rowIndex = dgvParts.Rows.Add(
                    (dgvParts.Rows.Count + 1).ToString(),
                    page.ToString(),
                    number.ToString(),
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    PartVerifyStatus.NotChecked,
                    string.Empty,
                    string.Empty);

                row = dgvParts.Rows[rowIndex];
                row.Tag = null;
                ApplyRowStyle(row, PartVerifyStatus.NotChecked);
                dgvParts.CurrentCell = row.Cells["colPartName"];
            }
            finally
            {
                _suppressGridEvents = false;
            }

            SetDirty(true);
            RenumberRows();

            if (dgvParts.IsHandleCreated)
            {
                dgvParts.BeginEdit(true);
            }
        }

        /// <summary>「删除行」：删除表格中选中的行（可按住 Ctrl / Shift 多选）。</summary>
        private void BtnDeleteRow_Click(object sender, EventArgs e)
        {
            DeleteSelectedRows();
        }

        /// <summary>删除当前选中的行，并刷新序号与统计。</summary>
        private void DeleteSelectedRows()
        {
            dgvParts.EndEdit();

            List<DataGridViewRow> targets = new List<DataGridViewRow>();

            foreach (DataGridViewRow row in dgvParts.SelectedRows)
            {
                if (!row.IsNewRow)
                {
                    targets.Add(row);
                }
            }

            if (targets.Count == 0 && dgvParts.CurrentCell != null)
            {
                DataGridViewRow currentRow = dgvParts.CurrentCell.OwningRow;

                if (currentRow != null && !currentRow.IsNewRow)
                {
                    targets.Add(currentRow);
                }
            }

            if (targets.Count == 0)
            {
                MessageBox.Show(this, "请先选中要删除的行。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (MessageBox.Show(this,
                    "确定删除选中的 " + targets.Count + " 行吗？\n（点击【保存】后才会真正从数据库删除）",
                    "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return;
            }

            _suppressGridEvents = true;

            try
            {
                foreach (DataGridViewRow row in targets)
                {
                    dgvParts.Rows.Remove(row);
                }
            }
            finally
            {
                _suppressGridEvents = false;
            }

            RenumberRows();
            SetDirty(true);
        }

        /// <summary>「刷新」：重新从数据库读取当前図番的部品明细（会丢弃未保存的修改）。</summary>
        private void BtnRefresh_Click(object sender, EventArgs e)
        {
            if (!ConfirmUnsavedChanges("重新载入"))
            {
                return;
            }

            LoadParts();
        }

        /// <summary>「保存」：把表格内容写回数据库（该図番整体替换）。</summary>
        private void BtnSave_Click(object sender, EventArgs e)
        {
            SaveParts();
        }

        /// <summary>
        /// 校验表格内容并整体保存到数据库，成功后重新载入画面。
        /// </summary>
        /// <returns>true = 已保存（或无修改）；false = 校验失败 / 保存失败 / 用户取消。</returns>
        private bool SaveParts()
        {
            if (_drawingNo.Length == 0)
            {
                MessageBox.Show(this, "无法从 PDF 文件名解析出図番，不能保存部品信息。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            dgvParts.EndEdit();

            List<PartBasicInfo> parts = new List<PartBasicInfo>();
            List<string> errors = new List<string>();
            Dictionary<string, int> usedKeys = new Dictionary<string, int>();

            foreach (DataGridViewRow row in dgvParts.Rows)
            {
                if (row.IsNewRow)
                {
                    continue;
                }

                int rowNo = row.Index + 1;
                int page;
                int number;

                if (!int.TryParse(GetCellText(row, "colPage"), out page) || page < 0)
                {
                    errors.Add("第 " + rowNo + " 行：「页码」必须是不小于 0 的整数。");
                    continue;
                }

                if (!int.TryParse(GetCellText(row, "colNumber"), out number) || number <= 0)
                {
                    errors.Add("第 " + rowNo + " 行：「番号」必须是大于 0 的整数。");
                    continue;
                }

                string key = page.ToString() + "/" + number.ToString();
                int firstRowNo;

                if (usedKeys.TryGetValue(key, out firstRowNo))
                {
                    errors.Add("第 " + rowNo + " 行：「页码 " + page + " / 番号 " + number +
                        "」与第 " + firstRowNo + " 行重复。");
                    continue;
                }

                usedKeys[key] = rowNo;

                PartBasicInfo origin = row.Tag as PartBasicInfo;

                PartBasicInfo part = new PartBasicInfo(
                    _drawingNo,
                    page,
                    number,
                    GetCellText(row, "colPartName"),
                    GetCellText(row, "colPartDrawingNo"),
                    GetCellText(row, "colFrameKind"),
                    GetCellText(row, "colQuantity"),
                    GetCellText(row, "colBasicSize"),
                    GetCellText(row, "colCutSize"),
                    GetCellText(row, "colPartCode"));

                string status = GetCellText(row, "colVerifyStatus");
                part.VerifyStatus = IsKnownVerifyStatus(status) ? status : PartVerifyStatus.NotChecked;
                part.Remark = GetCellText(row, "colRemark");

                if (origin != null)
                {
                    // 未修改的行沿用原来的来源与登录时间
                    part.SourceFile = origin.SourceFile ?? string.Empty;
                    part.PdfFile = string.IsNullOrEmpty(origin.PdfFile) ? _pdfPath : origin.PdfFile;
                    part.LoginTime = origin.LoginTime ?? string.Empty;
                }
                else
                {
                    // 新追加的行：登录时间留空，由仓储写入当前时间
                    part.SourceFile = string.Empty;
                    part.PdfFile = _pdfPath;
                    part.LoginTime = string.Empty;
                }

                parts.Add(part);
            }

            if (errors.Count > 0)
            {
                MessageBox.Show(this, "保存前请先修正以下问题：\n\n" + string.Join("\n", errors.ToArray()),
                    "无法保存", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            int blankCount = 0;

            foreach (PartBasicInfo part in parts)
            {
                if (part.PartName.Length == 0 && part.PartCode.Length == 0)
                {
                    blankCount++;
                }
            }

            if (blankCount > 0 &&
                MessageBox.Show(this, "有 " + blankCount + " 行的「部品名」与「部品コード」都为空，仍要保存吗？",
                    "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return false;
            }

            if (parts.Count == 0 &&
                MessageBox.Show(this, "表格中没有任何行，保存会清空该図番的全部部品明细。\n确定要继续吗？",
                    "确认清空", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return false;
            }

            string databasePath = _databasePath ?? PartBasicInfoRepository.ResolveDatabasePath();
            int written;

            try
            {
                using (PartBasicInfoRepository repository = new PartBasicInfoRepository(databasePath))
                {
                    written = repository.ReplaceByDrawing(_drawingNo, parts);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存到数据库失败：\n" + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            LoadParts();

            MessageBox.Show(this, "已保存 " + written + " 条部品基本信息。", "完成",
                MessageBoxButtons.OK, MessageBoxIcon.Information);

            return true;
        }

        /// <summary>「预览 PDF」：显示该図番对应的 PDF（窗口已打开时前置）。</summary>
        private void BtnPreviewPdf_Click(object sender, EventArgs e)
        {
            if (_pdfPath.Length == 0 || !File.Exists(_pdfPath))
            {
                MessageBox.Show(this,
                    "找不到对应的 PDF 文件：\n" + (_pdfPath.Length == 0 ? "(未指定)" : _pdfPath),
                    "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ShowPreviewWindow(true);
        }

        /// <summary>
        /// 显示（必要时创建）PDF 预览窗口并让它显示当前图番的 PDF。
        /// 本画面尚未显示时延后到 <see cref="OnShown"/> 执行，避免窗口出现在错误的屏幕 / 位置。
        /// </summary>
        /// <param name="activatePreview">是否把预览窗口提到最前（用户点【预览 PDF】时为 true）。</param>
        private void ShowPreviewWindow(bool activatePreview)
        {
            if (_pdfPath.Length == 0 || !File.Exists(_pdfPath))
            {
                return;
            }

            if (!Visible)
            {
                _openPreviewOnShown = true;
                return;
            }

            bool created = _previewForm == null || _previewForm.IsDisposed;

            if (created)
            {
                _previewForm = new PdfPreviewForm();
                _previewForm.FormClosed += PreviewForm_FormClosed;
                _previewForm.Show(this);
            }
            else if (!_previewForm.Visible)
            {
                _previewForm.Show(this);
            }

            if (!string.Equals(_previewForm.CurrentPath, _pdfPath, StringComparison.OrdinalIgnoreCase))
            {
                _previewForm.ShowPdf(_pdfPath);
            }

            bool placeEditor = !_editorPlaced;

            if (created && placeEditor)
            {
                // 首次打开：PDF 预览 → 屏幕 1，部品基本信息 → 屏幕 2（单屏时左右并排）
                _editorPlaced = true;
                WindowLayoutHelper.ArrangePreviewAndEditor(_previewForm, this);
            }
            else if (created)
            {
                // 用户关闭预览后再次打开：只把预览窗口放回屏幕 1
                WindowLayoutHelper.ArrangePreview(_previewForm);
            }
            else if (placeEditor)
            {
                // 预览窗口由主画面打开并已摆好，只把编辑画面移到屏幕 2
                _editorPlaced = true;
                WindowLayoutHelper.ArrangeEditor(this);
            }

            if (activatePreview)
            {
                _previewForm.BringToFront();
                _previewForm.Activate();
            }
        }

        /// <summary>把已打开的 PDF 预览窗口同步到当前图番的 PDF（不抢焦点）。</summary>
        private void SyncPreviewDocument()
        {
            if (_previewForm == null || _previewForm.IsDisposed || _pdfPath.Length == 0)
            {
                return;
            }

            if (!string.Equals(_previewForm.CurrentPath, _pdfPath, StringComparison.OrdinalIgnoreCase))
            {
                _previewForm.ShowPdf(_pdfPath);
            }
        }

        /// <summary>
        /// 接管主画面已经打开的 PDF 预览窗口（避免同时存在两个预览窗口）。
        /// 接管后由本画面负责切换内容，并在本画面关闭时一并关闭。
        /// </summary>
        /// <param name="previewForm">主画面已打开的预览窗口。</param>
        internal void AttachPreviewForm(PdfPreviewForm previewForm)
        {
            if (previewForm == null || previewForm.IsDisposed)
            {
                return;
            }

            if (_previewForm != null && !_previewForm.IsDisposed
                && !ReferenceEquals(_previewForm, previewForm))
            {
                PdfPreviewForm oldPreview = _previewForm;
                _previewForm = null;
                oldPreview.Close();
                oldPreview.Dispose();
            }

            _previewForm = previewForm;
            _previewForm.FormClosed += PreviewForm_FormClosed;
        }

        /// <summary>显示 / 前置本画面的 PDF 预览窗口（主画面复用同一窗口时调用）。</summary>
        /// <param name="bringToFront">是否把预览窗口提到最前。</param>
        internal void ShowPreview(bool bringToFront)
        {
            ShowPreviewWindow(bringToFront);
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

        /// <summary>「关闭」。</summary>
        private void BtnClose_Click(object sender, EventArgs e)
        {
            Close();
        }

        /// <summary>更新标题（有未保存的修改时追加提示）。</summary>
        private void UpdateTitle()
        {
            string title = _drawingNo.Length == 0 ? "部品基本信息" : "部品基本信息 - " + _drawingNo;

            Text = _isDirty ? title + "　＊有未保存的修改" : title;
            btnSave.Text = _isDirty ? "保存 ＊" : "保存";
        }

        /// <summary>设置「是否有未保存的修改」并刷新标题、统计标签。</summary>
        private void SetDirty(bool dirty)
        {
            _isDirty = dirty;
            UpdateTitle();
            RefreshGridSummary();
        }

        /// <summary>按表格当前内容统计条数与校验状态，并提示是否有未保存的修改。</summary>
        private void RefreshGridSummary()
        {
            int total = 0;
            int passedCount = 0;
            int failedCount = 0;
            int notCheckedCount = 0;

            foreach (DataGridViewRow row in dgvParts.Rows)
            {
                if (row.IsNewRow)
                {
                    continue;
                }

                total++;

                string status = GetCellText(row, "colVerifyStatus");

                if (status == PartVerifyStatus.Passed)
                {
                    passedCount++;
                }
                else if (status == PartVerifyStatus.Failed)
                {
                    failedCount++;
                }
                else
                {
                    notCheckedCount++;
                }
            }

            lblSummary.Text = string.Format("共 {0} 条（坐标一致 {1} / 不符 {2} / 未校验 {3}）{4}",
                total, passedCount, failedCount, notCheckedCount, _isDirty ? "　＊未保存" : string.Empty);
        }

        /// <summary>重新编号「No」列（追加/删除行后保持连续）。</summary>
        private void RenumberRows()
        {
            _suppressGridEvents = true;

            try
            {
                for (int index = 0; index < dgvParts.Rows.Count; index++)
                {
                    DataGridViewRow row = dgvParts.Rows[index];

                    if (row.IsNewRow)
                    {
                        continue;
                    }

                    row.Cells["colNo"].Value = (index + 1).ToString();
                }
            }
            finally
            {
                _suppressGridEvents = false;
            }
        }

        /// <summary>按校验状态给行着色（与 <see cref="PopulateParts"/> 的着色规则一致）。</summary>
        private static void ApplyRowStyle(DataGridViewRow row, string status)
        {
            if (row == null)
            {
                return;
            }

            if (status == PartVerifyStatus.Failed)
            {
                row.DefaultCellStyle.BackColor = Color.FromArgb(255, 235, 238);
            }
            else if (status == PartVerifyStatus.Passed)
            {
                row.DefaultCellStyle.BackColor = Color.White;
            }
            else
            {
                row.DefaultCellStyle.BackColor = Color.FromArgb(255, 250, 225);
            }
        }

        /// <summary>读取单元格文本（NULL 视为空字符串，并去掉首尾空白）。</summary>
        private static string GetCellText(DataGridViewRow row, string columnName)
        {
            if (row == null)
            {
                return string.Empty;
            }

            object value = row.Cells[columnName].Value;

            return value == null || value == DBNull.Value
                ? string.Empty
                : Convert.ToString(value).Trim();
        }

        /// <summary>判断是否为 <see cref="PartVerifyStatus"/> 中定义的状态。</summary>
        private static bool IsKnownVerifyStatus(string status)
        {
            if (string.IsNullOrEmpty(status))
            {
                return false;
            }

            foreach (string item in PartVerifyStatus.All)
            {
                if (item == status)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 有未保存的修改时询问：「是」= 保存后继续，「否」= 放弃修改继续，「取消」= 中止操作。
        /// </summary>
        /// <param name="action">后续动作的描述（如「切换图番」「关闭窗口」）。</param>
        /// <returns>true = 可以继续；false = 中止当前操作。</returns>
        private bool ConfirmUnsavedChanges(string action)
        {
            if (!_isDirty)
            {
                return true;
            }

            DialogResult result = MessageBox.Show(this,
                "有未保存的修改。\n\n是否先保存，再" + action + "？\n\n" +
                "「是」：保存后" + action + "\n「否」：放弃修改并" + action + "\n「取消」：留在当前画面",
                "确认", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1);

            if (result == DialogResult.Cancel)
            {
                return false;
            }

            if (result == DialogResult.Yes)
            {
                return SaveParts();
            }

            return true;
        }

        /// <summary>单元格内容变化时：刷新行颜色、统计与未保存标记。</summary>
        private void DgvParts_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_suppressGridEvents || e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            DataGridViewRow row = dgvParts.Rows[e.RowIndex];
            string columnName = dgvParts.Columns[e.ColumnIndex].Name;

            if (columnName == "colVerifyStatus")
            {
                ApplyRowStyle(row, GetCellText(row, "colVerifyStatus"));
            }

            if (columnName != "colNo")
            {
                SetDirty(true);
            }
        }

        /// <summary>表格内按 Delete 键删除选中的行。</summary>
        private void DgvParts_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete && !dgvParts.IsCurrentCellInEditMode)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                DeleteSelectedRows();
            }
        }

        /// <summary>画面内按 Ctrl+S 保存。</summary>
        private void PartBasicInfoForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.S)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                SaveParts();
            }
        }

        /// <summary>关闭前确认未保存的修改（系统关机/任务管理器结束时不询问）。</summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.WindowsShutDown ||
                e.CloseReason == CloseReason.TaskManagerClosing)
            {
                base.OnFormClosing(e);
                return;
            }

            if (!ConfirmUnsavedChanges("关闭窗口"))
            {
                e.Cancel = true;
                return;
            }

            base.OnFormClosing(e);
        }

        /// <summary>
        /// 窗口显示后与部品基本信息画面同时打开 PDF 预览窗口：
        /// PDF 预览 → 屏幕 1，部品基本信息 → 屏幕 2（只有 1 块屏幕时左右并排）。
        /// </summary>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            if (!_openPreviewOnShown)
            {
                return;
            }

            _openPreviewOnShown = false;
            ShowPreviewWindow(false);

            // 自动打开的预览窗口不抢焦点，焦点留在编辑画面
            Activate();
        }

        /// <summary>关闭本画面时一并关闭 PDF 预览窗口，释放文件句柄。</summary>
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            PdfPreviewForm preview = _previewForm;
            _previewForm = null;

            // Close() 会触发 FormClosed，所以先把引用取到局部变量再关闭
            if (preview != null && !preview.IsDisposed)
            {
                preview.Close();
                preview.Dispose();
            }

            base.OnFormClosed(e);
        }
    }
}
