#!/usr/bin/env bash
# Cloud Linux x64 bootstrap. Official SDK artifact, verified before extraction.
set -euo pipefail
sdk_dir="${BT2_DOTNET_DIR:-/workspace/.tools/dotnet}"
if [[ -x "$sdk_dir/dotnet" ]] && [[ "$("$sdk_dir/dotnet" --version)" == "8.0.428" ]]; then
  exit 0
fi
staging_dir="$(mktemp -d)"
trap 'rm -rf "$staging_dir"' EXIT
curl -fsSL --retry 2 'https://builds.dotnet.microsoft.com/dotnet/Sdk/8.0.428/dotnet-sdk-8.0.428-linux-x64.tar.gz' -o "$staging_dir/sdk.tar.gz"
printf '%s  %s\n' '33212bb99a2bf003973d41106c7e8fe94208a12958ff601d4a476be379403c85a7dc9183a6ec64dfb44ded7102965be09f35add1758e68e9251bae7891d7b7bf' "$staging_dir/sdk.tar.gz" | sha512sum --check --status
mkdir -p "$sdk_dir"
tar -xzf "$staging_dir/sdk.tar.gz" -C "$sdk_dir"
printf 'Installed verified .NET SDK 8.0.428 at %s\n' "$sdk_dir"
