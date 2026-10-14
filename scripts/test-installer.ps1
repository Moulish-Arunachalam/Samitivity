# Destructive only to this fresh CI account's test install; never run on a recipient's account.
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or !$env:RUNNER_TEMP) { throw 'This smoke test requires a disposable GitHub-hosted runner.' }
$data = Join-Path $env:LOCALAPPDATA 'Samitivity'
if (Test-Path $data) { throw 'Refusing to touch existing user data.' }
$install = Join-Path $env:RUNNER_TEMP ('Samitivity-' + [guid]::NewGuid().ToString('N'))
$setup = (Get-Item (Join-Path $PSScriptRoot '../artifacts/installer/*.exe')).FullName
function Run([string]$Exe, [string[]]$Arguments, [int]$Expected = 0) {
    $p = Start-Process -FilePath $Exe -ArgumentList $Arguments -PassThru
    if (!$p.WaitForExit(60000)) { throw "Timed out: $([IO.Path]::GetFileName($Exe))" }
    if ($p.ExitCode -ne $Expected) { throw "Unexpected exit $($p.ExitCode): $([IO.Path]::GetFileName($Exe))" }
}
function Wait-Running([string]$Exe) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    do {
        $p = Get-Process Samitivity -ErrorAction SilentlyContinue | Where-Object Path -eq $Exe | Select-Object -First 1
        if ($p) {
            if (!$p.WaitForInputIdle(15000) -or $p.HasExited) { throw 'Installed application did not enter its Windows message loop.' }
            return
        }
        Start-Sleep -Milliseconds 200 # Bounded test-only wait for Task Scheduler to create the process.
    } while ($clock.Elapsed.TotalSeconds -lt 30)
    throw 'Installer did not launch Samitivity.'
}
# Seed the pre-upgrade storage path before first normal launch.
$legacy = Join-Path $env:LOCALAPPDATA 'LittleNotes' # Legacy migration fixture only.
if (Test-Path $legacy) { throw 'Refusing to touch existing legacy data.' }
New-Item $legacy -ItemType Directory | Out-Null
Copy-Item (Join-Path $PSScriptRoot '../samples/config.example.json') (Join-Path $legacy 'config.json')
Set-Content (Join-Path $legacy 'state.json') '{"Version":1,"Categories":{}}'
$legacyFolder = Join-Path $legacy 'VoiceNotes/05c22a8f210d4dabbc9a116a9c236101'
New-Item $legacyFolder -ItemType Directory -Force | Out-Null
$legacyNote = Join-Path $legacyFolder 'migration.wav'
Copy-Item (Get-ChildItem (Join-Path $PSScriptRoot '../samples/synthetic-*.wav') | Select-Object -First 1).FullName $legacyNote
$legacyHashes = @{}
foreach ($path in @((Join-Path $legacy 'config.json'), (Join-Path $legacy 'state.json'), $legacyNote)) { $legacyHashes[$path] = (Get-FileHash $path).Hash }
$setupArgs = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/DIR=`"$install`"")
Run $setup $setupArgs
$exe = Join-Path $install 'Samitivity.exe'
Wait-Running $exe
Run $exe @('--shutdown')
foreach ($path in $legacyHashes.Keys) {
    $relative = [IO.Path]::GetRelativePath($legacy, $path)
    if ((Get-FileHash (Join-Path $data $relative)).Hash -ne $legacyHashes[$path]) { throw 'Migration changed personal data.' }
    if ((Get-FileHash $path).Hash -ne $legacyHashes[$path]) { throw 'Migration changed the backup.' }
}
if (!(Test-Path (Join-Path $data 'storage-migration-v1.txt'))) { throw 'Migration did not finish.' }
foreach ($file in @('coreclr.dll','hostfxr.dll','System.Windows.Forms.dll')) {
    if (!(Test-Path (Join-Path $install $file))) { throw "Missing self-contained dependency: $file" }
}
Run $exe @('--diagnostic-exit-23') 23
$process = Start-Process $exe -ArgumentList '--background' -PassThru
if (!$process.WaitForInputIdle(15000) -or $process.HasExited) { throw 'Installed application did not enter its Windows message loop.' }
Run $exe @('--shutdown')
if (!$process.WaitForExit(15000)) { throw 'Installed application did not shut down.' }
# Seed valid version-1 data, then exercise the real install-over-existing and uninstall paths.
$configPath = Join-Path $data 'config.json'
$config = Get-Content $configPath -Raw | ConvertFrom-Json
$config.Categories[0].Name = 'Synthetic upgrade fixture'
$config | ConvertTo-Json -Depth 10 | Set-Content $configPath
$state = Join-Path $data 'state.json'
Set-Content $state '{"Version":1,"Categories":{}}'
$folder = Join-Path $data ('VoiceNotes/' + ([guid]$config.Categories[0].Id).ToString('N'))
New-Item $folder -ItemType Directory -Force | Out-Null
$fixture = Get-ChildItem (Join-Path $PSScriptRoot '../samples/synthetic-*.wav') | Select-Object -First 1
$note = Join-Path $folder 'synthetic-upgrade.wav'
Copy-Item $fixture.FullName $note
$hashes = @{}
foreach ($path in @($configPath,$state,$note)) { $hashes[$path] = (Get-FileHash $path).Hash }
Run $setup $setupArgs
Wait-Running $exe
Run $exe @('--shutdown')
foreach ($path in $hashes.Keys) { if ((Get-FileHash $path).Hash -ne $hashes[$path]) { throw 'Reinstall changed personal data.' } }
Run (Join-Path $install 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART')
foreach ($path in $hashes.Keys) { if ((Get-FileHash $path).Hash -ne $hashes[$path]) { throw 'Uninstall changed personal data.' } }
Write-Output 'PASS real install, managed startup, bundled runtime, reinstall and uninstall data preservation (Windows Server runner only).'
