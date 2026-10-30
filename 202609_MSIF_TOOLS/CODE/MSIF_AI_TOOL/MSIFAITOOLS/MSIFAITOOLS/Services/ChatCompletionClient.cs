using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace MSIFAITOOLS.Services
{
    /// <summary>可重试的接口异常（超时、限流、5xx）。</summary>
    internal sealed class AiTransientException : Exception
    {
        public AiTransientException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// OpenAI 兼容的 /chat/completions 客户端（只依赖 .NET Framework 自带的 HttpClient）。
    /// 请求体手工拼装、响应体用 JavaScriptSerializer 解析，避免额外 NuGet 依赖。
    /// </summary>
    internal sealed class ChatCompletionClient
    {
        /// <summary>进程内共享的 HttpClient（复用连接，避免端口耗尽）。</summary>
        private static readonly HttpClient SharedClient = new HttpClient();

        private const int MaxAttemptCount = 3;

        private readonly AiApiOptions _options;

        public ChatCompletionClient(AiApiOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (!options.IsConfigured)
            {
                throw new InvalidOperationException(
                    "未配置 AI 接口密钥：请在程序内【AI 设置】中填写 API Key" +
                    "（也可使用 App.config 的 AiApiKey 或环境变量 MSIF_AI_API_KEY）。");
            }

            _options = options;
        }

        /// <summary>
        /// 发送一次对话补全请求，返回助手回复的正文。
        /// 超时 / 429 / 5xx 会自动重试，其余错误直接抛出。
        /// </summary>
        /// <param name="systemPrompt">系统提示词。</param>
        /// <param name="userPrompt">用户提示词。</param>
        public string Complete(string systemPrompt, string userPrompt)
        {
            string requestBody = BuildRequestBody(systemPrompt, userPrompt);
            Exception lastError = null;

            for (int attempt = 1; attempt <= MaxAttemptCount; attempt++)
            {
                try
                {
                    return SendOnce(requestBody);
                }
                catch (AiTransientException ex)
                {
                    lastError = ex;

                    if (attempt < MaxAttemptCount)
                    {
                        Thread.Sleep(attempt * 2000);
                    }
                }
            }

            throw new InvalidOperationException(
                "AI 接口调用失败：" + (lastError == null ? "未知原因" : lastError.Message), lastError);
        }

        /// <summary>真正发起 HTTP 请求并解析回复。</summary>
        private string SendOnce(string requestBody)
        {
            byte[] payload = Encoding.UTF8.GetBytes(requestBody);

            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint))
            using (CancellationTokenSource timeoutSource =
                new CancellationTokenSource(TimeSpan.FromSeconds(_options.TimeoutSeconds)))
            {
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _options.ApiKey);
                request.Content = new ByteArrayContent(payload);
                request.Content.Headers.ContentType =
                    new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };

                HttpResponseMessage response = null;
                string responseText;

                try
                {
                    response = SharedClient.SendAsync(request, timeoutSource.Token).GetAwaiter().GetResult();
                    responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                }
                catch (OperationCanceledException ex)
                {
                    throw new AiTransientException("请求超时（" + _options.TimeoutSeconds + " 秒）", ex);
                }
                catch (HttpRequestException ex)
                {
                    throw new AiTransientException("网络错误：" + ex.Message, ex);
                }

                using (response)
                {
                    int statusCode = (int)response.StatusCode;

                    if (statusCode == 429 || statusCode >= 500)
                    {
                        throw new AiTransientException("接口返回 " + statusCode + "：" + Shorten(responseText), null);
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException("接口返回 " + statusCode + "：" + Shorten(responseText));
                    }
                }

                return ExtractContent(responseText);
            }
        }

        /// <summary>拼装 OpenAI 兼容的请求体。</summary>
        private string BuildRequestBody(string systemPrompt, string userPrompt)
        {
            StringBuilder builder = new StringBuilder();

            builder.Append("{\"model\":\"").Append(EscapeJson(_options.Model)).Append("\",");
            builder.Append("\"temperature\":0,");
            builder.Append("\"stream\":false,");
            builder.Append("\"messages\":[");
            builder.Append("{\"role\":\"system\",\"content\":\"").Append(EscapeJson(systemPrompt)).Append("\"},");
            builder.Append("{\"role\":\"user\",\"content\":\"").Append(EscapeJson(userPrompt)).Append("\"}");
            builder.Append("]}");

            return builder.ToString();
        }

        /// <summary>从响应 JSON 中取出 choices[0].message.content。</summary>
        private static string ExtractContent(string responseText)
        {
            object parsed;

            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = int.MaxValue;
                parsed = serializer.DeserializeObject(responseText);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "无法解析 AI 接口返回的 JSON：" + Shorten(responseText), ex);
            }

            Dictionary<string, object> root = parsed as Dictionary<string, object>;

            if (root == null)
            {
                throw new InvalidOperationException("AI 接口返回格式异常：" + Shorten(responseText));
            }

            object choicesValue;

            if (!root.TryGetValue("choices", out choicesValue))
            {
                throw new InvalidOperationException("AI 接口返回中没有 choices：" + Shorten(responseText));
            }

            object[] choices = choicesValue as object[];

            if (choices == null || choices.Length == 0)
            {
                throw new InvalidOperationException("AI 接口返回的 choices 为空：" + Shorten(responseText));
            }

            Dictionary<string, object> firstChoice = choices[0] as Dictionary<string, object>;
            object messageValue;
            Dictionary<string, object> message;

            if (firstChoice == null ||
                !firstChoice.TryGetValue("message", out messageValue) ||
                (message = messageValue as Dictionary<string, object>) == null)
            {
                throw new InvalidOperationException("AI 接口返回中没有 message：" + Shorten(responseText));
            }

            object contentValue;

            if (!message.TryGetValue("content", out contentValue) || contentValue == null)
            {
                return string.Empty;
            }

            return Convert.ToString(contentValue, CultureInfo.InvariantCulture);
        }

        /// <summary>把文本转成 JSON 字符串字面量内容（不含首尾引号，含内部的转义）。</summary>
        public static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(value.Length + 16);

            foreach (char c in value)
            {
                switch (c)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;

                    case '\\':
                        builder.Append("\\\\");
                        break;

                    case '\r':
                        builder.Append("\\r");
                        break;

                    case '\n':
                        builder.Append("\\n");
                        break;

                    case '\t':
                        builder.Append("\\t");
                        break;

                    case '\b':
                        builder.Append("\\b");
                        break;

                    case '\f':
                        builder.Append("\\f");
                        break;

                    default:
                        if (c < ' ')
                        {
                            builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(c);
                        }

                        break;
                }
            }

            return builder.ToString();
        }

        /// <summary>截断过长的响应文本，便于放进日志与异常信息。</summary>
        private static string Shorten(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "（空）";
            }

            string singleLine = text.Replace("\r", " ").Replace("\n", " ").Trim();

            return singleLine.Length <= 300 ? singleLine : singleLine.Substring(0, 300) + "...";
        }
    }
}
