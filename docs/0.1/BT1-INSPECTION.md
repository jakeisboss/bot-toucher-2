# BT2 0.1 reference inspection

BT1 is reference material, not authority. BT2 0.0 is the accepted baseline; preserve existing boundaries unless a concrete 0.1 requirement changes them.

## Inspected references

Repository `jakeisboss/bot-toucher`:

- Release source `6c30c11fe60a490ebeac2f81732988595eaaefc6` (`release-0.2.1.3-20261009`).
- Later updater source `8af02004e4a519768d661b93157f06981604182e` (`artifact/bot-toucher-0.2.1.7-rework`).

## Findings before implementation

| Piece | Useful material | Coupling/baggage to avoid |
|---|---|---|
| Core `Services/SelfUpdateService.cs`, later revision | Source/target inventory validation; deterministic ADD/REPLACE/DELETE/UNCHANGED classification; bounded file inventories; SHA-256 commitments; source verification; owned-path/reparse checks; shared cross-process stage lease | AppIdentity/StoragePaths coupling; recovery-version bindings; legacy receipt migration; whole payload materialized as byte arrays; in-place replacement must be reassessed against BT2 transaction design |
| Updater `Program.cs`, release revision | Parent shutdown coordination; stage re-verification; receipts and verified rollback | Independent-looking recovery paths and old application-specific update conventions must not become competing BT2 authorities |
| App `Controls/ApplicationInfoProvider.cs` | Read-only application/version/build/path fields; do not invent source revision or authenticated Bridge state | AppServices, project registry, Bridge and widget summary contracts not needed for an app-owned Home card |
| App `Controls/ActivityFeedPanel.xaml[.cs]` | Bounded event list, timestamp/category/severity, selection/details/copy behavior, Home compact presentation | Card/widget refresh-provider plumbing not needed; don't create a second log authority |
| Core `Services/ActivityLogService.cs` | Serialized append, real JSONL activity, bounded recent event retention | Process diagnostics bootstrap and existing StoragePaths are BT1-specific |
| App `Views/HomeView.xaml`, BT2 `Theme.xaml` | BT1 dark navy/card hierarchy; preserve BT2 rails and central layout | BT2 default WPF TabItem templates can still show bright areas; theme refinement should address actual control templates, not just brush values |

## Existing BT2 constraints

- `ShellState` is schema 1, including the right-rail selection; removing Widgets requires an explicit compatible preference migration without silently overwriting invalid state.
- `ShellPreferences` validates and preserves invalid/external data; retain that behavior for shell and widget layout.
- `HardwareMonitor` already has application lifetime and tested drain-before-dispose; keep it independent of widget lifetime.
- Packet Center's 0.0 Update Center placeholder must link to Settings → Updates, not instantiate another updater.
- The old `bt2-0.0-windows-download` branch contains historical compiled blobs from the previous handoff. Do not add payloads there, rewrite history or delete it as part of this task. All new compiled distribution uses GitHub Release assets.

## Observed access limitation

A fresh read-only `gh api repos/jakeisboss/bot-toucher-2/releases` failed with `Forbidden` in the cloud environment. This does not prove missing end-user credentials or repository visibility. Live metadata and Release publishing must be verified independently; do not silently replace GitHub Releases with binary Git commits. Source can still be committed/pushed with existing Git access.

## Repository visibility evidence

A fresh HTTPS repository-page read returned HTTP 200 and GitHub page metadata `octolytics-dimension-repository_public=true`, `visibilityLabel=Public`. This establishes the proposed public-release distribution assumption without inferring visibility from authenticated Git access. It does not establish Releases API readiness or publication permission.

## Corrected scope

Earlier widget-code inspection is superseded by the user’s scope correction. No widget implementation, contract, loader, messaging, lifecycle, proof or persistence is included in 0.1. Only updater and shell/UI material above is considered for this implementation.
