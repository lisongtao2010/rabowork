using System;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Windows.Forms;

namespace WindowsFormsApp1
{
    public partial class Form1 : Form
    {
        private TextBox txtFolder;
        private Button btnBrowseFolder;
        private Button btnSplit;
        private Label lblStatus;

        public Form1()
        {
            InitializeComponent();
            SetupCustomGui();
        }

        // 动态初始化控件，画面无需手动拖拽
        private void SetupCustomGui()
        {
            this.Text = "PDF 分解工具 (坐标 + 表格)";
            this.Size = new System.Drawing.Size(620, 200);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;

            // 1. PDF 文件夹（里面所有 PDF 都会被分解）
            Label lblFolder = new Label { Text = "PDF 文件夹:", Location = new System.Drawing.Point(20, 23), AutoSize = true };
            txtFolder = new TextBox { Location = new System.Drawing.Point(130, 20), Size = new System.Drawing.Size(330, 23) };
            btnBrowseFolder = new Button { Text = "浏览...", Location = new System.Drawing.Point(470, 19), Size = new System.Drawing.Size(100, 25) };
            btnBrowseFolder.Click += BtnBrowseFolder_Click;

            // 2. 一键分解按钮与状态
            btnSplit = new Button
            {
                Text = "一键分解 (坐标 + 表格)",
                Location = new System.Drawing.Point(130, 65),
                Size = new System.Drawing.Size(330, 38),
                Font = new System.Drawing.Font(this.Font, System.Drawing.FontStyle.Bold)
            };
            btnSplit.Click += BtnSplit_Click;

            lblStatus = new Label { Text = "状态: 就绪", Location = new System.Drawing.Point(20, 125), AutoSize = true, ForeColor = System.Drawing.Color.Blue };

            // 将控件添加到窗口
            this.Controls.Add(lblFolder);
            this.Controls.Add(txtFolder);
            this.Controls.Add(btnBrowseFolder);

            this.Controls.Add(btnSplit);
            this.Controls.Add(lblStatus);
        }

        // 选择 PDF 所在文件夹（図面文件夹）
        private void BtnBrowseFolder_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog fbd = new FolderBrowserDialog())
            {
                fbd.Description = "选择存放 PDF 図面的文件夹";
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    txtFolder.Text = fbd.SelectedPath;
                }
            }
        }

        // 一键分解：把文件夹内所有 PDF 生成 <図番号>_xy.txt（坐标）和 <図番号>_table.txt（表格）
        private void BtnSplit_Click(object sender, EventArgs e)
        {
            string folder = txtFolder.Text.Trim();

            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                MessageBox.Show("请确认已选择有效的 PDF 文件夹！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 统计文件夹中的 PDF 数量（严格匹配 .pdf 扩展名）
            string[] pdfFiles = Array.FindAll(
                Directory.GetFiles(folder),
                f => string.Equals(Path.GetExtension(f), ".pdf", StringComparison.OrdinalIgnoreCase));

            if (pdfFiles.Length == 0)
            {
                MessageBox.Show("该文件夹中没有找到 PDF 文件！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string scriptPath = Path.Combine(Application.StartupPath, "PDF_SPLIT.py");

            if (!File.Exists(scriptPath))
            {
                MessageBox.Show($"未找到 Python 脚本：\n{scriptPath}\n\n请确保 PDF_SPLIT.py 与生成的 exe 放在同一个目录下！",
                                "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            btnSplit.Enabled = false;
            lblStatus.Text = $"状态: 正在分解中（共 {pdfFiles.Length} 个PDF），请稍候...";
            lblStatus.ForeColor = System.Drawing.Color.OrangeRed;
            Application.DoEvents();

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = $"\"{scriptPath}\" \"{folder}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

            try
            {
                using (Process proc = Process.Start(psi))
                {
                    if (proc != null)
                    {
                        // 异步读取 stdout（脚本会输出处理进度），避免输出量大时管道堵塞
                        System.Threading.Tasks.Task<string> stdoutTask = proc.StandardOutput.ReadToEndAsync();
                        string err = proc.StandardError.ReadToEnd();
                        proc.WaitForExit();
                        stdoutTask.Wait();

                        if (proc.ExitCode == 0)
                        {
                            string xyDir = Path.Combine(folder, "xy");
                            string tableDir = Path.Combine(folder, "table");

                            lblStatus.Text = $"状态: 分解成功！{pdfFiles.Length} 个PDF → xy 文件夹 + table 文件夹";
                            lblStatus.ForeColor = System.Drawing.Color.Green;
                            MessageBox.Show($"批量分解成功！\n\n共处理 {pdfFiles.Length} 个PDF\n\n坐标文件（*_xy.txt）：\n{xyDir}\n\n表格文件（*_table.txt）：\n{tableDir}",
                                            "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        else
                        {
                            lblStatus.Text = "状态: 分解失败（详见报错信息）！";
                            lblStatus.ForeColor = System.Drawing.Color.Red;
                            MessageBox.Show($"执行失败，Python 报错：\n{err}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                lblStatus.Text = "状态: 异常";
                lblStatus.ForeColor = System.Drawing.Color.Red;
                MessageBox.Show($"启动 Python 失败：\n{ex.Message}\n请检查 Python 是否已加入环境变量。", "异常", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnSplit.Enabled = true;
            }
        }
    }
}