using System;
using System.IO;

namespace CadPartSplitter
{
    /// <summary>
    /// 零件检测与分割流程的全部可调参数。
    /// 每个参数均注明调优方向，适配不同 DPI 或图纸版式。
    /// </summary>
    public class DetectorOptions
    {
        // ── 区域屏蔽 ─────────────────────────────────────────────────────────

        /// <summary>
        /// 右侧 BOM 表 / 说明文字区域的遮蔽比例（0.0 ~ 1.0）。
        /// 典型值 0.48~0.52；右侧干扰零件检测时适当增大，左侧零件被切掉时减小。
        ///
        /// 注意：开启 <see cref="AutoMaskRightColumn"/>（默认开启）时，
        /// 本值仅作为「自动检测失败时的兜底比例」，不再直接生效。
        /// </summary>
        public double RightMaskRatio { get; set; } = 0.50;

        /// <summary>
        /// 是否自动检测右侧表格 / 标题栏的起始列作为遮蔽边界（默认开启）。
        ///
        /// 固定比例遮蔽（如 0.50）会把「图幅较宽、右侧还画有零件视图」的图纸误切，
        /// 典型现象：图纸上明明画了两个相同的零件（例如左右各一个「戸当り」），
        /// 分割结果却只有一个——右侧那个整块落在 50% 遮蔽区内被抹掉了。
        ///
        /// 自动检测依据「列墨迹」：表格 / 标题栏区每一列都有表格线或文字，
        /// 列墨迹稳定高于阈值；零件区列与列之间必然存在细窄空白列。
        /// 于是从最右侧向左延伸，遇到第一处低于阈值的列即为表格区左边界。
        /// 关闭本开关可退回 <see cref="RightMaskRatio"/> 的固定比例行为。
        /// </summary>
        public bool AutoMaskRightColumn { get; set; } = true;

        /// <summary>
        /// 自动遮蔽时判定「该列属于表格 / 标题栏」的列墨迹阈值（图高比例）。
        /// 默认 0.010（即一列里要有 1% 图高的前景像素才算“密实列”）。
        /// 300 DPI 的 A1 图纸（高约 7000 px）对应约 70 px。
        /// 阈值偏大 → 边界落在表格内部（右侧表格残留）；偏小 → 边界吃进零件区。
        /// </summary>
        public double AutoMaskColumnInkRatio { get; set; } = 0.010;

        /// <summary>
        /// 自动遮蔽检测结果的最小遮蔽宽度（占图宽比例）。
        /// 低于此值说明右侧没有成片的表格区，退回 <see cref="RightMaskRatio"/>。
        /// </summary>
        public double AutoMaskMinMaskedRatio { get; set; } = 0.05;

        /// <summary>
        /// 自动遮蔽检测结果的最大遮蔽宽度（占图宽比例）。
        /// 超出此值通常是误判（例如整页被零件/填充图元占满，找不到分界列），
        /// 此时退回 <see cref="RightMaskRatio"/> 的固定比例。
        /// </summary>
        public double AutoMaskMaxMaskedRatio { get; set; } = 0.60;

        // ── 图框擦除 ─────────────────────────────────────────────────────────

        /// <summary>
        /// 是否在二值化后擦除图纸四周的图框（默认开启）。
        ///
        /// 图框是一条贴近页边、几乎贯穿整幅的矩形线。它有两个害处：
        ///   ① 与内部视图通过尺寸线粘连后，整个框会被当成一个「零件」（框的内部全被带进来）；
        ///   ② 框被遮蔽线截断后，残片面积占比降到 <see cref="MaxBboxFillRatio"/> 以下，
        ///      反而躲过「外框过滤」，输出一张「半页图」。
        /// 擦掉后图框不再参与连通域，内部视图各自独立成块。
        /// </summary>
        public bool RemovePageFrame { get; set; } = true;

        /// <summary>
        /// 图框线允许出现的「页边带」宽度比例（默认 0.10，即只擦除顶部 / 底部 / 左侧 / 右侧各 10% 内的贯穿线）。
        /// 调大会擦掉靠近页边的零件长边线；调小可能漏掉内缩较多的图框。
        /// </summary>
        public double PageFrameEdgeRatio { get; set; } = 0.10;

