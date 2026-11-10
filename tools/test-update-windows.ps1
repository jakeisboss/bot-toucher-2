# Explicit Windows-only integration proof; never called by production startup.
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('BT2-update-proof-' + [Guid]::NewGuid().ToString('N'))
$installRoot = Join-Path $testRoot 'installed'
New-Item -ItemType Directory $testRoot | Out-Null
function Run-DotNet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed: $args" }
}
function Run-Updater {
    & $script:seedUpdater @args --root $script:installRoot
    if ($LASTEXITCODE -ne 0) { throw "Updater failed: $args" }
}
function Get-UpdateStatus {
    $text = & $script:seedUpdater status --root $script:installRoot
    if ($LASTEXITCODE -ne 0) { throw 'Status failed' }
    return ($text | ConvertFrom-Json)
}
function Close-TestApp {
    $pidFile = Join-Path $script:installRoot 'control/app.instance.lock'
    if (-not (Test-Path $pidFile)) { return }
    $pidStream = [System.IO.File]::Open($pidFile, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    $pidReader = [System.IO.StreamReader]::new($pidStream)
    try { $appPid = [int]$pidReader.ReadToEnd() } finally { $pidReader.Dispose() }
    $app = Get-Process -Id $appPid -ErrorAction SilentlyContinue
    if ($null -eq $app) { return }
    $limit = [DateTime]::UtcNow.AddSeconds(15)
    while (-not $app.HasExited -and [DateTime]::UtcNow -lt $limit) {
        if ($app.CloseMainWindow()) { break }
        Start-Sleep -Milliseconds 100
        $app.Refresh()
    }
    if (-not $app.WaitForExit(15000)) { throw 'Test app did not close cleanly' }
}
try {
    foreach ($version in @('0.1.0','0.1.1')) {
        $folder = Join-Path $testRoot $version
        Run-DotNet publish src/BotToucher2.App -c Release -r win-x64 --self-contained true -p:Version=$version -o (Join-Path $folder 'app')
        Run-DotNet publish src/BotToucher2.Updater -c Release -r win-x64 --self-contained true -p:Version=$version -o (Join-Path $folder 'engine')
        [System.IO.File]::WriteAllText((Join-Path $folder 'app/proof.txt'), $version)
    }
    $script:seedUpdater = Join-Path $testRoot '0.1.0/engine/BotToucher2.Updater.exe'
    $p0 = Join-Path $testRoot 'package0'
    $p1 = Join-Path $testRoot 'package1'
    Run-Updater package --app (Join-Path $testRoot '0.1.0/app') --engine (Join-Path $testRoot '0.1.0/engine') --out $p0 --version 0.1.0
    Run-Updater package --app (Join-Path $testRoot '0.1.1/app') --engine (Join-Path $testRoot '0.1.1/engine') --out $p1 --version 0.1.1 --source (Join-Path $p0 'transition.json')
    Run-Updater install --manifest (Join-Path $p0 'transition.json') --assets (Join-Path $p0 'assets')
    if ((Get-UpdateStatus).Current.Version -ne '0.1.0') { throw 'Initial install identity mismatch' }
    Close-TestApp
    $before = Get-UpdateStatus
    $probe = Join-Path $before.AppDirectory 'proof.txt'
    [System.IO.File]::WriteAllText($probe, 'unknown source bytes')
    & $seedUpdater stage --root $installRoot --manifest (Join-Path $p1 'transition.json') --assets (Join-Path $p1 'assets')
    if ($LASTEXITCODE -eq 0) { throw 'Source mismatch was accepted' }
    [System.IO.File]::WriteAllText($probe, '0.1.0')
    if ((Get-UpdateStatus).Current.InventoryId -ne $before.Current.InventoryId) { throw 'Failed stage changed current install' }
    Run-Updater install --manifest (Join-Path $p1 'transition.json') --assets (Join-Path $p1 'assets')
    if ((Get-UpdateStatus).Current.Version -ne '0.1.1') { throw 'Update identity mismatch' }
    Close-TestApp
    Run-Updater rollback
    if ((Get-UpdateStatus).Current.Version -ne '0.1.0') { throw 'Rollback identity mismatch' }
    Close-TestApp
    Run-Updater install --manifest (Join-Path $p1 'transition.json') --assets (Join-Path $p1 'assets')
    if ((Get-UpdateStatus).Current.Version -ne '0.1.1') { throw 'Reapply identity mismatch' }
    Close-TestApp
    Write-Host "PASS: real Windows install/update/source-mismatch/rollback/reapply and readiness. Evidence retained at $testRoot"
} finally {
    # Only this disposable root was touched. Preserve files for inspection; close only its app.
    if ($script:seedUpdater) { Close-TestApp }
}
