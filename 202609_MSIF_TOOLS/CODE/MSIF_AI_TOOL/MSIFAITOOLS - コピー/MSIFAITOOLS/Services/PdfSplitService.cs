using System;
using System.Collections.Generic;
using System.IO;

namespace MSIFAITOOLS.Services
{
    /// <summary>日志级别，供界面按级别着色。</summary>
    internal enum LogKind
    {
        /// <summary>一般信息。</summary>
        Info,

        /// <summary>成功信息。</summary>
        Success,

        /// <summary>警告信息。</summary>
        Warning,

        /// <summary>错误信息。</summary>
        Error
    }

    /// <summary>单个文件的处理进度。</summary>
    internal sealed class PdfSplitProgress
    {
        public PdfSplitProgress(int index, int total, string fileName, string stage)
        {
            Index = index;
            Total = total;
            FileName = fileName;
            Stage = stage;
        }

        /// <summary>当前处理到第几个文件（从 1 开始）。</summary>
        public int Index { get; }

        /// <summary>待处理的 PDF 总数。</summary>
        public int Total { get; }

        /// <summary>当前文件名（含扩展名）。</summary>
        public string FileName { get; }

        /// <summary>当前阶段说明。</summary>
        public string Stage { get; }

        /// <summary>完成百分比（0-100）。</summary>
        public int Percent
        {
            get { return Total <= 0 ? 0 : (int)Math.Round(Index * 100.0 / Total); }
        }
    }

    /// <summary>批量分解的执行结果。</summary>
    internal sealed class PdfSplitResult
    {
        public PdfSplitResult(int total, int succeeded, IList<string> errors,
            string xyDirectory, string tableDirectory, string pluDirectory,
            string classifyDirectory, IList<PdfClassification> classifications)
        {
            Total = total;
            Succeeded = succeeded;
            Errors = new List<string>(errors ?? new string[0]).AsReadOnly();
            XyDirectory = xyDirectory;
            TableDirectory = tableDirectory;
            PluDirectory = pluDirectory;
            ClassifyDirectory = classifyDirectory;
            Classifications = classifications == null
                ? new List<PdfClassification>().AsReadOnly()
                : classifications;
        }

        /// <summary>PDF 总数。</summary>
        public int Total { get; }

        /// <summary>完全成功的 PDF 数量。</summary>
        public int Succeeded { get; }

        /// <summary>失败明细，每条为 “文件名: 失败原因”。</summary>
        public IList<string> Errors { get; }

        /// <summary>坐标输出目录（&lt;文件夹&gt;\xy）。</summary>
        public string XyDirectory { get; }

        /// <summary>文本输出目录（&lt;文件夹&gt;\table）。</summary>
        public string TableDirectory { get; }

        /// <summary>plu 系列输出目录（&lt;文件夹&gt;\plu）。</summary>
        public string PluDirectory { get; }

        /// <summary>分类结果目录（&lt;文件夹&gt;\classify）。</summary>
        public string ClassifyDirectory { get; }

        /// <summary>每个 PDF 的分类结果（与文件名顺序一致）。</summary>
        public IList<PdfClassification> Classifications { get; }

        /// <summary>是否存在失败项。</summary>
        public bool HasErrors
        {
            get { return Errors.Count > 0; }
        }

        /// <summary>失败数量。</summary>
        public int Failed
        {
            get { return Total - Succeeded; }
        }
    }

    /// <summary>
    /// PDF 批量分解服务（纯 .NET 实现，替代原 PDF_SPLIT.py）。
    /// 对指定文件夹第一层的所有 PDF 生成三类结果：
    ///   1) &lt;文件夹&gt;\xy\&lt;図番号&gt;_xy.txt    —— 每页文字及其坐标；
    ///   2) &lt;文件夹&gt;\table\&lt;図番号&gt;_table.txt —— 每页整页文本（阅读顺序）；
    ///   3) &lt;文件夹&gt;\plu\&lt;図番号&gt;_plu_*.txt  —— pdfplumber 风格的五份结果
    ///      （_plu_text / _plu_layout / _plu_table / _plu_image / _plu_all）；
    ///      其中 _plu_image 为“图上文字”：按图片区域与文字坐标比对判定，
    ///      表格块内的文字不重复计入；
    ///   4) &lt;文件夹&gt;\classify\classification.csv —— 每个 PDF 的分类结果
    ///      （基本図 / 加工図 / アッセンブリ図 / 要領図 / 未知）。
    /// </summary>
    internal sealed class PdfSplitService
    {
        /// <summary>坐标结果子文件夹名。</summary>
        public const string XyFolderName = "xy";

        /// <summary>文本结果子文件夹名。</summary>
        public const string TableFolderName = "table";