        /// <summary>
        /// 判定为图框线所需的贯穿比例（默认 0.70，即一行 / 一列要有 70% 幅面有墨迹）。
        /// 调大会漏擦（图框被文字打断）；调小会把零件的长中心线也擦掉。
        /// </summary>
        public double PageFrameSpanRatio { get; set; } = 0.70;

        // ── 表格块遮蔽（代替整列遮蔽）────────────────────────────────────────

        /// <summary>
        /// 是否用「文字层推断出的表格块」精确遮蔽，而不是把右侧整列涂黑（默认开启）。
        ///
        /// 整列遮蔽的前提是「表格区与零件区在 X 方向可分」。当图纸的表格在右上、
        /// 零件视图在下方中部（两者 X 投影重叠）时，密实列带会一路吃进零件区，
        /// 把中部视图整块抹掉（典型现象：输出图只剩左侧一半）。
        /// 开启后只涂黑真正的表格 / 表題欄矩形，其余像素原样保留。
        /// 文字层不可用或检测不到表格块时，自动回退为整列遮蔽。
        /// </summary>
        public bool MaskTableBlocks { get; set; } = true;

        /// <summary>
        /// 表格块遮蔽时向外扩展的像素（默认 30），用来吃掉表格线框（文字外接框到表格线通常 10~30px）。
        /// 调大会连表格外的图元一起擦掉；调小会残留表格线，可能形成「表格残片」伪零件。
        /// </summary>
        public int TableMaskPadding { get; set; } = 30;

        // ── 二值化 ───────────────────────────────────────────────────────────

        /// <summary>
        /// 自适应阈值化的邻域块大小（奇数，≥3）。
        /// 扫描件光照不均时建议 51~101；纯矢量图导出建议 31~51。
        /// </summary>
        public int AdaptiveBlockSize { get; set; } = 51;

        /// <summary>
        /// 自适应阈值化的 C 常量（从邻域均值中减去）。
        /// 正值使二值化更严格（去更多噪点），负值更宽松。典型值 5~15。
        /// </summary>
        public int AdaptiveC { get; set; } = 10;

        // ── 形态学膨胀 ────────────────────────────────────────────────────────

        /// <summary>
        /// 膨胀核宽度（像素）。横向长条零件需宽远大于高。
        /// 300 DPI 建议 60~120；DPI 降低时等比缩减。
        /// </summary>
        public int DilateKernelWidth { get; set; } = 60;

        /// <summary>
        /// 膨胀核高度（像素）。控制纵向聚合范围。
        /// 若相邻零件纵向粘连则减小；同一零件内部断开则增大。
        /// </summary>
        public int DilateKernelHeight { get; set; } = 10;

        /// <summary>
        /// 形态学操作迭代次数。越多斑块越大越连通，但更易粘连相邻零件。
        /// </summary>
        public int DilateIterations { get; set; } = 2;

        // ── 轮廓过滤 ──────────────────────────────────────────────────────────

        /// <summary>
        /// 轮廓最小面积阈值（平方像素）。
        /// 小于此值视为噪点或孤立数字，直接丢弃。
        /// 300 DPI 建议 50000~150000；零件越小可适当降低。
        /// </summary>
        public double MinContourArea { get; set; } = 25000;

        /// <summary>
        /// 外接框最小宽度（像素）。过滤孤立细长噪线。
        /// </summary>
        public int MinBoundingWidth { get; set; } = 200;

        /// <summary>
        /// 外接框最小高度（像素）。过滤水平方向孤立刻度线。
        /// </summary>
        public int MinBoundingHeight { get; set; } = 80;

        // ── 裁切边距 ──────────────────────────────────────────────────────────

        /// <summary>
        /// 外接框四周扩展的像素边距，确保尺寸标注和引出线不被裁断。
        /// 300 DPI 建议 20~40 像素。
        /// </summary>
        public int Padding { get; set; } = 30;

        // ── 矩形合并 ──────────────────────────────────────────────────────────

        /// <summary>
        /// 矩形合并的重叠率阈值（0.0~1.0）。
        /// 两矩形交集面积占较小矩形面积之比超过此值时合并为一个。
        /// 过低会误合并不同零件；过高会导致同一零件被拆分。
        /// </summary>
        public double MergeOverlapThreshold { get; set; } = 0.30;

