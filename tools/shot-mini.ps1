param([string]$out = "shot-mini.png")
# 截主程序里的悬浮小窗：按 WS_EX_TOOLWINDOW 找到它，再用 PrintWindow（不吃桌面背景）
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class MiniShot {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("shcore.dll")] public static extern int SetProcessDpiAwareness(int v);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  public struct RECT { public int Left, Top, Right, Bottom; }
  public static IntPtr Found = IntPtr.Zero;
  public static uint Want;
  static uint Target;
  public static IntPtr Find(uint pid, uint exStyle) {
    Want = pid; Found = IntPtr.Zero; Target = exStyle;
    EnumWindows(Visit, IntPtr.Zero);
    return Found;
  }
  static bool Visit(IntPtr h, IntPtr l) {
    uint pid; GetWindowThreadProcessId(h, out pid);
    if (pid == Want && IsWindowVisible(h) && ((uint)GetWindowLong(h, -20) & Target) == Target) { Found = h; return false; }
    return true;
  }
}
"@
[MiniShot]::SetProcessDpiAwareness(2) | Out-Null
Add-Type -AssemblyName System.Drawing
$p = Get-Process EdgePad -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { "EdgePad 没在运行"; exit }
$h = [MiniShot]::Find([uint32]$p.Id, 0x80)
if ($h -eq [IntPtr]::Zero) { "没找到悬浮小窗（先用 --mini 启动）"; exit }
$r = New-Object MiniShot+RECT
[MiniShot]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.Right - $r.Left
$hgt = $r.Bottom - $r.Top
$bmp = New-Object System.Drawing.Bitmap $w, $hgt
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
[MiniShot]::PrintWindow($h, $hdc, 2) | Out-Null
$g.ReleaseHdc($hdc); $g.Dispose()
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
"printwindow=True saved $out ($w x $hgt)"
