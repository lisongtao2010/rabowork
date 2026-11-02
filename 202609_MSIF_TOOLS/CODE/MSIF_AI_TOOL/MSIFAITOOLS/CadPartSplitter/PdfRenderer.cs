using System;
using System.IO;
using System.Runtime.InteropServices;
using Docnet.Core;
using Docnet.Core.Models;
using OpenCvSharp;

namespace CadPartSplitter
{
    /// <summary>
    /// 使用 Docnet.Core（基于 PDFium）将 PDF 页面渲染为 OpenCV Mat（BGR 8UC3）。
    /// </summary>
    public static class PdfRenderer
    {
        /// <summary>
        /// 获取 PDF 文件的总页数，供界面提示与页码校验使用。
        /// </summary>
        public static int GetPageCount(string pdfPath)
        {
            if (!File.Exists(pdfPath))
                throw new FileNotFoundException("PDF 文件未找到", pdfPath);

            // 不 Dispose DocLib.Instance（全局单例），只 Dispose docReader
            using (var docReader = DocLib.Instance.GetDocReader(pdfPath, new PageDimensions(1.0)))
            {
                return docReader.GetPageCount();
            }
        }

        /// <summary>
        /// 将 PDF 文件指定页渲染为 Mat（BGR 格式）。
        /// </summary>
        /// <param name="pdfPath">PDF 文件完整路径。</param>
        /// <param name="pageIndex">页码（从 0 开始）。</param>
        /// <param name="dpi">渲染分辨率。PDF 原生单位为 pt（1pt = 1/72 inch），
        ///                   内部 scalingFactor = dpi / 72.0。300 DPI 推荐。</param>
        public static Mat RenderPage(string pdfPath, int pageIndex = 0, double dpi = 300.0)
        {
            if (!File.Exists(pdfPath))
                throw new FileNotFoundException("PDF 文件未找到", pdfPath);

            // PDF 基础分辨率为 72 DPI，缩放系数换算为目标 DPI
            double scalingFactor = dpi / 72.0;

            // 不 Dispose DocLib.Instance（全局单例），只 Dispose docReader / pageReader
            using (var docReader = DocLib.Instance.GetDocReader(pdfPath, new PageDimensions(scalingFactor)))
            using (var pageReader = docReader.GetPageReader(pageIndex))
            {
                int width  = pageReader.GetPageWidth();
                int height = pageReader.GetPageHeight();

                if (width <= 0 || height <= 0)
                    throw new InvalidOperationException(
                        $"PDF 页面尺寸无效（{width}×{height}），请确认页码是否存在。");

                // GetImage() 返回 BGRA 字节数组，每像素 4 字节。
                // PDFium 默认以透明背景渲染：透明像素的 BGRA = (0,0,0,0)。
                // 若直接做 BGRA→BGR 转换，透明区域和黑色线条都变成 (0,0,0)，
                // 导致整张图全黑，后续二值化完全失效。
                // 修复：先将 BGRA 合成到白色背景，再转换为 BGR。
                byte[] bgra = pageReader.GetImage();
                CompositeOverWhite(bgra);

                using (var bgraMat = new Mat(height, width, MatType.CV_8UC4))
                {
                    Marshal.Copy(bgra, 0, bgraMat.Data, bgra.Length);

                    Mat bgrMat = new Mat();
                    Cv2.CvtColor(bgraMat, bgrMat, ColorConversionCodes.BGRA2BGR);
                    return bgrMat;  // 调用者负责 Dispose
                }
            }
        }
        /// <summary>
        /// 将 BGRA 字节数组原地合成到白色背景。
        /// 透明像素 (A=0) → 白色 (255,255,255,255)；
        /// 半透明像素做线性混合；完全不透明像素 (A=255) 不变。
        /// </summary>
        private static void CompositeOverWhite(byte[] bgra)
        {
            for (int i = 0; i < bgra.Length; i += 4)
            {
                int a = bgra[i + 3];
                if (a == 255) continue;          // 完全不透明，无需处理

                if (a == 0)                      // 完全透明 → 白色
                {
                    bgra[i]     = 255;
                    bgra[i + 1] = 255;
                    bgra[i + 2] = 255;
                    bgra[i + 3] = 255;
                }
                else                             // 半透明：线性混合到白色
                {
                    float alpha = a / 255f;
                    bgra[i]     = (byte)(bgra[i]     * alpha + 255 * (1f - alpha));
                    bgra[i + 1] = (byte)(bgra[i + 1] * alpha + 255 * (1f - alpha));
                    bgra[i + 2] = (byte)(bgra[i + 2] * alpha + 255 * (1f - alpha));
                    bgra[i + 3] = 255;
                }
            }
        }
    }
}