        /// <summary>
        /// 外框过滤阈值：外接矩形面积 / 整图面积 超过此值时视为图纸边框而丢弃。
        /// 图纸通常有包围所有内容的外框矩形，其面积占比可达 40%~50%；
        /// 单个零件区域一般不超过 25%，故默认 0.25 可稳定过滤外框。
        /// 若零件本身非常大（全幅宽度），可适当调高至 0.40。
        /// </summary>
        public double MaxBboxFillRatio { get; set; } = 0.25;

        // ── 伪零件筛除（文字面积占比）─────────────────────────────────────────

        /// <summary>
        /// 是否按「文字行面积占比」丢弃非零件图（默认开启）。
        ///
        /// 零件图上的文字只有尺寸、引线注记等零星标注，文字行加起来占自身外框很小一块；
        /// 表題欄 / 変更履歴表 / 部品表这类表格每一行都横跨整张表、整块几乎全是文字行。
        /// 表格残片被当成零件输出时，图里根本没有零件视图，只有一张表 —— 开启本开关后
        /// 这类块在导出 PNG 之前就被丢弃（编号顺延，不会留下空号）。
        ///
        /// 位图输入没有文字层，本开关不生效（不做 OCR）。
        /// </summary>
        public bool DropTextHeavyParts { get; set; } = true;

        /// <summary>
        /// 「文字行面积占比」判定阈值（默认 0.60）。
        ///
        /// 计算方式：块内所有「文字行」外接框与块的交集面积之和 ÷ 块面积，
        /// 即「文字行合计占了该块多大面积」。≥ 此值判定该块不是要分割的零件图。
        ///
        /// 实测（300 DPI，A1/A2 图纸）：正常零件图 0.03~0.46，表格残片（表題欄 / 変更履歴表）0.65~0.72。
        /// 调小 → 更容易丢弃（文字标注密集的零件视图也可能被丢）；调大 → 只丢文字几乎铺满的块。
        /// </summary>
        public double TextHeavyRatio { get; set; } = 0.60;

        // ── PDF 渲染 ──────────────────────────────────────────────────────────

        /// <summary>
        /// PDF 渲染目标 DPI。300 适合高精度识别；内存受限可降至 150。
        /// </summary>
        public double RenderDpi { get; set; } = 300.0;

        /// <summary>
        /// 要处理的 PDF 页码（从 0 开始）。
        /// </summary>
        public int PdfPageIndex { get; set; } = 0;

        // ── 圈号标注（①、②…）───────────────────────────────────────────────

        /// <summary>
        /// 是否识别「圈号标注」（圆圈 + 圈内数字，如 ①1）（默认开启）。
        ///
        /// 圆圈在 PDF 里是矢量图形、不在文字层，所以直接复制文字只能得到孤立数字。
        /// 开启后按「矢量路径找整圆 + 文字层找圆内数字」配对，输出 ①②… 与圆心 / 半径坐标，
        /// 结果写入 parts_info.json（每张零件图内）与 callouts.txt（全局清单）。
        /// 位图输入、无矢量路径或识别失败时只写日志，不影响分割结果。
        /// </summary>
        public bool DetectCallouts { get; set; } = true;

        /// <summary>
        /// 圈号圆的最小半径（毫米，默认 0.8）。小于此值的圆（尺寸线圆点、螺纹牙底圆角）不当圈号。
        /// 300 DPI 下 0.8mm ≈ 9.4px。
        /// </summary>
        public double CalloutMinRadiusMm { get; set; } = 0.8;

        /// <summary>
        /// 圈号圆的最大半径（毫米，默认 12.0）。图纸上圈号直径通常 2~8mm，
        /// 但 A0/A1 大图幅会把圈号画到 20mm 以上，故默认放到 12mm（直径 24mm）。
        /// 超过此值的圆（放大镜视图、基准符号外圈）不当圈号。
        /// 另有「圈内数字必须装得进圈」的校验（数字高 ≤ 1.3 倍半径），大圆里没有数字不会被误认。
        /// </summary>
        public double CalloutMaxRadiusMm { get; set; } = 12.0;

