# Initial per-user install from an explicitly downloaded/trusted BT2 GitHub Release.
# Does not run tests, check releases, or mutate an existing install automatically.
param([string]$Root = (Join-Path $env:LOCALAPPDATA 'BotToucher2'))
$ErrorActionPreference = 'Stop'
$manifestPath = Join-Path $PSScriptRoot 'transition.json'
$manifest = Get-Content -Raw $manifestPath | ConvertFrom-Json
if ($null -ne $manifest.SourceId) { throw 'Initial bootstrap requires a complete initial manifest' }
$worker = Join-Path ([System.IO.Path]::GetTempPath()) ('BT2-bootstrap-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $worker | Out-Null
if ($manifest.Target.Count -gt 8192) { throw 'Bootstrap inventory too large' }
$seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $manifest.Target) {
    if (-not $seen.Add($entry.Path)) { throw 'Duplicate bootstrap path' }
    $parts = $entry.Path.Split('/')
    if ($parts.Count -lt 2 -or $parts[0] -notin @('app','engine')) { throw 'Invalid bootstrap role' }
    foreach ($part in $parts) {
        if ([string]::IsNullOrWhiteSpace($part) -or $part -in @('.','..') -or $part.EndsWith('.') -or $part.EndsWith(' ') -or $part -match '[<>:"\\|?*\x00-\x1f]' -or $part -match '^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)') { throw 'Unsafe bootstrap path segment' }
    }
    if ($entry.Size -lt 0 -or $entry.Size -gt 536870912) { throw 'Invalid bootstrap file size' }
    if (-not $entry.Path.StartsWith('engine/')) { continue }
    $relative = $entry.Path.Substring(7)
    if ([System.IO.Path]::IsPathRooted($relative) -or $relative.Contains('\') -or $relative.Contains(':') -or $relative.Split('/') -contains '..' -or $relative.Split('/') -contains '.') { throw 'Unsafe bootstrap path' }
    if ($entry.Sha256 -notmatch '^[a-f0-9]{64}$') { throw 'Invalid bootstrap hash' }
    $blob = Join-Path $PSScriptRoot ('assets/' + $entry.Sha256)
    if ((Get-Item $blob).Length -ne $entry.Size -or (Get-FileHash $blob -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.Sha256) { throw 'Bootstrap engine hash/size mismatch' }
    $destination = Join-Path $worker $relative
    New-Item -ItemType Directory ([System.IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
    Copy-Item $blob $destination
    if ((Get-Item $destination).Length -ne $entry.Size -or (Get-FileHash $destination -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.Sha256) { throw 'Copied bootstrap engine failed verification' }
}
$updater = Join-Path $worker 'BotToucher2.Updater.exe'
if (-not (Test-Path $updater)) { throw 'Verified bootstrap engine is missing' }
& $updater install --root $Root --manifest $manifestPath --assets (Join-Path $PSScriptRoot 'assets')
if ($LASTEXITCODE -ne 0) { throw "BT2 install failed. Verified bootstrap retained at $worker" }
Write-Host "BT2 installed for this user at $Root. Bootstrap files retained at $worker until no longer needed."
