# Cross-platform regression tests for package.ps1. Uses a fake compiler in an isolated temp workspace.
# This verifies packaging orchestration and privacy guards, NOT Inno syntax or a real installer.
$ErrorActionPreference = 'Stop'
$originalLocation = Get-Location
$originalCi = $env:CI
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('SamitivityPackageTests-' + [Guid]::NewGuid().ToString('N'))
New-Item "$testRoot/scripts", "$testRoot/artifacts/publish", "$testRoot/installer", "$testRoot/src/Samitivity.Windows" -ItemType Directory -Force | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'package.ps1') "$testRoot/scripts/package.ps1"
Set-Content "$testRoot/artifacts/publish/Samitivity.exe" 'test placeholder; never distributed'
Set-Content "$testRoot/src/Samitivity.Windows/Samitivity.Windows.csproj" '<Project><PropertyGroup><Version>0.1.2</Version></PropertyGroup></Project>'
$compiler = Join-Path $testRoot 'fake-compiler.ps1'
function Set-FakeCompiler([string]$Version = '6.7.3', [int]$ProbeExit = 0, [int]$CompileExit = 0, [switch]$MissingBanner) {
    @'
param([string]$FirstArgument, [string]$ProbeFile)
if ($FirstArgument -eq '/O-') {
    if (!(Test-Path $ProbeFile) -or (Get-Content $ProbeFile -Raw) -notmatch 'Output=no') { throw 'Probe must disable output.' }
    __BANNER__
    exit __PROBE_EXIT__
}
New-Item artifacts/installer -ItemType Directory -Force | Out-Null
Set-Content artifacts/installer/test-only.exe 'fake compiler output; not a Windows executable'
exit __COMPILE_EXIT__
'@.Replace('__BANNER__', $(if ($MissingBanner) { "Write-Output 'unrecognized output'" } else { "Write-Output 'Compiler engine version: Inno Setup $Version'" })).Replace('__PROBE_EXIT__', "$ProbeExit").Replace('__COMPILE_EXIT__', "$CompileExit") | Set-Content $compiler
}
Set-FakeCompiler
# Reproduce the real ISCC metadata that broke CI. Engine version must be used.
function Get-Item {
    param([Parameter(Position=0)][string]$Path)
    if ($Path -eq $compiler) { return [pscustomobject]@{ VersionInfo = [pscustomobject]@{ ProductVersion = '0.0.0.0' } } }
    Microsoft.PowerShell.Management\Get-Item -Path $Path
}
$package = "$testRoot/scripts/package.ps1"
$bundle = "$testRoot/private-bundle"
$category = '05c22a8f210d4dabbc9a116a9c236101'
function Reset-Bundle {
    if (Test-Path $bundle) { Remove-Item $bundle -Recurse -Force }
    New-Item "$bundle/$category" -ItemType Directory -Force | Out-Null
    Set-Content "$bundle/$category/synthetic.wav" 'packaging-only fixture, not decoded by these tests'
}
function Expect-Rejection([string]$Name, [string]$Message) {
    $failure = $null
    try { & $package -InnoCompiler $compiler -PrivateBundle $bundle | Out-Null }
    catch { $failure = $_.Exception.Message }
    if (!$failure -or $failure -notlike "*$Message*") { throw "FAIL $Name — expected '$Message', received '$failure'" }
    if (Test-Path "$testRoot/artifacts/publish/BundledVoiceNotes") { throw "FAIL $Name — private staging was not cleaned" }
    Write-Output "PASS $Name"
}
try {
    $env:CI = ''
    New-Item "$testRoot/artifacts/publish/BundledVoiceNotes" -ItemType Directory | Out-Null
    Set-Content "$testRoot/artifacts/publish/BundledVoiceNotes/stale.txt" 'private test data'
    & $package -InnoCompiler $compiler | Out-Null
    if (Test-Path "$testRoot/artifacts/publish/BundledVoiceNotes") { throw 'Public packaging retained a stale private bundle.' }
    Write-Output 'PASS ordinary package removes stale private bundle'
    Reset-Bundle
    & $package -InnoCompiler $compiler -PrivateBundle $bundle | Out-Null
    if (Test-Path "$testRoot/artifacts/publish/BundledVoiceNotes") { throw 'Successful private packaging retained private staging.' }
    Write-Output 'PASS valid private bundle and cleanup'
    Reset-Bundle; Set-Content "$bundle/secret.txt" 'must not be copied'
    Expect-Rejection 'root-file rejection' 'Bundle root'
    Reset-Bundle; New-Item "$bundle/$category/nested" -ItemType Directory | Out-Null
    Expect-Rejection 'nested-directory rejection' 'Nested'
    Reset-Bundle; Set-Content "$bundle/$category/secret.pfx" 'must not be copied'
    Expect-Rejection 'non-audio rejection' 'non-audio'
    Reset-Bundle; Rename-Item "$bundle/$category" '00000000000000000000000000000000'
    Expect-Rejection 'unknown-category rejection' 'three default category'
    Reset-Bundle; $env:CI = 'true'
    Expect-Rejection 'CI private-bundle rejection' 'outside CI'
    $env:CI = ''
    Reset-Bundle
    Set-FakeCompiler -CompileExit 1
    Expect-Rejection 'compiler-failure cleanup' 'Installer compilation failed'
    Reset-Bundle; Set-FakeCompiler -Version '6.7.2'
    Expect-Rejection 'wrong engine version' 'Expected Inno Setup 6.7.3; found 6.7.2'
    Set-FakeCompiler -Version '6.7.30'
    Expect-Rejection 'version prefix is insufficient' 'Expected Inno Setup 6.7.3; found 6.7.30'
    Set-FakeCompiler -MissingBanner
    Expect-Rejection 'missing engine version' 'Could not determine'
    Set-FakeCompiler -ProbeExit 2
    Expect-Rejection 'failed engine probe' 'version probe failed'
    Set-FakeCompiler
    & $package -InnoCompiler $compiler | Out-Null
    Write-Output 'PASS exact engine version accepted despite zero product metadata'
    Set-FakeCompiler
    & $package -InnoCompiler $compiler -AppVersion '0.1.1' | Out-Null
    Write-Output 'PASS release version can be supplied explicitly'
    $badVersion = $null
    try { & $package -InnoCompiler $compiler -AppVersion 'latest' | Out-Null } catch { $badVersion = $_.Exception.Message }
    if (!$badVersion -or $badVersion -notlike '*Invalid installer version*') { throw 'FAIL invalid installer version was accepted' }
    Write-Output 'PASS invalid installer version is rejected'
} finally {
    $env:CI = $originalCi
    Set-Location $originalLocation
    Remove-Item $testRoot -Recurse -Force
}

# All assertions passed if execution reaches this point.
exit 0
