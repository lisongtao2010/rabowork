# 校验界面「输出目录自动推导」规则（不需要真的弹窗）：
# 反射加载 bin\Debug\CadPartSplitter.exe，构造 MainForm 后调用私有 SetInputFile，
# 读回 txtOutput 的实际内容，核对 <图纸目录>\AiAppData\<图纸名>[_P页号]、以及「手改后不被覆盖」。
# 用法：powershell -ExecutionPolicy Bypass -File _test\gui_output_probe.ps1
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$work = Join-Path $PSScriptRoot 'verify_gui'
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Path $work | Out-Null
$pdf = Join-Path $work 'H310A20009.pdf'
Copy-Item (Join-Path $root 'H310A20009.pdf') $pdf

Add-Type -AssemblyName System.Windows.Forms

$asm = [Reflection.Assembly]::LoadFrom((Join-Path $root 'bin\Debug\CadPartSplitter.exe'))
$type = $asm.GetType('CadPartSplitter.MainForm')
if ($null -eq $type) { throw '找不到 CadPartSplitter.MainForm（先编译 bin\Debug）' }

$flags = [Reflection.BindingFlags]'NonPublic,Instance'
$form  = [Activator]::CreateInstance($type)

$setInput = $type.GetMethod('SetInputFile', $flags)
$txtIn    = $type.GetField('txtInput',  $flags).GetValue($form)
$txtOut   = $type.GetField('txtOutput', $flags).GetValue($form)
$numPage  = $type.GetField('numPage',  $flags).GetValue($form)
$expected = Join-Path $work 'AiAppData\H310A20009'

function Show($title, $value) {
    "{0,-22}: {1}" -f $title, $value
}

'=== 1) 选文件（页码 0）==='
$setInput.Invoke($form, [object[]]@([string]$pdf))
Show '输入文件框' $txtIn.Text
Show '输出目录框' $txtOut.Text
Show '命中预期目录(应为 True)' ([IO.Path]::GetFullPath($txtOut.Text) -eq [IO.Path]::GetFullPath($expected))
Show 'PDF 页数上限' $numPage.Maximum

'=== 2) 页码 > 0 时的目录后缀（样例 PDF 只有 1 页，临时放大上限模拟）==='
$numPage.Maximum = 5
$numPage.Value   = 1
$setInput.Invoke($form, [object[]]@([string]$pdf))
Show '输出目录框' $txtOut.Text
Show '命中 _P2(应为 True)' ($txtOut.Text -eq ($expected + '_P2'))

'=== 3) 页码回到 0（应退回无后缀目录）==='
$numPage.Value = 0
$setInput.Invoke($form, [object[]]@([string]$pdf))
Show '输出目录框' $txtOut.Text

'=== 4) 手工改过输出目录后再选文件（不应被覆盖）==='
$txtOut.Text = 'D:\tmp\custom_out'
$setInput.Invoke($form, [object[]]@([string]$pdf))
Show '输出目录框' $txtOut.Text

'=== 5) 清空输出目录后选另一张图纸（各占一个子文件夹）==='
$txtOut.Text = ''
$pdf2 = Join-Path $work 'U10004.pdf'
Copy-Item (Join-Path $root 'H310A20009.pdf') $pdf2
$setInput.Invoke($form, [object[]]@([string]$pdf2))
Show '输出目录框' $txtOut.Text

$form.Dispose()
'完成。'
