#requires -Version 7.0
# Explicit release publication only. Requires authenticated gh with release-write permission.
param([string]$Version = '0.1.0', [string]$PreviousManifest)
$ErrorActionPreference = 'Stop'
trap {
    if ($env:GITHUB_ACTIONS -eq 'true') {
        $details = ($_ | Out-String) + $_.ScriptStackTrace
        $escaped = $details.Replace('%','%25').Replace("`r",'%0D').Replace("`n",'%0A')
        Write-Output "::error title=BT2 release publication failed::$escaped"
    }
    throw $_
}
Set-Location (Join-Path $PSScriptRoot '..')
if ($Version -notmatch '^0\.1\.[0-9]+$') { throw 'This release script is limited to approved 0.1 versions' }
function Run-DotNet {
    $PSNativeCommandUseErrorActionPreference = $false
    $lines = @(& dotnet @args 2>&1)
    $result = $LASTEXITCODE
    $lines | ForEach-Object { Write-Host $_ }
    if ($result -ne 0) { throw ("dotnet failed: $args`n" + (($lines | Select-Object -Last 60) -join "`n")) }
}
$output = Join-Path (Get-Location) ('artifacts/release-' + $Version + '-' + [Guid]::NewGuid().ToString('N'))
Run-DotNet publish src/BotToucher2.App -c Release -r win-x64 --self-contained true "-p:Version=$Version" -o (Join-Path $output 'app')
Run-DotNet publish src/BotToucher2.Updater -c Release -r win-x64 --self-contained true "-p:Version=$Version" -o (Join-Path $output 'engine')
$updater = Join-Path $output 'engine/BotToucher2.Updater.exe'
$full = Join-Path $output 'full'
& $updater package --app (Join-Path $output 'app') --engine (Join-Path $output 'engine') --out $full --version $Version
if ($LASTEXITCODE -ne 0) { throw 'Initial package generation failed' }
$metadata = Get-Content -Raw (Join-Path $full 'bt2-release.json') | ConvertFrom-Json
$upload = Join-Path $output 'upload'
New-Item -ItemType Directory $upload | Out-Null
Copy-Item (Join-Path $full 'assets/*') $upload
Copy-Item (Join-Path $full 'transition-initial.json') $upload
Copy-Item tools/bootstrap.ps1 $full
$bootstrap = Join-Path $upload ('BotToucher2-' + $Version + '-bootstrap-win-x64.zip')
Compress-Archive -Path (Join-Path $full 'assets'),(Join-Path $full 'transition.json'),(Join-Path $full 'bootstrap.ps1') -DestinationPath $bootstrap
if ($PreviousManifest) {
    $delta = Join-Path $output 'delta'
    & $updater package --app (Join-Path $output 'app') --engine (Join-Path $output 'engine') --out $delta --version $Version --source $PreviousManifest
    if ($LASTEXITCODE -ne 0) { throw 'Delta package generation failed' }
    $deltaMetadata = Get-Content -Raw (Join-Path $delta 'bt2-release.json') | ConvertFrom-Json
    $metadata.Transitions = @($metadata.Transitions) + @($deltaMetadata.Transitions)
    Copy-Item (Join-Path $delta $deltaMetadata.Transitions[0].ManifestAsset) $upload
    Copy-Item (Join-Path $delta 'assets/*') $upload -Force
    $source = Get-Content -Raw $PreviousManifest | ConvertFrom-Json
    $packet = Join-Path $upload ('BotToucher2-' + $source.Version + '-to-' + $Version + '-update.zip')
    Compress-Archive -Path (Join-Path $delta 'assets'),(Join-Path $delta 'transition.json') -DestinationPath $packet
    $targetBytes = ($metadata.Transitions | Select-Object -First 1).PayloadBytes
    [ordered]@{ SourceVersion=$source.Version; TargetVersion=$Version; SourceId=$source.TargetId; FullPayloadBytes=$targetBytes; DeltaBytes=$deltaMetadata.Transitions[0].PayloadBytes; PacketBytes=(Get-Item $packet).Length } | ConvertTo-Json | Set-Content -Encoding utf8NoBOM (Join-Path $upload 'delta-evidence.json')
}
$metadata.Notes = 'Installed/rollback version records and cohesive dark navy shell. Updater semantics unchanged.'
$metadata | ConvertTo-Json -Depth 12 | Set-Content -Encoding utf8NoBOM (Join-Path $upload 'bt2-release.json')
$notes = Join-Path $output 'notes.md'
"BT2 ${Version}: installed/rollback state display and dark navy shell refinements. Initial bootstrap ZIP is for new installs. Existing managed installs download changed/new assets only. Windows acceptance results must be recorded separately." | Set-Content $notes
# Keep the release hidden until every asset exists; batch short names for Windows command-line limits.
$tag = 'v' + $Version
& gh release create $tag --repo jakeisboss/bot-toucher-2 --target (git rev-parse HEAD) --title ('Bot Toucher 2 ' + $Version) --notes-file $notes --prerelease --draft
if ($LASTEXITCODE -ne 0) { throw 'Draft release creation failed; no Git binary fallback is permitted' }
Push-Location $upload
try {
    $files = @(Get-ChildItem -File | ForEach-Object Name)
    for ($offset = 0; $offset -lt $files.Count; $offset += 20) {
        $last = [Math]::Min($offset + 19, $files.Count - 1)
        $batch = @($files[$offset..$last])
        & gh release upload $tag --repo jakeisboss/bot-toucher-2 @batch
        if ($LASTEXITCODE -ne 0) { throw 'Asset upload failed; draft retained hidden for inspection' }
    }
} finally { Pop-Location }
& gh release edit $tag --repo jakeisboss/bot-toucher-2 --draft=false --prerelease
if ($LASTEXITCODE -ne 0) { throw 'Release publication failed; uploaded draft retained, no Git binary fallback' }
Write-Host ('Published https://github.com/jakeisboss/bot-toucher-2/releases/tag/' + $tag)
