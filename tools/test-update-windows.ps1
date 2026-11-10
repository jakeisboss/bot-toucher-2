#requires -Version 7.0
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
function Read-SharedJson {
    param([string]$Path)
    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, ([System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete))
    $reader = [System.IO.StreamReader]::new($stream)
    try { return ($reader.ReadToEnd() | ConvertFrom-Json) } finally { $reader.Dispose() }
}
function Run-ManagedUpdater {
    param([string]$Executable)
    # Only the invoked CLI uses the managed directory as CWD; the proof shell must
    # remain outside it so it does not itself hold the directory being pruned.
    $start = [System.Diagnostics.ProcessStartInfo]::new($Executable)
    $start.WorkingDirectory = [System.IO.Path]::GetDirectoryName($Executable)
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    for ($i = 0; $i -lt $args.Count; $i++) {
        $start.ArgumentList.Add($args[$i])
        if ($args[$i] -in @('--manifest', '--assets')) {
            $i++
            $start.ArgumentList.Add([System.IO.Path]::GetRelativePath($start.WorkingDirectory, [System.IO.Path]::GetFullPath($args[$i])))
        }
    }
    $start.ArgumentList.Add('--root')
    $start.ArgumentList.Add([System.IO.Path]::GetRelativePath($start.WorkingDirectory, $script:installRoot))
    $caller = [System.Diagnostics.Process]::Start($start)
    try {
        $stdout = $caller.StandardOutput.ReadToEndAsync()
        $stderr = $caller.StandardError.ReadToEndAsync()
        if (-not $caller.WaitForExit(30000)) { $caller.Kill(); throw 'Managed caller did not exit after handoff' }
        if (-not $stdout.Wait(600000) -or -not $stderr.Wait(600000)) { throw 'Managed command output did not close within ten minutes' }
        $acceptedText = $stdout.GetAwaiter().GetResult()
        $errorText = $stderr.GetAwaiter().GetResult()
        if ($caller.ExitCode -ne 0) { throw "Managed updater rejected handoff: $errorText" }
    } finally { $caller.Dispose() }
    $accepted = $acceptedText | ConvertFrom-Json
    if ($accepted.State -ne 'Accepted') { throw 'Managed invocation did not explicitly report handoff acceptance' }
    $ownedCommands = [System.IO.Path]::GetFullPath((Join-Path $script:installRoot 'control/commands')) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $accepted.ResultPath.StartsWith($ownedCommands, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unowned command result path' }
    $request = Get-Content ($accepted.ResultPath.Replace('.result.json', '.request.json')) -Raw | ConvertFrom-Json
    $versions = [System.IO.Path]::GetFullPath((Join-Path $script:installRoot 'install/versions'))
    if ($request.WorkerDirectory.StartsWith($versions, [StringComparison]::OrdinalIgnoreCase)) { throw 'Loaded worker is a mutation target' }
    $deadline = [DateTime]::UtcNow.AddMinutes(10)
    do {
        $result = Read-SharedJson $accepted.ResultPath
        if ($result.State -in @('Completed', 'Failed')) { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($result.State -ne 'Completed' -or $result.ExitCode -ne 0) { throw "Managed command did not complete successfully: $($result | ConvertTo-Json -Compress). Result: $($accepted.ResultPath)" }
    Write-Host "Managed command completed: $($accepted.ResultPath)"
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
    Run-DotNet test tests/BotToucher2.Update.Tests -c Release --filter 'FullyQualifiedName~Status'
    foreach ($version in @('0.1.0','0.1.1','0.1.2')) {
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
    # Invoke the retained version that this transition must prune. Its frontend must exit first.
    $oldEngine = Join-Path $installRoot ('install/versions/' + (Get-UpdateStatus).Previous.InventoryId + '/engine/BotToucher2.Updater.exe')
    $p2 = Join-Path $testRoot 'package2'
    Run-Updater package --app (Join-Path $testRoot '0.1.2/app') --engine (Join-Path $testRoot '0.1.2/engine') --out $p2 --version 0.1.2 --source (Join-Path $p1 'transition.json')
    Run-ManagedUpdater $oldEngine install --manifest (Join-Path $p2 'transition.json') --assets (Join-Path $p2 'assets')
    if ((Get-UpdateStatus).Current.Version -ne '0.1.2' -or (Test-Path $oldEngine)) { throw 'Old managed engine was not safely pruned' }
    Close-TestApp
    # Full recovery quarantines the current bundle containing the invoking frontend.
    $full = Join-Path $testRoot 'package2-full'
    Run-Updater package --app (Join-Path $testRoot '0.1.2/app') --engine (Join-Path $testRoot '0.1.2/engine') --out $full --version 0.1.2
    $current = Get-UpdateStatus
    [System.IO.File]::WriteAllText((Join-Path $current.AppDirectory 'proof.txt'), 'damaged current')
    Run-ManagedUpdater (Join-Path $current.EngineDirectory 'BotToucher2.Updater.exe') full-recover --manifest (Join-Path $full 'transition.json') --assets (Join-Path $full 'assets')
    if ((Get-UpdateStatus).Pending) { throw 'Managed full recovery left a pending transaction' }
    if ((Get-Content (Join-Path (Get-UpdateStatus).AppDirectory 'proof.txt') -Raw) -ne '0.1.2') { throw 'Full recovery did not restore verified bytes' }
    Close-TestApp
    Write-Host "PASS: managed current recovery and retained-engine pruning; status metadata sharing; real Windows install/update/source-mismatch/rollback/reapply and readiness. Evidence retained at $testRoot"
} finally {
    # Only this disposable root was touched. Preserve files for inspection; close only its app.
    if ($script:seedUpdater) { Close-TestApp }
}
