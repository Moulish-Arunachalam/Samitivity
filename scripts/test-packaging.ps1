# Cross-platform regression tests for package.ps1. Uses a fake compiler in an isolated temp workspace.
# This verifies packaging orchestration and privacy guards, NOT Inno syntax or a real installer.
$ErrorActionPreference = 'Stop'
$originalLocation = Get-Location
$originalCi = $env:CI
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('SamitivityPackageTests-' + [Guid]::NewGuid().ToString('N'))
New-Item "$testRoot/scripts", "$testRoot/artifacts/publish", "$testRoot/installer" -ItemType Directory -Force | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'package.ps1') "$testRoot/scripts/package.ps1"
Set-Content "$testRoot/artifacts/publish/Samitivity.exe" 'test placeholder; never distributed'
$compiler = Join-Path $testRoot 'fake-compiler.ps1'
@'
New-Item artifacts/installer -ItemType Directory -Force | Out-Null
Set-Content artifacts/installer/test-only.exe 'fake compiler output; not a Windows executable'
exit 0
'@ | Set-Content $compiler
# Mock only the compiler's version resource. All filesystem inspection remains real.
function Get-Item {
    param([Parameter(Position=0)][string]$Path)
    if ($Path -eq $compiler) { return [pscustomobject]@{ VersionInfo = [pscustomobject]@{ ProductVersion = '6.7.3' } } }
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
    Set-Content $compiler 'exit 1'
    Expect-Rejection 'compiler-failure cleanup' 'Installer compilation failed'
} finally {
    $env:CI = $originalCi
    Set-Location $originalLocation
    Remove-Item $testRoot -Recurse -Force
}
