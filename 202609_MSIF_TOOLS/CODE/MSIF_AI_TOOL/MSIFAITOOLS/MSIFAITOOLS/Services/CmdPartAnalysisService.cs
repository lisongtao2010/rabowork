using System;
using System.Collections.Generic;
using System.IO;

namespace MSIFAITOOLS.Services
{
    /// <summary>
    /// 【CMD 分析】编排服务：遍历一览中分类为「アッセンブリ図」的 PDF，
    /// 读取 CadPartSplitter 生成的 <c>01_アッセンブリ図_部品機能情報.csv</c>（旧名 <c>LT_STEP1.csv</c>，<b>不调用 AI</b>），
    /// 用 <c>xy.txt</c> 校验后登录到与【AI 分析】完全相同的 SQLite 表
    /// （<see cref="PartBasicInfoRepository.TableName"/>）。
    ///
    /// 与【AI 分析】（<see cref="PartAnalysisService"/>）的区别只在「数据从哪儿来」：
    ///   · AI 分析：plu_table.txt → 送 AI → 解析 AI 返回结果；
    ///   · CMD 分析：01_アッセンブリ図_部品機能情報.csv → 本工具自己的列名映射 → 部品明细。
    /// 入库方式、校验状态、部品信息画面的显示方式完全一致（同一 DB / 同一表）。
    ///
    /// 追加步骤（<see cref="CmdInstructionPartCollector"/>）：上面按图纸登录完成后，
    /// 再按 <c>classify\PdfInfoList.txt</c> 的「図面分类 = アッセンブリ図」与「分类依据」，
    /// 顺着 <c>01_アッセンブリ図_要領図.csv</c> → 各要領図的 <c>02_要領図_部品機能情報.csv</c>
    /// 把「品種命中分类依据」的部品行也登录到同一张表（図番 = アッセンブリ図、部品図番 = 要領図）。
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
        /// 每个 PDF 的数据来源：&lt;输出根目录&gt;\&lt;図番号&gt;\01_アッセンブリ図_部品機能情報.csv
        /// （多页 PDF 第 2 页起为 &lt;図番号&gt;_P2\01_アッセンブリ図_部品機能情報.csv）。
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