        /// <summary>坐标文件名后缀。</summary>
        public const string XyFileSuffix = "_xy.txt";

        /// <summary>文本文件名后缀。</summary>
        public const string TableFileSuffix = "_table.txt";

        /// <summary>plu 系列结果子文件夹名。</summary>
        public const string PluFolderName = "plu";

        /// <summary>plu 纯文本后缀（对应 pdfplumber 的 extract_text()）。</summary>
        public const string PluTextFileSuffix = "_plu_text.txt";

        /// <summary>plu 版面文本后缀（对应 pdfplumber 的 extract_text(layout=True)）。</summary>
        public const string PluLayoutFileSuffix = "_plu_layout.txt";

        /// <summary>plu 表格后缀（对应 pdfplumber 的 extract_tables()）。</summary>
        public const string PluTableFileSuffix = "_plu_table.txt";

        /// <summary>plu 图上文字后缀（图片区域与文字坐标比对的结果）。</summary>
        public const string PluImageFileSuffix = "_plu_image.txt";

        /// <summary>plu 文本 + 表格合并后缀。</summary>
        public const string PluAllFileSuffix = "_plu_all.txt";

        /// <summary>
        /// 找出文件夹第一层中所有 PDF 文件，返回按序号（Ordinal）排序的文件名（不含路径）。
        /// 与原脚本一致：扩展名不区分大小写，且不进入子文件夹。
        /// </summary>
        /// <param name="folder">待扫描的文件夹。</param>
        public static string[] FindPdfFiles(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                throw new ArgumentException("文件夹路径不能为空。", nameof(folder));
            }

            if (!Directory.Exists(folder))
            {
                throw new DirectoryNotFoundException("找不到文件夹: " + folder);
            }

            List<string> names = new List<string>();

            foreach (string path in Directory.GetFiles(folder, "*", SearchOption.TopDirectoryOnly))
            {
                if (string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    names.Add(Path.GetFileName(path));
                }
            }

            // 使用序数比较，排序结果与 Python 的 sorted() 一致
            names.Sort(StringComparer.Ordinal);

            return names.ToArray();
        }

        /// <summary>
        /// 批量分解文件夹中的所有 PDF。
        /// </summary>
        /// <param name="folder">PDF 所在文件夹。</param>
        /// <param name="onProgress">进度回调，可为 null。</param>
        /// <param name="onLog">日志回调，可为 null。</param>
        /// <returns>分解结果。</returns>
        public PdfSplitResult Split(string folder, Action<PdfSplitProgress> onProgress, Action<LogKind, string> onLog)
        {
            string[] pdfNames = FindPdfFiles(folder);

            if (pdfNames.Length == 0)
            {
                throw new InvalidOperationException("该文件夹中没有找到 PDF 文件！");
            }

            string xyDirectory = Path.Combine(folder, XyFolderName);
            string tableDirectory = Path.Combine(folder, TableFolderName);
            string pluDirectory = Path.Combine(folder, PluFolderName);
            string classifyDirectory = Path.Combine(folder, PdfClassifier.ClassifyFolderName);
            Directory.CreateDirectory(xyDirectory);
            Directory.CreateDirectory(tableDirectory);
            Directory.CreateDirectory(pluDirectory);
            Directory.CreateDirectory(classifyDirectory);

            ReportLog(onLog, LogKind.Info, "坐标输出目录：" + xyDirectory);
            ReportLog(onLog, LogKind.Info, "文本输出目录：" + tableDirectory);
            ReportLog(onLog, LogKind.Info, "plu 输出目录：" + pluDirectory);
            ReportLog(onLog, LogKind.Info, "分类结果目录：" + classifyDirectory);

            List<string> errors = new List<string>();
            int total = pdfNames.Length;
            int succeeded = 0;

            for (int index = 0; index < total; index++)
            {
                string fileName = pdfNames[index];
                string pdfPath = Path.Combine(folder, fileName);
                // 図番号 = PDF 文件名（不含扩展名）
                string stem = Path.GetFileNameWithoutExtension(fileName);

                ReportProgress(onProgress, new PdfSplitProgress(index + 1, total, fileName, "正在处理"));
                ReportLog(onLog, LogKind.Info,
                    string.Format("[{0}/{1}] 正在处理: {2}", index + 1, total, fileName));

                bool failed = false;

                // 1) 文字 + 坐标（替代 PyMuPDF）
                if (!RunStep(onLog, errors, fileName, "坐标提取 (PdfPig)", delegate
                    {
                        PdfTextExtractor.WriteWordCoordinates(pdfPath, Path.Combine(xyDirectory, stem + XyFileSuffix));
                    }))
                {
                    failed = true;
                }

                // 2) 整页文本（替代 MarkItDown）
                if (!RunStep(onLog, errors, fileName, "文本提取 (PdfPig)", delegate
                    {
                        PdfTextExtractor.WriteDocumentText(pdfPath, Path.Combine(tableDirectory, stem + TableFileSuffix));
                    }))
                {
                    failed = true;
                }

                // 3) plu：pdfplumber 风格五份输出（一次解析，同时写 5 个文件）
                if (!RunStep(onLog, errors, fileName, "plu 提取", delegate
                    {
                        PdfTextExtractor.WritePluSet(
                            pdfPath,
                            Path.Combine(pluDirectory, stem + PluTextFileSuffix),
                            Path.Combine(pluDirectory, stem + PluLayoutFileSuffix),
                            Path.Combine(pluDirectory, stem + PluTableFileSuffix),
                            Path.Combine(pluDirectory, stem + PluAllFileSuffix),
                            Path.Combine(pluDirectory, stem + PluImageFileSuffix));
                    }))
                {
                    failed = true;
                }

                if (!failed)
                {
                    succeeded++;
                    ReportLog(onLog, LogKind.Success, fileName + " 已生成坐标、文本及 plu 五种文件。");
                }
            }

            if (errors.Count > 0)
            {
                foreach (string message in errors)
                {
                    ReportLog(onLog, LogKind.Error, "ERROR: " + message);
                }

                ReportLog(onLog, LogKind.Error,
                    string.Format("FINISHED {0}/{1}（有失败）", succeeded, total));
            }
            else
            {
                ReportLog(onLog, LogKind.Success, string.Format("SUCCESS {0}/{1}", succeeded, total));
            }

            // 分解完成后按図番号规则分类（分类失败不影响分解结果）
            IList<PdfClassification> classifications =
                ClassifyAll(folder, pdfNames, pluDirectory, classifyDirectory, onLog);

            return new PdfSplitResult(total, succeeded, errors, xyDirectory, tableDirectory, pluDirectory,
                classifyDirectory, classifications);
        }