        /// <summary>
        /// 圆度容差（默认 0.15）：圆上各点到圆心的最大 / 最小半径之比允许超出 1 的比例。
        /// 调大能容忍被引线打断的不圆整的圈；调小可排除椭圆、圆弧。
        /// 另有两项硬性条件不在此调节：包围盒宽高比 ≥ 0.90、八个方位必须全被覆盖。
        /// </summary>
        public double CalloutRadiusTolerance { get; set; } = 0.15;

        // ── 文字清单粒度 ──────────────────────────────────────────────────────

        /// <summary>
        /// 文字清单（`parts_text.txt` / `outside_text.txt` 的「其它未归属」段）是否
        /// **一个词一行**输出（默认开启）。
        ///
        /// 合并成一句虽然读起来像原图上的文字，但几个词挤在一条记录里、只带一个并集坐标
        /// （如 `①1 鴨居 G部`），下游按行解析时无法知道每个词各自的框与位置。
        /// 开启后一个 PDF 词就是一行：坐标是该词自己的外接框（y / x / w / h + 局部坐标），
        /// 文本是该词自身的文字。顺序仍是自上而下、自左而右，同一水平线上的词依旧相邻显示，
        /// 所以「原本是同一行」照样看得出来。
        ///
        /// 需要旧行为（同一水平线上的词拼成一句 + 并集坐标）时设为 false，
        /// 命令行对应 `--merge-text-lines`。表格区（`outside_text.txt` 第一段）
        /// 本来就是「表 → 行 → 单元格」，不受本开关影响。
        /// </summary>
        public bool SplitTextToWords { get; set; } = true;

        // ── 有线表格内容提取（单元格四面都有框线的表格）─────────────────────

        /// <summary>
        /// 是否从 PDF 矢量路径提取「有线表格」（单元格四面都被框线围住的表）内容并落盘
        /// （默认开启，仅 PDF 生效；位图没有矢量路径，自动跳过）。
        ///
        /// 与 <see cref="MaskTableBlocks"/>（按文字层推断表格块、只用于遮蔽）不同：
        /// 本开关走的是「只认框线」的准确路径 —— 横 / 竖框线围出的最小闭合矩形就是单元格，
        /// 相邻单元格按共用边界并成表，行列号与合并跨度都由几何位置决定，
        /// 因此空单元格不会错位、合并单元格也不会被拆列。
        ///
        /// 产物（写在 <see cref="OutputDirectory"/>）：
        ///   · <c>tables_info.json</c> ── 机器可读，与 <c>outside_info.json</c> 同构；
        ///   · <c>tables_text.txt</c>  ── 人眼可读，含框线画的二维表 + 单元格明细；
        ///   · <c>tables\table_NN.csv</c> ── Excel 可直接打开的表格内容。
        /// 只读，不影响零件框选 / 合并 / 命名。命令行对应 `--no-tables`。
        /// </summary>
        public bool ExtractRuledTables { get; set; } = true;

        /// <summary>
        /// 判定为表格框线所需的最短长度（毫米，默认 2.0）。
        ///
        /// 用来筛掉不是框线的短边：填充细矩形在线宽方向的两条短边（0.2~0.5mm）、
        /// 文字笔画、箭头、标注短线。表格线每一段至少有一个单元格那么长
        /// （格子边长 ≥ <see cref="RuledTableMinCellMm"/>），所以 2mm 不会误伤真框线。
        /// 图上有特别细密的表格（格子 &lt; 2mm）时才需要调小，但要同时调小
        /// <see cref="RuledTableMinCellMm"/>，否则形状闭合了也会被当噪声丢掉。
        /// </summary>
        public double RuledTableMinLineMm { get; set; } = 2.0;

        /// <summary>
        /// 共线容差（毫米，默认 0.6）：固定坐标（横线的 y / 竖线的 x）相差在此范围内、
        /// 且区间重叠或间隙也在容差内的线段，视为同一条框线并合并。
        ///
        /// CAD 导出的图纸上，同一条表格线常被画两遍（上一格的底边 + 下一格的顶边），
        /// 粗线还会变成「0.2mm 高的填充四边形」（两条长边相距 0.2mm）。
        /// 调大能把略有错位的框线并到一起；过大（&gt; 行高的 1/4）会把相邻两行并成一行。
        /// </summary>
        public double RuledTableLineToleranceMm { get; set; } = 0.6;

