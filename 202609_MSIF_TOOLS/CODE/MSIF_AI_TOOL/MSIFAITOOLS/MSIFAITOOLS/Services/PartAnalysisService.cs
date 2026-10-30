using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace MSIFAITOOLS.Services
{
    /// <summary>单个 PDF 的 AI 分析 + 登录结果状态。</summary>
    internal enum PartAnalysisFileStatus
    {
        /// <summary>没有 *_plu_table.txt（尚未执行分解）。</summary>
        MissingTableFile,

        /// <summary>AI 没有提取到部品明细（无符合表头或番号不连续等）。</summary>
        NoPartData,

        /// <summary>已提取并登录到数据库。</summary>
        Saved,

        /// <summary>分析或登录失败。</summary>
        Failed
    }

    /// <summary>单个 PDF 的分析结果。</summary>
    internal sealed class PartAnalysisFileResult
    {
        public PartAnalysisFileResult(string filePath, string drawingNo, PartAnalysisFileStatus status,
            int partCount, int savedCount, string message)
        {
            FilePath = filePath ?? string.Empty;
            DrawingNo = drawingNo ?? string.Empty;
            Status = status;
            PartCount = partCount;
            SavedCount = savedCount;
            Message = message ?? string.Empty;
        }

        /// <summary>PDF 完整路径。</summary>
        public string FilePath { get; private set; }

        /// <summary>図番。</summary>
        public string DrawingNo { get; private set; }

        /// <summary>处理状态。</summary>
        public PartAnalysisFileStatus Status { get; private set; }

        /// <summary>AI 提取到的条数。</summary>
        public int PartCount { get; private set; }

        /// <summary>写入数据库的条数。</summary>
        public int SavedCount { get; private set; }

        /// <summary>补充说明（错误信息 / 提示）。</summary>
        public string Message { get; private set; }
    }

    /// <summary>一次 AI 分析的汇总结果。</summary>
    internal sealed class PartAnalysisSummary
    {
        public PartAnalysisSummary(string databasePath)
        {
            DatabasePath = databasePath ?? string.Empty;
            Files = new List<PartAnalysisFileResult>();
        }

        /// <summary>数据库文件路径。</summary>
        public string DatabasePath { get; private set; }

        /// <summary>各 PDF 的处理结果。</summary>
        public IList<PartAnalysisFileResult> Files { get; private set; }

        /// <summary>一览中被判定为アッセンブリ図 的 PDF 数量。</summary>
        public int TargetCount { get; set; }

        /// <summary>成功登录的 PDF 数量。</summary>
        public int SavedFileCount { get; set; }

        /// <summary>没有提取到部品数据的 PDF 数量。</summary>
        public int NoDataFileCount { get; set; }

        /// <summary>缺少表格文件的 PDF 数量。</summary>
        public int MissingTableFileCount { get; set; }

        /// <summary>失败的 PDF 数量。</summary>
        public int FailedFileCount { get; set; }

        /// <summary>写入数据库的总行数。</summary>
        public int SavedRowCount { get; set; }

        /// <summary>坐标校验一致 / 不符的条数。</summary>
        public int VerifiedPassedCount { get; set; }

        /// <summary>坐标校验不符的条数。</summary>
        public int VerifiedFailedCount { get; set; }
    }

    /// <summary>
    /// AI 分析编排服务：遍历一览中分类为「アッセンブリ図」的 PDF，
    /// 用 AI 解析其 *_plu_table.txt 取得【部品基本信息列表】，
    /// 用 *_xy.txt 的文字坐标校验，最后登录到 SQLite 的部品基本信息表。
    /// </summary>
    internal sealed class PartAnalysisService
    {
        /// <summary>
        /// 【开发调试开关】true 时只分析 <see cref="DebugSingleFileName"/> 指定的 PDF，
        /// 便于开发阶段反复验证同一份图纸（其余「アッセンブリ図」全部跳过）。
        /// 发布前请改回 false（false 时分析所有「アッセンブリ図」）。
        /// </summary>
        public const bool DebugSingleFileOnly = true;

        /// <summary>【开发调试】只分析的文件名（仅在 DebugSingleFileOnly = true 时生效，忽略大小写）。</summary>
        public const string DebugSingleFileName = "H310U10004.pdf";

        private readonly AiApiOptions _options;

        private readonly string _databasePath;

        public PartAnalysisService(AiApiOptions options, string databasePath)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            _options = options;
            _databasePath = string.IsNullOrWhiteSpace(databasePath)
                ? PartBasicInfoRepository.ResolveDatabasePath()
                : databasePath;
        }

        /// <summary>本次使用的数据库路径。</summary>
        public string DatabasePath
        {
            get { return _databasePath; }
        }

        /// <summary>
        /// 分析一览中「アッセンブリ図」的 PDF 并登录到 SQLite，返回汇总结果。
        /// </summary>
        /// <param name="classifications">一览（分类结果）数据。</param>
        /// <param name="xyDirectory">&lt;PDF 文件夹&gt;\AppData\xy（坐标文件目录）。</param>
        /// <param name="pluDirectory">&lt;PDF 文件夹&gt;\AppData\plu（表格文件目录）。</param>
        /// <param name="onLog">日志回调（kind, message）。</param>
        public PartAnalysisSummary AnalyzeAndSave(IList<PdfClassification> classifications,
            string xyDirectory, string pluDirectory, Action<LogKind, string> onLog)
        {
            if (!_options.IsConfigured)
            {
                throw new InvalidOperationException(
                    "未配置 AI 接口密钥：请在程序内【AI 设置】中填写 API Key" +
                    "（也可使用 App.config 的 AiApiKey 或环境变量 MSIF_AI_API_KEY）。");
            }

            if (string.IsNullOrWhiteSpace(pluDirectory) || !Directory.Exists(pluDirectory))
            {
                throw new DirectoryNotFoundException(
                    "找不到表格输出目录（请先执行【开始分解】）：" + pluDirectory);
            }

            PartAnalysisSummary summary = new PartAnalysisSummary(_databasePath);

            ReportLog(onLog, LogKind.Info, "AI 配置：" + _options.Describe());
            ReportLog(onLog, LogKind.Info, "部品基本信息数据库：" + _databasePath);

            AiPromptSet prompts = AiPromptStore.Load();
            ReportLog(onLog, LogKind.Info, "AI 提示语：" + prompts.Source);

            if (!string.IsNullOrEmpty(AiPromptStore.LastLoadError))
            {
                ReportLog(onLog, LogKind.Warning, "  " + AiPromptStore.LastLoadError);
            }

            List<PdfClassification> assemblyTargets = SelectAssemblyTargets(classifications);

            if (assemblyTargets.Count == 0)
            {
                ReportLog(onLog, LogKind.Warning,
                    "一览中没有分类为「" + PdfCategoryNames.Assembly + "」的 PDF，无需分析。");

                return summary;
            }

            ReportLog(onLog, LogKind.Info, string.Format("待分析「{0}」PDF：{1} 个",
                PdfCategoryNames.Assembly, assemblyTargets.Count));

            // 【开发调试】DebugSingleFileOnly = true 时只保留指定文件
            List<PdfClassification> targets = ApplyDebugFilter(assemblyTargets);
            summary.TargetCount = targets.Count;

            if (DebugSingleFileOnly && targets.Count != assemblyTargets.Count)
            {
                ReportLog(onLog, LogKind.Warning, string.Format(
                    "【调试模式】只分析指定文件：{0}（其余 {1} 个跳过；发布前请把 PartAnalysisService.DebugSingleFileOnly 改为 false）",
                    DebugSingleFileName, assemblyTargets.Count - targets.Count));
            }

            if (targets.Count == 0)
            {
                ReportLog(onLog, LogKind.Warning,
                    "【调试模式】一览中没有「" + DebugSingleFileName + "」，本次不分析。");

                return summary;
            }

            using (PartBasicInfoRepository repository = new PartBasicInfoRepository(_databasePath))
            {
                AiPartAnalyzer analyzer = new AiPartAnalyzer(new ChatCompletionClient(_options));

                for (int index = 0; index < targets.Count; index++)
                {
                    AnalyzeOne(targets[index], index + 1, targets.Count, xyDirectory, pluDirectory,
                        analyzer, repository, summary, onLog);
                }
            }

            return summary;
        }

        /// <summary>处理单个 PDF（异常在此捕获，保证批量分析不中断）。</summary>
        private void AnalyzeOne(PdfClassification item, int index, int total, string xyDirectory,
            string pluDirectory, AiPartAnalyzer analyzer, PartBasicInfoRepository repository,
            PartAnalysisSummary summary, Action<LogKind, string> onLog)
        {
            string stem = Path.GetFileNameWithoutExtension(item.FilePath);

            ReportLog(onLog, LogKind.Info, string.Format("[{0}/{1}] AI 分析：{2}", index, total, item.FileName));

            string tablePath = Path.Combine(pluDirectory, stem + PdfSplitService.PluTableFileSuffix);

            if (!File.Exists(tablePath))
            {
                summary.MissingTableFileCount++;
                summary.Files.Add(new PartAnalysisFileResult(item.FilePath, stem,
                    PartAnalysisFileStatus.MissingTableFile, 0, 0, "缺少表格文件：" + tablePath));
                ReportLog(onLog, LogKind.Warning,
                    "  跳过：找不到 " + Path.GetFileName(tablePath) + "（请先执行【开始分解】）");

                return;
            }

            string xyPath = string.IsNullOrWhiteSpace(xyDirectory)
                ? null
                : Path.Combine(xyDirectory, stem + PdfSplitService.XyFileSuffix);

            try
            {
                AiPartAnalysisResult result = analyzer.Analyze(item.FilePath, tablePath, xyPath);

                foreach (string note in result.Notes)
                {
                    ReportLog(onLog, LogKind.Info, "  提示：" + note);
                }

                if (result.Parts.Count == 0)
                {
                    summary.NoDataFileCount++;
                    summary.Files.Add(new PartAnalysisFileResult(item.FilePath, result.DrawingNo,
                        PartAnalysisFileStatus.NoPartData, 0, 0, "未提取到部品明细"));
                    ReportLog(onLog, LogKind.Warning, "  未提取到部品明细（表格标题可能不含「番号 / 部品名」）");

                    return;
                }

                int saved = repository.Save(result.DrawingNo, result.Parts);

                summary.SavedFileCount++;
                summary.SavedRowCount += saved;
                summary.VerifiedPassedCount += CountStatus(result.Parts, PartVerifyStatus.Passed);
                summary.VerifiedFailedCount += CountStatus(result.Parts, PartVerifyStatus.Failed);
                summary.Files.Add(new PartAnalysisFileResult(item.FilePath, result.DrawingNo,
                    PartAnalysisFileStatus.Saved, result.Parts.Count, saved, string.Empty));

                ReportLog(onLog, LogKind.Success,
                    string.Format("  登录成功：{0} 条（図番 {1}）", saved, result.DrawingNo));

                LogSampleRows(onLog, result.Parts);
            }
            catch (Exception ex)
            {
                summary.FailedFileCount++;
                summary.Files.Add(new PartAnalysisFileResult(item.FilePath, stem,
                    PartAnalysisFileStatus.Failed, 0, 0, ex.Message));
                ReportLog(onLog, LogKind.Error, "  分析失败：" + ex.Message);
            }
        }

        /// <summary>从一览中挑出「アッセンブリ図」（含用户手动改判的）。</summary>
        private static List<PdfClassification> SelectAssemblyTargets(IList<PdfClassification> classifications)
        {
            List<PdfClassification> targets = new List<PdfClassification>();

            if (classifications == null)
            {
                return targets;
            }

            foreach (PdfClassification item in classifications)
            {
                if (item == null || string.IsNullOrEmpty(item.FilePath))
                {
                    continue;
                }

                if (item.Category == PdfCategory.Assembly)
                {
                    targets.Add(item);
                }
            }

            return targets;
        }

        /// <summary>当前是否处于「只分析指定文件」的调试模式。</summary>
        public static bool IsDebugSingleFileMode
        {
            get { return DebugSingleFileOnly; }
        }

        /// <summary>
        /// 调试模式（<see cref="DebugSingleFileOnly"/> = true）下只保留文件名等于
        /// <see cref="DebugSingleFileName"/> 的 PDF（忽略大小写）；开关为 false 时原样返回。
        /// 界面侧在调用 <see cref="AnalyzeAndSave"/> 之前也可先调用本方法，
        /// 让进度、日志中的「N 个 PDF」与实际分析数量一致。
        /// </summary>
        public static List<PdfClassification> ApplyDebugFilter(IList<PdfClassification> targets)
        {
            List<PdfClassification> filtered = new List<PdfClassification>();

            if (targets == null || targets.Count == 0)
            {
                return filtered;
            }

            foreach (PdfClassification item in targets)
            {
                if (item == null)
                {
                    continue;
                }

                if (!DebugSingleFileOnly ||
                    string.Equals(item.FileName, DebugSingleFileName, StringComparison.OrdinalIgnoreCase))
                {
                    filtered.Add(item);
                }
            }

            return filtered;
        }

        /// <summary>统计指定校验状态的条数。</summary>
        private static int CountStatus(IList<PartBasicInfo> parts, string status)
        {
            int count = 0;

            foreach (PartBasicInfo part in parts)
            {
                if (string.Equals(part.VerifyStatus, status, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>把前几条明细打印到日志，便于确认提取结果。</summary>
        private static void LogSampleRows(Action<LogKind, string> onLog, IList<PartBasicInfo> parts)
        {
            const int MaxSampleCount = 3;

            for (int index = 0; index < parts.Count && index < MaxSampleCount; index++)
            {
                PartBasicInfo part = parts[index];

                ReportLog(onLog, LogKind.Info, string.Format("    {0}：{1} / {2} / 員数 {3} / {4}",
                    part.ToShortText(), part.FrameKind, part.PartDrawingNo, part.Quantity, part.VerifyStatus));
            }

            if (parts.Count > MaxSampleCount)
            {
                ReportLog(onLog, LogKind.Info,
                    string.Format("    ...（共 {0} 条）", parts.Count.ToString(CultureInfo.InvariantCulture)));
            }
        }

        private static void ReportLog(Action<LogKind, string> onLog, LogKind kind, string message)
        {
            if (onLog != null)
            {
                onLog(kind, message);
            }
        }

        /// <summary>生成一行中文汇总（界面状态栏与对话框共用）。</summary>
        public static string BuildSummaryText(PartAnalysisSummary summary)
        {
            if (summary == null)
            {
                return string.Empty;
            }

            return string.Format(
                "対象 {0} 个：登录成功 {1}，无部品数据 {2}，缺少表格 {3}，失败 {4}；共登录 {5} 行（坐标一致 {6} / 不符 {7}）",
                summary.TargetCount, summary.SavedFileCount, summary.NoDataFileCount,
                summary.MissingTableFileCount, summary.FailedFileCount, summary.SavedRowCount,
                summary.VerifiedPassedCount, summary.VerifiedFailedCount);
        }
    }
}
