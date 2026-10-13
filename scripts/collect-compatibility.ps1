# Read-only collection: no administrator rights, registry changes, audio, config or filenames collected.
param([string]$OutputDirectory = (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Samitivity-diagnostics'))
$ErrorActionPreference = 'Stop'
New-Item $OutputDirectory -ItemType Directory -Force | Out-Null
$os = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
$exe = Join-Path $env:LOCALAPPDATA 'Programs\Samitivity\Samitivity.exe'
[ordered]@{
    ProductName = $os.ProductName
    DisplayVersion = $os.DisplayVersion
    ReleaseId = $os.ReleaseId
    Build = $os.CurrentBuildNumber
    Revision = $os.UBR
    InstallationType = $os.InstallationType
    Is64BitOS = [Environment]::Is64BitOperatingSystem
    PowerShellVersion = $PSVersionTable.PSVersion.ToString()
    ApplicationVersion = $(if (Test-Path $exe) { (Get-Item $exe).VersionInfo.ProductVersion } else { 'Not installed at default location' })
} | ConvertTo-Json | Set-Content (Join-Path $OutputDirectory 'environment.json')
$data = Join-Path $env:LOCALAPPDATA 'LittleNotes'
foreach ($leaf in @('performance.tsv','performance.tsv.previous','last-error.txt')) {
    $path = Join-Path (Join-Path $data 'logs') $leaf
    if (Test-Path $path) { Copy-Item $path (Join-Path $OutputDirectory $leaf) -Force }
}
$startup = Join-Path $data 'startup-status.txt'
if (Test-Path $startup) { Copy-Item $startup $OutputDirectory -Force }
Write-Output "Diagnostics copied to $OutputDirectory. Review before sharing. No recordings or configuration were copied."
