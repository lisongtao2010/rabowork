# =============================================================================
# dump_ai_prompt.ps1
#   Replays exactly what the tool sends to the AI for one PDF and dumps it:
#     01_system.txt / 02_user.txt / 03_request_body.json
#     04_response_raw.json / 05_ai_content.json / 06_parsed_rows.txt
#
#   Usage (repo root):
#     powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\dump_ai_prompt.ps1 `
#         -PdfPath "D:\...\testresult\H310U10004.pdf"
#
#   The script uses the very same internal services as the app
#   (PdfSplitService -> PluTablePageLocator -> AiPromptStore -> ChatCompletionClient
#    -> AiPartResponseParser), so the dumped text is what the app really sends.
#   Note: it performs ONE real API call (costs tokens).
# =============================================================================
param(
    [Parameter(Mandatory = $true)][string]$PdfPath,
    [string]$AppDir = '',
    [string]$OutDir = ''
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $PdfPath)) { throw ('pdf not found: ' + $PdfPath) }
if ([string]::IsNullOrWhiteSpace($AppDir)) {
    $AppDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'bin\Debug'
}
if (-not (Test-Path -LiteralPath (Join-Path $AppDir 'MSIFAITOOLS.exe'))) {
    throw ('MSIFAITOOLS.exe not found in: ' + $AppDir + ' (build the project first)')
}
if ([string]::IsNullOrWhiteSpace($OutDir)) { $OutDir = Join-Path $env:TEMP 'msif_prompt_demo' }

$stem = [System.IO.Path]::GetFileNameWithoutExtension($PdfPath)
if (Test-Path -LiteralPath $OutDir) { Remove-Item -LiteralPath $OutDir -Recurse -Force }
$pdfDir = Join-Path $OutDir 'pdf'
New-Item -ItemType Directory -Path $pdfDir -Force | Out-Null
Copy-Item -LiteralPath $PdfPath -Destination $pdfDir

Add-Type -AssemblyName System.Net.Http
Add-Type -AssemblyName System.Web.Extensions
$asm = [System.Reflection.Assembly]::LoadFrom((Join-Path $AppDir 'MSIFAITOOLS.exe'))
$BF = [System.Reflection.BindingFlags]
$anyFlags = [System.Reflection.BindingFlags]($BF::Instance -bor $BF::Static -bor $BF::Public -bor $BF::NonPublic)
$utf8 = New-Object System.Text.UTF8Encoding($false)

function T([string]$name) {
    $t = $asm.GetType($name)
    if ($null -eq $t) { throw ('type not found: ' + $name) }
    return $t
}

function GetM($type, [string]$name) {
    $m = $type.GetMethod($name, $anyFlags)
    if ($null -eq $m) { throw ('method not found: ' + $type.FullName + '.' + $name) }
    return $m
}

# the "return ,(...)" keeps PowerShell from enumerating IList results
function Inv0($type, [string]$name, $target) { return ,((GetM $type $name).Invoke($target, (New-Object object[] 0))) }
function Inv1($type, [string]$name, $target, $a1) {
    $al = New-Object System.Collections.ArrayList
    [void]$al.Add($a1)
    return ,((GetM $type $name).Invoke($target, $al.ToArray()))
}
function Inv2($type, [string]$name, $target, $a1, $a2) {
    $al = New-Object System.Collections.ArrayList
    [void]$al.Add($a1); [void]$al.Add($a2)
    return ,((GetM $type $name).Invoke($target, $al.ToArray()))
}
function Inv3($type, [string]$name, $target, $a1, $a2, $a3) {
    $al = New-Object System.Collections.ArrayList
    [void]$al.Add($a1); [void]$al.Add($a2); [void]$al.Add($a3)
    return ,((GetM $type $name).Invoke($target, $al.ToArray()))
}
function Inv4($type, [string]$name, $target, $a1, $a2, $a3, $a4) {
    $al = New-Object System.Collections.ArrayList
    [void]$al.Add($a1); [void]$al.Add($a2); [void]$al.Add($a3); [void]$al.Add($a4)
    return ,((GetM $type $name).Invoke($target, $al.ToArray()))
}

# ---------- 1) split the pdf (same code the tool runs) ----------
$splitType = T 'MSIFAITOOLS.Services.PdfSplitService'
$splitter = [Activator]::CreateInstance($splitType)
$splitResult = Inv3 $splitType 'Split' $splitter ([string]$pdfDir) $null $null
Write-Output ('split succeeded=' + $splitResult.Succeeded + ' errors=' + $splitResult.Errors.Count)
foreach ($e in $splitResult.Errors) { Write-Output ('  error: ' + $e) }

