using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using MSIFAITOOLS.Services;

namespace MSIFAITOOLS
{
    /// <summary>
    /// AI 接口设置的独立画面：服务端点（下拉）、模型（下拉 + 可手工输入）、API Key、超时。
    /// 保存到 %APPDATA%\MSIFAITOOLS\ai-settings.json，保存后立即生效（无需重启程序）。
    /// </summary>
    public partial class AiSettingsForm : Form
    {
        /// <summary>测试连接时使用的最大超时（秒），避免界面长时间等待。</summary>
        private const int TestTimeoutSeconds = 30;

        /// <summary>模型下拉列表的常用预设（也允许手工输入其它名称）。</summary>
        private static readonly string[] ModelPresets =
        {
            "deepseek-v4-flash",
            "deepseek-v3",
            "deepseek-r1",
            "qwen2.5-72b-instruct",
            "gpt-4o-mini"
        };

        /// <summary>服务端点下拉列表的预设（最后一项为“自定义”）。</summary>
        private static readonly EndpointPreset[] EndpointPresets =
        {
            new EndpointPreset("ModelArts 新加坡（ap-southeast-1）", AiApiOptions.DefaultBaseUrl),
            new EndpointPreset("OpenAI 官方", "https://api.openai.com/v1"),
            new EndpointPreset("DeepSeek 官方", "https://api.deepseek.com/v1"),
            new EndpointPreset("本地 Ollama", "http://localhost:11434/v1"),
            new EndpointPreset("自定义（手动填写）", string.Empty)
        };

        /// <summary>加载配置期间抑制下拉框事件，避免覆盖已保存的接口地址。</summary>
        private bool _loading;

        /// <summary>正在执行“测试连接”（此时锁定输入与关闭）。</summary>
        private bool _busy;

        public AiSettingsForm()
        {
            _loading = true;
            InitializeComponent();
            _loading = false;

            lblSettingsPath.Text = "保存位置：" + AiSettingsStore.SettingsFilePath;
            LoadCurrentSettings();
        }

        /// <summary>用当前生效的配置（用户设置 → App.config → 默认值）填充控件。</summary>
        private void LoadCurrentSettings()
        {
            _loading = true;

            try
            {
                AiApiOptions current = AiApiOptions.FromConfiguration();

                txtBaseUrl.Text = current.BaseUrl;
                txtApiKey.Text = current.ApiKey;
                numTimeout.Value = ClampTimeout(current.TimeoutSeconds);

                FillEndpointPresets(current.BaseUrl);
                FillModelPresets(current.Model);

                if (!string.IsNullOrEmpty(AiApiOptions.LastSettingsError))
                {
                    lblStatus.ForeColor = Color.FromArgb(198, 40, 40);
                    lblStatus.Text = "用户配置文件读取失败：" + AiApiOptions.LastSettingsError;
                }
                else if (!string.IsNullOrEmpty(AiApiOptions.LastConfigurationError))
                {
                    lblStatus.ForeColor = Color.FromArgb(198, 40, 40);
                    lblStatus.Text = "App.config 读取失败：" + AiApiOptions.LastConfigurationError;
                }
                else
                {
                    lblStatus.ForeColor = Color.FromArgb(52, 73, 94);
                    lblStatus.Text = "当前配置来源：" + current.ConfigurationSource;
                }
            }
            finally
            {
                _loading = false;
            }
        }

        /// <summary>填充服务端点下拉列表，并选中与当前地址匹配的预设。</summary>
        private void FillEndpointPresets(string baseUrl)
        {
            cboEndpointPreset.Items.Clear();
            int matchedIndex = -1;

            for (int i = 0; i < EndpointPresets.Length; i++)
            {
                cboEndpointPreset.Items.Add(EndpointPresets[i]);

                if (EndpointPresets[i].BaseUrl.Length > 0 &&
                    string.Equals(EndpointPresets[i].BaseUrl, baseUrl, StringComparison.OrdinalIgnoreCase))
                {
                    matchedIndex = i;
                }
            }

            // 未匹配到预设时选中“自定义”，由用户决定是否修改地址
            cboEndpointPreset.SelectedIndex = matchedIndex >= 0 ? matchedIndex : EndpointPresets.Length - 1;
        }

