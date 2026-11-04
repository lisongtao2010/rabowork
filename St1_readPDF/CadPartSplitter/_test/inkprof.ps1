# Ink profile helper (compiled C#: PS loops over 8M pixels are far too slow).
# Usage: powershell -ExecutionPolicy Bypass -File _test\inkprof.ps1 <png> [cols|edges|rows] [step]
param(
    [Parameter(Mandatory=$true)][string]$Path,
    [string]$Mode = 'edges',
    [int]$Step = 10,
    [switch]$Invert,
    [int]$X0 = 0,
    [int]$X1 = -1,
    [int]$Y0 = 0,
    [int]$Y1 = -1
)

Add-Type -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

public static class InkProf
{
    public static int RegX0 = 0, RegX1 = -1, RegY0 = 0, RegY1 = -1;
    static byte[] _buf;
    static int _stride, _w, _h;

    public static string Run(string path, string mode, int step, bool invert, out int w, out int h)
    {
        using (var src = new Bitmap(path))
        {
            _w = src.Width; _h = src.Height;
            using (var bmp = new Bitmap(_w, _h, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp)) g.DrawImage(src, 0, 0, _w, _h);
                var rect = new Rectangle(0, 0, _w, _h);
                var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                _stride = data.Stride;
                _buf = new byte[_stride * _h];
                Marshal.Copy(data.Scan0, _buf, 0, _buf.Length);
                bmp.UnlockBits(data);
            }
        }

        w = _w; h = _h;
        _invert = invert;
        var sb = new StringBuilder();

        if (mode == "cols")
        {
            for (int x = 0; x < _w; x += step)
            {
                int c = 0;
                for (int y = 0; y < _h; y++) if (Ink(x, y)) c++;
                if (c > 0) sb.AppendLine(string.Format("x={0,5}  ink={1}", x, c));
            }
        }
        else if (mode == "rows")
        {
            for (int y = 0; y < _h; y += step)
            {
                int c = 0;
                for (int x = 0; x < _w; x++) if (Ink(x, y)) c++;
                if (c > 0) sb.AppendLine(string.Format("y={0,5}  ink={1}", y, c));
            }
        }
        else if (mode == "region")
        {
            int x1r = RegX1 < 0 ? _w - 1 : RegX1;
            int y1r = RegY1 < 0 ? _h - 1 : RegY1;
            int bx0 = _w, bx1 = -1, by0 = _h, by1 = -1;
            for (int y = RegY0; y <= y1r; y++)
            {
                if (y < 0 || y >= _h) continue;
                for (int x = RegX0; x <= x1r; x++)
                {
                    if (x < 0 || x >= _w) continue;
                    if (Ink(x, y))
                    {
                        if (x < bx0) bx0 = x;
                        if (x > bx1) bx1 = x;
                        if (y < by0) by0 = y;
                        if (y > by1) by1 = y;
                    }
                }
            }
            sb.AppendLine(string.Format("region X {0}~{1} Y {2}~{3}  -> ink bbox X {4}~{5}  Y {6}~{7}",
                RegX0, x1r, RegY0, y1r, bx0, bx1, by0, by1));
            if (bx1 >= 0)
            {
                sb.AppendLine("-- column profile (ink count per column, step " + step + ") --");
                for (int x = bx0; x <= bx1; x += step)
                {
                    int c = 0;
                    for (int y = RegY0; y <= y1r; y++) if (y >= 0 && y < _h && Ink(x, y)) c++;
                    sb.Append(string.Format("{0}:{1}  ", x, c));
                }
                sb.AppendLine();
                sb.AppendLine("-- row profile --");
                for (int y = by0; y <= by1; y += step)
                {
                    int c = 0;
                    for (int x = RegX0; x <= x1r; x++) if (x >= 0 && x < _w && Ink(x, y)) c++;
                    sb.Append(string.Format("{0}:{1}  ", y, c));
                }
                sb.AppendLine();
            }
        }
        else
        {
            int minX = _w, maxX = -1, minY = _h, maxY = -1;
            for (int y = 0; y < _h; y++)
            {
                int row = y * _stride;
                for (int x = 0; x < _w; x++)
                {
                    int i = row + x * 4;
                    if (_buf[i] < 128 && _buf[i + 1] < 128 && _buf[i + 2] < 128)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }
            sb.AppendLine(string.Format("ink bbox: X {0}~{1}  Y {2}~{3}", minX, maxX, minY, maxY));
            foreach (int x in new int[] { 0, 20, 40, maxX, maxX - 1, maxX - 20, _w - 41, _w - 21, _w - 1 })
            {
                if (x < 0 || x >= _w) continue;
                int c = 0;
                for (int y = 0; y < _h; y++) if (Ink(x, y)) c++;
                sb.AppendLine(string.Format("column x={0,5}  ink={1} / {2}", x, c, _h));
            }
            foreach (int y in new int[] { 0, 20, 40, maxY, maxY - 1, maxY - 20, _h - 41, _h - 21, _h - 1 })
            {
                if (y < 0 || y >= _h) continue;
                int c = 0;
                for (int x = 0; x < _w; x++) if (Ink(x, y)) c++;
                sb.AppendLine(string.Format("row    y={0,5}  ink={1} / {2}", y, c, _w));
            }
        }
        return sb.ToString();
    }

    static bool _invert; static bool Ink(int x, int y){ int i = y * _stride + x * 4; if (_invert) return _buf[i] > 128 || _buf[i + 1] > 128 || _buf[i + 2] > 128; return _buf[i] < 128 && _buf[i + 1] < 128 && _buf[i + 2] < 128; }
}
'@ -ReferencedAssemblies System.Drawing

$w = 0; $h = 0
[InkProf]::RegX0 = $X0; [InkProf]::RegX1 = $X1
[InkProf]::RegY0 = $Y0; [InkProf]::RegY1 = $Y1
$report = [InkProf]::Run((Resolve-Path -LiteralPath $Path).ToString(), $Mode, $Step, $Invert.IsPresent, [ref]$w, [ref]$h)
Write-Host ("image: {0}  {1}x{2}  mode={3}" -f (Split-Path $Path -Leaf), $w, $h, $Mode)
Write-Host $report

