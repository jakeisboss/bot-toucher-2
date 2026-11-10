# Bot Toucher 2 — 0.0

An independent Windows-first WPF shell. Version 0.0 provides two rails, navigation, a central content boundary and built-in observation-only hardware monitoring. Future functionality is represented by clearly labeled destinations; it is not implemented.

**Status:** source implemented; Linux cross-build and portable checks pass. Windows launch, interaction and visual acceptance remain unverified. See [verification](docs/VERIFICATION.md) and the [0.0 handoff](docs/HANDOFF-0.0.md).

## Build and run on Windows

Install the .NET 8 SDK (8.0.428 or a compatible 8.0.4xx patch) and use PowerShell from this checkout:

```powershell
dotnet build BotToucher2.sln -c Release
dotnet test tests/BotToucher2.Core.Tests -c Release
dotnet run --project src/BotToucher2.App -c Release
```

Or run `./tools/check-windows.ps1` to build, run portable checks and launch the targeted Windows shell smoke harness. The smoke executable must exit zero and print its individual PASS lines. It does not replace the manual Windows checks.

To make a self-contained Windows x64 folder:

```powershell
dotnet publish src/BotToucher2.App -c Release -r win-x64 --self-contained true -o artifacts/win-x64
```

Run `artifacts/win-x64/BotToucher2.exe`. Keep the entire published directory together. This build needs no separately installed .NET runtime. It is not an installer or updater.

## Development in this Linux cloud environment

Use the existing checkout; the cloud task is already isolated. Do not create another worktree unless explicitly requested.

```bash
./tools/bootstrap-dotnet.sh
./tools/dotnet.sh restore BotToucher2.sln
./tools/dotnet.sh build BotToucher2.sln -c Release
./tools/dotnet.sh test tests/BotToucher2.Core.Tests -c Release --no-build
```

The bootstrap downloads the pinned official Linux x64 SDK and verifies its SHA-512 before extraction under `/workspace/.tools`. The CLI wrapper keeps first-run data and NuGet packages in that writable directory. In a sandbox that blocks .NET shared-memory mutexes, use the platform's approved command escalation; do not disable verification or change sandbox policy. NuGet access requires `api.nuget.org` and its package CDN destinations permitted by the environment policy. No application secrets or external services are required.

WPF cannot launch on Linux. The compiled Windows smoke harness is intentionally excluded from portable test execution. Cross-building is not Windows launch evidence.

## Shell behavior

- Home, Packet Center, Data, Lab, Settings and Help/About are reachable from the left rail.
- Packet Center has Project, Packets and Activity; Update Center is reachable inside Packets and has a return button.
- Context and Widgets are right-rail surfaces. The built-in monitor sits outside those tabs and keeps sampling even if the rail is hidden.
- Hide/restore either rail with the top buttons; drag or focus a splitter and use arrows to resize. Layout is saved on normal application close.
- Shortcuts: Ctrl+1–4 for Home/Packet Center/Data/Lab, Ctrl+, for Settings, F1 for Help; Ctrl+Shift+L for the left rail and Ctrl+L for the right.
- Preferences live in `%LOCALAPPDATA%/BotToucher2/layout.json`. Invalid/unreadable preferences use defaults and display a nonfatal notice; saving is disabled for that session so the original is preserved. Close the app and manually archive/remove an invalid file if you want a fresh layout next time. Detected external file changes disable saving. If a change races with atomic replacement, the displaced bytes are kept in a `layout.json.conflict-*` backup and its path is reported.
- Telemetry is serial background work. Closing requests cancellation, awaits in-flight sampling and disposes counters before exiting. Unsupported metrics read unavailable; the monitor has no resource controls.

## Structure and boundaries

- `src/BotToucher2.App`: WPF shell, views, monitor presentation and Windows sampler.
- `src/BotToucher2.Core`: shell state/preferences and monitor lifetime/snapshot boundary.
- `tests/BotToucher2.Core.Tests`: targeted portable behavior checks.
- `tests/BotToucher2.Shell.Smoke`: real WPF shell interaction harness for Windows.
- `docs/BT1-REUSE.md`: inspected BT1 reference and adaptation decisions.

There is no universal widget host/runtime, packet loader/runtime, updater, allocator, executor, result system or workload logic. These belong to later versions. 0.1 work requires separate approval.
