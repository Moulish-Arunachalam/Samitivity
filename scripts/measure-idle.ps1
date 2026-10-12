param([int]$Seconds = 60)
$ErrorActionPreference = 'Stop'
$p = Get-Process Samitivity -ErrorAction Stop | Select-Object -First 1
$before = $p.TotalProcessorTime.TotalMilliseconds
$clock = [Diagnostics.Stopwatch]::StartNew()
Start-Sleep -Seconds $Seconds
$p.Refresh()
[pscustomobject]@{
    ProcessId = $p.Id
    IntervalSeconds = $clock.Elapsed.TotalSeconds
    CpuMilliseconds = $p.TotalProcessorTime.TotalMilliseconds - $before
    WorkingSetMiB = [math]::Round($p.WorkingSet64 / 1MB, 2)
    PrivateMiB = [math]::Round($p.PrivateMemorySize64 / 1MB, 2)
    Handles = $p.HandleCount
    Threads = $p.Threads.Count
} | Format-List
