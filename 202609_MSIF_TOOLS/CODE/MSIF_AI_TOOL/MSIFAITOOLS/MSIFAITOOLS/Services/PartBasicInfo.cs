using System;

namespace MSIFAITOOLS.Services
{
    /// <summary>
    /// 从 PDF 表格（*_plu_table.txt）中提取的一条部品基本信息。
    /// 字段与 SQLite 的「部品基本信息」表一一对应，一处改动需同步另一处。
    /// </summary>
    internal sealed class PartBasicInfo
    {
        public PartBasicInfo(string drawingNo, int page, int number, string partName, string partDrawingNo,
            string frameKind, string quantity, string basicSize, string cutSize, string partCode)
        {
            DrawingNo = Clean(drawingNo);
            Page = page < 0 ? 0 : page;
            Number = number;
            PartName = Clean(partName);
            PartDrawingNo = Clean(partDrawingNo);
            FrameKind = Clean(frameKind);
            Quantity = Clean(quantity);
            BasicSize = Clean(basicSize);
            CutSize = Clean(cutSize);
            PartCode = Clean(partCode);
            VerifyStatus = PartVerifyStatus.NotChecked;
            Remark = string.Empty;
            SourceFile = string.Empty;
            PdfFile = string.Empty;
            LoginTime = string.Empty;
        }

        /// <summary>図番（PDF 文件名去掉扩展名）。</summary>
        public string DrawingNo { get; private set; }

        /// <summary>表格所在页码（从 1 开始；无法判定时为 0）。</summary>
        public int Page { get; private set; }

        /// <summary>番号。</summary>
        public int Number { get; private set; }

        /// <summary>部品名。</summary>
        public string PartName { get; private set; }

        /// <summary>部品図番（表格没有该列时为空）。</summary>
        public string PartDrawingNo { get; private set; }

        /// <summary>枠区分。</summary>
        public string FrameKind { get; private set; }

        /// <summary>員数（可能是数字或字母，故按文本保存）。</summary>
        public string Quantity { get; private set; }

        /// <summary>基本寸法。</summary>
        public string BasicSize { get; private set; }

        /// <summary>切断寸法。</summary>
        public string CutSize { get; private set; }

        /// <summary>部品コード。</summary>
        public string PartCode { get; private set; }

        /// <summary>坐标校验状态（取值见 <see cref="PartVerifyStatus"/>）。</summary>
        public string VerifyStatus { get; set; }

        /// <summary>备注：坐标校验明细、AI 提示等。</summary>
        public string Remark { get; set; }

        /// <summary>来源文件（*_plu_table.txt 的完整路径）。</summary>
        public string SourceFile { get; set; }

        /// <summary>来源 PDF 的完整路径。</summary>
        public string PdfFile { get; set; }

        /// <summary>登录时间（从数据库读出时填充；新提取的对象为空）。</summary>
        public string LoginTime { get; set; }

        /// <summary>用于日志/界面的简短描述。</summary>
        public string ToShortText()
        {
            return string.Format("#{0} {1}（{2}）", Number, PartName, PartCode);
        }

        private static string Clean(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }
    }

    /// <summary>部品基本信息的坐标校验状态（同时作为写入数据库的文字）。</summary>
    internal static class PartVerifyStatus
    {
        /// <summary>部品名 / 部品コード 在 _xy.txt 的对应页中全部找到。</summary>
        public const string Passed = "坐标一致";

        /// <summary>有部品名 / 部品コード 未能在 _xy.txt 中找到。</summary>
        public const string Failed = "坐标不符";

        /// <summary>没有 _xy.txt（或无法解析），未做校验。</summary>
        public const string NotChecked = "未校验";

        /// <summary>全部状态（画面下拉框的候选项）。</summary>
        public static readonly string[] All = new string[] { Passed, Failed, NotChecked };
    }
}
