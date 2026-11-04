# Crop a region from an image and save it (optionally scaled) for zoomed inspection.
# Usage: powershell -ExecutionPolicy Bypass -File _test\crop.ps1 <src> <x> <y> <w> <h> <out.png> [scale]
param(
    [Parameter(Mandatory=$true)][string]$Source,
    [Parameter(Mandatory=$true)][int]$X,
    [Parameter(Mandatory=$true)][int]$Y,
    [Parameter(Mandatory=$true)][int]$W,
    [Parameter(Mandatory=$true)][int]$H,
    [Parameter(Mandatory=$true)][string]$Out,
    [double]$Scale = 1.0
)

Add-Type -AssemblyName System.Drawing

$src = [System.Drawing.Image]::FromFile((Resolve-Path -LiteralPath $Source).ToString())
$rect = New-Object System.Drawing.Rectangle($X, $Y, [Math]::Min($W, $src.Width - $X), [Math]::Min($H, $src.Height - $Y))
$bmp = New-Object System.Drawing.Bitmap([int]($rect.Width * $Scale), [int]($rect.Height * $Scale))
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.DrawImage($src, (New-Object System.Drawing.Rectangle(0, 0, $bmp.Width, $bmp.Height)), $rect, [System.Drawing.GraphicsUnit]::Pixel)
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose(); $src.Dispose()
Write-Host ("saved {0}  {1}x{2} (from X={3} Y={4} W={5} H={6})" -f $Out, [int]($rect.Width * $Scale), [int]($rect.Height * $Scale), $X, $Y, $rect.Width, $rect.Height)