                // 追加步骤：要領図（02_要領図_部品機能情報.csv）的部品挂到アッセンブリ図 上
                CollectInstructionParts(targets, outputDirectory, repository, summary, onLog);
            }

            return summary;
        }

        /// <summary>处理单个 PDF：定位 01_アッセンブリ図_部品機能情報.csv → 解析 → 入库。</summary>
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
                        PartAnalysisFileStatus.NoPartData, 0, 0, "未从 01_アッセンブリ図_部品機能情報.csv 读到部品明细"));

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

        /// <summary>
        /// 追加步骤：按要領図清单把要領図的部品登录到アッセンブリ図 名下。
        ///
        /// 每个アッセンブリ図：
        /// <list type="number">
        ///   <item>先取该図番已有的明细，去掉上一次本步骤写入的行（来源表格 = 02_要領図_部品機能情報.csv）；</item>
        ///   <item>由 <see cref="CmdInstructionPartCollector"/> 收集新品種命中的行（番号接在已有行之后）；
        ///         番号不同即不会命中唯一索引（図番 + 页码 + 番号 + 部品コード）；</item>
        ///   <item>合并后一次写库（<see cref="PartBasicInfoRepository.Save"/> 会先清空该図番再插入，
        ///         所以必须把已有行一起提交，否则会把 01_アッセンブリ図_部品機能情報.csv 的明细冲掉）。</item>
        /// </list>
        /// 找不到 01 / 02 文件时只记日志，不影响上面已登录的数据。
        /// </summary>
        private static void CollectInstructionParts(IList<PdfClassification> targets, string outputDirectory,
            PartBasicInfoRepository repository, PartAnalysisSummary summary, Action<LogKind, string> onLog)
        {
            PartAnalysisService.ReportLog(onLog, LogKind.Info, string.Empty);
            PartAnalysisService.ReportLog(onLog, LogKind.Info, string.Format(
                "追加步骤：{0} → 各要領図的 {1}，把品種命中分类依据的部品挂到「{2}」上",
                CmdInstructionPartCollector.YoryoListFileName,
                CmdInstructionPartCollector.YoryoPartFileName, PdfCategoryNames.Assembly));

            CmdInstructionPartCollector collector = new CmdInstructionPartCollector();
            int processed = 0;

            foreach (PdfClassification item in targets)
            {
                if (item == null)
                {
                    continue;
                }

                string drawingNo = item.Stem;

                try
                {
                    List<PartBasicInfo> existing = new List<PartBasicInfo>(repository.Query(drawingNo));

                    // 上一次【CMD 分析】追加过的要領図部品先剔掉，保证重复执行不产生重复行
                    int removed = existing.RemoveAll(IsInstructionRow);
                    int startNumber = NextNumber(existing);

                    CmdInstructionPartResult result = collector.Collect(item, outputDirectory, startNumber);

                    foreach (string note in result.Notes)
                    {
                        PartAnalysisService.ReportLog(onLog, LogKind.Info, "  " + note);
                    }

                    summary.InstructionMissingFileCount += result.MissingFileCount;

                    if (result.Parts.Count == 0 && removed == 0)
                    {
                        continue;
                    }

                    List<PartBasicInfo> merged = new List<PartBasicInfo>(existing);
                    merged.AddRange(result.Parts);

                    repository.Save(drawingNo, merged);

                    processed++;
                    summary.InstructionDrawingCount = processed;
                    summary.InstructionFileCount += result.YoryoPartFileCount;
                    summary.InstructionRowCount += result.Parts.Count;

                    PartAnalysisService.ReportLog(onLog, LogKind.Success, string.Format(
                        "  要領図部品登录：{0} 条（図番 {1}，该図番合计 {2} 行）",
                        result.Parts.Count, drawingNo, merged.Count));
                }
                catch (Exception ex)
                {
                    summary.FailedFileCount++;

                    PartAnalysisService.ReportLog(onLog, LogKind.Error,
                        "  要領図部品登录失败（図番 " + drawingNo + "）：" + ex.Message);
                }
            }

            if (processed == 0)
            {
                PartAnalysisService.ReportLog(onLog, LogKind.Info, "  没有需要追加的要領図部品");
            }
        }

        /// <summary>是否为追加步骤写入的行（来源表格是 02_要領図_部品機能情報.csv / 旧名 LT_STEP2.csv）。</summary>
        private static bool IsInstructionRow(PartBasicInfo part)
        {
            return part != null && CmdInstructionPartCollector.IsYoryoPartFile(part.SourceFile);
        }

        /// <summary>取该図番第 1 页已有的最大「番号」+1（没有行时返回 1）。</summary>
        private static int NextNumber(IList<PartBasicInfo> parts)
        {
            int max = 0;

            foreach (PartBasicInfo part in parts)
            {
                if (part != null && part.Page <= 1 && part.Number > max)
                {
                    max = part.Number;
                }
            }

            return max + 1;
        }

        /// <summary>生成一行中文汇总（界面状态栏与对话框共用）。</summary>
        public static string BuildSummaryText(PartAnalysisSummary summary)
        {
            if (summary == null)
            {
                return string.Empty;
            }

            return string.Format(
                "対象 {0} 个：登录成功 {1}，无部品数据 {2}，缺少 {3} {4}，失败 {5}；共登录 {6} 行（坐标一致 {7} / 不符 {8}）；" +
                "要領図部品 {9} 行（02 文件 {10} 个 / 缺 {11}）",
                summary.TargetCount, summary.SavedFileCount, summary.NoDataFileCount,
                CmdPartAnalyzer.LtStep1FileName, summary.MissingTableFileCount, summary.FailedFileCount,
                summary.SavedRowCount, summary.VerifiedPassedCount, summary.VerifiedFailedCount,
                summary.InstructionRowCount, summary.InstructionFileCount, summary.InstructionMissingFileCount);
        }
    }
}