        /// <summary>
        /// 单元格最小边长（毫米，默认 2.0）：横 / 竖两边间距小于此值的候选矩形不认。
        /// 用来忽略重叠框线之间、粗框线内部被围出来的窄缝（0.2~1mm 宽），
        /// 避免一个格子被拆成一串细条。
        /// </summary>
        public double RuledTableMinCellMm { get; set; } = 2.0;

        /// <summary>
        /// 上下两条横线能配成一个单元格的最大间距（毫米，默认 120）。
        ///
        /// 合并单元格（特别是纵向跨多行的表題欄栏杆）只有上下两条长横线，
        /// 中间没有横线可依，所以这个上限要大于最高的合并单元格。
        /// 调大能容纳更高的合并格，但枚举量随带内横线数增长，过大只是变慢、不影响正确性。
        /// </summary>
        public double RuledTableMaxRowGapMm { get; set; } = 120.0;

        // ── PDF 信息清单（PdfInfoList.txt）────────────────────────────────────

        /// <summary>
        /// 是否生成「PDF 信息清单」<c>classify\PdfInfoList.txt</c>（默认开启，仅 PDF 生效）。
        ///
        /// 整批共用一份，放在输出根目录下的 <c>classify</c> 里：
        /// 单张是 <c>&lt;图纸目录&gt;\AiAppData\classify\PdfInfoList.txt</c>，
        /// 批量是 <c>&lt;输出根目录&gt;\classify\PdfInfoList.txt</c>（<b>不是</b>每张图纸各一份）。
        /// 一张图一行，固定四列：図番号 / 文件名 / 图面分类（基本図 / 加工図 / アッセンブリ図 / 要領図）/
        /// 分类依据；同一张图重跑时就地更新那一行。分类判定与 <c>drawing_list.csv</c> 同一套规则
        /// （在 <c>tables\*.csv</c> 的单元格里找「図番号前 4 位 + 关键字」）。
        ///
        /// 生成时机：<c>tables\*.csv</c> 落盘之后、<see cref="BuildLtStep1Table"/> 与
        /// <see cref="BuildLtStep2Table"/> 之前 —— 后两者要读它，<see cref="BuildLtStep1Table"/>
        /// 只有分类是「アッセンブリ図」时才写 <c>01_アッセンブリ図_部品機能情報.csv</c>，
        /// <see cref="BuildLtStep2Table"/> 只有分类是「要領図」时才写 <c>02_要領図_部品機能情報.csv</c>。
        /// 命令行对应 `--no-pdf-info-list`。
        /// </summary>
        public bool BuildPdfInfoList { get; set; } = true;

        /// <summary>
        /// 是否在表格 CSV 生成之后，再合成下游 AI 环节约定的
        /// <c>01_アッセンブリ図_部品機能情報.csv</c>（默认开启；旧文件名 <c>LT_STEP1.csv</c>）。
        ///
        /// 做法：遍历 <c>tables\table_NN.csv</c> ——
        ///   · 表头同时含「番号」「部品名」「部品コード」的是部品表（主表），取它的明细行；
        ///   · 明细行里「部品コード」写「下表参照」的，往后找第一张含「部品コード」列的参照表
        ///     （图纸上位于下方的那张小表），它有 N 条明细就把这一行拆成 N 条，
        ///     只把「部品コード」换成参照表里的值，其余列沿用原行；
        ///   · 合成结果按 <see cref="OutputDirectory"/> 写成
        ///     <c>01_アッセンブリ図_部品機能情報.csv</c>（带 BOM 的 UTF-8）。
        ///   · **只有图面分类为「アッセンブリ図」的图纸才写**：分类读自
        ///     <c>classify\PdfInfoList.txt</c>（<see cref="BuildPdfInfoList"/>），基本図 / 加工図 / 要領図 /
        ///     未识别 一律跳过，并清掉上一次留下的同名产物。
        /// 生成时机在 <see cref="BuildLtStep2Table"/> 之前（先出 <c>01_…</c>，再出 <c>02_…</c>；
        /// 只读 <c>tables\*.csv</c>）。
        /// 找不到部品表、或没有「下表参照」行时什么都不写（只记日志）。命令行对应 `--no-lt-step1`。
        /// </summary>
        public bool BuildLtStep1Table { get; set; } = true;

