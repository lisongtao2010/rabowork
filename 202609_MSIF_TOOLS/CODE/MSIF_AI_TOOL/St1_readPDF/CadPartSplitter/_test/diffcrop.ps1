# Pixel diff: compare a crop of <Source> (at X,Y,W,H) with <Part> (same size).
# Usage: powershell -ExecutionPolicy Bypass -File _test\diffcrop.ps1 <Source.png> <Part.png> <X> <Y>
param(
    [Parameter(Mandatory=$true)][string]$Source,
    [Parameter(Mandatory=$true)][string]$Part,
    [Parameter(Mandatory=$true)][int]$X,
    [Parameter(Mandatory=$true)][int]$Y
)

Add-Type -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class PixelDiff
{
    public static string Compare(string srcPath, string partPath, int ox, int oy, out int pw, out int ph)
    {
        using (var a = new Bitmap(srcPath))
        using (var b = new Bitmap(partPath))
        {
            pw = b.Width; ph = b.Height;
            if (ox + pw > a.Width || oy + ph > a.Height) return "part box exceeds source image";

            byte[] ba = Unpack(a), bb = Unpack(b);
            int sa = a.Width * 4, sb = b.Width * 4;
            long sum = 0; int max = 0; long over = 0; int maxAtX = -1, maxAtY = -1;
            for (int y = 0; y < ph; y++)
            {
                for (int x = 0; x < pw; x++)
                {
                    int ia = (y + oy) * sa + (x + ox) * 4;
                    int ib = y * sb + x * 4;
                    int d = Math.Abs(ba[ia] - bb[ib]) + Math.Abs(ba[ia + 1] - bb[ib + 1]) + Math.Abs(ba[ia + 2] - bb[ib + 2]);
                    sum += d;
                    if (d > 12) over++;
                    if (d > max) { max = d; maxAtX = x + ox; maxAtY = y + oy; }
                }
            }
            long n = (long)pw * ph;
            return string.Format("size {0}x{1}  meanDiff={2:F4}  pixelsDiff>12: {3} ({4:F4}%)  maxDiff={5} at src({6},{7})",
                pw, ph, (double)sum / n, over, 100.0 * over / n, max, maxAtX, maxAtY);
        }
    }

    static byte[] Unpack(Bitmap src)
    {
        int w = src.Width, h = src.Height;
        using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp)) g.DrawImage(src, 0, 0, w, h);
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var buf = new byte[data.Stride * h];
            Marshal.Copy(data.Scan0, buf, 0, buf.Length);
            bmp.UnlockBits(data);
            return buf;
        }
    }
}
'@ -ReferencedAssemblies System.Drawing

$pw = 0; $ph = 0
$r = [PixelDiff]::Compare((Resolve-Path -LiteralPath $Source).ToString(), (Resolve-Path -LiteralPath $Part).ToString(), $X, $Y, [ref]$pw, [ref]$ph)
Write-Host ("{0} vs {1}[{2},{3}] -> {4}" -f (Split-Path $Part -Leaf), (Split-Path $Source -Leaf), $X, $Y, $r)
