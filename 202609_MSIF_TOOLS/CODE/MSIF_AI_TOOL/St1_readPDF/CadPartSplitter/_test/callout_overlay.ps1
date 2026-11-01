# 验证脚本：把 parts_info.json 里识别出的圈号（圆心 + 半径）画回导出的零件图上，
# 用来肉眼确认「圈 + 数字」配对是否准确（红圈 = 识别结果）。
# 用法：powershell -ExecutionPolicy Bypass -File _test\callout_overlay.ps1 <输出目录>
param([string]$Dir = '_test\verify_call_A20009')

Add-Type -AssemblyName System.Drawing

$jsonPath = Join-Path $Dir 'parts_info.json'
$parts = Get-Content $jsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($null -eq $parts) { $parts = @() }

foreach ($p in $parts) {
    $img = Join-Path $Dir $p.fileName
    if (-not (Test-Path $img)) { Write-Host "skip (missing): $img"; continue }

    $bmp = [System.Drawing.Bitmap]::FromFile((Resolve-Path $img))
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::Red), 3
    $font = New-Object System.Drawing.Font 'Segoe UI', 20, ([System.Drawing.FontStyle]::Bold)
    $brush = [System.Drawing.Brushes]::Blue

    $n = 0
    foreach ($c in $p.callouts) {
        $cx = [double]$c.local.cx; $cy = [double]$c.local.cy; $r = [double]$c.circle.r
        $g.DrawEllipse($pen, [float]($cx - $r - 4), [float]($cy - $r - 4), [float]($r * 2 + 8), [float]($r * 2 + 8))
        $g.DrawString($c.display, $font, $brush, [float]($cx + $r + 6), [float]($cy - 14))
        $n++
    }

    $g.Dispose()
    $out = Join-Path $Dir ($p.fileName -replace '\.png$', '_callouts.png')
    $bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host ("{0}: 圈号 {1} 个 -> {2}" -f $p.fileName, $n, $out)
}
