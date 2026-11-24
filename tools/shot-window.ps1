Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class D { [DllImport("shcore.dll")] public static extern int SetProcessDpiAwareness(int v); }
"@
[D]::SetProcessDpiAwareness(2) | Out-Null
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$x = 1000; $y = 470; $w = 1840; $h = 1210
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size $w, $h))
$bmp.Save("$PWD\docs\tmp-screen.png", [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
"saved"