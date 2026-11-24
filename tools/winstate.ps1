Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class W {
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  public struct RECT { public int L, T, R, B; }
}
"@
$p = Get-Process EdgePad | Select-Object -First 1
$h = $p.MainWindowHandle
"handle=$h visible=" + [W]::IsWindowVisible($h) + " iconic=" + [W]::IsIconic($h)
$r = New-Object W+RECT
[W]::GetWindowRect($h, [ref]$r) | Out-Null
"rect=$($r.L),$($r.T),$($r.R),$($r.B)"