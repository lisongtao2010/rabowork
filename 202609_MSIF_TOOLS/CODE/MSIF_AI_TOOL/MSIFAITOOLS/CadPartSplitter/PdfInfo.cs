using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace CadPartSplitter
{
    /// <summary>
    /// 一张 PDF 的信息（分类清单 <c>classify\PdfInfoList.txt</c> 里的一行）：
    /// 図番号 / 文件名 / 图面分类（基本図 / 加工図 / アッセンブリ図 / 要領図）/ 分类依据。
    ///
    /// 其余字段（页数 / 页面尺寸 / 表格张数 / 零件个数 / 文件大小 / 更新日期）清单里不再写，
    /// 模型仍保留它们，供读老清单（十列）与其它调用方使用。
    ///
    /// 下游与后续步骤用它回答「这张图是什么图」—— 例如
    /// <see cref="LtStep1Builder"/> 只在分类是 <c>アッセンブリ図</c> 时才生成
    /// <c>01_アッセンブリ図_部品機能情報.csv</c>。
    /// </summary>
    public sealed class PdfInfo
    {
        /// <summary>図番号：PDF 文件名去掉扩展名（例如 <c>H310U10004</c>）。</summary>
        public string DrawingNumber { get; set; }

        /// <summary>文件名：PDF 文件名（带扩展名，例如 <c>H310U10004.pdf</c>）。</summary>
        public string FileName { get; set; }

        /// <summary>总页数（PDF 的总页数，不是本次处理的页码）；取不到时为 0。</summary>
        public int PageCount { get; set; }

        /// <summary>页面宽（毫米，按本次渲染 DPI 由像素换算）；取不到时为 0。</summary>
        public double WidthMm { get; set; }

        /// <summary>页面高（毫米）；取不到时为 0。</summary>
        public double HeightMm { get; set; }

        /// <summary>
        /// 图面分类：基本図 / 加工図 / アッセンブリ図 / 要領図；判不出来时为空串
        /// （判定规则与 <see cref="DrawingListBuilder"/> 的 <c>drawing_list.csv</c> 完全一致）。
        /// </summary>
        public string Category { get; set; }

        /// <summary>命中的表格 CSV 文件名（判不出来时为 null）。</summary>
        public string MatchedTable { get; set; }

        /// <summary>命中的单元格原文（判不出来时为 null）。</summary>
        public string MatchedCell { get; set; }

        /// <summary>本次识别到的有线表格张数。</summary>
        public int TableCount { get; set; }

        /// <summary>本次分割出的零件个数。</summary>
        public int PartCount { get; set; }

        /// <summary>PDF 文件大小（KB）。</summary>
        public long SizeKb { get; set; }

        /// <summary>PDF 文件的最后修改时间；取不到时为 <see cref="DateTime.MinValue"/>。</summary>
        public DateTime ModifiedTime { get; set; }

        /// <summary>是否已经判出分类。</summary>
        public bool Classified
        {
            get { return !string.IsNullOrEmpty(Category); }
        }

        /// <summary>是否アッセンブリ図（組立図）。</summary>
        public bool IsAssemblyDrawing
        {
            get { return PdfInfoListBuilder.IsAssemblyCategory(Category); }
        }

        /// <summary>调试用一行描述。</summary>
        public override string ToString()
        {
            return DrawingNumber + " | " + FileName + " | " + (Classified ? Category : "（未识别）");
        }
    }

    /// <summary>
    /// 「PDF 信息清单」的生成与读取：把每张 PDF 的 図番号 / 文件名 / 图面分类 / 分类依据
    /// 记进**整批共用的一份** <c>PdfInfoList.txt</c>（UTF-8 带 BOM + CRLF + RFC 4180，与其它清单同一套规则）。
    ///
    /// 位置：<c>&lt;输出根目录&gt;\classify\PdfInfoList.txt</c> —— 不是每张图纸各一份：
    /// 一个输出根目录（批量是「输出根目录」，单张是 <c>&lt;图纸目录&gt;\AiAppData</c>）只有一份清单，
    /// 每张图纸一行；同一张图重跑时就地更新那一行（<see cref="Build"/>）。
    ///
    /// 生成时机：有线表格 CSV（<c>tables\*.csv</c>）落盘之后、<c>01_アッセンブリ図_部品機能情報.csv</c> 与
    /// <c>02_要領図_部品機能情報.csv</c> 之前 —— 后两者要「读它」才知道这张图是什么图。
    /// 分类判定复用 <see cref="DrawingListBuilder.Classify"/>（在 <c>tables\*.csv</c> 的单元格里找
    /// 「図番号前 4 位 + 基本図 / 加工図 / アッセンブリ図 / 要領図」），与 <c>drawing_list.csv</c> 同源。
    ///
    /// 只读 PDF 元信息与 <c>tables\*.csv</c>，不改动任何已有产物
    /// （唯一会删的是老版本「一张图一份」的 <c>&lt;输出目录&gt;\PdfInfoList.txt</c>）。
    /// </summary>
    public static class PdfInfoListBuilder
    {
        /// <summary>输出文件名（整批一份，放在 <see cref="ClassifyFolderName"/> 目录下）。</summary>
        public const string OutputFileName = "PdfInfoList.txt";

        /// <summary>
        /// 清单所在子目录名：输出根目录下的 <c>classify</c>
        /// （<c>&lt;输出根目录&gt;\classify\PdfInfoList.txt</c>）。
        /// </summary>
        public const string ClassifyFolderName = "classify";

        /// <summary>「アッセンブリ図」关键字：分类命中它才认为这张图是組立図。</summary>
        public const string AssemblyCategoryKeyword = "アッセンブリ図";

        /// <summary>「要領図」关键字：分类命中它才认为这张图是要領図（要領書）。</summary>
        public const string RequirementCategoryKeyword = "要領図";

        /// <summary>
        /// 「分类依据」里表示「人工在界面（<see cref="PdfInfoEditorForm"/>）上设定」的前缀。
        ///
        /// <see cref="Build"/> 看到这个前缀就不再改这一行的分类 —— 自动判定（在 <c>tables\*.csv</c> 里找
        /// 「図番号前 4 位 + 基本図 / 加工図 / アッセンブリ図 / 要領図」）判不出来时人工补的值，
        /// 重跑分割、清单一合并就被冲掉的话这个维护窗口就没意义了。
        /// </summary>
        public const string ManualBasisPrefix = "手工设定";

        /// <summary>tables 子目录名（与 <see cref="RuledTableWriter"/> 写出的位置一致）。</summary>
        private const string TablesFolderName = "tables";

        /// <summary>输出表头：固定四列，下游按列取数。</summary>
        private static readonly string[] Header =
        {
            "図番号", "文件名", "图面分类", "分类依据"
        };

        /// <summary>
        /// 图种关键字，顺序即优先级（与 <see cref="DrawingListBuilder"/> 一致）：
        /// 前面的先找、命中即定。
        /// </summary>
        private static readonly string[] RowCategoryKeywords = { "基本図", "加工図", "アッセンブリ図", "要領図" };

        // ── 生成 ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 提取一张 PDF 的 図番号 / 文件名 / 图面分类，合并进整批共用的
        /// <c>&lt;输出根目录&gt;\classify\PdfInfoList.txt</c>：同一张图（同図番号）就替换那一行，否则追加一行。
        /// </summary>
        /// <param name="pdfPath">PDF 路径（非 PDF 直接跳过，不生成文件）。</param>
        /// <param name="outputDirectory">该图纸的输出目录（用来推回输出根目录 → <c>classify</c>）。</param>
        /// <param name="partCount">本次分割出的零件个数（清单只写四列，参数保留兼容调用方）。</param>
        /// <param name="tableCount">本次识别到的有线表格张数（同上）。</param>
        /// <param name="renderWidth">本次渲染的像素宽（同上）。</param>
        /// <param name="renderHeight">本次渲染的像素高（同上）。</param>
        /// <param name="opts">参数 / 日志（可为 null）。</param>
        /// <returns>清单全路径；未生成（被关闭 / 非 PDF / 写出失败）时返回 null。</returns>
        public static string Build(string pdfPath, string outputDirectory, int partCount, int tableCount,
                                   int renderWidth, int renderHeight, DetectorOptions opts)
        {
            DetectorOptions o = opts ?? new DetectorOptions();

            if (!o.BuildPdfInfoList)
            {
                o.Log("[INFO] PdfInfoList 已关闭（BuildPdfInfoList = false）。");
                return null;
            }

            if (string.IsNullOrWhiteSpace(outputDirectory) || string.IsNullOrWhiteSpace(pdfPath))
                return null;

            // 只有 PDF 才有图面分类可谈（位图输入不生成清单）
            if (!string.Equals(Path.GetExtension(pdfPath), ".pdf", StringComparison.OrdinalIgnoreCase))
                return null;

            var info = new PdfInfo
            {
                DrawingNumber = Path.GetFileNameWithoutExtension(pdfPath),
                FileName = Path.GetFileName(pdfPath),
                TableCount = tableCount,
                PartCount = partCount,
                Category = string.Empty
            };

            // 图面分类：与 drawing_list.csv 同一套规则（在 tables\*.csv 的单元格里找图番号前缀 + 关键字）
            string cell;
            string table;
            info.Category = DrawingListBuilder.Classify(
                info.DrawingNumber, Path.Combine(outputDirectory, TablesFolderName), o, out cell, out table);
            info.MatchedCell = cell;
            info.MatchedTable = table;

            // 同一格没命中时按「同一行」再找一次：表題欄常把「図番号」和「図種」写在相邻的两格里
            // （例如 `H310U10025 | 枠 … 要領図`），那种写法在上面的逐格判定里是找不到的。
            if (!info.Classified)
            {
                string rowCell;
                string rowTable;
                string byRow = ClassifyByRow(info.DrawingNumber, Path.Combine(outputDirectory, TablesFolderName), o,
                                             out rowCell, out rowTable);

                if (!string.IsNullOrEmpty(byRow))
                {
                    o.Log("[INFO] PdfInfoList：图面分类改按「同一行」命中 —— " + rowTable + "：" + CsvFile.SingleLine(rowCell));
                    info.Category = byRow;
                    info.MatchedCell = rowCell;
                    info.MatchedTable = rowTable;
                }
            }

            // 整批共用一份清单：<输出根目录>\classify\PdfInfoList.txt
            string path = Path.Combine(GetClassifyDirectory(outputDirectory, info.DrawingNumber), OutputFileName);
            List<string[]> rows = ReadExistingRows(path, o);      // 表头之外的数据行（读不动时为空）
            // 手工维护优先：界面上手工设定的分类不被自动判定覆盖；这次没判出来的，也保留上次的值
            KeepManualCategory(rows, info, o);

            int replaced = ReplaceRow(rows, ToRow(info), info.DrawingNumber);

            try
            {
                string listDirectory = Path.GetDirectoryName(path);

                if (!string.IsNullOrEmpty(listDirectory))
                    Directory.CreateDirectory(listDirectory);

                CsvFile.Write(path, Header, rows);
            }
            catch (IOException ex)
            {
                o.Log("[ERROR] PdfInfoList 写出失败：" + ex.GetType().Name + "：" + ex.Message);
                return null;
            }
            catch (UnauthorizedAccessException ex)
            {
                o.Log("[ERROR] PdfInfoList 写出失败：" + ex.GetType().Name + "：" + ex.Message);
                return null;
            }

            // 老版本是「一张图一份」写在图纸自己的输出目录里，顺手清掉，免得下游按老位置取到过期内容
            RemovePerDrawingList(outputDirectory, o);

            o.Log("[INFO] PdfInfoList：" + info.DrawingNumber + " → 图面分类 "
                  + (info.Classified ? info.Category : "未识别")
                  + (replaced >= 0 ? "（更新第 " + (replaced + 1) + " 行）" : "（新增一行）")
                  + "，清单共 " + rows.Count + " 条，已保存：" + path);

            return path;
        }

        /// <summary>
        /// 手工维护优先：清单里这张图已有的分类若「是界面上手工设的」（分类依据以 <see cref="ManualBasisPrefix"/> 开头），
        /// 或者这次自动判定没命中（分类为空），就沿用清单里的分类与依据，不覆盖 ——
        /// 否则人工修正会在下次分割、清单一合并时被冲掉。
        ///
        /// 只改内存里的 <paramref name="info"/>，不碰 <paramref name="rows"/>（替换由 <see cref="ReplaceRow"/> 做）。
        /// </summary>
        /// <param name="rows">清单里表头之外的数据行（第一列是図番号，第三列是図面分類，第四列是分类依据）。</param>
        /// <param name="info">本次要写进去的那条记录（会被就地修改）。</param>
        /// <param name="o">参数 / 日志。</param>
        private static void KeepManualCategory(List<string[]> rows, PdfInfo info, DetectorOptions o)
        {
            if (rows == null || info == null || string.IsNullOrEmpty(info.DrawingNumber))
                return;

            string[] existing = null;

            foreach (string[] row in rows)
            {
                if (row.Length > 0
                    && string.Equals((row[0] ?? string.Empty).Trim(), info.DrawingNumber, StringComparison.OrdinalIgnoreCase))
                {
                    existing = row;
                    break;
                }
            }

            if (existing == null)
                return;

            string oldCategory = Cell(existing, 2);
            if (string.IsNullOrEmpty(oldCategory))
                return;

            string oldBasis = Cell(existing, 3);
            bool manual = oldBasis.TrimStart().StartsWith(ManualBasisPrefix, StringComparison.Ordinal);

            // 这次判出来了、又不是手工设定 → 按新结果更新
            if (!manual && !string.IsNullOrEmpty(info.Category))
                return;

            info.Category = oldCategory;
            info.MatchedTable = null;
            info.MatchedCell = oldBasis;

            o.Log(manual
                ? "[INFO] PdfInfoList：" + info.DrawingNumber + " 的图面分类是手工设定的（"
                  + CsvFile.SingleLine(oldCategory) + "），保留清单里的值"
                : "[INFO] PdfInfoList：" + info.DrawingNumber + " 这次没判出图面分类，保留清单里上次的值（"
                  + CsvFile.SingleLine(oldCategory) + "）");
        }

        // ── 清单位置 ──────────────────────────────────────────────────────────

        /// <summary>
        /// 清单所在的 <c>classify</c> 目录：整批图纸共用一份 <c>PdfInfoList.txt</c>，
        /// 统一放在「输出根目录」下的 <c>classify</c> 里（<c>&lt;输出根目录&gt;\classify\PdfInfoList.txt</c>）。
        ///
        /// 输出根目录的认定：<paramref name="outputDirectory"/> 的目录名就是这张图的図番号
        /// （批量 / 默认单张的 <c>&lt;根&gt;\&lt;图纸名&gt;</c>，多页为 <c>&lt;图纸名&gt;_P2</c>）时取它的上一层；
        /// 否则（显式给了输出目录、没有按图纸名分子文件夹，例如 <c>out</c>）就取它自己。
        /// </summary>
        /// <param name="outputDirectory">该图纸的输出目录。</param>
        /// <param name="drawingNumber">図番号（PDF 文件名去扩展名）。</param>
        public static string GetClassifyDirectory(string outputDirectory, string drawingNumber)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
                return string.Empty;

            string dir;

            try
            {
                dir = Path.GetFullPath(outputDirectory)
                          .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
            catch (NotSupportedException)
            {
                return string.Empty;
            }
            catch (PathTooLongException)
            {
                return string.Empty;
            }

            if (string.IsNullOrEmpty(dir))
                return string.Empty;

            // 目录名 == 図番号（多页带 _P2 后缀，DrawingNumberFromOutputDirectory 会去掉）→ 上一层才是根
            if (!string.IsNullOrEmpty(drawingNumber)
                && string.Equals(DrawingNumberFromOutputDirectory(dir), drawingNumber, StringComparison.OrdinalIgnoreCase))
            {
                string parent = Path.GetDirectoryName(dir);

                if (!string.IsNullOrEmpty(parent))
                    dir = parent;
            }

            return Path.Combine(dir, ClassifyFolderName);
        }

        // ── 累加 ──────────────────────────────────────────────────────────────

        /// <summary>读回一份已存在的清单，只取「表头之外」的数据行；文件不存在 / 读不动时返回空列表。</summary>
        private static List<string[]> ReadExistingRows(string path, DetectorOptions o)
        {
            var rows = new List<string[]>();

            if (!File.Exists(path))
                return rows;

            List<string[]> all;

            try
            {
                all = CsvFile.Read(path);
            }
            catch (IOException ex)
            {
                o.Log("[WARN] PdfInfoList：读不动旧清单 " + path + "（" + ex.GetType().Name + "），按空清单重写。");
                return rows;
            }
            catch (UnauthorizedAccessException ex)
            {
                o.Log("[WARN] PdfInfoList：无权限读旧清单 " + path + "（" + ex.GetType().Name + "），按空清单重写。");
                return rows;
            }

            for (int r = 1; r < all.Count; r++)   // 第 0 行是表头
            {
                if (!CsvFile.IsEmptyRow(all[r]))
                    rows.Add(all[r]);
            }

            return rows;
        }

        /// <summary>
        /// 把这条记录放进清单：已有同一张图（两版清单的第一列都是 `図番号`）就替换那一行并返回其下标，
        /// 没有就追加到末尾并返回 -1。
        /// </summary>
        private static int ReplaceRow(List<string[]> rows, string[] newRow, string drawingNumber)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Length > 0
                    && string.Equals((rows[i][0] ?? string.Empty).Trim(), drawingNumber, StringComparison.OrdinalIgnoreCase))
                {
                    rows[i] = newRow;
                    return i;
                }
            }

            rows.Add(newRow);
            return -1;
        }

        /// <summary>
        /// 清掉老版本「一张图一份」的 <c>&lt;输出目录&gt;\PdfInfoList.txt</c>
        /// （现在只保留整批共用的 <c>classify\PdfInfoList.txt</c>）。
        /// 删不掉（被 Excel 打开 / 无权限）只记 WARN，不影响本次输出。
        /// </summary>
        private static void RemovePerDrawingList(string outputDirectory, DetectorOptions o)
        {
            string legacy = Path.Combine(outputDirectory, OutputFileName);

            if (!File.Exists(legacy))
                return;

            try
            {
                File.Delete(legacy);
                o.Log("[INFO] PdfInfoList：已删除老位置的分图纸清单 " + legacy + "（现在统一放 classify 目录）。");
            }
            catch (IOException)
            {
                o.Log("[WARN] PdfInfoList：删不掉老位置的分图纸清单 " + legacy + "（可能正被 Excel 打开）。");
            }
            catch (UnauthorizedAccessException)
            {
                o.Log("[WARN] PdfInfoList：删不掉老位置的分图纸清单 " + legacy + "（无权限）。");
            }
        }

        // ── 读取 / 判定 ────────────────────────────────────────────────────────

        /// <summary>
        /// 读回 <paramref name="directory"/> 下的 <c>PdfInfoList.txt</c>（老版本「一张图一份」的位置；
        /// 整批共用的清单见 <see cref="ReadForDrawing"/>）。文件不存在 / 读不动时返回空列表（不抛异常）。
        /// </summary>
        public static List<PdfInfo> Read(string directory, DetectorOptions opts)
        {
            if (string.IsNullOrWhiteSpace(directory))
                return new List<PdfInfo>();

            return ReadFile(Path.Combine(directory, OutputFileName), opts ?? new DetectorOptions());
        }

        /// <summary>
        /// 为一张图纸找分类清单：先读整批共用的 <c>&lt;输出根目录&gt;\classify\PdfInfoList.txt</c>，
        /// 没有再退回老版本「一张图一份」的 <c>&lt;输出目录&gt;\PdfInfoList.txt</c>；
        /// 两处都没有时返回空列表（调用方退回现判）。
        /// </summary>
        /// <param name="outputDirectory">该图纸的输出目录。</param>
        /// <param name="drawingNumber">図番号（PDF 文件名去扩展名）。</param>
        /// <param name="opts">参数 / 日志（可为 null）。</param>
        /// <param name="source">读到的是哪个文件（<c>classify\PdfInfoList.txt</c> / <c>PdfInfoList.txt</c>）；都没有时为 null。</param>
        public static List<PdfInfo> ReadForDrawing(string outputDirectory, string drawingNumber,
                                                   DetectorOptions opts, out string source)
        {
            DetectorOptions o = opts ?? new DetectorOptions();
            source = null;

            if (string.IsNullOrWhiteSpace(outputDirectory))
                return new List<PdfInfo>();

            List<PdfInfo> items =
                ReadFile(Path.Combine(GetClassifyDirectory(outputDirectory, drawingNumber), OutputFileName), o);

            if (items.Count > 0)
            {
                source = ClassifyFolderName + "\\" + OutputFileName;
                return items;
            }

            items = ReadFile(Path.Combine(outputDirectory, OutputFileName), o);

            if (items.Count > 0)
                source = OutputFileName;

            return items;
        }

        /// <summary>
        /// 按「清单文件全路径」读回整份清单（文件不存在 / 读不动时返回空列表，不抛异常）。
        ///
        /// 与 <see cref="Read"/> 的区别：<see cref="Read"/> 只认目录下固定文件名的
        /// <see cref="OutputFileName"/>，这里给的是完整路径 —— GUI 的清单维护窗口
        /// （<see cref="PdfInfoEditorForm"/>）用它读用户选中的任意文件。
        /// </summary>
        /// <param name="path">清单文件全路径。</param>
        /// <param name="opts">参数 / 日志（可为 null）。</param>
        public static List<PdfInfo> ReadFile(string path, DetectorOptions opts)
        {
            DetectorOptions o = opts ?? new DetectorOptions();
            var items = new List<PdfInfo>();

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return items;

            List<string[]> rows;

            try
            {
                rows = CsvFile.Read(path);
            }
            catch (IOException ex)
            {
                o.Log("[WARN] PdfInfoList：读不动 " + path + "（" + ex.GetType().Name + "），按「没有清单」处理。");
                return items;
            }
            catch (UnauthorizedAccessException ex)
            {
                o.Log("[WARN] PdfInfoList：无权限读 " + path + "（" + ex.GetType().Name + "），按「没有清单」处理。");
                return items;
            }

            if (rows.Count == 0)
                return items;

            string[] header = rows[0];
            int cNumber = CsvFile.IndexOfHeader(header, "図番号");
            int cName = CsvFile.IndexOfHeader(header, "文件名");
            int cPages = CsvFile.IndexOfHeader(header, "页数");
            int cSize = CsvFile.IndexOfHeader(header, "页面尺寸mm");
            int cCategory = CsvFile.IndexOfHeader(header, "图面分类");
            int cBasis = CsvFile.IndexOfHeader(header, "分类依据");
            int cTables = CsvFile.IndexOfHeader(header, "表格数");
            int cParts = CsvFile.IndexOfHeader(header, "零件数");

            for (int r = 1; r < rows.Count; r++)
            {
                string[] row = rows[r];

                if (CsvFile.IsEmptyRow(row))
                    continue;

                var info = new PdfInfo
                {
                    DrawingNumber = Cell(row, cNumber),
                    FileName = Cell(row, cName),
                    PageCount = IntCell(row, cPages),
                    TableCount = IntCell(row, cTables),
                    PartCount = IntCell(row, cParts),
                    Category = Cell(row, cCategory),
                    MatchedCell = Cell(row, cBasis),
                    ModifiedTime = DateTime.MinValue
                };

                SizeTextToMm(Cell(row, cSize), info);
                items.Add(info);
            }

            return items;
        }

        /// <summary>按図番号找一行（忽略大小写）；找不到返回 null。</summary>
        public static PdfInfo Find(List<PdfInfo> items, string drawingNumber)
        {
            if (items == null || string.IsNullOrWhiteSpace(drawingNumber))
                return null;

            foreach (PdfInfo item in items)
            {
                if (string.Equals(item.DrawingNumber, drawingNumber, StringComparison.OrdinalIgnoreCase))
                    return item;
            }

            return null;
        }

        /// <summary>
        /// 判定一张图的图面分类：先读分类清单（整批共用的 <c>&lt;输出根目录&gt;\classify\PdfInfoList.txt</c>，
        /// 老版本的 <c>&lt;输出目录&gt;\PdfInfoList.txt</c> 也认）；清单不存在 / 里面没有这张图时，
        /// 退回按 <c>tables\*.csv</c> 现判（与清单同一套规则）。
        /// </summary>
        /// <param name="outputDirectory">该图纸的输出目录。</param>
        /// <param name="drawingNumber">図番号（PDF 文件名去扩展名）。</param>
        /// <param name="opts">参数 / 日志（可为 null）。</param>
        /// <param name="source">依据（清单文件 + 命中的单元格 / 表格 CSV），供日志核对。</param>
        /// <returns>基本図 / 加工図 / アッセンブリ図 / 要領図；判不出来返回空串。</returns>
        public static string ResolveCategory(string outputDirectory, string drawingNumber, DetectorOptions opts,
                                             out string source)
        {
            DetectorOptions o = opts ?? new DetectorOptions();

            string listSource;
            PdfInfo info = Find(ReadForDrawing(outputDirectory, drawingNumber, o, out listSource), drawingNumber);

            if (info != null)
            {
                string basis = CsvFile.SingleLine(info.MatchedCell ?? string.Empty);

                source = (listSource ?? OutputFileName)
                         + (basis.Length == 0 ? "（无判定依据）" : "（" + basis + "）");
                return info.Category ?? string.Empty;
            }

            string cell;
            string table;
            string tablesDir = Path.Combine(outputDirectory, TablesFolderName);
            string category = DrawingListBuilder.Classify(drawingNumber, tablesDir, o, out cell, out table);

            // 逐格没命中时按「同一行」再找一次（表題欄常把図番号与図種写在相邻两格里）
            if (string.IsNullOrEmpty(category))
                category = ClassifyByRow(drawingNumber, tablesDir, o, out cell, out table);

            source = string.IsNullOrEmpty(table)
                         ? TablesFolderName + "\\*.csv（没有清单，现判）"
                         : TablesFolderName + "\\" + table + "（没有清单，现判：" + CsvFile.SingleLine(cell) + "）";

            return category;
        }

        /// <summary>
        /// 行级兜底判定：表題欄常把「図番号」和「図種」写在**同一行的不同格**里
        /// （例如 <c>H310U10025 | 枠 … 要領図</c>），而 <see cref="DrawingListBuilder.Classify"/>
        /// 要求两者落在同一格，那种写法判不出来 —— 这里再按「同一行里既有图番号前缀、又有图种关键字」找一次。
        /// 关键字优先级与 <see cref="DrawingListBuilder"/> 相同：基本図 → 加工図 → アッセンブリ図 → 要領図。
        /// </summary>
        /// <param name="drawingNumber">図番号（PDF 文件名去掉扩展名）。</param>
        /// <param name="tablesDirectory">该图纸的 <c>tables</c> 目录。</param>
        /// <param name="opts">参数 / 日志（可为 null）。</param>
        /// <param name="matchedCell">命中那一行的文字（格与格之间用 <c> / </c> 连）。</param>
        /// <param name="matchedTable">命中的 CSV 文件名。</param>
        /// <returns>基本図 / 加工図 / アッセンブリ図 / 要領図；都没命中返回空串。</returns>
        public static string ClassifyByRow(string drawingNumber, string tablesDirectory, DetectorOptions opts,
                                           out string matchedCell, out string matchedTable)
        {
            matchedCell = null;
            matchedTable = null;

            string prefix = DrawingListBuilder.DrawingPrefix(drawingNumber);

            if (prefix.Length == 0 || string.IsNullOrWhiteSpace(tablesDirectory) || !Directory.Exists(tablesDirectory))
                return string.Empty;

            string[] files = Directory.GetFiles(tablesDirectory, "*.csv");

            if (files.Length == 0)
                return string.Empty;

            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            // 一次读进内存，四个关键字共用（通常十几张小表）
            var tables = new List<KeyValuePair<string, List<string[]>>>(files.Length);

            foreach (string file in files)
            {
                try
                {
                    tables.Add(new KeyValuePair<string, List<string[]>>(Path.GetFileName(file), CsvFile.Read(file)));
                }
                catch (IOException)
                {
                    // 被 Excel 占用 / 读到一半被删：跳过这一张
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            foreach (string keyword in RowCategoryKeywords)
            {
                string lastRow = null;
                string lastTable = null;

                foreach (KeyValuePair<string, List<string[]>> table in tables)
                {
                    foreach (string[] row in table.Value)
                    {
                        bool hasPrefix = false;
                        var texts = new List<string>(row.Length);

                        foreach (string text in row)
                        {
                            if (string.IsNullOrEmpty(text))
                                continue;

                            string flat = CsvFile.Normalize(text);
                            texts.Add(flat);

                            if (flat.IndexOf(prefix, StringComparison.OrdinalIgnoreCase) >= 0)
                                hasPrefix = true;
                        }

                        if (!hasPrefix)
                            continue;

                        string joined = string.Join(" / ", texts.ToArray());

                        if (joined.IndexOf(keyword, StringComparison.Ordinal) < 0)
                            continue;

                        lastRow = joined;
                        lastTable = table.Key;
                    }
                }

                if (lastRow != null)
                {
                    matchedCell = lastRow;
                    matchedTable = lastTable;
                    return keyword;
                }
            }

            return string.Empty;
        }

        /// <summary>分类是否「アッセンブリ図」（归一化后包含即算，容忍空格 / 全半角）。</summary>
        public static bool IsAssemblyCategory(string category)
        {
            return CsvFile.Normalize(category ?? string.Empty)
                          .IndexOf(AssemblyCategoryKeyword, StringComparison.Ordinal) >= 0;
        }

        /// <summary>分类是否「要領図」（归一化后包含即算，容忍空格 / 全半角）。</summary>
        public static bool IsRequirementCategory(string category)
        {
            return CsvFile.Normalize(category ?? string.Empty)
                          .IndexOf(RequirementCategoryKeyword, StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// 由输出目录推回図番号：取目录名；多页 PDF 的 <c>_P2</c> / <c>_P3</c> 后缀会去掉
        /// （清单按 PDF 记一条，不按页码分开）。
        /// </summary>
        public static string DrawingNumberFromOutputDirectory(string outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
                return string.Empty;

            string name;

            try
            {
                name = Path.GetFileName(
                    outputDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }

            if (string.IsNullOrEmpty(name))
                return string.Empty;

            int page = name.LastIndexOf("_P", StringComparison.OrdinalIgnoreCase);

            if (page > 0 && page + 2 < name.Length)
            {
                bool digits = true;

                for (int i = page + 2; i < name.Length; i++)
                {
                    if (!char.IsDigit(name[i]))
                    {
                        digits = false;
                        break;
                    }
                }

                if (digits)
                    name = name.Substring(0, page);
            }

            return name;
        }

        // ── 手工维护（GUI「图面分类清单」窗口）─────────────────────────────────

        /// <summary>
        /// 清单文件的完整路径（<c>&lt;输出根目录&gt;\classify\PdfInfoList.txt</c>），
        /// 供 GUI 的清单维护窗口（<see cref="PdfInfoEditorForm"/>）定位要编辑的文件。
        /// </summary>
        /// <param name="outputDirectory">该图纸的输出目录。</param>
        /// <param name="drawingNumber">
        /// 図番号（单张图纸传 PDF 文件名去扩展名）；批量模式传空串，
        /// 此时直接取 <paramref name="outputDirectory"/> 下的 <c>classify</c>。
        /// </param>
        public static string GetListPath(string outputDirectory, string drawingNumber)
        {
            string directory = GetClassifyDirectory(outputDirectory, drawingNumber);
            return string.IsNullOrEmpty(directory) ? string.Empty : Path.Combine(directory, OutputFileName);
        }

        /// <summary>
        /// 整份清单覆盖写回（GUI 手工维护「图面分类」用）：固定四列、RFC 4180 + BOM + CRLF，
        /// 与 <see cref="Build"/> 写出的格式完全一致；目录不存在时自动创建。
        ///
        /// 与 <see cref="Build"/> 的唯一区别：<b>分类为空也照样写出「分类依据」</b> ——
        /// 人在界面上填的依据不该因为分类暂时空着就丢掉。
        /// </summary>
        /// <param name="path">清单文件全路径（见 <see cref="GetListPath"/>）。</param>
        /// <param name="items">要写入的全部行（顺序即写入顺序）。</param>
        /// <param name="opts">参数 / 日志（可为 null）。</param>
        /// <param name="error">写失败的原因；成功时为 null。</param>
        /// <returns>true = 已写出。</returns>
        public static bool Save(string path, IEnumerable<PdfInfo> items, DetectorOptions opts, out string error)
        {
            DetectorOptions o = opts ?? new DetectorOptions();
            error = null;

            if (string.IsNullOrWhiteSpace(path))
            {
                error = "清单路径为空。";
                return false;
            }

            var rows = new List<string[]>();

            if (items != null)
            {
                foreach (PdfInfo info in items)
                {
                    if (info != null)
                        rows.Add(ToRow(info, true));
                }
            }

            try
            {
                string directory = Path.GetDirectoryName(Path.GetFullPath(path));

                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                CsvFile.Write(path, Header, rows);
            }
            catch (IOException ex)
            {
                error = ex.GetType().Name + "：" + ex.Message;
            }
            catch (UnauthorizedAccessException ex)
            {
                error = ex.GetType().Name + "：" + ex.Message;
            }
            catch (ArgumentException ex)
            {
                error = ex.GetType().Name + "：" + ex.Message;
            }
            catch (NotSupportedException ex)
            {
                error = ex.GetType().Name + "：" + ex.Message;
            }

            if (error != null)
            {
                o.Log("[ERROR] PdfInfoList：清单保存失败（" + path + "）：" + error);
                return false;
            }

            o.Log("[INFO] PdfInfoList：已保存 " + rows.Count + " 条（手工维护）→ " + path);
            return true;
        }

        // ── 一行 <-> 对象 ──────────────────────────────────────────────────────

        /// <summary>把一条信息转成一行数据（列顺序与 <see cref="Header"/> 一致：図番号 / 文件名 / 图面分类 / 分类依据）。</summary>
        private static string[] ToRow(PdfInfo info)
        {
            return ToRow(info, false);
        }

        /// <summary>把一条信息转成一行数据（列顺序与 <see cref="Header"/> 一致：図番号 / 文件名 / 图面分类 / 分类依据）。</summary>
        /// <param name="info">一条信息。</param>
        /// <param name="keepBasisWhenUnclassified">
        /// 分类为空时是否照样写出「分类依据」：<see cref="Build"/> 写盘用 false（与历史行为一致，未识别就不留依据）；
        /// GUI 手工维护（<see cref="Save"/>）用 true —— 人在界面上填的依据不该因为分类暂时空着就丢掉。
        /// </param>
        private static string[] ToRow(PdfInfo info, bool keepBasisWhenUnclassified)
        {
            return new[]
            {
                info.DrawingNumber ?? string.Empty,
                info.FileName ?? string.Empty,
                info.Category ?? string.Empty,
                BasisText(info, keepBasisWhenUnclassified)
            };
        }

        /// <summary>把 <c>420.0x297.0</c> 解析回毫米（解析不了就留 0）。</summary>
        private static void SizeTextToMm(string text, PdfInfo info)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            string[] parts = text.Split('x', 'X');

            if (parts.Length != 2)
                return;

            double width;
            double height;

            if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out width))
                info.WidthMm = width;

            if (double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out height))
                info.HeightMm = height;
        }

        /// <summary>分类依据文本：<c>table_05.csv：H310 … アッセンブリ図</c>；未识别时为空。</summary>
        private static string BasisText(PdfInfo info, bool keepWhenUnclassified)
        {
            if (!info.Classified && !keepWhenUnclassified)
                return string.Empty;

            string cell = CsvFile.SingleLine(info.MatchedCell ?? string.Empty);

            return string.IsNullOrEmpty(info.MatchedTable) ? cell : info.MatchedTable + "：" + cell;
        }

        /// <summary>取一列（越界 / 缺列给空串）。</summary>
        private static string Cell(string[] row, int column)
        {
            return column >= 0 && column < row.Length ? (row[column] ?? string.Empty) : string.Empty;
        }

        /// <summary>取一个整数列（缺列 / 非数字给 0）。</summary>
        private static int IntCell(string[] row, int column)
        {
            int value;

            return int.TryParse(Cell(row, column), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                       ? value
                       : 0;
        }
    }
}