        /// <summary>
        /// 是否把「品種 + 多列重复」的表格摊平，合成下游 AI 环节约定的
        /// <c>02_要領図_部品機能情報.csv</c>（默认开启；旧文件名 <c>LT_STEP2.csv</c>）。
        ///
        /// 图纸上这类表把同一个品種的几组部品横向排开，表头重复出现：
        /// <code>
        /// 品種,部品名 ： 縦枠上部スペーサー,,部品名 ： 小縦枠上部スペーサー,,
        /// ,部品コード,員数,部品コード,員数
        /// 片引戸,MEACC428,1,MEACC428,2
        /// </code>
        /// 做法：遍历 <c>tables\table_NN.csv</c> ——
        ///   · 表头区里有含「品種」的单元格，且同一列名重复出现 ≥ 2 次的是品種表；
        ///   · 重复列名每次出现的列 = 一组部品ブロック，块标题（上方单元格里的「部品名 ： …」）就是品名；
        ///   · 摊平成 品種 / 部品名 / 部品コード / 員数 四列，品種按合并单元格往下填满；
        ///   · 结果按 <see cref="OutputDirectory"/> 写成 <c>02_要領図_部品機能情報.csv</c>（带 BOM 的 UTF-8）。
        ///   · **只有图面分类为「要領図」的图纸才写**：分类读自 <c>classify\PdfInfoList.txt</c>
        ///     （<see cref="BuildPdfInfoList"/>），基本図 / 加工図 / アッセンブリ図 / 未识别 一律跳过，
        ///     并清掉上一次留下的同名产物。
        /// 找不到这种表时什么都不写（只记日志）。生成时机在 <see cref="BuildLtStep1Table"/> 之后
        /// （两张成品清单里最后出）。命令行对应 `--no-lt-step2`。
        /// </summary>
        public bool BuildLtStep2Table { get; set; } = true;

        /// <summary>
        /// 是否把「項目 / 要領図漢字 / 図番」这种表抽成下游 AI 环节约定的
        /// <c>01_アッセンブリ図_要領図.csv</c>（默认开启）。
        ///
        /// 图纸上这张小表列的是「这张組立図用到了哪些要領図」，样子（表头被拆散、中间那列没有标题）：
        /// <code>
        /// ,,,項目,,,,,,,,,,,,図番,
        /// ,,,"A,M",,枠 縦枠/小縦枠スペーサー取付要領図,,,,,,H310U10022,
        /// ,,,B,,枠 ロットNO.ラベル 貼付要領図,,,,,,H310U10034,
        /// </code>
        /// 做法：遍历 <c>tables\table_NN.csv</c> ——
        ///   · 表头（含「項目」与「図番」的那一行）同时出现这两个词的才是对象文件，其余表格跳过；
        ///   · 「項目」「図番」按列名找；「要領図漢字」多数图纸上没有标题，就取「項目」与「図番」
        ///     之间第一条有内容的列（表头里写了这个列名时优先按列名找）；
        ///   · 表头下面一行一条，读到「図番」为空或不是 10 位（<c>H310U10022</c> 这类）就停 ——
        ///     再往下是表題欄、変更履歴表之类的杂行；
        ///   · 摊平成 項目 / 要領図漢字 / 図番 三列，按 <see cref="OutputDirectory"/> 写成
        ///     <c>01_アッセンブリ図_要領図.csv</c>（带 BOM 的 UTF-8 + CRLF）。
        ///   · **只有图面分类为「アッセンブリ図」的图纸才写**：分类读自 <c>classify\PdfInfoList.txt</c>
        ///     （<see cref="BuildPdfInfoList"/>），基本図 / 加工図 / 要領図 / 未识别 一律跳过，
        ///     并清掉上一次留下的同名产物。
        /// 生成时机在 <see cref="BuildLtStep1Table"/> 之后、<see cref="BuildLtStep2Table"/> 之前。
        /// 没有这种表时不生成文件（只记日志）。命令行对应 `--no-assembly-yoryo`。
        /// </summary>
        public bool BuildAssemblyYoryoList { get; set; } = true;

