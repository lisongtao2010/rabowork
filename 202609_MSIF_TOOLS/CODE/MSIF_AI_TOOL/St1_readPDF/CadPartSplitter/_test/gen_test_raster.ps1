# 生成一张用于验证「位图输入（无文字层）」分支的合成图纸
# 白底 + 两个粗线框"零件" + 右侧假表格（黑色密实短横线）
Add-Type -AssemblyName System.Drawing

$w = 3000; $h = 2000
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::White)
$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::Black), 6

# 零件 1
$g.DrawRectangle($pen, 200, 200, 900, 500)
$g.DrawEllipse($pen, 400, 300, 400, 300)
# 零件 2
$g.DrawRectangle($pen, 200, 1100, 900, 500)
$g.DrawRectangle($pen, 350, 1200, 600, 300)

# 右侧"表格"：密集横线，触发自动遮蔽
for ($y = 100; $y -lt 1900; $y += 40) {
    $g.DrawLine($pen, 2000, $y, 2900, $y)
}
$g.DrawLine($pen, 2000, 100, 2000, 1900)
$g.DrawLine($pen, 2900, 100, 2900, 1900)

$g.Dispose()
$out = Join-Path $PSScriptRoot 'test_raster.png'
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output "saved: $out"
