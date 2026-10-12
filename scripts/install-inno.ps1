# Build-host tooling only. Never distributed as an app runtime prerequisite.
$ErrorActionPreference = 'Stop'
$version = '6.7.3'
$dest = Join-Path $PSScriptRoot '../.tools/inno'
New-Item $dest -ItemType Directory -Force | Out-Null
$download = Join-Path $env:TEMP "Samitivity-innosetup-$version.exe"
Invoke-WebRequest "https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-$version.exe" -OutFile $download
$expectedHash = '9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732'
if ((Get-FileHash $download -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Inno Setup SHA-256 verification failed.' }
$signature = Get-AuthenticodeSignature $download
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Pyrsys B\.V\.') { throw 'Inno Setup publisher verification failed.' }
$p = Start-Process $download -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CURRENTUSER',"/DIR=`"$dest`"") -Wait -PassThru
if ($p.ExitCode -ne 0) { throw "Inno Setup tool installation failed: $($p.ExitCode)" }
Remove-Item $download
Write-Host "Verified Inno Setup $version installed at $dest"
