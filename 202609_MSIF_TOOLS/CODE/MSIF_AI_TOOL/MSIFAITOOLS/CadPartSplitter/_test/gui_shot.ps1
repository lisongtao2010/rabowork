# Launch the WinForms GUI, grab a screenshot of its window, then close it.
# Used to eyeball the layout after adding controls.
# Usage: powershell -ExecutionPolicy Bypass -File _test\gui_shot.ps1

param(
    [string]$Exe = '.\bin\Debug\CadPartSplitter.exe',
    [string]$Out = '_test\gui_shot.png'
)

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public struct RECT { public int Left, Top, Right, Bottom; }
public class WinApi {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
}
"@

$proc = Start-Process -FilePath $Exe -PassThru
Start-Sleep -Seconds 4
$proc.Refresh()

[WinApi]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 700

$rect = New-Object RECT
[WinApi]::GetWindowRect($proc.MainWindowHandle, [ref]$rect) | Out-Null
$width  = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top

$bitmap   = New-Object System.Drawing.Bitmap($width, $height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size($width, $height)))
$bitmap.Save((Join-Path (Get-Location) $Out), [System.Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose()
$bitmap.Dispose()

Stop-Process -Id $proc.Id -Force
"Saved $Out ($width x $height)"