        /// <summary>填充模型下拉列表；当前模型不在预设中时插入到首位并选中。</summary>
        private void FillModelPresets(string model)
        {
            cboModel.Items.Clear();

            foreach (string preset in ModelPresets)
            {
                cboModel.Items.Add(preset);
            }

            if (!string.IsNullOrWhiteSpace(model) && !cboModel.Items.Contains(model))
            {
                cboModel.Items.Insert(0, model);
            }

            cboModel.Text = string.IsNullOrWhiteSpace(model) ? AiApiOptions.DefaultModel : model;
        }

        /// <summary>把配置中的超时限制在下拉框允许的范围内。</summary>
        private decimal ClampTimeout(int timeoutSeconds)
        {
            decimal value = timeoutSeconds > 0 ? timeoutSeconds : AiApiOptions.DefaultTimeoutSeconds;

            if (value < numTimeout.Minimum)
            {
                value = numTimeout.Minimum;
            }

            if (value > numTimeout.Maximum)
            {
                value = numTimeout.Maximum;
            }

            return value;
        }

        /// <summary>选择服务端点预设时自动填写接口地址。</summary>
        private void CboEndpointPreset_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_loading)
            {
                return;
            }

            EndpointPreset preset = cboEndpointPreset.SelectedItem as EndpointPreset;

            if (preset == null || preset.BaseUrl.Length == 0)
            {
                // “自定义”保留当前地址，交给用户手工编辑
                txtBaseUrl.Focus();
                return;
            }

