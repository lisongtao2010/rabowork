using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MSIFAITOOLS
{
    /// <summary>
    /// 「部品基本信息」与「PDF 预览」两个窗口的摆放规则。
    /// <para>有 2 块以上屏幕时：PDF 预览放到屏幕 1（主屏）、部品基本信息放到屏幕 2。</para>
    /// <para>只有 1 块屏幕时：PDF 预览占左侧、部品基本信息占右侧，尽量互不遮挡。</para>
    /// </summary>
    internal static class WindowLayoutHelper
    {
        /// <summary>PDF 预览窗口允许的最小宽度（与 PdfPreviewForm.MinimumSize 的宽度一致）。</summary>
        internal const int PreviewMinWidth = 760;

        /// <summary>部品基本信息窗口允许的最小宽度（与 PartBasicInfoForm.MinimumSize 的宽度一致）。</summary>
        internal const int EditorMinWidth = 860;

        /// <summary>单屏并排时部品基本信息窗口占工作区宽度的比例。</summary>
        private const double EditorWidthRatio = 0.45;

        /// <summary>
        /// 首次打开时摆放两个窗口：PDF 预览 → 屏幕 1，部品基本信息 → 屏幕 2
        /// （只有 1 块屏幕时左右并排）。
        /// </summary>
        /// <param name="previewForm">PDF 预览窗口。</param>
        /// <param name="editorForm">部品基本信息窗口。</param>
        internal static void ArrangePreviewAndEditor(Form previewForm, Form editorForm)
        {
            Rectangle previewBounds;
            Rectangle editorBounds;

            SplitForCurrentScreens(out previewBounds, out editorBounds);

            ApplyBounds(previewForm, previewBounds);
            ApplyBounds(editorForm, editorBounds);
        }

        /// <summary>
        /// 只摆放 PDF 预览窗口（用户关闭预览后再次打开时调用，不移动部品基本信息窗口）。
        /// </summary>
        /// <param name="previewForm">PDF 预览窗口。</param>
        internal static void ArrangePreview(Form previewForm)
        {
            Rectangle previewBounds;
            Rectangle editorBounds;

            SplitForCurrentScreens(out previewBounds, out editorBounds);

            ApplyBounds(previewForm, previewBounds);
        }

        /// <summary>
        /// 只摆放部品基本信息窗口（PDF 预览窗口已由其他画面打开并摆好时调用）。
        /// </summary>
        /// <param name="editorForm">部品基本信息窗口。</param>
        internal static void ArrangeEditor(Form editorForm)
        {
            Rectangle previewBounds;
            Rectangle editorBounds;

            SplitForCurrentScreens(out previewBounds, out editorBounds);

            ApplyBounds(editorForm, editorBounds);
        }

        /// <summary>按当前屏幕数量计算两个窗口的位置。</summary>
        private static void SplitForCurrentScreens(out Rectangle previewBounds, out Rectangle editorBounds)
        {
            Rectangle[] workingAreas = GetOrderedWorkingAreas();

            if (workingAreas.Length >= 2)
            {
                SplitAcrossScreens(workingAreas, out previewBounds, out editorBounds);
            }
            else if (workingAreas.Length == 1)
            {
                SplitOnSingleScreen(workingAreas[0], out previewBounds, out editorBounds);
            }
            else
            {
                SplitOnSingleScreen(new Rectangle(0, 0, 1280, 800), out previewBounds, out editorBounds);
            }
        }

        /// <summary>
        /// 按「屏幕 1 = 主屏，屏幕 2 = 主屏之外的第一块屏幕」的顺序返回各屏幕工作区。
        /// （<see cref="Screen.AllScreens"/> 的下标并不保证主屏在最前，所以这里显式排序）
        /// </summary>
        private static Rectangle[] GetOrderedWorkingAreas()
        {
            Screen[] screens = Screen.AllScreens;
            List<Rectangle> areas = new List<Rectangle>(screens.Length);
            Screen primary = Screen.PrimaryScreen;

            if (primary != null)
            {
                areas.Add(primary.WorkingArea);
            }

            for (int i = 0; i < screens.Length; i++)
            {
                if (screens[i] != null && !screens[i].Primary)
                {
                    areas.Add(screens[i].WorkingArea);
                }
            }

            return areas.ToArray();
        }

        /// <summary>
        /// 多屏：屏幕 1（<paramref name="workingAreas"/>[0]）给 PDF 预览，屏幕 2 给部品基本信息。
        /// 副屏在主屏左侧 / 上方时工作区坐标可能为负值，这里直接使用工作区矩形。
        /// </summary>
        /// <param name="workingAreas">各屏幕的工作区（下标 0 = 屏幕 1）。</param>
        /// <param name="previewBounds">PDF 预览窗口的目标位置。</param>
        /// <param name="editorBounds">部品基本信息窗口的目标位置。</param>
        internal static void SplitAcrossScreens(Rectangle[] workingAreas, out Rectangle previewBounds, out Rectangle editorBounds)
        {
            if (workingAreas == null || workingAreas.Length == 0)
            {
                throw new ArgumentException("至少需要一个屏幕工作区。", "workingAreas");
            }

            previewBounds = workingAreas[0];
            editorBounds = workingAreas.Length >= 2 ? workingAreas[1] : workingAreas[0];
        }

        /// <summary>
        /// 单屏：左侧给 PDF 预览、右侧给部品基本信息；工作区太窄时按窗口最小宽度分配。
        /// </summary>
        /// <param name="area">屏幕工作区。</param>
        /// <param name="previewBounds">PDF 预览窗口的目标位置。</param>
        /// <param name="editorBounds">部品基本信息窗口的目标位置。</param>
        internal static void SplitOnSingleScreen(Rectangle area, out Rectangle previewBounds, out Rectangle editorBounds)
        {
            int editorWidth = (int)Math.Round(area.Width * EditorWidthRatio);

            if (editorWidth < EditorMinWidth)
            {
                editorWidth = Math.Min(EditorMinWidth, area.Width);
            }

            int previewWidth = area.Width - editorWidth;

            if (previewWidth < PreviewMinWidth)
            {
                previewWidth = Math.Min(PreviewMinWidth, area.Width);
                editorWidth = area.Width - previewWidth;
            }

            previewBounds = new Rectangle(area.Left, area.Top, previewWidth, area.Height);
            editorBounds = new Rectangle(area.Left + previewWidth, area.Top, editorWidth, area.Height);
        }

        /// <summary>把窗口移动到指定矩形（之后用户仍可自由拖动；只在首次打开时调用）。</summary>
        /// <param name="form">要摆放的窗口。</param>
        /// <param name="bounds">目标位置（屏幕坐标）。</param>
        internal static void ApplyBounds(Form form, Rectangle bounds)
        {
            if (form == null || form.IsDisposed)
            {
                return;
            }

            if (form.WindowState != FormWindowState.Normal)
            {
                form.WindowState = FormWindowState.Normal;
            }

            // 设计器里是 CenterParent，必须先改成 Manual，否则 Bounds 会被居中逻辑覆盖
            form.StartPosition = FormStartPosition.Manual;
            form.Bounds = bounds;
        }
    }
}
