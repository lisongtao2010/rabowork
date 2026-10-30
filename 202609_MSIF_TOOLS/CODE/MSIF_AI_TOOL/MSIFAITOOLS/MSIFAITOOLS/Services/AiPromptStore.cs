using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace MSIFAITOOLS.Services
{
    /// <summary>
    /// 一次加载得到的 AI 提示语：系统提示词 + 用户提示词模板，
    /// 以及来源说明（外部文件 / 内置资源 / 内置默认值）。
    /// </summary>
    internal sealed class AiPromptSet
    {
        public AiPromptSet(string systemPrompt, string userTemplate, string source, bool isExternal)
        {
            SystemPrompt = string.IsNullOrEmpty(systemPrompt) ? string.Empty : systemPrompt.Trim();
            UserTemplate = string.IsNullOrEmpty(userTemplate) ? string.Empty : userTemplate.Trim();
            Source = source ?? string.Empty;
            IsExternal = isExternal;
        }

        /// <summary>系统提示词（角色与输出约束）。</summary>
        public string SystemPrompt { get; private set; }

        /// <summary>用户提示词模板（含 {{DRAWING_NO}} / {{TABLE_TEXT}} / {{#COORDINATE}} 占位符）。</summary>
        public string UserTemplate { get; private set; }

        /// <summary>提示语来源说明（文件路径等，用于写入运行日志）。</summary>
        public string Source { get; private set; }

        /// <summary>是否来自外部文件（可在程序运行时直接修改）。</summary>
        public bool IsExternal { get; private set; }

        /// <summary>提示语是否完整（系统提示词与用户模板都不为空）。</summary>
        public bool IsValid
        {
            get { return SystemPrompt.Length > 0 && UserTemplate.Length > 0; }
        }

        /// <summary>按占位符生成实际发送给 AI 的用户提示词。</summary>
        /// <param name="drawingNo">図番。</param>
        /// <param name="tableSnippet">表格文本片段。</param>
        /// <param name="coordinateSnippet">坐标片段；为空时不输出坐标段。</param>
        public string BuildUserPrompt(string drawingNo, string tableSnippet, string coordinateSnippet)
        {
            string text = UserTemplate;

            text = ReplaceToken(text, "DRAWING_NO", drawingNo);
            text = ApplyCoordinateBlock(text, string.IsNullOrEmpty(coordinateSnippet)
                ? string.Empty
                : coordinateSnippet);
            text = ReplaceToken(text, "TABLE_TEXT", tableSnippet);

            return text;
        }

        /// <summary>替换 {{NAME}} 形式的占位符。</summary>
        private static string ReplaceToken(string text, string token, string value)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            return text.Replace("{{" + token + "}}", value ?? string.Empty);
        }

        /// <summary>
        /// 处理 {{#COORDINATE}} ... {{/COORDINATE}} 条件块：
        /// 有坐标时保留块内文字并替换 {{COORDINATE_TEXT}}，没有坐标时整块删除。
        /// </summary>
        private static string ApplyCoordinateBlock(string text, string coordinateSnippet)
        {
            const string openToken = "{{#COORDINATE}}";
            const string closeToken = "{{/COORDINATE}}";

            int start = text.IndexOf(openToken, StringComparison.Ordinal);

            if (start < 0)
            {
                return ReplaceToken(text, "COORDINATE_TEXT", coordinateSnippet);
            }

            int end = text.IndexOf(closeToken, start + openToken.Length, StringComparison.Ordinal);

            if (end < 0)
            {
                string withoutOpen = text.Remove(start, openToken.Length).Replace(closeToken, string.Empty);

                return ReplaceToken(withoutOpen, "COORDINATE_TEXT", coordinateSnippet);
            }

            string inner = text.Substring(start + openToken.Length, end - start - openToken.Length);
            inner = string.IsNullOrEmpty(coordinateSnippet)
                ? string.Empty
                : ReplaceToken(inner, "COORDINATE_TEXT", coordinateSnippet);

            return text.Substring(0, start) + inner + text.Substring(end + closeToken.Length);
        }
    }

    /// <summary>
    /// AI 提示语的加载器：优先读取 exe 目录下的 Prompts\AiPartAnalyzer.Prompt.txt
    /// （可运行时修改、无需重新编译），不可用时回退到编译进 exe 的同名内嵌资源，
    /// 最后回退到内置默认值。文件内容变化后（按修改时间判断）自动重新加载。
    /// </summary>
    internal static class AiPromptStore
    {
        /// <summary>提示语文件所在目录（相对 exe 目录）。</summary>
        public const string PromptDirectoryName = "Prompts";

        /// <summary>提示语文件名。</summary>
        public const string PromptFileName = "AiPartAnalyzer.Prompt.txt";

        /// <summary>内嵌资源名（编译进 exe 的同一份提示语文件）。</summary>
        public const string EmbeddedResourceName = "MSIFAITOOLS.Prompts.AiPartAnalyzer.Prompt.txt";

        /// <summary>来源说明：外部文件。</summary>
        public const string SourceExternal = "外部文件";

        /// <summary>来源说明：内嵌资源。</summary>
        public const string SourceEmbedded = "内置资源";

        /// <summary>来源说明：内置默认值。</summary>
        public const string SourceDefault = "内置默认值";

        private const string SectionSystem = "system";

        private const string SectionUser = "user";

        /// <summary>兜底用的系统提示词（仅在外部文件与内置资源都不可用时使用）。</summary>
        private const string DefaultSystemPrompt =
            "你是建筑门窗图纸（MSIF）部品明细表的解析专家。你只输出一个 JSON 对象，" +
            "不输出解释文字，不输出 Markdown 代码块，不输出注释。";

        /// <summary>兜底用的用户提示词模板（仅在外部文件与内置资源都不可用时使用）。</summary>
        private const string DefaultUserTemplate =
            "图番：{{DRAWING_NO}}\n\n" +
            "任务：从【表格文本】中找出表头含「番号」「部品名」的明细行，" +
            "只输出 JSON：{\"tables\":[{\"page\":1,\"rows\":[{\"番号\":1,\"部品名\":\"\"}]}]}\n\n" +
            "【表格文本】\n{{TABLE_TEXT}}\n" +
            "{{#COORDINATE}}{{COORDINATE_TEXT}}{{/COORDINATE}}";

        private static readonly object SyncRoot = new object();

        private static AiPromptSet _cache;

        private static string _cacheKey;

        private static string _lastLoadError;

        /// <summary>最近一次读取提示语文件/资源失败的原因（正常时为空）。</summary>
        public static string LastLoadError
        {
            get { return _lastLoadError ?? string.Empty; }
        }

        /// <summary>提示语文件的全路径（exe 目录\Prompts\AiPartAnalyzer.Prompt.txt）。</summary>
        public static string PromptFilePath
        {
            get
            {
                return Path.Combine(ResolveBaseDirectory(), PromptDirectoryName, PromptFileName);
            }
        }

        /// <summary>
        /// 取提示语所在的基础目录：优先 exe 目录；若 exe 目录下没有 Prompts 文件夹，
        /// 则退回到程序集所在目录（便于被其他宿主进程加载时仍能找到提示语）。
        /// </summary>
        private static string ResolveBaseDirectory()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;

            try
            {
                if (!string.IsNullOrEmpty(baseDirectory) &&
                    Directory.Exists(Path.Combine(baseDirectory, PromptDirectoryName)))
                {
                    return baseDirectory;
                }

                string location = typeof(AiPromptStore).Assembly.Location;

                if (!string.IsNullOrEmpty(location))
                {
                    string assemblyDirectory = Path.GetDirectoryName(location);

                    if (!string.IsNullOrEmpty(assemblyDirectory) &&
                        Directory.Exists(Path.Combine(assemblyDirectory, PromptDirectoryName)))
                    {
                        return assemblyDirectory;
                    }
                }
            }
            catch (Exception)
            {
                // 目录探测失败时按默认 exe 目录处理
            }

            return string.IsNullOrEmpty(baseDirectory)
                ? AppDomain.CurrentDomain.BaseDirectory
                : baseDirectory;
        }

        /// <summary>加载提示语（外部文件优先，文件变化后自动重新加载）。</summary>
        public static AiPromptSet Load()
        {
            string path = PromptFilePath;

            lock (SyncRoot)
            {
                string key = BuildCacheKey(path);

                if (_cache != null && string.Equals(_cacheKey, key, StringComparison.Ordinal))
                {
                    return _cache;
                }

                _cache = LoadCore(path);
                _cacheKey = key;

                return _cache;
            }
        }

        /// <summary>清空缓存（下次 Load 必定重新读取文件）。</summary>
        public static void ClearCache()
        {
            lock (SyncRoot)
            {
                _cache = null;
                _cacheKey = null;
                _lastLoadError = null;
            }
        }

        /// <summary>缓存键：文件路径 + 修改时间 + 大小；文件不存在时使用固定标记。</summary>
        private static string BuildCacheKey(string path)
        {
            try
            {
                FileInfo info = new FileInfo(path);

                if (info.Exists)
                {
                    return info.FullName + "|" + info.LastWriteTimeUtc.Ticks.ToString() + "|" +
                           info.Length.ToString();
                }
            }
            catch (Exception ex)
            {
                SetLoadError("检查提示语文件失败（" + path + "）：" + ex.Message);
            }

            return "no-file";
        }


        /// <summary>读取顺序：外部文件 → 内置资源 → 内置默认值。</summary>
        private static AiPromptSet LoadCore(string path)
        {
            _lastLoadError = null;

            AiPromptSet external = BuildSet(TryReadFile(path), SourceExternal + "：" + path, true);

            if (external != null)
            {
                return external;
            }

            AiPromptSet embedded = BuildSet(TryReadEmbedded(), SourceEmbedded + "（" + EmbeddedResourceName + "）",
                false);

            if (embedded != null)
            {
                return embedded;
            }

            return new AiPromptSet(DefaultSystemPrompt, DefaultUserTemplate, SourceDefault, false);
        }

        /// <summary>把提示语文本解析成提示语集合；缺少 [system] / [user] 时返回 null。</summary>
        private static AiPromptSet BuildSet(string text, string source, bool isExternal)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            IDictionary<string, string> sections = ParseSections(text);

            if (!sections.ContainsKey(SectionSystem) || !sections.ContainsKey(SectionUser))
            {
                SetLoadError("提示语缺少 [" + SectionSystem + "] 或 [" + SectionUser + "] 分节：" + source);

                return null;
            }

            AiPromptSet set = new AiPromptSet(sections[SectionSystem], sections[SectionUser], source, isExternal);

            if (!set.IsValid)
            {
                SetLoadError("提示语内容为空：" + source);

                return null;
            }

            return set;
        }

        /// <summary>
        /// 按 [节名] 切分文本。第一个分节之前的非注释行忽略；
        /// 分节内的内容原样保留（包括空行与 # 开头的行）。
        /// </summary>
        private static IDictionary<string, string> ParseSections(string text)
        {
            Dictionary<string, string> sections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrEmpty(text))
            {
                return sections;
            }

            StringBuilder current = null;
            string currentName = null;
            bool seenSection = false;

            using (StringReader reader = new StringReader(text))
            {
                string line;

                while ((line = reader.ReadLine()) != null)
                {
                    string trimmed = line.Trim();

                    if (trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[trimmed.Length - 1] == ']')
                    {
                        FlushSection(sections, currentName, current);
                        currentName = trimmed.Substring(1, trimmed.Length - 2).Trim();
                        current = new StringBuilder();
                        seenSection = true;

                        continue;
                    }

                    if (!seenSection)
                    {
                        // 文件开头的说明/注释（# 开头）与空行不参与解析
                        continue;
                    }

                    if (current == null)
                    {
                        continue;
                    }

                    current.AppendLine(line);
                }
            }

            FlushSection(sections, currentName, current);

            return sections;
        }

        /// <summary>把当前分节内容写入字典（同名分节多次出现时按顺序拼接）。</summary>
        private static void FlushSection(IDictionary<string, string> sections, string name, StringBuilder content)
        {
            if (string.IsNullOrWhiteSpace(name) || content == null)
            {
                return;
            }

            string text = content.ToString().Trim();

            if (text.Length == 0)
            {
                return;
            }

            if (sections.ContainsKey(name))
            {
                sections[name] = (sections[name] + "\n" + text).Trim();
            }
            else
            {
                sections[name] = text;
            }
        }

        /// <summary>读取外部提示语文件（UTF-8，自动识别 BOM）。</summary>
        private static string TryReadFile(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    SetLoadError("未找到提示语文件（将使用内置提示语）：" + path);

                    return null;
                }

                return File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                SetLoadError("读取提示语文件失败（" + path + "）：" + ex.Message);

                return null;
            }
        }

        /// <summary>读取编译进 exe 的提示语资源。</summary>
        private static string TryReadEmbedded()
        {
            try
            {
                Assembly assembly = typeof(AiPromptStore).Assembly;

                using (Stream stream = assembly.GetManifestResourceStream(EmbeddedResourceName))
                {
                    if (stream == null)
                    {
                        SetLoadError("未找到内置提示语资源：" + EmbeddedResourceName);

                        return null;
                    }

                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        return reader.ReadToEnd();
                    }
                }
            }
            catch (Exception ex)
            {
                SetLoadError("读取内置提示语资源失败：" + ex.Message);

                return null;
            }
        }

        /// <summary>记录加载过程中的问题（同时清空缓存，便于下次重新尝试）。</summary>
        private static void SetLoadError(string message)
        {
            _lastLoadError = message ?? string.Empty;
        }
    }
}