            txtBaseUrl.Text = preset.BaseUrl;
        }

        /// <summary>勾选“显示”时取消密钥掩码。</summary>
        private void ChkShowKey_CheckedChanged(object sender, EventArgs e)
        {
            txtApiKey.UseSystemPasswordChar = !chkShowKey.Checked;
        }

        /// <summary>保存设置并返回 DialogResult.OK（由主界面据此刷新提示）。</summary>
        private void BtnSave_Click(object sender, EventArgs e)
        {
            string error;
            AiSettings settings = BuildSettings(out error);

            if (settings == null)
            {
                ShowValidationError(error);
                return;
            }

            if (!AiSettingsStore.Save(settings, out error))
            {
                lblStatus.ForeColor = Color.FromArgb(198, 40, 40);
                lblStatus.Text = "保存失败：" + error;
                MessageBox.Show(this, "保存设置失败：\n" + error, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>取消（不写入配置文件）。</summary>
        private void BtnCancel_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        /// <summary>
        /// 用当前输入测试接口连通性（不写入配置文件），
        /// 便于在保存前确认地址、模型与密钥是否可用。
        /// </summary>
        private async void BtnTest_Click(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }

            string error;
            AiSettings settings = BuildSettings(out error);

            if (settings == null)
            {
                ShowValidationError(error);
                return;
            }

            // 测试连接使用较短超时，避免界面长时间无响应
            AiApiOptions options = new AiApiOptions(settings.BaseUrl, settings.ApiKey, settings.Model,
                Math.Min(settings.TimeoutSeconds, TestTimeoutSeconds), AiApiOptions.SourceUserSettings);

            SetBusy(true, "正在连接 " + options.Endpoint + " ...");

            try
            {
                // 用匿名方法（delegate）代替 Lambda 表达式：Task.Run 需要一个无参委托
                Func<string> testWork = delegate
                {
                    ChatCompletionClient client = new ChatCompletionClient(options);
                    return client.Complete("你是接口连通性测试助手。",
                        "请只回复 OK 两个字符，不要输出任何其它内容。");
                };

                string reply = await Task.Run(testWork);

                SetBusy(false, string.Empty);
                lblStatus.ForeColor = Color.FromArgb(46, 125, 50);
                lblStatus.Text = "连接成功，模型返回：" + Shorten(reply, 60);
            }
            catch (Exception ex)
            {
                SetBusy(false, string.Empty);
                lblStatus.ForeColor = Color.FromArgb(198, 40, 40);
                lblStatus.Text = "连接失败：" + Shorten(ex.Message, 200);
            }
        }

        /// <summary>打开用户配置所在目录，便于手工查看或备份。</summary>
        private void BtnOpenFolder_Click(object sender, EventArgs e)
        {
            try
            {
                Directory.CreateDirectory(AiSettingsStore.SettingsFolderPath);
                Process.Start("explorer.exe", "\"" + AiSettingsStore.SettingsFolderPath + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "打开配置目录失败：\n" + ex.Message, "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>把界面上的输入整理为设置对象；校验失败时返回 null 并给出原因。</summary>
        private AiSettings BuildSettings(out string error)
        {
            error = string.Empty;

            string baseUrl = txtBaseUrl.Text.Trim();
            string apiKey = txtApiKey.Text.Trim();
            string model = cboModel.Text.Trim();

            if (baseUrl.Length == 0)
            {
                error = "请填写接口地址（例如 https://api-ap-southeast-1.modelarts-maas.com/v1）。";
                return null;
            }

            Uri uri;

            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                error = "接口地址必须是 http:// 或 https:// 开头的完整地址。";
                return null;
            }

            if (apiKey.Length == 0)
            {
                error = "请填写 API Key。";
                return null;
            }

            if (model.Length == 0)
            {
                error = "请选择或填写模型名称。";
                return null;
            }

            return new AiSettings
            {
                BaseUrl = baseUrl,
                ApiKey = apiKey,
                Model = model,
                TimeoutSeconds = (int)numTimeout.Value
            };
        }

        /// <summary>显示校验提示并把焦点移到需要补填的控件。</summary>
        private void ShowValidationError(string message)
        {
            lblStatus.ForeColor = Color.FromArgb(239, 108, 0);
            lblStatus.Text = message;
            MessageBox.Show(this, message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            if (message.IndexOf("接口地址", StringComparison.Ordinal) >= 0)
            {
                txtBaseUrl.Focus();
            }
            else if (message.IndexOf("API Key", StringComparison.Ordinal) >= 0)
            {
                txtApiKey.Focus();
            }
            else if (message.IndexOf("模型", StringComparison.Ordinal) >= 0)
            {
                cboModel.Focus();
            }
        }

        /// <summary>测试连接期间锁定输入控件并显示进度文字。</summary>
        private void SetBusy(bool busy, string status)
        {
            _busy = busy;
            UseWaitCursor = busy;

            cboEndpointPreset.Enabled = !busy;
            txtBaseUrl.Enabled = !busy;
            cboModel.Enabled = !busy;
            txtApiKey.Enabled = !busy;
            chkShowKey.Enabled = !busy;
            numTimeout.Enabled = !busy;
            btnTest.Enabled = !busy;
            btnSave.Enabled = !busy;
            btnTest.Text = busy ? "测试中..." : "测试连接";

            if (!string.IsNullOrEmpty(status))
            {
                lblStatus.ForeColor = Color.FromArgb(230, 126, 34);
                lblStatus.Text = status;
            }
        }

        /// <summary>测试连接进行中不允许关闭窗口，避免回调访问已释放的控件。</summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_busy)
            {
                e.Cancel = true;
                lblStatus.ForeColor = Color.FromArgb(230, 126, 34);
                lblStatus.Text = "测试连接进行中，请稍候...";
                return;
            }

            base.OnFormClosing(e);
        }

        /// <summary>把多行文本压成单行并截断，用于状态栏显示。</summary>
        private static string Shorten(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "（空）";
            }

            string singleLine = text.Replace("\r", " ").Replace("\n", " ").Trim();

            return singleLine.Length <= maxLength
                ? singleLine
                : singleLine.Substring(0, maxLength) + "...";
        }

        /// <summary>服务端点下拉列表项。</summary>
        private sealed class EndpointPreset
        {
            public EndpointPreset(string name, string baseUrl)
            {
                Name = name;
                BaseUrl = baseUrl;
            }

            /// <summary>下拉列表中显示的名称。</summary>
            public string Name { get; private set; }

            /// <summary>接口地址；为空表示“自定义（手动填写）”。</summary>
            public string BaseUrl { get; private set; }

            public override string ToString()
            {
                return Name;
            }
        }
    }
}
