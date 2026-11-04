# 校验界面「批量模式（选文件夹）」的行为（不需要真的弹窗）：
# 通过反射构造 MainForm，调用私有 SetInputFolder，核对
#   1) 输出目录自动填 <文件夹>\AiAppData（不再是含图纸名的路径）
#   2) 开始按钮文字变成「开始批量处理」，页码上限放开（逐张校验）
#   3) 手工改过输出目录后不被覆盖
#   4) 换回单文件后按钮文字复原为「开始分割」、输出目录回到 <目录>\AiAppData\<图纸名>
#   5) 批量表头日志里的 <图纸名> 只是说明文字，不参与路径运算（否则 Path.Combine 会抛
#      ArgumentException：パスに無効な文字が含まれています。）—— 用真实日文路径回归
#   6) 输入框可编辑：粘贴带引号的路径会被清理、多行只取第一行、file:/// 能识别，
#      且粘文件夹后自动进入批量模式（按钮文字 / 输出目录随之更新）、粘 PDF 后页码上限跟着收窄
# 用法：powershell -ExecutionPolicy Bypass -File _test\gui_batch_probe.ps1 [-AssemblyPath <程序集路径>]
param(
    [string]$AssemblyPath = ''
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrEmpty($AssemblyPath)) {
    $AssemblyPath = Join-Path $root 'bin\Debug\CadPartSplitter.exe'

    # 主工程的 legacy csproj 有时装不上 NuGet，退回临时校验宿主编译出的同一份源码
    if (-not (Test-Path $AssemblyPath)) {
        $AssemblyPath = Join-Path $env:TEMP 'cps_verify\bin\Debug\net48\Verify.exe'
    }
}

if (-not (Test-Path $AssemblyPath)) { throw "找不到程序集：$AssemblyPath（先编译）" }

$work = Join-Path $PSScriptRoot 'verify_gui_batch'
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Path $work | Out-Null

$pdf = Join-Path $work 'H310A20009.pdf'
Copy-Item (Join-Path $root 'H310A20009.pdf') $pdf
'占位——不是 PDF' | Set-Content (Join-Path $work 'notes.txt')

Add-Type -AssemblyName System.Windows.Forms

$asm  = [Reflection.Assembly]::LoadFrom($AssemblyPath)
$type = $asm.GetType('CadPartSplitter.MainForm')
if ($null -eq $type) { throw "找不到 CadPartSplitter.MainForm（$AssemblyPath）" }

$flags = [Reflection.BindingFlags]'NonPublic,Instance'
$form  = [Activator]::CreateInstance($type)

$setFolder = $type.GetMethod('SetInputFolder', $flags)
$setFile   = $type.GetMethod('SetInputFile',   $flags)

if ($null -eq $setFolder) {
    throw "程序集里没有 SetInputFolder（$AssemblyPath）—— 这是旧版本编译的产物，请重新构建，或用 -AssemblyPath 指向新编译的 exe"
}

$txtIn     = $type.GetField('txtInput',   $flags).GetValue($form)
$txtOut    = $type.GetField('txtOutput',  $flags).GetValue($form)
$numPage   = $type.GetField('numPage',    $flags).GetValue($form)
$btnStart  = $type.GetField('btnStart',   $flags).GetValue($form)
$lblStatus = $type.GetField('lblStatus',  $flags).GetValue($form)
$txtLog    = $type.GetField('txtLog',     $flags).GetValue($form)

$expectedRoot = Join-Path $work 'AiAppData'

function Show($title, $value) {
    "{0,-26}: {1}" -f $title, $value
}

function Same($a, $b) {
    if ([string]::IsNullOrEmpty($a) -or [string]::IsNullOrEmpty($b)) { return $false }
    return [IO.Path]::GetFullPath($a) -eq [IO.Path]::GetFullPath($b)
}

'=== 1) 选文件夹（输出目录应为 <文件夹>\AiAppData）==='
$txtOut.Text = ''
$setFolder.Invoke($form, [object[]]@([string]$work))
Show '输入框' $txtIn.Text
Show '输出目录框' $txtOut.Text
Show '命中 <文件夹>\AiAppData(应为 True)' (Same $txtOut.Text $expectedRoot)
Show '开始按钮文字(应为 开始批量处理)' $btnStart.Text
Show '页码上限(应为 9999)' $numPage.Maximum

'=== 2) 手工改过输出目录后再选文件夹（不应被覆盖）==='
$txtOut.Text = 'D:\tmp\custom_root'
$setFolder.Invoke($form, [object[]]@([string]$work))
Show '输出目录框' $txtOut.Text

'=== 3) 换回单文件（按钮文字复原、输出目录含图纸名）==='
$txtOut.Text = ''
$setFile.Invoke($form, [object[]]@([string]$pdf))
Show '输入框' $txtIn.Text
Show '输出目录框' $txtOut.Text
Show '开始按钮文字(应为 开始分割)' $btnStart.Text
Show '输出目录含图纸名(应为 True)' ((Split-Path -Leaf $txtOut.Text) -eq 'H310A20009')

