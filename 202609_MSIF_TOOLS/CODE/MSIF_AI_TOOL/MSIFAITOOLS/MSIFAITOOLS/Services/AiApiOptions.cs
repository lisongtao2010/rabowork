using System;
using System.Configuration;
using System.Globalization;

namespace MSIFAITOOLS.Services
{
    /// <summary>
    /// AI（OpenAI 兼容接口）的连接配置。
    /// 取值优先级：环境变量 → AI 设置画面保存的用户配置（%APPDATA%\MSIFAITOOLS\ai-settings.json）
    /// → App.config 的 appSettings → 内置默认值。
    /// </summary>
    internal sealed class AiApiOptions
    {
        /// <summary>默认接口地址（OpenAI Compatible）。</summary>
        public const string DefaultBaseUrl = "https://api-ap-southeast-1.modelarts-maas.com/v1";

        /// <summary>默认模型。</summary>
        public const string DefaultModel = "deepseek-v4-flash";

        /// <summary>默认请求超时（秒）。</summary>
        public const int DefaultTimeoutSeconds = 180;

        /// <summary>App.config 中的接口地址键名。</summary>
        public const string BaseUrlSettingKey = "AiBaseUrl";

        /// <summary>App.config 中的密钥键名。</summary>
        public const string ApiKeySettingKey = "AiApiKey";

        /// <summary>App.config 中的模型键名。</summary>
        public const string ModelSettingKey = "AiModel";

        /// <summary>App.config 中的超时键名。</summary>
        public const string TimeoutSettingKey = "AiTimeoutSeconds";

        /// <summary>覆盖接口地址的环境变量。</summary>
        public const string BaseUrlEnvironmentKey = "MSIF_AI_BASE_URL";

        /// <summary>覆盖密钥的环境变量（优先级最高，便于临时替换密钥）。</summary>
        public const string ApiKeyEnvironmentKey = "MSIF_AI_API_KEY";

        /// <summary>覆盖模型的环境变量。</summary>
        public const string ModelEnvironmentKey = "MSIF_AI_MODEL";

        /// <summary>配置来源：环境变量。</summary>
        public const string SourceEnvironment = "环境变量";

        /// <summary>配置来源：AI 设置画面保存的用户配置。</summary>
        public const string SourceUserSettings = "AI 设置画面";

        /// <summary>配置来源：App.config。</summary>
        public const string SourceAppConfig = "App.config";

        /// <summary>配置来源：内置默认值。</summary>
        public const string SourceDefault = "内置默认值";

        public AiApiOptions(string baseUrl, string apiKey, string model, int timeoutSeconds,
            string source = SourceDefault)
        {
            BaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.Trim();
            ApiKey = string.IsNullOrWhiteSpace(apiKey) ? string.Empty : apiKey.Trim();
            Model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();
            TimeoutSeconds = timeoutSeconds <= 0 ? DefaultTimeoutSeconds : timeoutSeconds;
            ConfigurationSource = string.IsNullOrWhiteSpace(source) ? SourceDefault : source;
        }

        /// <summary>接口基础地址（不含 /chat/completions）。</summary>
        public string BaseUrl { get; private set; }

        /// <summary>API 密钥。</summary>
        public string ApiKey { get; private set; }

        /// <summary>模型名称。</summary>
        public string Model { get; private set; }

        /// <summary>单次请求超时（秒）。</summary>
        public int TimeoutSeconds { get; private set; }

        /// <summary>本次配置的实际来源（环境变量 / AI 设置画面 / App.config / 内置默认值）。</summary>
        public string ConfigurationSource { get; private set; }

        /// <summary>聊天补全接口的完整地址。</summary>
        public string Endpoint
        {
            get { return BaseUrl.TrimEnd('/') + "/chat/completions"; }
        }

        /// <summary>是否已配置密钥（未配置时不允许发起分析）。</summary>
        public bool IsConfigured
        {
            get { return ApiKey.Length > 0; }
        }