        /// <summary>对本次处理的 PDF 分类，写出汇总 CSV，并把每项结果写入日志。</summary>
        private static IList<PdfClassification> ClassifyAll(string folder, string[] pdfNames, string pluDirectory,
            string classifyDirectory, Action<LogKind, string> onLog)
        {
            IList<PdfClassification> items;

            try
            {
                items = PdfClassifier.ClassifyFiles(folder, pdfNames, pluDirectory);
            }
            catch (Exception ex)
            {
                ReportLog(onLog, LogKind.Error, "分类失败：" + ex.Message);
                return new List<PdfClassification>();
            }

            ReportLog(onLog, LogKind.Info, "--------------------------------------------------");
            ReportLog(onLog, LogKind.Info, "分类结果：" + PdfClassifier.BuildSummaryText(items));

            foreach (PdfClassification item in items)
            {
                bool needChoice = item.NeedsUserChoice;

                ReportLog(onLog, needChoice ? LogKind.Warning : LogKind.Info,
                    string.Format("  {0} → {1}{2}（{3}）",
                        item.FileName, item.CategoryName,
                        needChoice ? "，需在画面上选择" : string.Empty,
                        item.Evidence));
            }

            try
            {
                string summaryPath = PdfClassifier.SaveSummary(classifyDirectory, items);
                ReportLog(onLog, LogKind.Info, "分类汇总已保存：" + summaryPath);
            }
            catch (Exception ex)
            {
                ReportLog(onLog, LogKind.Warning, "分类汇总保存失败：" + ex.Message);
            }

            return items;
        }

        /// <summary>执行单个提取步骤：捕获异常并记录到错误列表，返回是否成功。</summary>
        private static bool RunStep(Action<LogKind, string> onLog, List<string> errors,
            string fileName, string stepName, Action action)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception ex)
            {
                errors.Add(fileName + ": " + stepName + " 失败: " + ex.Message);
                ReportLog(onLog, LogKind.Error, fileName + " " + stepName + "失败：" + ex.Message);
                return false;
            }
        }

        private static void ReportProgress(Action<PdfSplitProgress> onProgress, PdfSplitProgress progress)
        {
            if (onProgress == null)
            {
                return;
            }

            try
            {
                onProgress(progress);
            }
            catch (Exception)
            {
                // 界面回调异常不应中断批量处理
            }
        }

        private static void ReportLog(Action<LogKind, string> onLog, LogKind kind, string message)
        {
            if (onLog == null)
            {
                return;
            }

            try
            {
                onLog(kind, message);
            }
            catch (Exception)
            {
                // 界面回调异常不应中断批量处理
            }
        }
    }
}
