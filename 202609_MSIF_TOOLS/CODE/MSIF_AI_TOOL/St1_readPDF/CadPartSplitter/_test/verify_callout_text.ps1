# 校验「圈号 → parts_text.txt 词记录替换」是否按位置正确命中。
#
# 做法：读输出目录里的 parts_info.json（拿每个圈号的圆心 + ①1 显示形式），
# 再读 parts_text.txt 的文字记录（`y= x= w= h= 局部(..)  文本`；默认逐词一行，
# 加 --merge-text-lines 时是整行），检查「外接框包含圆心的那一条」的文本里
# 确实出现了对应的 ①1。命中不了就打印出来，便于判断是替换漏了还是行合并把它并到别处。
#
# 用法： powershell -ExecutionPolicy Bypass -File _test\verify_callout_text.ps1 _test\verify_call_A20009

param([Parameter(Mandatory = $true)][string]$Dir)

$Dir = (Resolve-Path $Dir).Path
$json = [System.IO.File]::ReadAllText((Join-Path $Dir 'parts_info.json'), [System.Text.Encoding]::UTF8) | ConvertFrom-Json
$text = [System.IO.File]::ReadAllText((Join-Path $Dir 'parts_text.txt'), [System.Text.Encoding]::UTF8)
$lines = $text -split "`r`n"

# 文字行：  y=626   x=4313  w=42    h=57   局部(1650,87)  ③3
$lineRe = [regex]'^\s+y=(\d+)\s+x=(\d+)\s+w=(\d+)\s+h=(\d+)\s+局部\((-?\d+),(-?\d+)\)\s+(.*)$'

$rows = @()
foreach ($l in $lines) {
    $m = $lineRe.Match($l)
    if (-not $m.Success) { continue }
    $rows += [pscustomobject]@{
        Y = [int]$m.Groups[1].Value
        X = [int]$m.Groups[2].Value
        W = [int]$m.Groups[3].Value
        H = [int]$m.Groups[4].Value
        Text = $m.Groups[7].Value
    }
}

$total = 0; $ok = 0
foreach ($p in $json) {
    foreach ($c in $p.callouts) {
        $total++
        $cx = $c.circle.cx
        $cy = $c.circle.cy

        $hit = $rows | Where-Object {
            $cx -ge $_.X -and $cx -le ($_.X + $_.W) -and $cy -ge $_.Y -and $cy -le ($_.Y + $_.H)
        } | Where-Object { $_.Text.Contains($c.display) } | Select-Object -First 1

        if ($hit) {
            $ok++
            Write-Host ("  OK   " + $c.display.PadRight(6) + $p.fileName + "  行: " + $hit.Text)
        }
        else {
            Write-Host ("  MISS " + $c.display.PadRight(6) + $p.fileName + "  圆心($cx,$cy) 未在行文本中找到带圈形式")
        }
    }
}

Write-Host ""
Write-Host ("圈号 $total 个，按位置命中带圈文本 $ok 个")
