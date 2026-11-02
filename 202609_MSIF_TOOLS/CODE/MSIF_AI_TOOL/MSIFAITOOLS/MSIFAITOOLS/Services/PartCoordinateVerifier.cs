using System;
using System.Collections.Generic;
using System.IO;

namespace MSIFAITOOLS.Services
{
    /// <summary>
    /// 用 xy.txt 的文字坐标校验部品明细（【AI 分析】与【CMD 分析】共用）。
    ///
    /// 判定规则（两边共用同一套，保证「校验状态」的含义一致）：
    ///   · 部品名 / 部品コード / 部品図番 必须出现在该条明细的页码上 → <see cref="PartVerifyStatus.Passed"/>；
    ///   · 有任一项找不到（或只出现在其他页） → <see cref="PartVerifyStatus.Failed"/>，明细写进备注；
    ///   · 三项都是空值 / "-" / 「下表参照」之类的指示性文字 → <see cref="PartVerifyStatus.NotChecked"/>。
    /// </summary>
    internal static class PartCoordinateVerifier
    {
        /// <summary>
        /// 加载坐标索引；文件不存在、解析失败、解析不到任何文字时返回 null，
        /// 并把原因记到 <paramref name="notes"/>（不抛异常，校验降级为「未校验」）。
        /// </summary>
        public static XyCoordinateIndex LoadOrNull(string xyFilePath, IList<string> notes)
        {
            if (string.IsNullOrWhiteSpace(xyFilePath) || !File.Exists(xyFilePath))
            {
                AddNote(notes, "未找到坐标文件（xy.txt），本次跳过坐标校验");

                return null;
            }

            try
            {
                XyCoordinateIndex index = XyCoordinateIndex.Load(xyFilePath);

                if (index.WordCount == 0)
                {
                    AddNote(notes, "坐标文件没有解析到任何文字，本次跳过坐标校验");

                    return null;
                }

                return index;
            }
            catch (Exception ex)
            {
                AddNote(notes, "坐标文件解析失败，本次跳过坐标校验：" + ex.Message);

                return null;
            }
        }

        /// <summary>
        /// 逐条校验 <paramref name="parts"/>，写入「校验状态」与「备注」，并把统计记到 <paramref name="notes"/>。
        /// </summary>
        public static void Verify(IList<PartBasicInfo> parts, XyCoordinateIndex coordinates, IList<string> notes)
        {
            if (parts == null || parts.Count == 0 || coordinates == null)
            {
                return;
            }

            int passed = 0;
            int failed = 0;
            int skipped = 0;

            foreach (PartBasicInfo part in parts)
            {
                List<string> problems = new List<string>();
                bool anyChecked = false;

                CheckValue(coordinates, part.Page, part.PartName, "部品名", problems, ref anyChecked);
                CheckValue(coordinates, part.Page, part.PartCode, "部品コード", problems, ref anyChecked);
                CheckValue(coordinates, part.Page, part.PartDrawingNo, "部品図番", problems, ref anyChecked);

                if (!anyChecked)
                {
                    part.VerifyStatus = PartVerifyStatus.NotChecked;
                    skipped++;
                }
                else if (problems.Count == 0)
                {
                    part.VerifyStatus = PartVerifyStatus.Passed;
                    passed++;
                }
                else
                {
                    part.VerifyStatus = PartVerifyStatus.Failed;
                    part.Remark = string.Join("；", problems.ToArray());
                    failed++;
                }
            }

            AddNote(notes, string.Format("坐标校验：一致 {0} 条，不符 {1} 条，无有效文字可校验 {2} 条",
                passed, failed, skipped));
        }

        /// <summary>校验单个字段（空值、"-"、"下表参照" 之类不参与校验）。</summary>
        private static void CheckValue(XyCoordinateIndex coordinates, int page, string value, string label,
            List<string> problems, ref bool anyChecked)
        {
            if (!IsCheckableValue(value))
            {
                return;
            }

            anyChecked = true;

            XyMatchResult result = coordinates.Verify(page, value);

            if (result == XyMatchResult.Found)
            {
                return;
            }

            if (result == XyMatchResult.FoundOnOtherPage)
            {
                problems.Add(label + "「" + value + "」只出现在其他页");
            }
            else if (result == XyMatchResult.NotFound)
            {
                problems.Add(label + "「" + value + "」坐标未找到");
            }
        }

        /// <summary>判断字段是否值得做坐标校验（排除空值、单符号、指示性文字）。</summary>
        private static bool IsCheckableValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string normalized = XyCoordinateIndex.Normalize(value);

            if (normalized.Length < 2)
            {
                return false;
            }

            if (normalized.IndexOf("参照", StringComparison.Ordinal) >= 0 ||
                normalized.IndexOf("下表", StringComparison.Ordinal) >= 0 ||
                normalized.IndexOf("同上", StringComparison.Ordinal) >= 0)
            {
                return false;
            }

            foreach (char c in normalized)
            {
                if (char.IsLetterOrDigit(c))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>把提示追加到 notes（notes 允许为 null）。</summary>
        private static void AddNote(IList<string> notes, string message)
        {
            if (notes != null && !string.IsNullOrEmpty(message))
            {
                notes.Add(message);
            }
        }
    }
}
