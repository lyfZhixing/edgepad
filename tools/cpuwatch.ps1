param([int]$sec = 10)
$p = Get-Process EdgePad -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { 'EdgePad not running'; exit }
$c0 = $p.TotalProcessorTime.TotalMilliseconds
$w0 = $p.WorkingSet64
$g0 = [GC]::CollectionCount(0)
Start-Sleep -Seconds $sec
$p.Refresh()
$c1 = $p.TotalProcessorTime.TotalMilliseconds
$w1 = $p.WorkingSet64
$cores = [Environment]::ProcessorCount
$ms = $c1 - $c0
$cpuPctOfAll = [math]::Round($ms / ($sec * 1000) * 100, 1)
'window=' + $sec + 's  cpu_ms=' + [math]::Round($ms,0) + '  avg_cpu_all_cores=' + $cpuPctOfAll + '%  of_one_core=' + [math]::Round($ms/($sec*10),1) + '%'
'threads=' + $p.Threads.Count + '  ws_mb=' + [math]::Round($w1/1MB,1) + '  private_mb=' + [math]::Round($p.PrivateMemorySize64/1MB,1)
'gc_gen0_total_process_wide=' + [GC]::CollectionCount(0)
