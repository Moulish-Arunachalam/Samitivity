param([Parameter(Mandatory)][string]$Executable, [int]$ObserveSeconds = 145)
# Run interactively as the recipient, not elevated. Creates only a temporary diagnostic task.
$ErrorActionPreference = 'Stop'
$Executable = (Resolve-Path $Executable).Path
$sid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$name = "Samitivity-RecoveryProbe-$([Guid]::NewGuid().ToString('N'))"
$service = New-Object -ComObject 'Schedule.Service'
$service.Connect()
$folder = $service.GetFolder('\')
$definition = $service.NewTask(0)
$definition.RegistrationInfo.Description = 'Temporary Samitivity nonzero-exit restart validation; no audio.'
$definition.Principal.UserId = $sid
$definition.Principal.LogonType = 3 # InteractiveToken
$definition.Principal.RunLevel = 0
$definition.Settings.ExecutionTimeLimit = 'PT0S'
$definition.Settings.DisallowStartIfOnBatteries = $false
$definition.Settings.StopIfGoingOnBatteries = $false
$definition.Settings.RestartCount = 2
$definition.Settings.RestartInterval = 'PT1M'
$action = $definition.Actions.Create(0)
$action.Path = $Executable
$action.Arguments = '--diagnostic-exit-23'
$task = $folder.RegisterTaskDefinition($name, $definition, 6, $null, $null, 3)
$observed = [System.Collections.Generic.HashSet[datetime]]::new()
$rows = [System.Collections.Generic.List[object]]::new()
try {
    $null = $task.Run($null)
    $deadline = (Get-Date).AddSeconds($ObserveSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 2
        $current = $folder.GetTask($name)
        if ($current.LastRunTime.Year -gt 2000 -and $observed.Add($current.LastRunTime)) {
            $row = [pscustomobject]@{ ObservedAt = (Get-Date).ToString('o'); LastRunTime = $current.LastRunTime; Result = $current.LastTaskResult; State = $current.State }
            $rows.Add($row); $row | Format-List | Out-Host
        }
    }
    $current = $folder.GetTask($name)
    $report = [pscustomobject]@{ OS = [Environment]::OSVersion.VersionString; UserSid = $sid; DistinctRuns = $observed.Count; LastResult = $current.LastTaskResult; Passed = ($observed.Count -ge 2 -and $current.LastTaskResult -eq 23); Observations = $rows }
    New-Item (Join-Path $PSScriptRoot '../artifacts') -ItemType Directory -Force | Out-Null
    $report | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $PSScriptRoot '../artifacts/task-recovery.json')
    if (!$report.Passed) { throw 'FAIL: nonzero-exit restart was not observed. Keep crash recovery marked unverified/unavailable; inspect Task Scheduler History.' }
    Write-Host 'PASS: at least one scheduled restart after exit code 23 was observed. Test abrupt termination separately.'
} finally {
    $task.Stop(0)
    $folder.DeleteTask($name, 0)
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($service)
}
