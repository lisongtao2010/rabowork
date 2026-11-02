using System;
using System.Collections.Generic;
using System.IO;

namespace MSIFAITOOLS.Services
{
    /// <summary>
    /// 【CMD 分析】编排服务：遍历一览中分类为「アッセンブリ図」的 PDF，
    /// 读取 CadPartSplitter 生成的 <c>LT_STEP1.csv</c>（<b>不调用 AI</b>），
    /// 用 <c>xy.txt</c> 校验后登录到与【AI 分析】完全相同的 SQLite 表
    /// （<see cref="PartBasicInfoRepository.TableName"/>）。
    ///
    /// 与【AI 分析】（<see cref="PartAnalysisService"/>）的区别只在「数据从哪儿来」：
    ///   · AI 分析：plu_table.txt → 送 AI → 解析 AI 返回结果；
    ///   · CMD 分析：LT_STEP1.csv → 本工具自己的列名映射 → 部品明细。
    /// 入库方式、校验状态、部品信息画面的显示方式完全一致（同一 DB / 同一表）。
    /// </summary>
    internal sealed class CmdPartAnalysisService
    {
        private readonly string _databasePath;

        public CmdPartAnalysisService(string databasePath)
        {
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
        /// 每个 PDF 的数据来源：&lt;输出根目录&gt;\&lt;図番号&gt;\LT_STEP1.csv
        /// （多页 PDF 第 2 页起为 &lt;図番号&gt;_P2\LT_STEP1.csv）。
        /// </summary>
        /// <param name="classifications">一览（分类结果）数据。</param>
        /// <param name="outputDirectory">分解输出根目录（&lt;PDF 文件夹&gt;\AiAppData）。</param>
        /// <param name="onLog">日志回调（kind, message）。</param>
        public PartAnalysisSummary AnalyzeAndSave(IList<PdfClassification> classifications,
            string outputDirectory, Action<LogKind, string> onLog)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
            {
                throw new DirectoryNotFoundException(
                    "找不到分解输出目录（请先执行【开始分解】）：" + outputDirectory);
            }

            PartAnalysisSummary summary = new PartAnalysisSummary(_databasePath);

            PartAnalysisService.ReportLog(onLog, LogKind.Info,
                "数据来源：" + CmdPartAnalyzer.LtStep1FileName + "（CadPartSplitter 生成，不调用 AI）");
            PartAnalysisService.ReportLog(onLog, LogKind.Info, "部品基本信息数据库：" + _databasePath);

            List<PdfClassification> targets = PartAnalysisService.SelectAssemblyTargets(classifications);

            if (targets.Count == 0)
            {
                PartAnalysisService.ReportLog(onLog, LogKind.Warning,
                    "一览中没有分类为「" + PdfCategoryNames.Assembly + "」的 PDF，无需分析。");

                return summary;
            }

            summary.TargetCount = targets.Count;

            PartAnalysisService.ReportLog(onLog, LogKind.Info, string.Format("待分析「{0}」PDF：{1} 个",
                PdfCategoryNames.Assembly, targets.Count));

            using (PartBasicInfoRepository repository = new PartBasicInfoRepository(_databasePath))
            {
                CmdPartAnalyzer analyzer = new CmdPartAnalyzer();

                for (int index = 0; index < targets.Count; index++)
                {
                    AnalyzeFile(targets[index], index + 1, targets.Count, outputDirectory, analyzer,
                        repository, summary, onLog);
                }
            }

            return summary;
        }

        /// <summary>处理单个 PDF：定位 LT_STEP1.csv → 解析 → 入库。</summary>
        private static void AnalyzeFile(PdfClassification item, int index, int total, string outputDirectory,
            CmdPartAnalyzer analyzer, PartBasicInfoRepository repository, PartAnalysisSummary summary,
            Action<LogKind, string> onLog)
        {
            string stem = Path.GetFileNameWithoutExtension(item.FilePath);

            PartAnalysisService.ReportLog(onLog, LogKind.Info,
                string.Format("[{0}/{1}] CMD 分析：{2}", index, total, item.FileName));

            string pdfOutputDirectory = PdfSplitService.GetPdfOutputDirectory(outputDirectory, stem);
            List<CmdPartSource> sources = CmdPartAnalyzer.LocateSources(outputDirectory, stem);

            if (sources.Count == 0)
            {
                string expected = Path.Combine(pdfOutputDirectory, CmdPartAnalyzer.LtStep1FileName);

                summary.MissingTableFileCount++;
                summary.Files.Add(new PartAnalysisFileResult(item.FilePath, stem,
                    PartAnalysisFileStatus.MissingLtStep1File, 0, 0, "缺少数据文件：" + expected));

                PartAnalysisService.ReportLog(onLog, LogKind.Warning, "  跳过：找不到 " + expected);
                PartAnalysisService.ReportLog(onLog, LogKind.Warning,
                    "        请在【开始分解】时勾选「先调用 CadPartSplitter」（并确认 " +
                    CadPartSplitterRunner.ExeFileName + " 已部署），或手工把该文件放到上面的路径。");

                return;
            }

            string xyPath = Path.Combine(pdfOutputDirectory, PdfSplitService.XyFileName);

            try
            {
                CmdPartAnalysisResult result = analyzer.Analyze(item.FilePath, sources, xyPath);

                foreach (string note in result.Notes)
                {
                    PartAnalysisService.ReportLog(onLog, LogKind.Info, "  提示：" + note);
                }

                if (result.Parts.Count == 0)
                {
                    summary.NoDataFileCount++;
                    summary.Files.Add(new PartAnalysisFileResult(item.FilePath, result.DrawingNo,
                        PartAnalysisFileStatus.NoPartData, 0, 0, "未从 LT_STEP1.csv 读到部品明细"));

                    PartAnalysisService.ReportLog(onLog, LogKind.Warning,
                        "  未读到部品明细（表头可能不含「部品名 / 部品コード」列）");

                    return;
                }

                int saved = repository.Save(result.DrawingNo, result.Parts);

                summary.SavedFileCount++;
                summary.SavedRowCount += saved;
                summary.VerifiedPassedCount += PartAnalysisService.CountStatus(result.Parts,
                    PartVerifyStatus.Passed);
                summary.VerifiedFailedCount += PartAnalysisService.CountStatus(result.Parts,
                    PartVerifyStatus.Failed);
                summary.Files.Add(new PartAnalysisFileResult(item.FilePath, result.DrawingNo,
                    PartAnalysisFileStatus.Saved, result.Parts.Count, saved, string.Empty));

                PartAnalysisService.ReportLog(onLog, LogKind.Success,
                    string.Format("  登录成功：{0} 条（図番 {1}）", saved, result.DrawingNo));

                PartAnalysisService.LogSampleRows(onLog, result.Parts);
            }
            catch (Exception ex)
            {
                summary.FailedFileCount++;
                summary.Files.Add(new PartAnalysisFileResult(item.FilePath, stem,
                    PartAnalysisFileStatus.Failed, 0, 0, ex.Message));

                PartAnalysisService.ReportLog(onLog, LogKind.Error, "  分析失败：" + ex.Message);
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
                "対象 {0} 个：登录成功 {1}，无部品数据 {2}，缺少 {3} {4}，失败 {5}；共登录 {6} 行（坐标一致 {7} / 不符 {8}）",
                summary.TargetCount, summary.SavedFileCount, summary.NoDataFileCount,
                CmdPartAnalyzer.LtStep1FileName, summary.MissingTableFileCount, summary.FailedFileCount,
                summary.SavedRowCount, summary.VerifiedPassedCount, summary.VerifiedFailedCount);
        }
    }
}
