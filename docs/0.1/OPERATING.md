# BT2 0.1 operating and release format

0.1 contains shell/UI refinements and one manual updater only. No widget, packet or execution systems.

## Windows build and explicit validation

Use .NET SDK 8.0.428 from the repository root:

```powershell
dotnet build BotToucher2.sln -c Release
./tools/check-windows.ps1
./tools/test-update-windows.ps1
```

The first script runs the three targeted test projects and real WPF shell smoke. The second builds disposable 0.1.0/0.1.1 test bundles and runs actual initial install, exact-source mismatch refusal, update, rollback and reapply with production app readiness. It touches only a GUID-named temporary root and preserves its evidence. Neither script runs during normal app launch. All Windows execution is UNVERIFIED until run on Windows; Linux builds are not acceptance evidence.

Manual Windows checks: Home information reflects actual version/source/path data; real startup/hardware/preference/update events appear on Home and Packet Center Activity; both rails and splitters work at minimum/maximized/restored sizes; dark controls/focus/selection are readable; Settings is the only update workflow. Check performs no payload download. During explicit update, normal app shutdown drains hardware first, helper waits for parent exit, candidate is read-only until commit. While that critical section is active, normal candidate close is deferred; helper death must show a recovery message and permit safe exit without saving state. Test helper termination before and after readiness and recover the journal explicitly. Verify GUI handoff errors remain visible and prior verified app relaunches when safe.

## Initial installation

Release assets are the distribution authority; source ZIPs are not executable installations. A normal compiled distribution contains a bootstrap ZIP with:

- `transition.json`: complete source-null initial manifest.
- `assets/<sha256>`: unique full replacement blobs; identical app/engine runtime content is stored once.
- `bootstrap.ps1`: reconstructs/verifies a standalone updater engine, then calls its explicit install operation.

Download only from this project's GitHub Release, extract the whole bootstrap ZIP, inspect/run `bootstrap.ps1` from PowerShell. Default root is `%LOCALAPPDATA%/BotToucher2`; no administrator elevation is requested. A loose 0.0 ZIP is not adopted as a trusted source. Existing 0.0 root `layout.json` is inspected/imported safely; original bytes remain retained. Candidate initialization reads/migrates legacy preferences in memory before readiness; persistence happens after commit and fails nonfatally with saving disabled if necessary.

## Manual updater commands

Invoke the selected `install/versions/<inventory-id>/engine/BotToucher2.Updater.exe`, or an independently verified Release bootstrap copy if app/engine files are damaged. The executable is a frontend to the exact same `BotToucher2.Update` library used by the app frontend.

```powershell
BotToucher2.Updater.exe status
BotToucher2.Updater.exe check
BotToucher2.Updater.exe install-release
BotToucher2.Updater.exe stage --manifest transition.json --assets assets
BotToucher2.Updater.exe apply --stage <opaque-stage-id>
BotToucher2.Updater.exe rollback
BotToucher2.Updater.exe recover
BotToucher2.Updater.exe full-recover --manifest initial-transition.json --assets assets
BotToucher2.Updater.exe launch
```

Use `--root <directory>` for an explicitly chosen/disposable root. `stage` returns an opaque manifest-bound ID; do not substitute a filesystem path. `install` combines explicit stage/apply for local release files. `install-release` explicitly fetches metadata and required assets and installs the compatible release. It is not called automatically. Close BT2 before manual apply/rollback, or use the in-app workflow which hands off the parent PID and orderly shutdown. The engine refuses activation while the app-instance lock is owned.

`recover` resolves an interrupted journal. `full-recover` is an explicit source-null verified replacement for a damaged install, preserving evidence and the known-good rollback instead of promoting damaged bytes. Normal updates and rollback never silently relax source verification.

Rollback restores the prior version's state snapshot, preserves current state in a reported displaced-state directory, and refuses another rollback until that evidence is explicitly resolved. After saving it externally, an operator may run:

```powershell
BotToucher2.Updater.exe acknowledge-state --confirm state-saved
```

That permits later rotation; do not acknowledge data you have not saved/resolved. Current, previous and the durable 1.0 application/engine anchor are distinct from quarantine/recovery evidence. Invalid records or unexpected bytes block cleanup rather than authorize recursive deletion of unknown data.

## Paths

Under the per-user root:

| Path | Purpose |
|---|---|
| `install/current.json` | Sole atomic committed active/previous selection |
| `install/versions/<inventory-id>/{app,engine}` | Immutable paired inventories |
| `control/app.instance.lock` | App-instance lease, outside snapshots |
| `control/workers/<owned-id>/engine` | Verified helper copies, outside mutation targets |
| `state/layout.json` | Shell preferences only |
| `updates/staging/<opaque-stage-id>` | Full staged target assembled from local unchanged + required downloaded bytes |
| `updates/transactions` | Journal, readiness-bound receipts and bounded history |
| `updates/snapshots` | Hashed state snapshots |
| `updates/readiness` | Nonce-bound OS frontend readiness markers |
| `updates/rollback` | Displaced-state resolution evidence |
| `updates/recovery`, `quarantine`, `deleting` | Recovery/deletion evidence, not accepted version chain |
| `logs/activity.jsonl` | Bounded real activity stream |

## Release metadata and payload format

Public HTTPS GitHub Releases in `jakeisboss/bot-toucher-2`, one numeric version channel including prereleases. Drafts excluded. No credentials are embedded. Hashes protect byte integrity; this is not a signing system. Redirects are restricted to the repository's Release download paths and GitHub asset CDN hosts.

`bt2-release.json` contains `Schema=1`, `ApplicationId=bot-toucher-2`, `Version`, `Notes`, and `Transitions`. Each transition descriptor contains `SourceId`, `SourceVersion`, `ManifestAsset`, `ManifestSha256`, `ManifestBytes`, `PayloadBytes`. Source-null is initial/full recovery; managed transitions bind exact source inventory/version.

`transition-<source-id-or-initial>.json` contains target `Version`, `SourceVersion`, `SourceId`, `TargetId`, canonical `Source` and `Target` inventories, deterministic `Operations`, `RequiredBytes` and `MinimumBootstrapProtocol`. Inventory entries have `Path` prefixed `app/` or `engine/`, `Size`, `Sha256`. Operations are add/replace/delete/unchanged. Assets are named by target SHA-256; new/replaced identical content transfers once and is reused locally. Deleted/unchanged operations have no remote payload. Binary patches are not implemented.

Opening Updates reads bounded local receipt metadata. Manual Check fetches bounded release index/metadata/manifest only. Install performs full source hash verification, bounded asset streaming and complete staged-target verification. Large application/payload bytes are absent from the open/check path.

## Publication

```powershell
./tools/publish-release.ps1 -Version 0.1.0
# A later 0.1 patch can also include a transition from a previous release manifest:
./tools/publish-release.ps1 -Version 0.1.1 -PreviousManifest previous-transition.json
```

This explicitly builds and publishes compiled blobs, bootstrap ZIP, small metadata and initial/source-specific manifests as GitHub Release assets. Never commit those files to Git. The script uses authenticated `gh`; access must be supplied by the user's environment, never pasted into chat. A manual GitHub Actions workflow `BT2 0.1 Windows validation` can validate on Windows and optionally publish with the repository-provided token. It has no push/startup trigger. In this cloud environment the live API request is denied by the proxy; publication has not been performed and no artifact link is fabricated.
