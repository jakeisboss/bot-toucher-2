$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
function Invoke-DotNet {
    $PSNativeCommandUseErrorActionPreference = $false
    $lines = @(& dotnet @args 2>&1)
    $result = $LASTEXITCODE
    $lines | ForEach-Object { Write-Host $_ }
    if ($result -ne 0) {
        $details = ($lines | Select-Object -Last 80 | ForEach-Object { $_.ToString() }) -join "`n"
        if ($env:GITHUB_ACTIONS -eq 'true') {
            $escaped = $details.Replace('%','%25').Replace("`r",'%0D').Replace("`n",'%0A')
            Write-Output "::error title=BT2 targeted Windows check failed::$escaped"
        }
        throw "dotnet failed with exit code $result"
    }
}
Invoke-DotNet build BotToucher2.sln -c Release
Invoke-DotNet test tests/BotToucher2.Core.Tests -c Release --no-build
Invoke-DotNet test tests/BotToucher2.Update.Tests -c Release --no-build
Invoke-DotNet test tests/BotToucher2.Release.Tests -c Release --no-build
Invoke-DotNet run --project tests/BotToucher2.Shell.Smoke -c Release --no-build
Write-Host 'Automated Windows checks passed. Run tools/test-update-windows.ps1 for real process transactions, then the manual UI checklist.'
