#!/usr/bin/env bash
set -euo pipefail
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-/workspace/.tools/dotnet-home}"
export NUGET_PACKAGES="${NUGET_PACKAGES:-/workspace/.tools/nuget}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
if command -v dotnet >/dev/null 2>&1; then
  exec dotnet "$@"
fi
exec /workspace/.tools/dotnet/dotnet "$@"
