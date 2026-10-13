param([switch]$Publish)
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
function CheckExit { if ($LASTEXITCODE -ne 0) { throw "Build command failed: exit $LASTEXITCODE" } }
dotnet restore Samitivity.slnx --locked-mode; CheckExit
dotnet build Samitivity.slnx --no-restore -c Release; CheckExit
dotnet test tests/Samitivity.Tests/Samitivity.Tests.csproj --no-build -c Release --logger 'trx;LogFileName=portable.trx'; CheckExit
dotnet test tests/Samitivity.Windows.Tests/Samitivity.Windows.Tests.csproj --no-build -c Release --logger 'trx;LogFileName=windows.trx'; CheckExit
if ($Publish) {
    # Publish from a clean staging directory so old DLLs/private bundles cannot leak into a new installer.
    if (Test-Path artifacts/publish) { Remove-Item artifacts/publish -Recurse -Force }
    dotnet publish src/Samitivity.Windows/Samitivity.Windows.csproj -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true -p:PublishSingleFile=false -o artifacts/publish; CheckExit
    Copy-Item scripts/collect-compatibility.ps1 artifacts/publish/
    Copy-Item README.md,INSTALLATION.md,SECURITY.md,THIRD-PARTY-NOTICES.md artifacts/publish/
}
