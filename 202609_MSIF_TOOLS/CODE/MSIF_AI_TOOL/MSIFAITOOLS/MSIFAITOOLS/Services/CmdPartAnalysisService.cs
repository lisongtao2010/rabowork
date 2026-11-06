using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// 点一次【CMD 分析】依次执行 <b>4 步</b>（名字与日志前缀见 <see cref="CmdAnalysisSteps"/>，
    /// 详细说明见 <c>docs\CMD分析_步骤命名.md</c>）：
    /// <list type="number">
    ///   <item><b>第 1 步 装配图部品入库</b>（旧称「阶段 A」；<see cref="CmdPartAnalyzer"/>）；</item>
    ///   <item><b>第 2 步 圈号拆分</b>（旧称「阶段 A2」；<see cref="CmdCalloutPartSplitter"/>）；</item>
    ///   <item><b>第 3 步 要領図部品追加</b>（旧称「阶段 B」；<see cref="CmdInstructionPartCollector"/>）；</item>
    ///   <item><b>第 4 步 tz.json 部品追加</b>（<see cref="CmdTzPartCollector"/>）。</item>
    /// </list>
    /// 这 4 步之前还有 <b>第 0 步 分解与分类</b>（【开始分解】= CadPartSplitter 分割 + 本工具分解 +
    /// <c>classify\PdfInfoList.txt</c>），它不属于本按钮。
    ///
    /// 追加步骤（第 2 / 3 / 4 步，都只对 <c>classify\PdfInfoList.txt</c> 里「図面分类 = アッセンブリ図」的记录生效）：
    /// <list type="number">
    ///   <item><b>按圈号分割</b>（<see cref="CmdCalloutPartSplitter"/>）：对「来源标识 = アッセンブリ図」的行，
    ///         用「番号」在 <c>parts_info.json</c> 的 <c>callouts</c> 里找圈号（如 <c>text=4</c> / <c>display=④4</c>）；
    ///         圈号必须出现在 <b>index 不同</b>的零件图里，且这些零件图的 <c>texts</c> 整体互为包含（A ⊆ B / B ⊆ A），
    ///         才按坐标判排列方向（左右 / 上下）把方向加到部品名前并拆成多条；</item>
    ///   <item><b>追加要領図 部品</b>（<see cref="CmdInstructionPartCollector"/>）：顺着
    ///         <c>01_アッセンブリ図_要領図.csv</c> → 各要領図的 <c>02_要領図_部品機能情報.csv</c>，
    ///         把「品種命中分类依据」的部品行、以及「品種为空（所有品種共用）」的部品行
    ///         也登录到同一张表（図番 = アッセンブリ図、部品図番 = 要領図；备註 <c>品種：共通</c>）；</item>
    ///   <item><b>追加 tz.json 部品</b>（<see cref="CmdTzPartCollector"/>）：读 <c>classify\tz.json</c>（Excel 部品表），
    ///         把「品種」命中分类依据的明细（部品名 / 枠区分 / 部品コード / 追加対応特注寸法）
    ///         登录到同一张表（図番 = アッセンブリ図、备註 <c>品種：…；特注寸法：…</c>）。</item>
    /// </list>
    ///
    /// 执行范围由 <see cref="CmdAnalysisStepKind"/> 决定：主按钮【CMD 分析】传
    /// <see cref="CmdAnalysisStepKind.All"/>（一次跑完 4 步，行为与以前完全一样）；
    /// 界面上的四个子按钮各传一个值，只重跑其中一步（调试 / 单步重试用）。
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
            return AnalyzeAndSave(classifications, outputDirectory, onLog, CmdAnalysisStepKind.All);
        }

        /// <summary>
        /// 同上，但可以选择只执行其中一步（界面上的四个子按钮）：
        /// <list type="bullet">
        ///   <item><see cref="CmdAnalysisStepKind.All"/>：依次跑 4 步（= 主按钮【CMD 分析】，行为不变）；</item>
        ///   <item><see cref="CmdAnalysisStepKind.Assembly"/>：只跑第 1 步 装配图部品入库；</item>
        ///   <item><see cref="CmdAnalysisStepKind.CalloutSplit"/>：只跑第 2 步 圈号拆分；</item>
        ///   <item><see cref="CmdAnalysisStepKind.YoryoAppend"/>：只跑第 3 步 要領図部品追加；</item>
        ///   <item><see cref="CmdAnalysisStepKind.TzAppend"/>：只跑第 4 步 tz.json 部品追加。</item>
        /// </list>
        /// 第 2 / 3 / 4 步依赖第 1 步已经入库的行：数据库里没有该図番的行时只记一条警告，不做任何写库。
        /// </summary>
        /// <param name="classifications">一览（分类结果）数据。</param>
        /// <param name="outputDirectory">分解输出根目录（&lt;PDF 文件夹&gt;\AiAppData）。</param>
        /// <param name="onLog">日志回调（kind, message）。</param>
        /// <param name="stepKind">要执行的步骤（主按钮传 <see cref="CmdAnalysisStepKind.All"/>）。</param>
        public PartAnalysisSummary AnalyzeAndSave(IList<PdfClassification> classifications,
            string outputDirectory, Action<LogKind, string> onLog, CmdAnalysisStepKind stepKind)
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
            bool runAssembly = stepKind == CmdAnalysisStepKind.All || stepKind == CmdAnalysisStepKind.Assembly;
            bool runCalloutSplit = stepKind == CmdAnalysisStepKind.All || stepKind == CmdAnalysisStepKind.CalloutSplit;
            bool runYoryoAppend = stepKind == CmdAnalysisStepKind.All || stepKind == CmdAnalysisStepKind.YoryoAppend;
            bool runTzAppend = stepKind == CmdAnalysisStepKind.All || stepKind == CmdAnalysisStepKind.TzAppend;

            PartAnalysisService.ReportLog(onLog, LogKind.Info, CmdAnalysisSteps.DescribeRun(stepKind));

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
                if (!runAssembly && !HasSavedRows(repository, targets))
                {
                    // 第 2 / 3 / 4 步都依赖第 1 步写入的行：一行都没有就只提示，不做任何写库
                    PartAnalysisService.ReportLog(onLog, LogKind.Warning, string.Format(
                        "数据库中还没有这些図番的部品行，请先执行 {0}。", CmdAnalysisSteps.AssemblyStep.Label));

                    return summary;
                }

                if (runAssembly)
                {
                    CmdPartAnalyzer analyzer = new CmdPartAnalyzer();

                    // 第 1 步：装配图部品入库（读 01_アッセンブリ図_部品機能情報.csv → 建行 → xy.txt 校验 → 入库）
                    PartAnalysisService.ReportLog(onLog, LogKind.Info, string.Empty);
                    PartAnalysisService.ReportLog(onLog, LogKind.Info, string.Format(
                        "{0}读 {1} → 建行 → {2} 校验 → 入库（{3} 个 PDF）",
                        CmdAnalysisSteps.AssemblyStep.LogHeader, CmdPartAnalyzer.LtStep1FileName,
                        PdfSplitService.XyFileName, targets.Count));

                    for (int index = 0; index < targets.Count; index++)
                    {
                        AnalyzeFile(targets[index], index + 1, targets.Count, outputDirectory, analyzer,
                            repository, summary, onLog);
                    }
                }

                // 第 2 步：按 parts_info.json 的圈号（callouts）把アッセンブリ図 的部品行按位置分割
                if (runCalloutSplit)
                {
                    SplitAssemblyParts(targets, outputDirectory, repository, summary, onLog);
                }

                // 第 3 步：要領図（02_要領図_部品機能情報.csv）的部品挂到アッセンブリ図 上
                if (runYoryoAppend)
                {
                    CollectInstructionParts(targets, outputDirectory, repository, summary, onLog);
                }

                // 第 4 步：classify\tz.json（Excel 部品表）里品種命中分类依据的明细挂到アッセンブリ図 上
                if (runTzAppend)
                {
                    AppendTzParts(targets, outputDirectory, repository, summary, onLog);
                }
            }

            return summary;
        }

        /// <summary>
        /// 数据库里是否已经有这些図番的部品行 —— 第 2 / 3 / 4 步单独执行前的前置检查。
        /// 任意一个図番有行即视为第 1 步执行过（各図番的处理互不影响）。
        /// </summary>
        private static bool HasSavedRows(PartBasicInfoRepository repository, IList<PdfClassification> targets)
        {
            if (repository == null || targets == null)
            {
                return false;
            }

            foreach (PdfClassification item in targets)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Stem))
                {
                    continue;
                }

                if (new List<PartBasicInfo>(repository.Query(item.Stem)).Count > 0)
                {
                    return true;
                }
            }

            return false;
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

                // 部材名：用「番号」在 parts_info.json 的 callouts 里找圈号，取该圈号所在零件图的
                // texts（去掉数字、去掉「XX部」）用 | 连接（例：番号 4 → 縦枠）
                int memberNamed = ApplyMemberNames(outputDirectory, stem, result.Parts);

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

                if (memberNamed > 0)
                {
                    PartAnalysisService.ReportLog(onLog, LogKind.Info, string.Format(
                        "  部材名：{0} 行按「番号 → 圈号 → 零件图 texts」取自 {1}",
                        memberNamed, PartsInfoIndex.FileName));
                }

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
        /// 【第 1 步】填「部材名」：用每行的「番号」到该页目录的 <c>parts_info.json</c> 找圈号
        /// （<c>"text": "4"</c> 且 <c>"display": "④4"</c> 的 <c>callouts</c>），
        /// 取该圈号所在零件图（<c>index</c>）的全部 <c>texts</c>，<b>去掉含数字的文字</b>、
        /// <b>去掉「XX部」形式的位置标记</b>，剩下的去重后用 <c>|</c> 连接
        /// （见 <see cref="PartsInfoIndex.ResolveCalloutMemberName(int)"/>）。
        /// 同一页的 <c>parts_info.json</c> 只读一次；读不到时该页的行留空（不报错、不影响入库）。
        /// </summary>
        /// <returns>填了部材名的行数。</returns>
        private static int ApplyMemberNames(string outputDirectory, string drawingNo, IList<PartBasicInfo> parts)
        {
            if (parts == null || parts.Count == 0)
            {
                return 0;
            }

            Dictionary<int, PartsInfoIndex> indexes = new Dictionary<int, PartsInfoIndex>();
            int named = 0;

            foreach (PartBasicInfo part in parts)
            {
                if (part == null || part.Number <= 0)
                {
                    continue;
                }

                PartsInfoIndex index = LoadMemberNameIndex(outputDirectory, drawingNo, part.Page, indexes);

                if (index == null)
                {
                    continue;
                }

                string memberName = index.ResolveCalloutMemberName(part.Number);

                if (memberName.Length == 0)
                {
                    continue;
                }

                part.MemberName = memberName;
                named++;
            }

            return named;
        }

        /// <summary>
        /// 读取该页目录的 <c>parts_info.json</c>（第 1 页 <c>&lt;根&gt;\&lt;図番&gt;</c>，
        /// 第 2 页起 <c>&lt;根&gt;\&lt;図番&gt;_P2</c>）；同一页只读一次，读不到时缓存 null。
        /// </summary>
        private static PartsInfoIndex LoadMemberNameIndex(string outputDirectory, string drawingNo, int page,
            Dictionary<int, PartsInfoIndex> cache)
        {
            PartsInfoIndex cached;

            if (cache.TryGetValue(page, out cached))
            {
                return cached;
            }

            int pageNumber = page <= 1 ? 1 : page;
            string folder = pageNumber == 1
                ? drawingNo
                : drawingNo + "_P" + pageNumber.ToString(CultureInfo.InvariantCulture);
            string path = Path.Combine(outputDirectory ?? string.Empty, folder, PartsInfoIndex.FileName);
            PartsInfoIndex index = PartsInfoIndex.Load(path);

            if (index != null && !string.IsNullOrEmpty(index.LoadError))
            {
                index = null;
            }

            cache[page] = index;

            return index;
        }

        /// <summary>
        /// 第 2 步「圈号拆分」（旧称「阶段 A2」）：按圈号（<c>parts_info.json</c> 的 <c>callouts</c>）
        /// 把アッセンブリ図 的部品行按位置分割。
        ///
        /// 只处理「参照元 = アッセンブリ図」的行（<see cref="CmdCalloutPartSplitter.IsAssemblyRow"/>）：
        /// 用行上的「番号」找 <c>text</c> / <c>display</c> 自洽的圈号，圈号必须出现在 <b>index 不同</b>、
        /// <b>texts 互为包含</b>的零件图里，才按坐标定方向（左右 / 上下）把方向加到部品名前并拆成多条。
        /// 有变化时整张覆盖写回（<see cref="PartBasicInfoRepository.Save"/> 先删后插，必须提交全部行）。
        /// </summary>
        private static void SplitAssemblyParts(IList<PdfClassification> targets, string outputDirectory,
            PartBasicInfoRepository repository, PartAnalysisSummary summary, Action<LogKind, string> onLog)
        {
            List<string> drawingNumbers = GetAssemblyDrawingNumbers(targets, outputDirectory);

            if (drawingNumbers.Count == 0)
            {
                return;
            }

            PartAnalysisService.ReportLog(onLog, LogKind.Info, string.Empty);
            PartAnalysisService.ReportLog(onLog, LogKind.Info, string.Format(
                "{0}按 {1} 的圈号（callouts）把「{2}」的部品行按位置分割（{3} 个図番）",
                CmdAnalysisSteps.CalloutSplitStep.LogHeader,
                PartsInfoIndex.FileName, PdfCategoryNames.Assembly, drawingNumbers.Count));

            CmdCalloutPartSplitter splitter = new CmdCalloutPartSplitter();
            int processed = 0;

            foreach (string drawingNo in drawingNumbers)
            {
                if (string.IsNullOrWhiteSpace(drawingNo))
                {
                    continue;
                }

                try
                {
                    List<PartBasicInfo> existing = new List<PartBasicInfo>(repository.Query(drawingNo));

                    if (existing.Count == 0)
                    {
                        continue;
                    }

                    CmdCalloutSplitResult result = splitter.Split(outputDirectory, drawingNo, existing);

                    if (result.SplitGroupCount == 0 && result.BlockedCount == 0)
                    {
                        continue;
                    }

                    foreach (string note in result.Notes)
                    {
                        PartAnalysisService.ReportLog(onLog, LogKind.Info, "  " + note);
                    }

                    if (result.SplitGroupCount == 0)
                    {
                        continue;
                    }
                    repository.Save(drawingNo, result.Rows);

                    processed++;
                    summary.SplitDrawingCount = processed;
                    summary.SplitGroupCount += result.SplitGroupCount;
                    summary.SplitRowCount += result.AddedRowCount;

                    PartAnalysisService.ReportLog(onLog, LogKind.Success, string.Format(
                        "  按位置分割：{0} 条部品（図番 {1}，该図番合计 {2} 行）",
                        result.SplitGroupCount, drawingNo, result.Rows.Count));
                }
                catch (Exception ex)
                {
                    PartAnalysisService.ReportLog(onLog, LogKind.Error,
                        "  按位置分割失败（図番 " + drawingNo + "）：" + ex.Message);
                }
            }

            if (processed == 0)
            {
                PartAnalysisService.ReportLog(onLog, LogKind.Info, "  没有需要按位置分割的部品");
            }
        }

        /// <summary>
        /// 第 2 步「圈号拆分」的対象図番：<c>classify\PdfInfoList.txt</c> 里「图面分类 = アッセンブリ図」的记录，
        /// 再补上一览里的「アッセンブリ図」——清单不存在时靠后者，清单的「図番号」与 PDF 文件名不一致时
        /// 数据库里的図番（= 文件名去扩展名）也能被覆盖到。
        /// </summary>
        private static List<string> GetAssemblyDrawingNumbers(IList<PdfClassification> targets,
            string outputDirectory)
        {
            List<string> numbers = PdfClassifier.ReadDrawingNumbers(outputDirectory, PdfCategory.Assembly);

            if (targets == null)
            {
                return numbers;
            }

            foreach (PdfClassification item in targets)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Stem))
                {
                    continue;
                }

                if (!numbers.Contains(item.Stem))
                {
                    numbers.Add(item.Stem);
                }
            }

            return numbers;
        }

        /// <summary>
        /// 第 3 步「要領図部品追加」（旧称「阶段 B」）：按要領図清单把要領図的部品登录到アッセンブリ図 名下。
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
                "{0}{1} → 各要領図的 {2}，把品種命中分类依据的部品（含没有品種的共通部品）挂到「{3}」上",
                CmdAnalysisSteps.YoryoAppendStep.LogHeader,
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

                    string sharedText = result.SharedRowCount > 0
                        ? "，其中无品種（共通）" + result.SharedRowCount + " 条"
                        : string.Empty;

                    PartAnalysisService.ReportLog(onLog, LogKind.Success, string.Format(
                        "  要領図部品登录：{0} 条（図番 {1}，该図番合计 {2} 行）{3}",
                        result.Parts.Count, drawingNo, merged.Count, sharedText));
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

        /// <summary>
        /// 第 4 步「tz.json 部品追加」：读 <c>classify\PdfInfoList.txt</c> 里「图面分类 = アッセンブリ図」的
        /// 分类依据，把 <c>classify\tz.json</c>（Excel 部品表）中「品種」命中该依据的明细
        /// （<see cref="CmdTzPartCollector"/>）挂到该装配图名下。
        ///
        /// 每个アッセンブリ図：先去掉上一次本步骤写入的行（来源标识 = tz.json），按分类依据重新收集，
        /// 与已有行合并后一次写库（<see cref="PartBasicInfoRepository.Save"/> 会先清空该図番再插入，
        /// 所以必须把已有行一起提交，否则会把第 1 / 3 步的明细冲掉）。
        /// 合并后为空（该図番只有上次的 tz 行、这次一条都不再命中）时改用
        /// <see cref="PartBasicInfoRepository.ReplaceByDrawing"/> 把该図番清空 ——
        /// <see cref="PartBasicInfoRepository.Save"/> 收到空表会直接返回，不清库。
        /// </summary>
        private static void AppendTzParts(IList<PdfClassification> targets, string outputDirectory,
            PartBasicInfoRepository repository, PartAnalysisSummary summary, Action<LogKind, string> onLog)
        {
            PartAnalysisService.ReportLog(onLog, LogKind.Info, string.Empty);
            PartAnalysisService.ReportLog(onLog, LogKind.Info, string.Format(
                "{0}读 {1}（图面分类 = 「{2}」的记录，取分类依据）→ {3} 中「品種」命中分类依据的明细挂到「{2}」上",
                CmdAnalysisSteps.TzAppendStep.LogHeader, CmdTzPartCollector.PdfInfoListFileName,
                PdfCategoryNames.Assembly, CmdTzPartCollector.FileName));

            List<string> notes = new List<string>();
            Dictionary<string, string> evidence =
                CmdTzPartCollector.ReadAssemblyEvidence(outputDirectory, notes);

            foreach (string note in notes)
            {
                PartAnalysisService.ReportLog(onLog, LogKind.Warning, "  " + note);
            }

            notes.Clear();

            List<CmdTzEntry> entries = CmdTzPartCollector.LoadEntries(outputDirectory, notes);

            foreach (string note in notes)
            {
                PartAnalysisService.ReportLog(onLog, LogKind.Warning, "  " + note);
            }

            if (entries.Count == 0)
            {
                PartAnalysisService.ReportLog(onLog, LogKind.Warning,
                    "  没有读到 tz.json 明细，跳过 " + CmdAnalysisSteps.TzAppendStep.Label);

                return;
            }

            summary.TzEntryCount = entries.Count;

            PartAnalysisService.ReportLog(onLog, LogKind.Info, string.Format(
                "  {0}：{1} 条明细 / 清单里「{2}」{3} 条",
                CmdTzPartCollector.FileName, entries.Count, PdfCategoryNames.Assembly, evidence.Count));

            CmdTzPartCollector collector = new CmdTzPartCollector();
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
                    // 分类依据来自 PdfInfoList.txt；清单里没有该図番时退回一览的 Evidence
                    string basis = FindEvidence(evidence, drawingNo, item.Evidence);

                    if (string.IsNullOrWhiteSpace(basis))
                    {
                        PartAnalysisService.ReportLog(onLog, LogKind.Warning, string.Format(
                            "  図番 {0}：没有分类依据（清单里不是「{1}」？），跳过", drawingNo,
                            PdfCategoryNames.Assembly));

                        continue;
                    }

                    List<PartBasicInfo> existing = new List<PartBasicInfo>(repository.Query(drawingNo));

                    // 去掉上一次本步骤写入的行，重新按分类依据收集（番号接在剩余行之后）
                    int removed = existing.RemoveAll(IsTzRow);
                    int startNumber = NextNumber(existing);

                    CmdTzPartResult result = collector.Collect(drawingNo, basis,
                        CmdTzPartCollector.GetFilePath(outputDirectory), item.FilePath, entries,
                        startNumber);

                    foreach (string note in result.Notes)
                    {
                        PartAnalysisService.ReportLog(onLog, LogKind.Info, "  " + note);
                    }

                    if (result.Parts.Count == 0 && removed == 0)
                    {
                        continue;
                    }

                    List<PartBasicInfo> merged = new List<PartBasicInfo>(existing);
                    merged.AddRange(result.Parts);

                    if (merged.Count == 0)
                    {
                        // 该図番原本只有上一次本步写入的 tz 行，这次一条都不再命中：
                        // Save 收到空表会直接返回（不清库），所以这里用 ReplaceByDrawing 把该図番清空。
                        repository.ReplaceByDrawing(drawingNo, merged);
                    }
                    else
                    {
                        repository.Save(drawingNo, merged);
                    }

                    processed++;
                    summary.TzDrawingCount = processed;
                    summary.TzRowCount += result.Parts.Count;

                    PartAnalysisService.ReportLog(onLog, LogKind.Success, string.Format(
                        "  tz.json 部品登录：{0} 条（図番 {1}；该図番合计 {2} 行{3}）",
                        result.Parts.Count, drawingNo, merged.Count,
                        removed > 0 ? string.Format("，已剔除上次 tz 行 {0} 条", removed) : string.Empty));
                }
                catch (Exception ex)
                {
                    summary.FailedFileCount++;

                    PartAnalysisService.ReportLog(onLog, LogKind.Error,
                        "  tz.json 部品登录失败（図番 " + drawingNo + "）：" + ex.Message);
                }
            }

            if (processed == 0)
            {
                PartAnalysisService.ReportLog(onLog, LogKind.Info, "  没有需要追加的 tz.json 部品");
            }
        }

        /// <summary>
        /// 是否为第 4 步写入的行（「数据来源」= CMD分析・tz.json）。
        /// 「参照元」现在写的是该行的図番，所以这里看「数据来源」；
        /// 老数据没有「数据来源」时退回看「参照元」（升级前的老行可能还是 tz.json）。
        /// </summary>
        private static bool IsTzRow(PartBasicInfo part)
        {
            if (part == null)
            {
                return false;
            }

            if (string.Equals(part.SourceKind, PartSourceKind.CmdTz, StringComparison.Ordinal))
            {
                return true;
            }

            return string.IsNullOrEmpty(part.SourceKind) &&
                string.Equals(part.SourceTag, PartSourceTag.CmdTz, StringComparison.Ordinal);
        }

        /// <summary>优先用清单里的分类依据；清单里没有该図番时退回一览的 Evidence。</summary>
        private static string FindEvidence(Dictionary<string, string> evidence, string drawingNo, string fallback)
        {
            string basis;

            if (evidence != null && drawingNo != null && evidence.TryGetValue(drawingNo, out basis) &&
                !string.IsNullOrWhiteSpace(basis))
            {
                return basis;
            }

            return fallback ?? string.Empty;
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
                "要領図部品 {9} 行（02 文件 {10} 个 / 缺 {11}）；" +
                "tz.json 部品 {12} 行（tz 明细 {13} 条 / {14} 个図番）；" +
                "按圈号分割 {15} 行（{16} 个部品 / {17} 个図番）",
                summary.TargetCount, summary.SavedFileCount, summary.NoDataFileCount,
                CmdPartAnalyzer.LtStep1FileName, summary.MissingTableFileCount, summary.FailedFileCount,
                summary.SavedRowCount, summary.VerifiedPassedCount, summary.VerifiedFailedCount,
                summary.InstructionRowCount, summary.InstructionFileCount, summary.InstructionMissingFileCount,
                summary.TzRowCount, summary.TzEntryCount, summary.TzDrawingCount,
                summary.SplitRowCount, summary.SplitGroupCount, summary.SplitDrawingCount);
        }
    }
}