$pdfOut = Join-Path (Join-Path $pdfDir 'AiAppData') $stem
$pluTable = Join-Path $pdfOut 'plu_table.txt'
$xyFile = Join-Path $pdfOut 'xy.txt'
if (-not (Test-Path -LiteralPath $pluTable)) { throw ('plu_table.txt not produced: ' + $pluTable) }
Write-Output ('plu_table.txt bytes = ' + (Get-Item -LiteralPath $pluTable).Length)
Write-Output ('xy.txt bytes        = ' + (Get-Item -LiteralPath $xyFile).Length)

# ---------- 2) build the exact prompts (mirror AiPartAnalyzer.Analyze) ----------
$locator = T 'MSIFAITOOLS.Services.PluTablePageLocator'
$tableText = [System.IO.File]::ReadAllText($pluTable, $utf8)
Write-Output ('plu_table.txt chars = ' + $tableText.Length)

$pages = Inv1 $locator 'SplitPages' $null ([string]$tableText)
$cand = Inv1 $locator 'FindCandidatePages' $null $pages
Write-Output ('candidate header pages = [' + (($cand | ForEach-Object { $_ }) -join ',') + ']')

$tablePages = Inv4 $locator 'ExpandWithFollowingPages' $null $pages $cand 1 6
Write-Output ('pages sent to AI = [' + (($tablePages | ForEach-Object { $_ }) -join ',') + ']')

$tableSnippet = [string](Inv3 $locator 'BuildTableSnippet' $null $pages $tablePages 40000)
Write-Output ('table snippet chars = ' + $tableSnippet.Length)

$analyzerType = T 'MSIFAITOOLS.Services.AiPartAnalyzer'
$xyType = T 'MSIFAITOOLS.Services.XyCoordinateIndex'
$xyIndex = $null
$coordSnippet = ''
if (Test-Path -LiteralPath $xyFile) {
    $xyIndex = Inv1 $xyType 'Load' $null ([string]$xyFile)
    $coordSnippet = [string](Inv3 $analyzerType 'BuildCoordinateSnippet' $null $xyIndex $tablePages $pages)
    Write-Output ('coordinate snippet chars = ' + $coordSnippet.Length + ' (words=' + $xyIndex.WordCount + ')')
}
else {
    Write-Output 'coordinate snippet chars = 0 (no xy.txt; the COORDINATE block is dropped)'
}

$promptStore = T 'MSIFAITOOLS.Services.AiPromptStore'
$promptSet = Inv0 $promptStore 'Load' $null
Write-Output ('prompt source = ' + $promptSet.Source)
$systemPrompt = [string]$promptSet.SystemPrompt
$userPrompt = [string](Inv3 $promptSet.GetType() 'BuildUserPrompt' $promptSet $stem $tableSnippet $coordSnippet)
Write-Output ('system chars = ' + $systemPrompt.Length + ' / user chars = ' + $userPrompt.Length)

# ---------- 3) api options (read App.config directly: this host is powershell.exe) ----------
[xml]$cfg = Get-Content -LiteralPath (Join-Path $AppDir 'MSIFAITOOLS.exe.config') -Encoding UTF8
$settings = @{}
foreach ($node in $cfg.configuration.appSettings.add) { $settings[$node.key] = $node.value }
$optType = T 'MSIFAITOOLS.Services.AiApiOptions'
$ctorArgs = New-Object object[] 5
$ctorArgs[0] = [string]$settings['AiBaseUrl']
$ctorArgs[1] = [string]$settings['AiApiKey']
$ctorArgs[2] = [string]$settings['AiModel']
$ctorArgs[3] = [int]$settings['AiTimeoutSeconds']
$ctorArgs[4] = 'App.config'
$options = [Activator]::CreateInstance($optType, $ctorArgs)
Write-Output ('endpoint = ' + $options.Endpoint + ' / model = ' + $options.Model)

$clientType = T 'MSIFAITOOLS.Services.ChatCompletionClient'
$clientArgs = New-Object object[] 1
$clientArgs[0] = $options
$client = [Activator]::CreateInstance($clientType, $clientArgs)
$requestBody = [string](Inv2 $clientType 'BuildRequestBody' $client ([string]$systemPrompt) ([string]$userPrompt))
Write-Output ('request body chars = ' + $requestBody.Length)

