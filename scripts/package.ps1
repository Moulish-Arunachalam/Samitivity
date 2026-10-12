param([string]$InnoCompiler = (Join-Path $PSScriptRoot '../.tools/inno/ISCC.exe'), [string]$PrivateBundle)
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
if (!(Test-Path artifacts/publish/Samitivity.exe)) { throw 'Run scripts/build-windows.ps1 -Publish first.' }
if (!(Test-Path $InnoCompiler)) { throw 'Run scripts/install-inno.ps1 on this Windows build host first.' }
$compilerVersion = (Get-Item $InnoCompiler).VersionInfo.ProductVersion
if ($compilerVersion -notlike '6.7.3*') { throw "Expected Inno Setup 6.7.3; found $compilerVersion" }
# Prevent accidental reuse of a private bundle by a later ordinary/public build.
if (Test-Path artifacts/publish/BundledVoiceNotes) { Remove-Item artifacts/publish/BundledVoiceNotes -Recurse -Force }
try {
    if ($PrivateBundle) {
        if ($env:CI) { throw 'Private voice bundles must be packaged locally, outside CI.' }
        $source = (Resolve-Path $PrivateBundle).Path
        if (!(Test-Path $source -PathType Container)) { throw 'PrivateBundle must be a directory.' }
        $allowedCategories = @('05c22a8f210d4dabbc9a116a9c236101', '05c22a8f210d4dabbc9a116a9c236102', '05c22a8f210d4dabbc9a116a9c236103')
        $entries = @(Get-ChildItem $source -Force -Recurse) + @(Get-Item $source)
        if ($entries | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'Linked private bundle paths are not allowed.' }
        if (Get-ChildItem $source -File -Force) { throw 'Bundle root must contain only the documented category folders.' }
        $audioCount = 0
        foreach ($folder in Get-ChildItem $source -Directory -Force) {
            if ($folder.Name -notin $allowedCategories) { throw 'Use the three default category GUID folders documented in INSTALLATION.md.' }
            if (Get-ChildItem $folder.FullName -Directory -Force) { throw 'Nested bundle directories are not allowed.' }
            foreach ($file in Get-ChildItem $folder.FullName -File -Force) {
                if ($file.Extension.ToLowerInvariant() -notin @('.m4a','.mp3','.wav')) { throw 'Bundle contains a non-audio file.' }
                $audioCount++
            }
        }
        if ($audioCount -eq 0) { throw 'Private bundle contains no recordings.' }
        Copy-Item $source artifacts/publish/BundledVoiceNotes -Recurse
    }
    & $InnoCompiler installer/Samitivity.iss
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    Get-FileHash artifacts/installer/*.exe -Algorithm SHA256 | Format-Table
} finally {
    if ($PrivateBundle -and (Test-Path artifacts/publish/BundledVoiceNotes)) { Remove-Item artifacts/publish/BundledVoiceNotes -Recurse -Force }
}
