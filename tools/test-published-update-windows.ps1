#requires -Version 7.0
# Explicit disposable proof against the real published 0.1.0 installation.
param([Parameter(Mandatory)][string]$Updater)
$ErrorActionPreference = 'Stop'
trap {
    $details = ($_ | Out-String) + $_.ScriptStackTrace
    $escaped = $details.Replace('%','%25').Replace("`r",'%0D').Replace("`n",'%0A')
    Write-Output "::error title=Published delta verification failed::$escaped"
    throw $_
}
$proof = Join-Path $env:RUNNER_TEMP ('BT2-real-delta-' + [Guid]::NewGuid().ToString('N'))
$root = Join-Path $proof 'installed'
New-Item -ItemType Directory $proof | Out-Null
function Invoke-Helper {
    $PSNativeCommandUseErrorActionPreference = $false
    $lines = @(& $script:Updater @args --root $script:root 2>&1)
    $code = $LASTEXITCODE
    if ($code -ne 0) { throw ($lines -join "`n") }
    return ($lines -join "`n")
}
function Close-ProofApp {
    $lock = Join-Path $script:root 'control/app.instance.lock'
    if (-not (Test-Path $lock)) { return }
    $stream = [System.IO.File]::Open($lock, 'Open', 'Read', 'ReadWrite')
    $reader = [System.IO.StreamReader]::new($stream)
    try { $appPid = [int]$reader.ReadToEnd() } finally { $reader.Dispose() }
    $app = Get-Process -Id $appPid -ErrorAction SilentlyContinue
    if ($app) {
        $limit = [DateTime]::UtcNow.AddSeconds(15)
        while (-not $app.HasExited -and [DateTime]::UtcNow -lt $limit) {
            if ($app.CloseMainWindow()) { break }
            Start-Sleep -Milliseconds 100
            $app.Refresh()
        }
        if (-not $app.WaitForExit(15000)) { throw 'Disposable published app did not close' }
    }
}
try {
    gh release download v0.1.0 --repo jakeisboss/bot-toucher-2 --pattern BotToucher2-0.1.0-bootstrap-win-x64.zip --dir $proof
    if ($LASTEXITCODE -ne 0) { throw 'Could not download published source bootstrap' }
    Expand-Archive (Join-Path $proof 'BotToucher2-0.1.0-bootstrap-win-x64.zip') (Join-Path $proof 'source')
    & (Join-Path $proof 'source/bootstrap.ps1') -Root $root
    if ($LASTEXITCODE -ne 0) { throw 'Published source bootstrap failed' }
    Close-ProofApp
    $offer = Invoke-Helper check | ConvertFrom-Json
    $source = Get-Content -Raw (Join-Path $proof 'source/transition.json') | ConvertFrom-Json
    $fullBytes = ($source.Target | Measure-Object Size -Sum).Sum
    if ($offer.Version -ne '0.1.1' -or $offer.RequiredBytes -ge $fullBytes) { throw 'Published check did not offer a smaller exact-source 0.1.1 delta' }
    foreach ($command in @('install-release', 'rollback', 'install-release')) {
        Invoke-Helper $command | Write-Host
        $status = Invoke-Helper status | ConvertFrom-Json
        $expected = if ($command -eq 'rollback') { '0.1.0' } else { '0.1.1' }
        if ($status.Pending -or $status.Current.Version -ne $expected) { throw "Published $command did not verify $expected" }
        Close-ProofApp
    }
    $evidence = "PASS: exact published 0.1.0 -> 0.1.1 delta, rollback and reapply; required bytes $($offer.RequiredBytes), source full bytes $fullBytes"
    Write-Host $evidence
    Write-Output "::notice title=Published delta verified::$evidence"
} finally { Close-ProofApp }
