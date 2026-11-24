Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($b.X, $b.Y, 0, 0, (New-Object System.Drawing.Size $b.Width, $b.Height))
$small = New-Object System.Drawing.Bitmap $bmp, ([int]($b.Width/2)), ([int]($b.Height/2))
$small.Save("$PWD\docs\tmp-full.png", [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose(); $small.Dispose()
"screen=" + $b.Width + "x" + $b.Height