'=== 4) 批量表头日志：说明文字含 <图纸名> + 日文路径（曾经的崩溃点）==='
$logHeader = $type.GetMethod('LogBatchHeader', $flags)
if ($null -eq $logHeader) { throw '程序集里没有 LogBatchHeader（旧版本编译产物？先重新编译）' }

$optsType = $asm.GetType('CadPartSplitter.DetectorOptions')
if ($null -eq $optsType) { throw "找不到 CadPartSplitter.DetectorOptions（$AssemblyPath）" }
$opts = [Activator]::CreateInstance($optsType)

# 用户实际踩到的路径（図面 = 日文）；旧代码对 "<图纸名>" 调 Path.Combine 会抛
# ArgumentException：パスに無効な文字が含まれています。
$jaFolder = 'D:\Github_lis2010\rabowork\202609_MSIF_TOOLS\図面-20260918T025344Z-1-001\図面'
if (-not (Test-Path -LiteralPath $jaFolder)) { $jaFolder = Join-Path $work '図面' }

$threw = $false
try {
    $txtLog.Clear()
    $logHeader.Invoke($form, [object[]]@([string]$jaFolder, [string]($jaFolder + '\AiAppData'), [int]27, $opts))
} catch {
    $threw = $true
    "  !! 抛异常：$($_.Exception.GetBaseException().Message)"
}

$logged = $txtLog.Text
Show '未抛异常(应为 True)' (-not $threw)
Show '日志含 <图纸名>(应为 True)' ($logged -like '*<图纸名>*')
Show '日志首行' (($logged -split "`r?`n")[0])

# 顺带确认：选真实日文文件夹也不报错，且能数出 PDF 张数
$threw2 = $false
try { $setFolder.Invoke($form, [object[]]@([string]$jaFolder)) } catch { $threw2 = $true }
Show '选日文文件夹未抛异常(应为 True)' (-not $threw2)
if (Test-Path -LiteralPath $jaFolder) { Show '输入框' $txtIn.Text }

'=== 5) 粘贴路径：去引号 / file:/// / 多行取第一行（输入框可编辑）==='
$norm = $type.GetMethod('NormalizePath', [Reflection.BindingFlags]'NonPublic,Static')
if ($null -eq $norm) { throw '程序集里没有 NormalizePath（旧版本编译产物？先重新编译）' }

function Norm($s) { $norm.Invoke($null, [object[]]@([string]$s)) }

Show '去双引号'      (Norm ('"' + $work + '"'))
Show '去首尾空白'    (Norm ('   ' + $work + '   '))
Show 'file:/// 转换' (Norm ('file:///' + ($work -replace '\\', '/')))
Show '多行取第一行'  (Norm ($work + "`r`nD:\other"))

# 模拟「把带引号的文件夹路径粘进输入框」：应自动去掉引号并进入批量模式
$txtIn.Text = '"' + $work + '"'
Show '粘贴后输入框(应无引号)'          ((Split-Path -Leaf $txtIn.Text) -eq 'verify_gui_batch')
Show '开始按钮可用(应为 True)'         $btnStart.Enabled
Show '开始按钮文字(应为 开始批量处理)'  $btnStart.Text
Show '输出目录应回到 <文件夹>\AiAppData' (Same $txtOut.Text $expectedRoot)

# 粘单个 PDF：页码上限应收到「总页数-1」、状态栏回「就绪」（与「浏览…」选文件一致）
$txtIn.Text = '"' + $pdf + '"'
Show '粘 PDF 后输入框'              ($txtIn.Text -eq $pdf)
Show '粘 PDF 后页码上限已受限'       ($numPage.Maximum -lt 9999)
Show '粘 PDF 后状态栏(应为 就绪)'    $lblStatus.Text
Show '粘 PDF 后按钮文字'            $btnStart.Text

'=== 6) Ctrl+V 实际走 PastePathInto（读剪贴板，带引号 + 换行）==='
$pasteInto = $type.GetMethod('PastePathInto', $flags)
if ($null -eq $pasteInto) { throw '程序集里没有 PastePathInto（旧版本编译产物？先重新编译）' }

try {
    [System.Windows.Forms.Clipboard]::SetText('"' + $work + '"' + "`r`n")
    $txtIn.Text = ''
    $txtLog.Clear()
    $pasteInto.Invoke($form, [object[]]@($txtIn))

    Show '剪贴板粘贴后输入框'          ($txtIn.Text -eq $work)
    Show '日志提示已粘贴文件夹'         ($txtLog.Text -like '*已粘贴文件夹*')
    Show '粘贴后按钮文字'              $btnStart.Text
} catch {
    "  (跳过：剪贴板不可用 —— $($_.Exception.GetBaseException().Message))"
}

$form.Dispose()
'完成。'
