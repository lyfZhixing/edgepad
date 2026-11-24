param([string]$out = "shot.png")
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class W {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
[W]::SetProcessDPIAware() | Out-Null
$p = Get-Process EdgePad -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $p) { Write-Output "no-window"; exit 1 }
$h = $p.MainWindowHandle
[W]::ShowWindow($h, 5) | Out-Null
Start-Sleep -Milliseconds 500
$r = New-Object W+RECT
[W]::GetWindowRect($h, [ref]$r) | Out-Null
$wd = $r.Right - $r.Left
$ht = $r.Bottom - $r.Top
$bmp = New-Object System.Drawing.Bitmap $wd, $ht
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
$ok = [W]::PrintWindow($h, $hdc, 2)
$g.ReleaseHdc($hdc)
$g.Dispose()
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output ("printwindow=" + $ok + " saved " + $out + " " + $wd + "x" + $ht)
