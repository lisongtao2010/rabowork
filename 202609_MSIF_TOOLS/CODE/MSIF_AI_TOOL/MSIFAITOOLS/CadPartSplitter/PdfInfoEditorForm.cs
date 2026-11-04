using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace CadPartSplitter
{
    /// <summary>
    /// 「PDF 信息清单」维护窗口：直接编辑整批共用的
    /// <c>&lt;输出根目录&gt;\classify\PdfInfoList.txt</c>（図番号 / 文件名 / 图面分类 / 分类依据）。
    ///
    /// 为什么需要它：图面分类是自动判出来的（在 <c>tables\*.csv</c> 的单元格里找
    /// 「図番号前 4 位 + 基本図 / 加工図 / アッセンブリ図 / 要領図」），表題欄写得不规范、
    /// 或者在图纸上根本看不出来时这一列就会**空着**。空着会导致
    /// <c>01_アッセンブリ図_部品機能情報.csv</c> / <c>01_アッセンブリ図_要領図.csv</c> /
    /// <c>02_要領図_部品機能情報.csv</c> 这些「只有特定图种才生成」的产物不落盘。
    /// 所以这里给一个人工兜底：在表格里把「图面分类」填上 / 选上，保存后下一轮分割直接生效。
    ///
    /// 两条约定：
    ///   · 手工填过的行，「分类依据」记作「手工设定」（<see cref="PdfInfoListBuilder.ManualBasisPrefix"/>），
    ///     <see cref="PdfInfoListBuilder.Build"/> 看到这个前缀就不再改写这一行 —— 否则重跑一次人工修正就丢了；
    ///   · 自动判定这次判不出来、而清单里已经有分类时，也保留清单里的值。
    ///
    /// Designer 文件只负责控件摆放，事件绑定与全部逻辑都在这里。
    /// </summary>
    public partial class PdfInfoEditorForm : Form
    {
        /// <summary>「图面分类」下拉里给的四类（与 <see cref="DrawingListBuilder"/> 的关键字一致）。</summary>
        private static readonly string[] CategoryChoices = { "基本図", "加工図", "アッセンブリ図", "要領図" };

        /// <summary>控件提示（字段持有，避免被 GC 掉）。</summary>
        private readonly ToolTip _tip = new ToolTip();

        /// <summary>
        /// 「输出根目录」：清单在 <c>&lt;根&gt;\classify\</c> 下，各图纸的 <c>tables</c> 在
        /// <c>&lt;根&gt;\&lt;図番号&gt;\tables</c>。自动判定要靠它找表格 CSV；推不出来时为 null（只能手填）。
        /// </summary>
        private string _rootDirectory;

        /// <summary>程序在批量改写单元格时的嵌套深度：&gt; 0 时不当成「用户手工编辑」。</summary>
        private int _silent;

        /// <summary>有没有未保存的改动。</summary>
        private bool _dirty;

        /// <summary>设计器 / 反射用。</summary>
        public PdfInfoEditorForm()
            : this(null)
        {
        }

        /// <param name="listPath">
        /// 要编辑的清单文件全路径（<c>&lt;输出根目录&gt;\classify\PdfInfoList.txt</c>）；
        /// null / 空串表示由「浏览…」再选。
        /// </param>
        public PdfInfoEditorForm(string listPath)
        {
            InitializeComponent();
            WireEvents();
            SetupUi();

            if (!string.IsNullOrWhiteSpace(listPath))
                txtListPath.Text = listPath;
        }

        /// <summary>当前编辑的清单文件全路径（调用方用它记住「上次用的清单」）。</summary>
        public string ListPath
        {
            get { return txtListPath.Text.Trim(); }
        }

        // ── 初始化 ────────────────────────────────────────────────────────────

        /// <summary>集中绑定控件事件，保持 Designer 文件仅负责布局。</summary>
        private void WireEvents()
        {
            btnBrowse.Click        += btnBrowse_Click;
            btnReload.Click        += btnReload_Click;
            btnApplyCategory.Click += btnApplyCategory_Click;
            btnAutoSelected.Click  += btnAutoSelected_Click;
            btnAutoEmpty.Click     += btnAutoEmpty_Click;
            btnAddRow.Click        += btnAddRow_Click;
            btnDeleteRow.Click     += btnDeleteRow_Click;
            btnSave.Click          += btnSave_Click;
            btnClose.Click         += btnClose_Click;

            grid.CellValueChanged             += grid_CellValueChanged;
            grid.CurrentCellDirtyStateChanged += grid_CurrentCellDirtyStateChanged;
            grid.DataError                    += grid_DataError;
            grid.KeyDown                      += grid_KeyDown;

            Load        += PdfInfoEditorForm_Load;
            FormClosing += PdfInfoEditorForm_FormClosing;
        }

        /// <summary>下拉候选、提示语、初始状态。</summary>
        private void SetupUi()
        {
            cmbCategory.Items.Clear();
            cmbCategory.Items.AddRange(CategoryChoices);
            cmbCategory.SelectedIndex = 0;

            _tip.SetToolTip(txtListPath,
                "整批共用一份：<输出根目录>\\classify\\PdfInfoList.txt" + Environment.NewLine
                + "也可以点「浏览…」选别的位置（比如上一次批量的 AiAppData\\classify）。");
            _tip.SetToolTip(btnApplyCategory, "把上面选的分类填进选中的行，并在「分类依据」里标记「手工设定」");
            _tip.SetToolTip(btnAutoSelected, "对这些行重跑自动判定（读它自己的 tables\\*.csv）");
            _tip.SetToolTip(btnAutoEmpty, "只对「图面分类」还空着的行重跑自动判定");
            _tip.SetToolTip(btnAddRow, "手工补一行（一般只补「図番号」+「图面分类」，文件名会自动补）");
            _tip.SetToolTip(btnDeleteRow, "删掉选中的行（保存后才写回文件）");

            SetStatus("就绪：可直接编辑表格，改完点「保存」。");
        }

        // ── 载入 ──────────────────────────────────────────────────────────────

        private void PdfInfoEditorForm_Load(object sender, EventArgs e)
        {
            LoadList();
        }

        /// <summary>
        /// 读清单 → 填表格。清单文件不存在时按「新建」处理（表格清空，保存时创建）。
        /// </summary>
        private void LoadList()
        {
            string path = ListPath;

            if (path.Length == 0)
            {
                grid.Rows.Clear();
                SetStatus("请先指定清单文件（classify\\PdfInfoList.txt）。");
                return;
            }

            _rootDirectory = ResolveRootDirectory(path);

            var items = new List<PdfInfo>();

            if (File.Exists(path))
                items = PdfInfoListBuilder.ReadFile(path, CreateOptions());

            _silent++;
            try
            {
                grid.Rows.Clear();

                foreach (PdfInfo item in items)
                {
                    grid.Rows.Add(item.DrawingNumber ?? string.Empty,
                                  item.FileName ?? string.Empty,
                                  item.Category ?? string.Empty,
                                  item.MatchedCell ?? string.Empty);
                }
            }
            finally
            {
                _silent--;
            }

            _dirty = false;
            UpdateStatus();

            if (!File.Exists(path))
                SetStatus("清单还不存在（第一次跑完分割会有）：保存时新建 " + path);
        }

        // ── 重载 / 换文件 ─────────────────────────────────────────────────────

        private void btnReload_Click(object sender, EventArgs e)
        {
            if (!ConfirmDiscard())
                return;

            LoadList();
        }

        private void btnBrowse_Click(object sender, EventArgs e)
        {
            if (!ConfirmDiscard())
                return;

            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "选择分类清单（PdfInfoList.txt）";
                dialog.Filter = "分类清单 (PdfInfoList.txt)|PdfInfoList.txt"
                                + "|文本 / CSV (*.txt;*.csv)|*.txt;*.csv"
                                + "|所有文件 (*.*)|*.*";
                dialog.CheckFileExists = false;

                string current = ListPath;

                if (current.Length > 0)
                {
                    try
                    {
                        string directory = Path.GetDirectoryName(current);

                        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                            dialog.InitialDirectory = directory;
                    }
                    catch (ArgumentException)
                    {
                        // 路径畸形就算了，让对话框用默认目录
                    }
                }

                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                txtListPath.Text = dialog.FileName;
            }

            LoadList();
        }

        // ── 状态 ──────────────────────────────────────────────────────────────

        /// <summary>由清单路径推「输出根目录」：<c>&lt;根&gt;\classify\PdfInfoList.txt</c> → <c>&lt;根&gt;</c>。</summary>
        private static string ResolveRootDirectory(string listPath)
        {
            try
            {
                string directory = Path.GetDirectoryName(Path.GetFullPath(listPath));

                if (string.IsNullOrEmpty(directory))
                    return null;

                // 清单在 <根>\classify 里 → 上一层才是根；老位置（清单直接躺在图纸输出目录）就用它自己，
                // 此时自动判定靠 FindTablesDirectory 的第二个候选（<根>\tables）兜住
                string parent = Path.GetDirectoryName(directory);
                return string.IsNullOrEmpty(parent) ? directory : parent;
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
        }

        /// <summary>状态栏：行数 / 未设定分类的行数 / 有没有未保存改动。</summary>
        private void UpdateStatus()
        {
            int unclassified = 0;

            foreach (DataGridViewRow row in grid.Rows)
            {
                if (CellText(row, colCategory.Name).Length == 0)
                    unclassified++;
            }

            SetStatus("共 " + grid.Rows.Count + " 行"
                      + (unclassified > 0 ? "，其中 " + unclassified + " 行未设定图面分类（需要手工补）" : string.Empty)
                      + (_dirty ? "　·　未保存" : string.Empty));
        }

        /// <summary>状态栏一行字（自动判定 / 保存的日志也走这里，避免弹窗打扰）。</summary>
        private void SetStatus(string message)
        {
            if (lblStatus.IsDisposed)
                return;

            if (lblStatus.InvokeRequired)
            {
                lblStatus.BeginInvoke(new Action<string>(SetStatus), message);
                return;
            }

            lblStatus.Text = string.IsNullOrEmpty(message) ? "就绪" : message;
        }

        /// <summary>给清单读写 / 自动判定用的参数（日志直接落到窗口状态栏）。</summary>
        private DetectorOptions CreateOptions()
        {
            return new DetectorOptions { Logger = SetStatus };
        }

        // ── 保存 ──────────────────────────────────────────────────────────────

        private void btnSave_Click(object sender, EventArgs e)
        {
            SaveList();
        }

        /// <summary>
        /// 把表格写回清单。返回是否「已经是保存好的状态」（关窗时的询问复用它：
        /// 用户选「保存」但保存失败 → 返回 false → 不关窗）。
        /// </summary>
        private bool SaveList()
        {
            string path = ListPath;

            if (path.Length == 0)
            {
                MessageBox.Show(this, "请先指定清单文件（classify\\PdfInfoList.txt）。", "提示",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            grid.EndEdit();

            List<PdfInfo> items;
            string problem = CollectItems(out items);

            if (problem != null)
            {
                MessageBox.Show(this, problem, "清单内容有问题", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetStatus("未保存：" + problem);
                return false;
            }

            int unclassified = 0;

            foreach (PdfInfo item in items)
            {
                if (string.IsNullOrEmpty(item.Category))
                    unclassified++;
            }

            if (unclassified > 0
                && MessageBox.Show(this,
                       "有 " + unclassified + " 行还没设定「图面分类」，下游会按「未识别」处理"
                       + "（不生成 部品機能情報 / 要領図一覧 这些分图种产物）。" + Environment.NewLine + Environment.NewLine
                       + "仍然保存吗？",
                       "确认保存", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return false;
            }

            string error;

            if (!PdfInfoListBuilder.Save(path, items, CreateOptions(), out error))
            {
                MessageBox.Show(this, "保存失败：" + Environment.NewLine + error, "错误",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetStatus("保存失败：" + error);
                return false;
            }

            // 写回后让表格与文件一致（例如空着没填的「文件名」已被补成 <図番号>.pdf）
            _silent++;
            try
            {
                grid.Rows.Clear();

                foreach (PdfInfo item in items)
                {
                    grid.Rows.Add(item.DrawingNumber ?? string.Empty,
                                  item.FileName ?? string.Empty,
                                  item.Category ?? string.Empty,
                                  item.MatchedCell ?? string.Empty);
                }
            }
            finally
            {
                _silent--;
            }

            _dirty = false;
            SetStatus("已保存 " + items.Count + " 行 → " + path);
            return true;
        }

        /// <summary>
        /// 读出表格 → <see cref="PdfInfo"/> 列表。有问题时返回提示文字（null = 可以保存）：
        /// 図番号 不能空、不能重复（清单按図番号一行一条）；整行全空的忽略。
        /// </summary>
        private string CollectItems(out List<PdfInfo> items)
        {
            items = new List<PdfInfo>();

            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < grid.Rows.Count; i++)
            {
                DataGridViewRow row = grid.Rows[i];

                if (row.IsNewRow)
                    continue;

                string number = CellText(row, colNumber.Name);
                string fileName = CellText(row, colFile.Name);
                string category = CellText(row, colCategory.Name);
                string basis = CellText(row, colBasis.Name);

                if (number.Length == 0 && fileName.Length == 0 && category.Length == 0 && basis.Length == 0)
                    continue;                                   // 整行空白：忽略，不算错误

                if (number.Length == 0)
                    return "第 " + (i + 1) + " 行的「図番号」是空的 —— 补上（例如 H310U10004），或删掉这一行。";

                if (seen.ContainsKey(number))
                    return "「図番号」重复：" + number
                           + "（第 " + seen[number] + " 行 与 第 " + (i + 1) + " 行）"
                           + Environment.NewLine + "清单按図番号一行一条，请合并或删掉多余的那行。";

                seen[number] = i + 1;

                items.Add(new PdfInfo
                {
                    DrawingNumber = number,
                    FileName = fileName.Length > 0 ? fileName : number + ".pdf",
                    Category = category,
                    MatchedTable = null,        // 依据整段存在 MatchedCell 里（读清单时就是这么装的）
                    MatchedCell = basis
                });
            }

            if (items.Count == 0)
                return "表格里还没有任何一行 —— 点「添加行」先加一行，或者换一个清单文件。";

            return null;
        }

        // ── 关窗 ──────────────────────────────────────────────────────────────

        private void btnClose_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void PdfInfoEditorForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!ConfirmDiscard())
                e.Cancel = true;
        }

        /// <summary>有未保存改动时询问保存；返回 false = 用户取消（不要继续丢弃改动）。</summary>
        private bool ConfirmDiscard()
        {
            if (!_dirty)
                return true;

            DialogResult result = MessageBox.Show(this,
                "清单有未保存的改动，要先保存吗？", "未保存",
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

            if (result == DialogResult.Cancel)
                return false;

            if (result == DialogResult.No)
                return true;

            return SaveList();
        }

        // ── 表格编辑 ──────────────────────────────────────────────────────────

        /// <summary>
        /// 用户改了单元格：置「未保存」，并且 —— 改的是「图面分类」时 ——
        /// 把「分类依据」记成「手工设定」，下一轮分割的 <see cref="PdfInfoListBuilder.Build"/> 就不会把这个值覆盖掉。
        /// </summary>
        private void grid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_silent > 0 || e.RowIndex < 0)
                return;

            _dirty = true;

            if (e.ColumnIndex != grid.Columns[colCategory.Name].Index)
                return;

            DataGridViewRow row = grid.Rows[e.RowIndex];
            string category = CellText(row, colCategory.Name);

            if (category.Length == 0)
                return;

            string basis = CellText(row, colBasis.Name);

            SetCellSilently(row, colBasis.Name,
                basis.Length == 0 || basis.StartsWith(PdfInfoListBuilder.ManualBasisPrefix, StringComparison.Ordinal)
                    ? PdfInfoListBuilder.ManualBasisPrefix
                    : PdfInfoListBuilder.ManualBasisPrefix + "（原：" + basis + "）");

            UpdateStatus();
        }

        /// <summary>单元格还在编辑状态时 <c>CellValueChanged</c> 不触发：一脏就提交，保证「手工设定」及时写上。</summary>
        private void grid_CurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            if (grid.IsCurrentCellDirty)
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }

        private void grid_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            e.ThrowException = false;
            SetStatus("第 " + (e.RowIndex + 1) + " 行的单元格内容有问题：" + e.Exception.Message);
        }

        /// <summary>表格里 <c>Delete</c> 键等同「删除行」按钮。</summary>
        private void grid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Delete || grid.IsCurrentCellInEditMode)
                return;

            e.SuppressKeyPress = true;
            btnDeleteRow.PerformClick();
        }

        private void btnAddRow_Click(object sender, EventArgs e)
        {
            grid.EndEdit();

            int index = grid.Rows.Add(string.Empty, string.Empty, string.Empty, string.Empty);

            _dirty = true;
            grid.CurrentCell = grid.Rows[index].Cells[colNumber.Name];
            grid.BeginEdit(true);

            UpdateStatus();
            SetStatus("已添加一行：填「図番号」，「文件名」留空会自动补成 図番号.pdf。");
        }

        private void btnDeleteRow_Click(object sender, EventArgs e)
        {
            grid.EndEdit();

            List<DataGridViewRow> rows = SelectedRows();

            if (rows.Count == 0)
            {
                SetStatus("先在表格里选中要删除的行。");
                return;
            }

            if (MessageBox.Show(this, "删除选中的 " + rows.Count + " 行？（保存后才写回文件）", "确认删除",
                                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            for (int i = rows.Count - 1; i >= 0; i--)      // 从后往前删，前面的下标才不会失效
                grid.Rows.Remove(rows[i]);

            _dirty = true;
            UpdateStatus();
        }

        /// <summary>改单元格但不算「用户手工编辑」（不触发 <c>CellValueChanged</c> 里的标记逻辑）。</summary>
        private void SetCellSilently(DataGridViewRow row, string columnName, string text)
        {
            if (row == null)
                return;

            _silent++;
            try
            {
                row.Cells[columnName].Value = text;
            }
            finally
            {
                _silent--;
            }
        }

        // ── 填分类 / 自动判定 ─────────────────────────────────────────────────

        /// <summary>把下拉里选的分类填进选中行，并在「分类依据」标记「手工设定」。</summary>
        private void btnApplyCategory_Click(object sender, EventArgs e)
        {
            grid.EndEdit();

            string category = cmbCategory.Text.Trim();

            if (category.Length == 0)
            {
                SetStatus("先在「分类：」里选一个（基本図 / 加工図 / アッセンブリ図 / 要領図）。");
                return;
            }

            List<DataGridViewRow> rows = SelectedRows();

            if (rows.Count == 0)
            {
                SetStatus("先在表格里选中要填的行（可 Ctrl / Shift 多选）。");
                return;
            }

            foreach (DataGridViewRow row in rows)
            {
                SetCellSilently(row, colCategory.Name, category);
                SetCellSilently(row, colBasis.Name, PdfInfoListBuilder.ManualBasisPrefix);
            }

            _dirty = true;
            UpdateStatus();
            SetStatus("已把「" + category + "」填进 " + rows.Count + " 行（标记为手工设定，重跑分割不会被覆盖）。");
        }

        private void btnAutoSelected_Click(object sender, EventArgs e)
        {
            int count = AutoJudge(SelectedRows(), false);

            if (count == 0)
                SetStatus("选中的行都没判出来 —— 手工填「图面分类」更省事。");
        }

        private void btnAutoEmpty_Click(object sender, EventArgs e)
        {
            int count = AutoJudge(AllRows(), true);

            if (count == 0)
                SetStatus("没有能自动判出来的空白行 —— 手工填「图面分类」吧。");
        }

        /// <summary>
        /// 对指定行重跑自动判定：读这张图自己的 <c>tables\*.csv</c>
        /// （逐格找 → 找不到再按「同一行」找，与 <see cref="PdfInfoListBuilder.Build"/> 完全同一套规则），
        /// 命中就填「图面分类 + 分类依据」。
        /// </summary>
        /// <param name="rows">要判定的行。</param>
        /// <param name="onlyEmpty">true = 跳过已经有分类的行。</param>
        /// <returns>命中并填上的行数。</returns>
        private int AutoJudge(List<DataGridViewRow> rows, bool onlyEmpty)
        {
            if (rows == null || rows.Count == 0)
                return 0;

            grid.EndEdit();

            DetectorOptions opts = CreateOptions();
            int changed = 0;
            int missed = 0;

            foreach (DataGridViewRow row in rows)
            {
                if (onlyEmpty && CellText(row, colCategory.Name).Length > 0)
                    continue;

                string number = CellText(row, colNumber.Name);

                if (number.Length == 0)
                {
                    missed++;
                    continue;
                }

                string tablesDirectory = FindTablesDirectory(number);
                string cell = null;
                string table = null;

                if (tablesDirectory == null)
                {
                    missed++;
                    continue;
                }

                string category = DrawingListBuilder.Classify(number, tablesDirectory, opts, out cell, out table);

                if (string.IsNullOrEmpty(category))
                    category = PdfInfoListBuilder.ClassifyByRow(number, tablesDirectory, opts, out cell, out table);

                if (string.IsNullOrEmpty(category))
                {
                    missed++;
                    continue;
                }

                SetCellSilently(row, colCategory.Name, category);
                SetCellSilently(row, colBasis.Name,
                    string.IsNullOrEmpty(table)
                        ? CsvFile.SingleLine(cell)
                        : table + "：" + CsvFile.SingleLine(cell));
                changed++;
            }

            if (changed > 0)
                _dirty = true;

            UpdateStatus();
            SetStatus("自动判定：命中 " + changed + " 行"
                      + (missed > 0 ? "，" + missed + " 行没判出来（需要手工设）" : string.Empty) + "。");
            return changed;
        }

        // ── 辅助 ──────────────────────────────────────────────────────────────

        /// <summary>表格里全部数据行。</summary>
        private List<DataGridViewRow> AllRows()
        {
            var rows = new List<DataGridViewRow>(grid.Rows.Count);

            foreach (DataGridViewRow row in grid.Rows)
            {
                if (!row.IsNewRow)
                    rows.Add(row);
            }

            return rows;
        }

        /// <summary>
        /// 选中行（整行选中与单元格选中都算，按行号去重）；
        /// 一个都没选时退回「当前单元格所在行」，免得按钮点了没反应。
        /// </summary>
        private List<DataGridViewRow> SelectedRows()
        {
            var rows = new List<DataGridViewRow>();

            foreach (DataGridViewRow row in grid.SelectedRows)
            {
                if (!row.IsNewRow && !rows.Contains(row))
                    rows.Add(row);
            }

            foreach (DataGridViewCell cell in grid.SelectedCells)
            {
                DataGridViewRow row = cell.OwningRow;

                if (row != null && !row.IsNewRow && !rows.Contains(row))
                    rows.Add(row);
            }

            if (rows.Count == 0 && grid.CurrentRow != null && !grid.CurrentRow.IsNewRow)
                rows.Add(grid.CurrentRow);

            rows.Sort(delegate (DataGridViewRow a, DataGridViewRow b) { return a.Index.CompareTo(b.Index); });
            return rows;
        }

        /// <summary>
        /// 找这张图的 <c>tables</c> 目录：先按批量的 <c>&lt;根&gt;\&lt;図番号&gt;\tables</c>，
        /// 再按单张「没按図番号分子文件夹」的 <c>&lt;根&gt;\tables</c>；都不在返回 null。
        /// </summary>
        private string FindTablesDirectory(string drawingNumber)
        {
            if (string.IsNullOrEmpty(_rootDirectory))
                return null;

            var candidates = new List<string>(2);

            try
            {
                candidates.Add(Path.Combine(_rootDirectory, drawingNumber, "tables"));
                candidates.Add(Path.Combine(_rootDirectory, "tables"));
            }
            catch (ArgumentException)
            {
                return null;        // 図番号 里带 * ? 之类的非法字符
            }

            foreach (string directory in candidates)
            {
                if (Directory.Exists(directory))
                    return directory;
            }

            return null;
        }

        /// <summary>取一行里某列的文本（去首尾空白；空值给空串）。</summary>
        private static string CellText(DataGridViewRow row, string columnName)
        {
            if (row == null)
                return string.Empty;

            object value = row.Cells[columnName].Value;
            return value == null ? string.Empty : value.ToString().Trim();
        }
    }
}