        /// <summary>
        /// 是否在流水线最后，把「图纸文件夹里的 Excel 部品表」抽成
        /// <c>classify\tz.json</c>（默认开启）。
        ///
        /// 扫「图纸所在文件夹」下的所有 Excel（<c>.xls</c> / <c>.xlsx</c> / <c>.xlsm</c>，
        /// <c>~$</c> 临时文件与 <c>AiAppData</c> 里的产物跳过），读工作簿里「カメラ」表的部品表，
        /// 取这五列：<c>品種 / 部品名 / 枠区分 / 部品コード / 追加対応特注寸法</c>。
        /// 参考版式：<c>H310Z10004.XLS</c> 的「カメラ」sheet。
        ///
        /// 输出位置与 <see cref="BuildPdfInfoList"/> 的清单同一个目录、同样是整批共用一份：
        ///   · 单张：<c>&lt;图纸目录&gt;\AiAppData\classify\tz.json</c>；
        ///   · 批量：<c>&lt;输出根目录&gt;\classify\tz.json</c>（<b>不是</b>每张图纸各一份）。
        ///
        /// 读取用 ExcelDataReader（只读，不要求机器上装 Excel）。没有「カメラ」表的工作簿、
        /// 或表里凑不齐那 5 个列名时跳过并记日志；整批一张都没凑齐时不写文件，也不删旧文件。
        /// 命令行的 <c>--no-tz</c> 关掉这一步。
        /// </summary>
        public bool BuildTzJson { get; set; } = true;

        // ── 输出 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// AI 流程约定的输出文件夹名，放在所选图纸所在目录下；其内每张图纸再单独占一个
        /// 「图纸名」子文件夹（<c>&lt;图纸目录&gt;\AiAppData\&lt;图纸名&gt;</c>）。
        /// 下游 AI 环节按「目录 + 图纸名」直接取分割结果，不用再问「结果输出到哪儿了」。
        /// </summary>
        public const string AiAppDataFolderName = "AiAppData";

        /// <summary>
        /// 由输入文件推导默认输出目录（按第 1 页算）：
        /// <c>&lt;输入文件所在目录&gt;\AiAppData\&lt;主文件名&gt;</c>。
        /// </summary>
        public static string GetDefaultOutputDirectory(string inputFile)
        {
            return GetDefaultOutputDirectory(inputFile, 0);
        }

        /// <summary>
        /// 由输入文件推导默认输出目录：<c>&lt;输入文件所在目录&gt;\AiAppData\&lt;主文件名&gt;</c>。
        ///
        /// 图形界面选好文件后自动填这个路径，命令行不写输出目录时也用它，
        /// 这样「选了 <c>D:\图纸\H310A20009.pdf</c>」结果就固定落在
        /// <c>D:\图纸\AiAppData\H310A20009\</c>，同一目录下的多张图纸互不覆盖。
        ///
        /// 多页 PDF 逐页分割时，第 2 页起自动加 <c>_P页号</c> 后缀（如
        /// <c>H310A20009_P2</c>），避免后一页覆盖前一页的结果；
        /// 取不到文件名时（路径以 <c>\</c> 结尾、输入框里还是半截路径）退回 <c>AiAppData</c> 本身。
        /// </summary>
        /// <param name="inputFile">输入文件路径。</param>
        /// <param name="pageIndex">PDF 页码（0 起）；0 即第 1 页，不加后缀。</param>
        public static string GetDefaultOutputDirectory(string inputFile, int pageIndex)
        {
            string dir = null;

            if (!string.IsNullOrWhiteSpace(inputFile))
            {
                try
                {
                    // 界面上可能还在输入半截路径，取不到绝对路径就退回当前目录
                    dir = Path.GetDirectoryName(Path.GetFullPath(inputFile));
                }
                catch (ArgumentException)
                {
                    dir = null;
                }
                catch (NotSupportedException)
                {
                    dir = null;
                }
                catch (PathTooLongException)
                {
                    dir = null;
                }
            }

            if (string.IsNullOrEmpty(dir))
                dir = Environment.CurrentDirectory;

            string root = Path.Combine(dir, AiAppDataFolderName);
            string drawingFolder = BuildDrawingFolderName(inputFile, pageIndex);
            return string.IsNullOrEmpty(drawingFolder) ? root : Path.Combine(root, drawingFolder);
        }