        /// <summary>
        /// 最后一次读取 App.config 失败的原因（为空表示读取正常）。
        /// 配置系统初始化失败（例如自定义节声明冲突）时 ConfigurationManager 会抛异常，
        /// 若不把原因暴露出来，界面只能报“未配置密钥”，无法定位问题。
        /// </summary>
        public static string LastConfigurationError { get; private set; }

        /// <summary>
        /// 从环境变量、AI 设置画面保存的用户配置与 App.config 读取配置。
        /// </summary>
        public static AiApiOptions FromConfiguration()
        {
            LastConfigurationError = string.Empty;
            LastSettingsError = string.Empty;

            string settingsError;
            AiSettings userSettings = AiSettingsStore.Load(out settingsError);
            LastSettingsError = settingsError;
            string source;

            string baseUrl = ReadSetting(BaseUrlEnvironmentKey, BaseUrlSettingKey, DefaultBaseUrl,
                userSettings == null ? null : userSettings.BaseUrl, out source);
            string apiKey = ReadSetting(ApiKeyEnvironmentKey, ApiKeySettingKey, string.Empty,
                userSettings == null ? null : userSettings.ApiKey, out source);
            // 以密钥的来源代表整体配置来源（密钥是判定“是否已配置”的关键）
            string apiKeySource = source;
            string model = ReadSetting(ModelEnvironmentKey, ModelSettingKey, DefaultModel,
                userSettings == null ? null : userSettings.Model, out source);

            int timeoutSeconds = DefaultTimeoutSeconds;
            string timeoutText = ReadSetting("MSIF_AI_TIMEOUT_SECONDS", TimeoutSettingKey,
                DefaultTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
                userSettings == null || userSettings.TimeoutSeconds <= 0
                    ? null
                    : userSettings.TimeoutSeconds.ToString(CultureInfo.InvariantCulture),
                out source);

            int parsedTimeout;

            if (!string.IsNullOrWhiteSpace(timeoutText) &&
                int.TryParse(timeoutText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out parsedTimeout) && parsedTimeout > 0)
            {
                timeoutSeconds = parsedTimeout;
            }

            return new AiApiOptions(baseUrl, apiKey, model, timeoutSeconds, apiKeySource);
        }

        /// <summary>用户配置文件读取失败的原因（为空表示正常或文件不存在）。</summary>
        public static string LastSettingsError { get; private set; }

        /// <summary>日志用的配置摘要（不输出完整密钥）。</summary>
        public string Describe()
        {
            return string.Format("接口 {0}，模型 {1}，密钥 {2}（来源：{3}）", BaseUrl, Model,
                MaskApiKey(), ConfigurationSource);
        }

        private string MaskApiKey()
        {
            if (ApiKey.Length == 0)
            {
                return "未配置";
            }

            if (ApiKey.Length <= 8)
            {
                return "********";
            }

            return ApiKey.Substring(0, 4) + "****" + ApiKey.Substring(ApiKey.Length - 4);
        }

        /// <summary>
        /// 按“环境变量 → 用户设置文件 → App.config → 默认值”的顺序取值，并记录实际来源。
        /// </summary>
        private static string ReadSetting(string environmentKey, string settingKey, string defaultValue,
            string userSettingValue, out string source)
        {
            string value = Environment.GetEnvironmentVariable(environmentKey);

            if (!string.IsNullOrWhiteSpace(value))
            {
                source = SourceEnvironment;
                return value.Trim();
            }

            if (!string.IsNullOrWhiteSpace(userSettingValue))
            {
                source = SourceUserSettings;
                return userSettingValue.Trim();
            }

            value = ReadAppSetting(settingKey);

            if (!string.IsNullOrWhiteSpace(value))
            {
                source = SourceAppConfig;
                return value.Trim();
            }

            source = SourceDefault;
            return defaultValue;
        }

        private static string ReadAppSetting(string key)
        {
            try
            {
                return ConfigurationManager.AppSettings[key];
            }
            catch (Exception ex)
            {
                // 配置文件损坏 / 配置节声明冲突时不影响程序启动，退化为内置默认值，
                // 但记录原因供界面提示（否则会表现为“密钥已填写却提示未配置”）。
                LastConfigurationError = ex.Message;
                return null;
            }
        }
    }
}
