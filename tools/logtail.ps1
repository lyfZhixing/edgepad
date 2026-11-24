param([int]$n = 60)
$p = Join-Path $env:APPDATA 'EdgePad\edgepad.log'
if (-not (Test-Path $p)) { 'no log at ' + $p; exit }
$f = Get-Item $p
'size=' + $f.Length + '  mtime=' + $f.LastWriteTime
Get-Content $p -Tail $n
