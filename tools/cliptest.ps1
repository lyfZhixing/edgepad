Add-Type @"
using System;
using System.Runtime.InteropServices;
public class C {
  [DllImport("user32.dll")] public static extern bool ClipCursor(ref RECT r);
  [DllImport("user32.dll")] public static extern bool ClipCursor(IntPtr none);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  public struct POINT { public int X, Y; }
  public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
$orig = New-Object C+POINT
[C]::GetCursorPos([ref]$orig) | Out-Null

$start = New-Object C+POINT
[C]::GetCursorPos([ref]$start) | Out-Null

$r = New-Object C+RECT
$r.Left = $start.X; $r.Top = $start.Y; $r.Right = $start.X + 1; $r.Bottom = $start.Y + 1
$clipped = [C]::ClipCursor([ref]$r)

# 尝试把光标强行移开 300px
[C]::SetCursorPos($start.X + 300, $start.Y + 120) | Out-Null
$after = New-Object C+POINT
[C]::GetCursorPos([ref]$after) | Out-Null

[C]::ClipCursor([IntPtr]::Zero) | Out-Null

# 释放后再移一次，确认限制确实解除
[C]::SetCursorPos($start.X + 120, $start.Y + 60) | Out-Null
$free = New-Object C+POINT
[C]::GetCursorPos([ref]$free) | Out-Null

[C]::SetCursorPos($orig.X, $orig.Y) | Out-Null

$dx = [Math]::Abs($after.X - $start.X)
$dy = [Math]::Abs($after.Y - $start.Y)
Write-Output ("clip=" + $clipped + " 冻结期间位移 dx=" + $dx + " dy=" + $dy + " -> " + $(if ($dx -le 1 -and $dy -le 1) { "FROZEN" } else { "MOVED" }))
$fx = [Math]::Abs($free.X - ($start.X + 120))
$fy = [Math]::Abs($free.Y - ($start.Y + 60))
Write-Output ("解除后位移误差 dx=" + $fx + " dy=" + $fy + " -> " + $(if ($fx -le 2 -and $fy -le 2) { "RELEASED" } else { "STILL-CLIPPED" }))
