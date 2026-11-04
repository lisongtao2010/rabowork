# Draw part bounding boxes (from parts_info.json) onto the original page image.
# Usage: powershell -ExecutionPolicy Bypass -File _test\overlay.ps1 <original.png> <outputDir> [scale]
param(
    [Parameter(Mandatory=$true)][string]$Source,
    [Parameter(Mandatory=$true)][string]$OutDir,
    [double]$Scale = 0.5
)

Add-Type -AssemblyName System.Drawing

$json = Get-Content -LiteralPath (Join-Path $OutDir 'parts_info.json') -Raw -Encoding UTF8
$pattern = '"fileName"\s*:\s*"(Part_\d+\.png)"\s*,\s*"pixel"\s*:\s*\{\s*"x"\s*:\s*(\d+)\s*,\s*"y"\s*:\s*(\d+)\s*,\s*"w"\s*:\s*(\d+)\s*,\s*"h"\s*:\s*(\d+)'
$boxes = [regex]::Matches($json, $pattern)
if ($boxes.Count -eq 0) { Write-Error 'no part boxes parsed from parts_info.json'; exit 1 }

$src = [System.Drawing.Image]::FromFile((Resolve-Path -LiteralPath $Source))
$w = [int]($src.Width * $Scale)
$h = [int]($src.Height * $Scale)
$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.DrawImage($src, 0, 0, $w, $h)

$palette = @(
    [System.Drawing.Color]::Red,
    [System.Drawing.Color]::Blue,
    [System.Drawing.Color]::LimeGreen,
    [System.Drawing.Color]::Magenta,
    [System.Drawing.Color]::DarkOrange
)
$font  = New-Object System.Drawing.Font('Consolas', 22, [System.Drawing.FontStyle]::Bold)
$brush = [System.Drawing.Brushes]::Black

for ($i = 0; $i -lt $boxes.Count; $i++) {
    $m  = $boxes[$i]
    $name = $m.Groups[1].Value
    $x = [int]$m.Groups[2].Value * $Scale
    $y = [int]$m.Groups[3].Value * $Scale
    $bw = [int]$m.Groups[4].Value * $Scale
    $bh = [int]$m.Groups[5].Value * $Scale
    $pen = New-Object System.Drawing.Pen($palette[$i % $palette.Count], 5)
    $g.DrawRectangle($pen, $x, $y, $bw, $bh)
    $g.DrawString($name, $font, $brush, $x + 8, $y + 6)
    $pen.Dispose()
    Write-Host ("{0}  X={1} Y={2} W={3} H={4}" -f $name, $m.Groups[2].Value, $m.Groups[3].Value, $m.Groups[4].Value, $m.Groups[5].Value)
}

$outPath = Join-Path $OutDir 'overlay_parts.png'
$bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose(); $src.Dispose(); $font.Dispose()
Write-Host "saved: $outPath"
