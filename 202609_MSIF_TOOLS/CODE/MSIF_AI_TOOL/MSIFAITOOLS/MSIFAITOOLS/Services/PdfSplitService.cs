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

        /// <summary>坐标输出目录（&lt;文件夹&gt;\AppData\xy）。</summary>
        public string XyDirectory { get; }

        /// <summary>表格输出目录（&lt;文件夹&gt;\AppData\table）。</summary>
        public string TableDirectory { get; }

        /// <summary>plu 系列输出目录（&lt;文件夹&gt;\AppData\plu）。</summary>
        public string PluDirectory { get; }

        /// <summary>分类结果目录（&lt;文件夹&gt;\AppData\classify）。</summary>
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
    /// 对指定文件夹第一层的所有 PDF 生成结果，统一输出到 &lt;文件夹&gt;\AppData 下（不存在时自动创建）：
    ///   1) &lt;文件夹&gt;\AppData\xy\&lt;図番号&gt;_xy.txt    —— 每页文字及其坐标；
    ///      &lt;文件夹&gt;\AppData\xy\&lt;図番号&gt;_xy_imgtext.txt —— 图面文字及其坐标
    ///      （按「番号」「タイプ名」表头的更靠左者作水平分界，只取分界左侧、页面高度 85% 以内的文字）；
    ///   2) &lt;文件夹&gt;\AppData\table\&lt;図番号&gt;_table.txt —— 每页表格（单元格以 “|” 分隔）；
    ///   3) &lt;文件夹&gt;\AppData\plu\&lt;図番号&gt;_plu_*.txt  —— pdfplumber 风格的五份结果
    ///      （_plu_text / _plu_layout / _plu_table / _plu_image / _plu_all）；
    ///      其中 _plu_image 为“图上文字”：按图片区域与文字坐标比对判定，表格文字不重复计入
    ///      （表格文字按“表格块外接矩形 + 有效 X 范围”判定，与表格左右并排的图纸标注仍算图上文字）；
    ///   4) &lt;文件夹&gt;\AppData\classify\classification.csv —— 每个 PDF 的分类结果
    ///      （基本図 / 加工図 / アッセンブリ図 / 要領図 / 未知）。
    /// </summary>
    internal sealed class PdfSplitService
    {
        /// <summary>
        /// 所有输出统一放到的根子文件夹名：&lt;PDF 文件夹&gt;\AppData（不存在时自动创建）。
        /// </summary>
        public const string OutputRootFolderName = "AppData";

        /// <summary>坐标结果子文件夹名。</summary>
        public const string XyFolderName = "xy";

        /// <summary>表格结果子文件夹名。</summary>
        public const string TableFolderName = "table";

        /// <summary>坐标文件名后缀。</summary>
        public const string XyFileSuffix = "_xy.txt";

        /// <summary>图面文字坐标文件名后缀（只包含图纸区域的文字）。</summary>
        public const string XyImageTextFileSuffix = "_xy_imgtext.txt";

        /// <summary>表格文件名后缀。</summary>
        public const string TableFileSuffix = "_table.txt";

        /// <summary>plu 系列结果子文件夹名。</summary>
        public const string PluFolderName = "plu";

        /// <summary>plu 纯文本后缀（对应 pdfplumber 的 extract_text()）。</summary>
        public const string PluTextFileSuffix = "_plu_text.txt";

        /// <summary>plu 版面文本后缀（对应 pdfplumber 的 extract_text(layout=True)）。</summary>
        public const string PluLayoutFileSuffix = "_plu_layout.txt";

        /// <summary>plu 表格后缀（对应 pdfplumber 的 extract_tables()）。</summary>
        public const string PluTableFileSuffix = "_plu_table.txt";

        /// <summary>plu 图上文字后缀（图片区域与文字坐标比对；无图片时退化为表格之外的文字）。</summary>
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
        /// 计算分解输出目录（只计算路径，不创建目录）：
        /// &lt;PDF 文件夹&gt;\AppData\{xy, table, plu, classify}。
        /// </summary>
        /// <param name="folder">PDF 所在文件夹。</param>
        /// <param name="xyDirectory">坐标输出目录。</param>
        /// <param name="tableDirectory">表格输出目录。</param>
        /// <param name="pluDirectory">plu 五份输出目录。</param>
        /// <param name="classifyDirectory">分类结果目录。</param>
        public static void GetOutputDirectories(string folder, out string xyDirectory,
            out string tableDirectory, out string pluDirectory, out string classifyDirectory)
        {
            string outputRoot = Path.Combine(folder, OutputRootFolderName);
            xyDirectory = Path.Combine(outputRoot, XyFolderName);
            tableDirectory = Path.Combine(outputRoot, TableFolderName);
            pluDirectory = Path.Combine(outputRoot, PluFolderName);
            classifyDirectory = Path.Combine(outputRoot, PdfClassifier.ClassifyFolderName);
        }

        /// <summary>
        /// 统计“分解产物已齐全”的 PDF 数量（用于判断是否需要询问用户重新分解）。
        /// 每个 PDF 需同时存在以下 txt：
        /// *_xy.txt、*_xy_imgtext.txt、*_table.txt、*_plu_text.txt、*_plu_layout.txt、
        /// *_plu_table.txt、*_plu_image.txt、*_plu_all.txt。
        /// </summary>
        /// <param name="folder">PDF 所在文件夹。</param>
        /// <param name="pdfNames">PDF 文件名集合（含扩展名，通常来自 <see cref="FindPdfFiles"/>）。</param>
        /// <param name="missing">不为 null 时收集缺失文件说明（最多 20 条）。</param>
        /// <returns>产物齐全的 PDF 数量。</returns>
        public static int CountCompletedOutputs(string folder, IList<string> pdfNames,
            IList<string> missing)
        {
            if (string.IsNullOrWhiteSpace(folder) || pdfNames == null || pdfNames.Count == 0)
            {
                return 0;
            }

            string xyDirectory;
            string tableDirectory;
            string pluDirectory;
            string classifyDirectory;
            GetOutputDirectories(folder, out xyDirectory, out tableDirectory, out pluDirectory,
                out classifyDirectory);

            int completed = 0;

            foreach (string name in pdfNames)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                // 図番号 = PDF 文件名（不含扩展名）
                string stem = Path.GetFileNameWithoutExtension(name);
                string[] paths = new string[]
                {
                    Path.Combine(xyDirectory, stem + XyFileSuffix),
                    Path.Combine(xyDirectory, stem + XyImageTextFileSuffix),
                    Path.Combine(tableDirectory, stem + TableFileSuffix),
                    Path.Combine(pluDirectory, stem + PluTextFileSuffix),
                    Path.Combine(pluDirectory, stem + PluLayoutFileSuffix),
                    Path.Combine(pluDirectory, stem + PluTableFileSuffix),
                    Path.Combine(pluDirectory, stem + PluImageFileSuffix),
                    Path.Combine(pluDirectory, stem + PluAllFileSuffix)
                };

                bool allExist = true;

                foreach (string path in paths)
                {
                    if (File.Exists(path))
                    {
                        continue;
                    }

                    allExist = false;

                    if (missing != null && missing.Count < 20)
                    {
                        missing.Add(Path.GetFileName(path));
                    }
                }

                if (allExist)
                {
                    completed++;
                }
            }

            return completed;
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

            // 所有输出集中到 <PDF 文件夹>\AppData 下（不存在时自动创建），避免污染所选文件夹根目录
            string outputRoot = Path.Combine(folder, OutputRootFolderName);
            string xyDirectory;
            string tableDirectory;
            string pluDirectory;
            string classifyDirectory;
            GetOutputDirectories(folder, out xyDirectory, out tableDirectory, out pluDirectory,
                out classifyDirectory);
            Directory.CreateDirectory(outputRoot);
            Directory.CreateDirectory(xyDirectory);
            Directory.CreateDirectory(tableDirectory);
            Directory.CreateDirectory(pluDirectory);
            Directory.CreateDirectory(classifyDirectory);

            ReportLog(onLog, LogKind.Info, "输出根目录：" + outputRoot);
            ReportLog(onLog, LogKind.Info, "坐标输出目录：" + xyDirectory);
            ReportLog(onLog, LogKind.Info, "表格输出目录：" + tableDirectory);
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

                // 2) 图面文字 + 坐标（只取图纸区域的文字）
                if (!RunStep(onLog, errors, fileName, "图面文字提取 (PdfPig)", delegate
                    {
                        PdfTextExtractor.WriteImageTextCoordinates(pdfPath,
                            Path.Combine(xyDirectory, stem + XyImageTextFileSuffix));
                    }))
                {
                    failed = true;
                }

                // 3) 表格（替代 MarkItDown 的表格输出：单元格以 “|” 分隔）
                if (!RunStep(onLog, errors, fileName, "表格提取 (PdfPig)", delegate
                    {
                        PdfTextExtractor.WriteTableText(pdfPath, Path.Combine(tableDirectory, stem + TableFileSuffix));
                    }))
                {
                    failed = true;
                }

                // 4) plu：pdfplumber 风格五份输出（一次解析，同时写 5 个文件）
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
                    ReportLog(onLog, LogKind.Success,
                        fileName + " 已生成坐标、图面文字、表格及 plu 五种文件。");
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
