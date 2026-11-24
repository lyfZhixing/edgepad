Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$w = 420; $h = 96
$x = 3400; $y = 1958
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size $w, $h))
$bmp.Save("$PWD\docs\edge-pad-toast.png", [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
"saved"