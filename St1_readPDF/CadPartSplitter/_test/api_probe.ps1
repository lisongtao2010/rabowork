# 校验对外接口 SplitApi 与参数持久化 GuiSettings
# 直接反射加载 bin\Debug\CadPartSplitter.exe，不需要编译宿主程序。
# 用法：powershell -ExecutionPolicy Bypass -File _test\api_probe.ps1
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$work = Join-Path $PSScriptRoot 'verify_api'
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Path $work | Out-Null
Copy-Item (Join-Path $root 'H310A20009.pdf') $work

[void][Reflection.Assembly]::LoadFrom((Join-Path $root 'bin\Debug\CadPartSplitter.exe'))
$pdf = Join-Path $work 'H310A20009.pdf'

'=== 1) SplitApi.Split(选择文件路径) ==='
$r = [CadPartSplitter.SplitApi]::Split($pdf)
"Success        : $($r.Success)"
"ExitCode       : $($r.ExitCode)"
"PartCount      : $($r.PartCount)"
"Message        : $($r.Message)"
"OutputDirectory: $($r.OutputDirectory)"
$expected = Join-Path $work 'AiAppData\H310A20009'
"预期目录命中  : $(([IO.Path]::GetFullPath($r.OutputDirectory)) -eq ([IO.Path]::GetFullPath($expected)))  （应为 True）"
"Elapsed        : $($r.ElapsedMilliseconds) ms"
"LogLines       : $((($r.Log -split "`r?`n") | Where-Object { $_ }).Count)"
"ToString       : $r"

'=== 2) 输出目录内容 ==='
Get-ChildItem $r.OutputDirectory | Select-Object -ExpandProperty Name

'=== 3) 文件不存在 ==='
$bad = [CadPartSplitter.SplitApi]::Split((Join-Path $work 'nope.pdf'))
"Success=$($bad.Success) ExitCode=$($bad.ExitCode) Error=$($bad.ErrorMessage)"

'=== 4) 空路径 ==='
$empty = [CadPartSplitter.SplitApi]::Split('')
"Success=$($empty.Success) ExitCode=$($empty.ExitCode) Error=$($empty.ErrorMessage)"

'=== 5) Split(路径, 指定输出目录) ==='
$r2 = [CadPartSplitter.SplitApi]::Split($pdf, (Join-Path $work 'custom_out'))
"Success=$($r2.Success) PartCount=$($r2.PartCount) Output=$($r2.OutputDirectory)"

'=== 5b) 默认目录规则：多页后缀 / 不同图纸各占一个子文件夹 ==='
"page=1        -> $([CadPartSplitter.DetectorOptions]::GetDefaultOutputDirectory($pdf, 1))"
"page=2        -> $([CadPartSplitter.DetectorOptions]::GetDefaultOutputDirectory($pdf, 2))"
"目录(带分隔符结尾) -> $([CadPartSplitter.DetectorOptions]::GetDefaultOutputDirectory($work + [IO.Path]::DirectorySeparatorChar))"
$pdf2 = Join-Path $work 'U10004.pdf'
Copy-Item $pdf $pdf2 -Force
$r3 = [CadPartSplitter.SplitApi]::Split($pdf2)
"另一张图      -> $($r3.OutputDirectory)"
Get-ChildItem (Join-Path $work 'AiAppData') | Select-Object -ExpandProperty Name

'=== 6) GuiSettings 存取往返 ==='
$ini = Join-Path $work 'settings_test.ini'
$s = New-Object CadPartSplitter.GuiSettings
$s.RenderDpi           = 250
$s.RightMaskRatio      = 0.45
$s.MinContourArea      = 50000
$s.Padding             = 40
$s.DilateKernelWidth   = 60
$s.DilateKernelHeight  = 20
$s.DropTextHeavyParts  = $false
$s.SaveDebugImages     = $true
"Save -> $($s.Save($ini))"
Get-Content $ini
$t = [CadPartSplitter.GuiSettings]::Load($ini)
"Read -> dpi=$($t.RenderDpi) page=$($t.PdfPageIndex) mask=$($t.RightMaskRatio) area=$($t.MinContourArea) pad=$($t.Padding) kw=$($t.DilateKernelWidth) kh=$($t.DilateKernelHeight) autoMask=$($t.AutoMaskRightColumn) debug=$($t.SaveDebugImages) drop=$($t.DropTextHeavyParts)"

'=== 7) 手改坏值的钳制 ==='
$badIni = Join-Path $work 'settings_bad.ini'
@(
    'RenderDpi=99999',
    'RightMaskRatio=5',
    'MinContourArea=-1',
    'DilateKernelWidth=0',
    'DropTextHeavyParts=maybe',
    'UnknownKey=1'
) | Set-Content -Path $badIni -Encoding UTF8
$b = [CadPartSplitter.GuiSettings]::Load($badIni)
"Clamped -> dpi=$($b.RenderDpi) mask=$($b.RightMaskRatio) area=$($b.MinContourArea) kw=$($b.DilateKernelWidth) drop=$($b.DropTextHeavyParts) (drop 应为默认 True)"

'=== 8) ApplyTo / FromOptions ==='
$o = New-Object CadPartSplitter.DetectorOptions
$t.ApplyTo($o)
"ApplyTo     -> dpi=$($o.RenderDpi) mask=$($o.RightMaskRatio) pad=$($o.Padding) debug=$($o.SaveDebugImages)"
$snap = [CadPartSplitter.GuiSettings]::FromOptions($o)
"FromOptions -> dpi=$($snap.RenderDpi) mask=$($snap.RightMaskRatio) pad=$($snap.Padding)"

'=== 9) 默认配置文件位置 ==='
[CadPartSplitter.GuiSettings]::SettingsFilePath
