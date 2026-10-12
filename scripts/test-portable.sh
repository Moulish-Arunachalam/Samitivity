#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
DOTNET="${SAMITIVITY_DOTNET:-dotnet}"
"$DOTNET" restore Samitivity.Portable.slnx --locked-mode
"$DOTNET" test tests/Samitivity.Tests/Samitivity.Tests.csproj --no-restore -c Release --logger 'trx;LogFileName=portable.trx'