        /// <summary>
        /// 批量模式：由输入文件夹推导默认的输出根目录 <c>&lt;输入文件夹&gt;\AiAppData</c>。
        ///
        /// 与单文件的 <see cref="GetDefaultOutputDirectory(string, int)"/> 落在同一个上级目录，
        /// 区别只是不再拼「图纸名」—— 批量时每张图纸各自一个子文件夹
        /// （<c>&lt;输入文件夹&gt;\AiAppData\&lt;图纸名&gt;</c>），由
        /// <see cref="BatchRunner"/> 按 <see cref="GetDrawingFolderName"/> 逐个补齐。
        /// </summary>
        /// <param name="inputDirectory">输入文件夹路径。</param>
        public static string GetDefaultBatchOutputDirectory(string inputDirectory)
        {
            string dir = null;

            if (!string.IsNullOrWhiteSpace(inputDirectory))
            {
                try
                {
                    dir = Path.GetFullPath(inputDirectory);
                }
                catch (ArgumentException)
                {
                    dir = null;
                }
                catch (NotSupportedException)
                {
                    dir = null;
                }
                catch (PathTooLongException)
                {
                    dir = null;
                }
            }

            if (string.IsNullOrEmpty(dir))
                dir = Environment.CurrentDirectory;

            return Path.Combine(dir, AiAppDataFolderName);
        }

        /// <summary>
        /// 取「图纸名」子文件夹名（批量模式给每个 PDF 分目录时用）：
        /// 主文件名（不含扩展名）；页码 &gt; 0 时补 <c>_P&lt;页码+1&gt;</c>。
        /// 取不到文件名时返回 null，调用方直接用输出根目录。
        /// </summary>
        /// <param name="inputFile">输入文件路径。</param>
        /// <param name="pageIndex">PDF 页码（0 起）；0 即第 1 页，不加后缀。</param>
        public static string GetDrawingFolderName(string inputFile, int pageIndex)
        {
            return BuildDrawingFolderName(inputFile, pageIndex);
        }

        /// <summary>
        /// 取「图纸名」子文件夹名：主文件名（不含扩展名）；页码 &gt; 0 时补 <c>_P&lt;页码+1&gt;</c>。
        /// 非法文件名字符替换为下划线（正常路径不会出现，防御手工拼的字符串）。
        /// 取不到文件名时返回 null，调用方退回 <see cref="AiAppDataFolderName"/> 本身。
        /// </summary>
        private static string BuildDrawingFolderName(string inputFile, int pageIndex)
        {
            string name;

            try
            {
                name = Path.GetFileNameWithoutExtension(inputFile);
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
            catch (PathTooLongException)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(name))
                return null;

            name = name.Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars())
                name = name.Replace(invalid, '_');

            return pageIndex > 0 ? name + "_P" + (pageIndex + 1) : name;
        }

        /// <summary>
        /// 分割零件图的输出目录，不存在时自动创建。
        /// 留空（null / 空串）时取 <see cref="GetDefaultOutputDirectory(string, int)"/>，
        /// 即 <c>&lt;输入文件所在目录&gt;\AiAppData\&lt;主文件名&gt;</c>。
        /// </summary>
        public string OutputDirectory { get; set; }

        /// <summary>
        /// 是否保存中间处理图（二值图、膨胀图）用于参数调试。
        /// </summary>
        public bool SaveDebugImages { get; set; } = false;

        // ── 日志 ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 日志输出回调。为 null 时写入控制台（命令行模式）；
        /// 图形界面模式下由 WinForms 窗体注入，将日志显示到界面日志面板。
        /// </summary>
        public Action<string> Logger { get; set; }

        /// <summary>
        /// 输出一行处理日志：优先调用 <see cref="Logger"/> 回调，否则写入控制台。
        /// </summary>
        public void Log(string message)
        {
            Action<string> logger = Logger;
            if (logger != null)
            {
                logger(message);
                return;
            }

            Console.WriteLine(message);
        }
    }
}
