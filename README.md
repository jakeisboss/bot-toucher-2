# Bot Toucher 2 — 0.1.2

Windows-first WPF shell plus **one manual updater**. 0.1 adds a cohesive dark shell, Context + Hardware Monitor right rail, Home Application Info/Recent Activity, and Settings → Updates. No widget system, DLL loading, packet loading, workloads or execution/results architecture is implemented.

The shell never checks for updates, installs, runs tests or benchmarks at production startup. Hardware observation and bounded local activity/preferences are the active shell services.

## Build and verify

Windows, .NET SDK 8.0.428; use PowerShell 7 for the scripted checks:

```powershell
dotnet build BotToucher2.sln -c Release
./tools/check-windows.ps1
./tools/test-update-windows.ps1
```

The Windows scripts are explicit test entry points. The transaction script uses a disposable temporary root, real app/CLI launches, source-mismatch refusal, update, rollback and reapply. Windows behavior must actually be run there; Linux cross-builds are not acceptance evidence.

Cloud Linux development uses the existing isolated checkout—do not create another worktree unless requested:

```bash
./tools/bootstrap-dotnet.sh
./tools/dotnet.sh build BotToucher2.sln -c Release
./tools/dotnet.sh test tests/BotToucher2.Core.Tests -c Release
./tools/dotnet.sh test tests/BotToucher2.Update.Tests -c Release
./tools/dotnet.sh test tests/BotToucher2.Release.Tests -c Release
```

SDK bootstrap verifies the official artifact checksum before extraction. Where the cloud sandbox blocks .NET shared-memory mutexes, use approved command escalation; do not disable artifact/TLS verification.

## Install and updates

[Download the Windows 0.1.2 bootstrap ZIP](https://github.com/jakeisboss/bot-toucher-2/releases/download/v0.1.2/BotToucher2-0.1.2-bootstrap-win-x64.zip). Extract completely and run `bootstrap.ps1` to install and launch BT2. [0.1.2 Windows verification results](docs/0.1.2/HANDOFF.md) distinguish automated checks from remaining physical acceptance.

Compiled distribution belongs in **GitHub Release assets**, not Git history. A release bootstrap ZIP includes content-addressed assets, a full manifest and `bootstrap.ps1`. Extract the whole trusted release and run that script explicitly for a per-user non-admin install under `%LOCALAPPDATA%/BotToucher2`.

Settings → Updates opens using small local state only. Check manually retrieves metadata/manifest, not payloads. Install verifies exact source bytes, downloads changed/new content only, reconstructs and verifies a complete target, and hands off to the shared external updater after orderly shutdown. Unchanged and deleted files require no payload download. Identical app/engine blobs download once. Binary patches are deferred.

`BotToucher2.Updater.exe` supports explicit status/check/install/stage/apply/rollback/recover/full-recover/launch/package. The selected updater is in `install/versions/<inventory-id>/engine`, separate from `app`; verified workers run outside mutation targets. There is no independent recovery or rollback engine.

Rollback retains one previous known-good version before 1.0; 1.0 becomes a protected anchor. Invalid preferences and mismatched source/receipt/payload bytes are never silently adopted. See [operating and release-format instructions](docs/0.1/OPERATING.md) for commands, state restoration consequences and complete paths.

## Structure

- `BotToucher2.App`: WPF shell and manual update frontend.
- `BotToucher2.Core`: shell preferences, hardware observation/lifetime and real bounded activity.
- `BotToucher2.Update`: shared updater authority, inventory/delta planner, transaction journal/receipts, verification, rollback/recovery and GitHub transport.
- `BotToucher2.Updater`: external OS/process/bootstrap/packaging frontend to that same engine.
- Three portable test projects plus an explicitly launched Windows WPF smoke executable.

[Approved corrected design](docs/0.1/DESIGN.md) · [Authoritative 0.1 DoD](docs/0.1/DEFINITION-OF-DONE.md) · [BT1 inspection/reuse](docs/0.1/BT1-INSPECTION.md) · [Operating instructions](docs/0.1/OPERATING.md) · [0.1 handoff](docs/0.1/HANDOFF.md)

No 0.2 work is authorized or implemented. Final verification results and every DoD status are recorded in the 0.1 handoff.

0.1.2 adds receipt-backed Current / Previous known-good / Last recorded transaction cards and centralized dark navy styling. Existing 0.1.1 installs update through Settings → Updates. [0.1.2 handoff](docs/0.1.2/HANDOFF.md) · [Actual Windows client renders](docs/0.1.2/screenshots/SOURCE.txt).
