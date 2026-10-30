using System;

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

        // ── PDF 渲染 ──────────────────────────────────────────────────────────

        /// <summary>
        /// PDF 渲染目标 DPI。300 适合高精度识别；内存受限可降至 150。
        /// </summary>
        public double RenderDpi { get; set; } = 300.0;

        /// <summary>
        /// 要处理的 PDF 页码（从 0 开始）。
        /// </summary>
        public int PdfPageIndex { get; set; } = 0;

        // ── 输出 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 分割零件图的输出目录，不存在时自动创建。
        /// </summary>
        public string OutputDirectory { get; set; } = "output";

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