# ---------- 4) save what we send ----------
[System.IO.File]::WriteAllText((Join-Path $OutDir '01_system.txt'), $systemPrompt, $utf8)
[System.IO.File]::WriteAllText((Join-Path $OutDir '02_user.txt'), $userPrompt, $utf8)
[System.IO.File]::WriteAllText((Join-Path $OutDir '03_request_body.json'), $requestBody, $utf8)


# ---------- 5) real HTTP round trip (retry like ChatCompletionClient) ----------
$http = New-Object System.Net.Http.HttpClient
$http.Timeout = [TimeSpan]::FromSeconds(300)
$req = New-Object System.Net.Http.HttpRequestMessage ([System.Net.Http.HttpMethod]::Post), $options.Endpoint
$req.Headers.TryAddWithoutValidation('Authorization', ('Bearer ' + $options.ApiKey)) | Out-Null
$bytes = [System.Text.Encoding]::UTF8.GetBytes($requestBody)
$body = [System.Net.Http.ByteArrayContent]::new($bytes)
$body.Headers.ContentType = New-Object System.Net.Http.Headers.MediaTypeHeaderValue 'application/json'
$req.Content = $body

$raw = ''
$statusCode = 0
for ($attempt = 1; $attempt -le 3; $attempt++) {
    $resp = $http.SendAsync($req).GetAwaiter().GetResult()
    $raw = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $statusCode = [int]$resp.StatusCode
    Write-Output ('attempt ' + $attempt + ' -> http status = ' + $statusCode + ' / response chars = ' + $raw.Length)
    $resp.Dispose()
    if ($statusCode -eq 200) { break }
    Start-Sleep -Seconds (2 * $attempt)
}
[System.IO.File]::WriteAllText((Join-Path $OutDir '04_response_raw.json'), $raw, $utf8)
if ($statusCode -ne 200) { throw ('api returned ' + $statusCode + '; see 04_response_raw.json') }

# ---------- 6) extract content + parse like the tool does ----------
$ser = New-Object System.Web.Script.Serialization.JavaScriptSerializer
$ser.MaxJsonLength = [int]::MaxValue
$obj = $ser.DeserializeObject($raw)
$choice = $obj['choices'][0]
$aiContent = [string]$choice['message']['content']
[System.IO.File]::WriteAllText((Join-Path $OutDir '05_ai_content.json'), $aiContent, $utf8)
$u = $obj['usage']
if ($null -ne $u) {
    Write-Output ('usage = prompt ' + $u['prompt_tokens'] + ' / completion ' + $u['completion_tokens'] + ' / total ' + $u['total_tokens'])
}
Write-Output ('finish_reason = ' + $choice['finish_reason'])

$notes = New-Object 'System.Collections.Generic.List[string]'
$fallbackPage = 1
if ($tablePages.Count -gt 0) { $fallbackPage = $tablePages[0] }
$parserType = T 'MSIFAITOOLS.Services.AiPartResponseParser'
$parts = Inv4 $parserType 'Parse' $null ([string]$aiContent) ([string]$stem) ([int]$fallbackPage) $notes
Write-Output ('parsed rows = ' + $parts.Count)

$sb = New-Object System.Text.StringBuilder
foreach ($n in $notes) { [void]$sb.AppendLine('NOTE ' + $n) }
[void]$sb.AppendLine('')
[void]$sb.AppendLine('No | drawing | page | number | partName | partDrawingNo | frameKind | quantity | basicSize | cutSize | partCode | verify | remark')
$i = 0
foreach ($p in $parts) {
    $i++
    $pt = $p.GetType()
    $v = @{}
    foreach ($nm in @('DrawingNo', 'Page', 'Number', 'PartName', 'PartDrawingNo', 'FrameKind', 'Quantity', 'BasicSize', 'CutSize', 'PartCode', 'VerifyStatus', 'Remark')) {
        $v[$nm] = [string]$pt.GetProperty($nm).GetValue($p, $null)
    }
    [void]$sb.AppendLine(('{0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} | {9} | {10} | {11} | {12}' -f $i,
        $v['DrawingNo'], $v['Page'], $v['Number'], $v['PartName'], $v['PartDrawingNo'], $v['FrameKind'],
        $v['Quantity'], $v['BasicSize'], $v['CutSize'], $v['PartCode'], $v['VerifyStatus'], $v['Remark']))
}
[System.IO.File]::WriteAllText((Join-Path $OutDir '06_parsed_rows.txt'), $sb.ToString(), $utf8)
Write-Output ''
Write-Output $sb.ToString()
Write-Output ('output dir = ' + $OutDir)
Write-Output 'DONE'

