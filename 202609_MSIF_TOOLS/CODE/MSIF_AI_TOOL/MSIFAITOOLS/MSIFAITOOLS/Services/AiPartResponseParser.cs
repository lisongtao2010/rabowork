using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace MSIFAITOOLS.Services
{
    /// <summary>
    /// 解析 AI（deepseek 等 OpenAI 兼容模型）返回的部品明细 JSON。
    /// 期望格式：{"tables":[{"page":1,"rows":[{"番号":1,"部品名":"...","部品図番":"",...}]}]}
    /// 同时兼容顶层直接是 rows 数组、以及字段名使用英文别名的情形。
    /// </summary>
    internal static class AiPartResponseParser
    {
        private static readonly string[] NumberKeys = { "番号", "号", "number", "no", "seq" };

        private static readonly string[] PartNameKeys = { "部品名", "品名", "name", "part_name", "partName" };

        private static readonly string[] PartDrawingNoKeys =
            { "部品図番", "部品図番号", "図番", "part_drawing_no", "partNo", "drawing_no" };

        private static readonly string[] FrameKindKeys = { "枠区分", "区分", "frame", "frame_kind", "frameKind" };

        private static readonly string[] QuantityKeys = { "員数", "数量", "个数", "qty", "quantity" };

        private static readonly string[] BasicSizeKeys = { "基本寸法", "basic_size", "basicSize" };

        private static readonly string[] CutSizeKeys = { "切断寸法", "cut_size", "cutSize" };

        private static readonly string[] PartCodeKeys = { "部品コード", "コード", "part_code", "partCode", "code" };

        private static readonly string[] PageKeys = { "page", "页码", "页" };

        private static readonly string[] NotesKeys = { "notes", "note", "备注", "说明", "reason" };

        private static readonly string[] RowArrayKeys = { "rows", "部品基本信息", "部品基本信息列表", "parts", "items" };

        private static readonly string[] TableArrayKeys = { "tables", "表格", "部品表" };

        private static readonly Regex NumberPattern = new Regex(@"-?\d+", RegexOptions.Compiled);

        /// <summary>
        /// 把 AI 回复正文解析为部品基本信息列表。
        /// </summary>
        /// <param name="content">AI 回复的正文（可能带 Markdown 代码块）。</param>
        /// <param name="drawingNo">当前 PDF 的図番（写入每条记录）。</param>
        /// <param name="fallbackPage">JSON 中没有页码时使用的页码。</param>
        /// <param name="notes">收集解析过程中的提示信息（由调用方展示到日志）。</param>
        public static IList<PartBasicInfo> Parse(string content, string drawingNo, int fallbackPage,
            IList<string> notes)
        {
            List<PartBasicInfo> parts = new List<PartBasicInfo>();

            if (notes == null)
            {
                notes = new List<string>();
            }

            string json = ExtractJson(content);

            if (json.Length == 0)
            {
                throw new InvalidOperationException(
                    "AI 回复中没有找到 JSON 内容（前 200 字：" + Shorten(content) + "）");
            }

            object parsed;

            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = int.MaxValue;
                parsed = serializer.DeserializeObject(json);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "AI 返回的 JSON 无法解析（前 200 字：" + Shorten(content) + "）", ex);
            }

            Dictionary<string, object> root = parsed as Dictionary<string, object>;

            if (root != null)
            {
                string rootNotes = ReadString(root, NotesKeys);

                if (rootNotes.Length > 0)
                {
                    notes.Add(rootNotes);
                }

                object tablesValue;

                if (TryGetValueIgnoreCase(root, TableArrayKeys, out tablesValue))
                {
                    foreach (object table in AsArray(tablesValue))
                    {
                        Dictionary<string, object> tableMap = table as Dictionary<string, object>;

                        if (tableMap == null)
                        {
                            continue;
                        }

                        int page = ReadInt(tableMap, PageKeys, fallbackPage);
                        object rowsValue;

                        if (TryGetValueIgnoreCase(tableMap, RowArrayKeys, out rowsValue))
                        {
                            AppendRows(parts, rowsValue, drawingNo, page);
                        }
                    }

                    return parts;
                }

                object rowsOnly;

                if (TryGetValueIgnoreCase(root, RowArrayKeys, out rowsOnly))
                {
                    AppendRows(parts, rowsOnly, drawingNo, fallbackPage);

                    return parts;
                }

                notes.Add("AI 返回的 JSON 中没有 rows / tables 字段");

                return parts;
            }

            object[] topLevel = parsed as object[];

            if (topLevel != null)
            {
                AppendRows(parts, topLevel, drawingNo, fallbackPage);

                return parts;
            }

            notes.Add("AI 返回的 JSON 结构无法识别");

            return parts;
        }

        /// <summary>把一组行对象追加到结果列表（番号缺失或非数字的行会被跳过）。</summary>
        private static void AppendRows(List<PartBasicInfo> parts, object rowsValue, string drawingNo, int page)
        {
            foreach (object row in AsArray(rowsValue))
            {
                Dictionary<string, object> rowMap = row as Dictionary<string, object>;

                if (rowMap == null)
                {
                    continue;
                }

                int number;

                if (!TryReadNumber(rowMap, out number))
                {
                    continue;
                }

                int rowPage = ReadInt(rowMap, PageKeys, page);

                parts.Add(new PartBasicInfo(
                    drawingNo,
                    rowPage,
                    number,
                    ReadString(rowMap, PartNameKeys),
                    ReadString(rowMap, PartDrawingNoKeys),
                    ReadString(rowMap, FrameKindKeys),
                    ReadString(rowMap, QuantityKeys),
                    ReadString(rowMap, BasicSizeKeys),
                    ReadString(rowMap, CutSizeKeys),
                    ReadString(rowMap, PartCodeKeys)));
            }
        }

        /// <summary>把 object 统一转成可枚举的数组（JavaScriptSerializer 返回 object[]）。</summary>
        private static IEnumerable<object> AsArray(object value)
        {
            object[] objects = value as object[];

            if (objects != null)
            {
                return objects;
            }

            System.Collections.IEnumerable enumerable = value as System.Collections.IEnumerable;

            if (enumerable == null || value is string)
            {
                return new object[0];
            }

            List<object> items = new List<object>();

            foreach (object item in enumerable)
            {
                items.Add(item);
            }

            return items;
        }

        /// <summary>取出文本内容（去首尾空白，null 视为空字符串）。</summary>
        private static string ReadString(Dictionary<string, object> map, string[] keys)
        {
            object value;

            if (map == null || !TryGetValueIgnoreCase(map, keys, out value) || value == null)
            {
                return string.Empty;
            }

            if (value is Dictionary<string, object> || value is object[])
            {
                return string.Empty;
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture).Trim();
        }

        /// <summary>按候选键名取整数；取不到（或不是数字）时返回 <paramref name="fallback"/>。</summary>
        private static int ReadInt(Dictionary<string, object> map, string[] keys, int fallback)
        {
            string text = ReadString(map, keys);

            if (text.Length == 0)
            {
                return fallback;
            }

            Match match = NumberPattern.Match(XyCoordinateIndex.Normalize(text));

            if (!match.Success)
            {
                return fallback;
            }

            int parsed;

            if (int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                return parsed;
            }

            return fallback;
        }

        /// <summary>读取番号（必须是有效数字，否则认为该行不是明细数据）。</summary>
        private static bool TryReadNumber(Dictionary<string, object> map, out int number)
        {
            number = 0;

            string text = ReadString(map, NumberKeys);

            if (text.Length == 0)
            {
                return false;
            }

            Match match = NumberPattern.Match(XyCoordinateIndex.Normalize(text));

            if (!match.Success)
            {
                return false;
            }

            return int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number);
        }

        /// <summary>按候选键名逐个查找（先精确、再忽略大小写）。</summary>
        private static bool TryGetValueIgnoreCase(Dictionary<string, object> map, string[] keys, out object value)
        {
            value = null;

            if (map == null || keys == null)
            {
                return false;
            }

            foreach (string key in keys)
            {
                if (map.TryGetValue(key, out value))
                {
                    return true;
                }
            }

            foreach (string key in keys)
            {
                foreach (KeyValuePair<string, object> pair in map)
                {
                    if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                    {
                        value = pair.Value;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>从 AI 回复中截取 JSON 主体（去掉 Markdown 代码块与解释文字）。</summary>
        private static string ExtractJson(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return string.Empty;
            }

            int objectStart = content.IndexOf('{');
            int arrayStart = content.IndexOf('[');
            bool useObject = objectStart >= 0 && (arrayStart < 0 || objectStart < arrayStart);
            int start = useObject ? objectStart : arrayStart;

            if (start < 0)
            {
                return string.Empty;
            }

            char closeChar = useObject ? '}' : ']';
            int end = content.LastIndexOf(closeChar);

            if (end <= start)
            {
                return string.Empty;
            }

            return content.Substring(start, end - start + 1);
        }

        private static string Shorten(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return "（空）";
            }

            string singleLine = content.Replace("\r", " ").Replace("\n", " ").Trim();

            return singleLine.Length <= 200 ? singleLine : singleLine.Substring(0, 200) + "...";
        }
    }
}


