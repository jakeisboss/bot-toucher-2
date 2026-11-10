$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
function Invoke-DotNet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" }
}
Invoke-DotNet build BotToucher2.sln -c Release
Invoke-DotNet test tests/BotToucher2.Core.Tests -c Release --no-build
Invoke-DotNet run --project tests/BotToucher2.Shell.Smoke -c Release --no-build
Write-Host 'Automated Windows checks passed. Perform the manual checklist in docs/VERIFICATION.md next.'
