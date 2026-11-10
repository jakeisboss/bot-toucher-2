# BT2 0.0 verification

## Observed in this Linux environment

Toolchain: official .NET SDK 8.0.428 (Linux x64), SHA-512 verified against Microsoft's release metadata before installation. No TLS/checksum bypasses. Windows desktop targeting references restored through NuGet.

- Shell/preference tests: 13 initially failed against behavior stubs, then 13 passed.
- Monitor lifetime tests: 4 initially failed against behavior stubs, then 4 passed.
- Review regressions: 9 initially failed (3 incomplete/foreign schema fixtures, 2 atomic file commit cases, 4 VRAM reading cases), then passed after fixes.
- Final combined portable suite: 26 passed, 0 failed, 0 skipped (Release).
- Release solution cross-build: all four projects compile, 0 warnings and 0 errors.
- Self-contained `win-x64` publish: completed; `BotToucher2.exe` identified as PE32+ x86-64 Windows GUI executable.
- Windows shell smoke harness: cross-compiled, **not executed**.

The test runner emits a nonfatal message about `VSTEST_ARTIFACTS_DIRECTORY` falling back to its temporary directory. It does not alter the reported executed test count or exit status.

Reproduce portable checks with `tools/dotnet.sh build BotToucher2.sln -c Release` and `tools/dotnet.sh test tests/BotToucher2.Core.Tests -c Release --no-build`. Publishing command is in the README. Captured build evidence does not establish Windows UI acceptance.

## Required Windows checks — unrun

From a Windows desktop with the pinned SDK, execute:

```powershell
./tools/check-windows.ps1
dotnet run --project src/BotToucher2.App -c Release
```

The smoke harness launches a real WPF window with isolated in-memory layout and application theme resources. It invokes actual shell button/tab handlers and checks destination state, Packet Center nesting/return, both rail toggles, drag-width recording, the WPF keyboard splitter path and width preservation across navigation, minimum-size central width, built-in monitor presence and a preference notice fixture. It does not write user preferences or start real telemetry. It must print PASS lines and exit zero. This is targeted integration coverage, not automated visual approval.

Manual acceptance must also confirm:

1. Launch the real application, not only the smoke window. All named destinations and both right-rail tabs render and remain keyboard reachable.
2. Drag both splitters physically; focus them and use arrow keys. Hide/restore rails using top buttons and shortcuts. Try 980×600, maximized and restored window sizes; long content scrolls, the center remains usable, focus indicators and contrast are readable.
3. CPU changes from sampling to actual measurements; RAM has useful units. GPU/VRAM either display valid hardware data or an honest unavailable state on this machine. Unsupported temperatures remain unavailable.
4. Hide the right rail, wait, restore it and confirm sampling is still active. Switch workspace/right tabs and confirm the monitor is independent of them.
5. Close during sampling and confirm orderly exit without a lingering process. Reopen and confirm navigation, sections, rail visibility and widths restore.
6. With the app closed, archive any existing layout file, put malformed JSON at `%LOCALAPPDATA%/BotToucher2/layout.json`, launch and verify defaults plus the visible notice. Close and verify the invalid bytes remain unchanged. Restore the archived layout afterward. Repeat with an unsupported schema if needed.
7. Leave the app open during sampling and resizing; confirm the UI stays responsive. Capture actual Windows screenshots for review.

No Windows executor is attached here. Application launch, native telemetry behavior, physical resizing/focus, performance and visual acceptance are therefore unverified. No PASS is inferred from cross-compilation.

## Independent source review

A separate reviewer inspected the whole implementation. Four Important findings were addressed: incomplete/foreign JSON preservation, keyboard splitter state capture, truthful VRAM unavailable state, and preservation of displaced external bytes during an atomic-replacement race. Portable regression checks were observed failing before those core fixes, then passing. Keyboard GUI regression coverage compiles but remains unrun on Windows. No Critical or deferred Minor findings were reported.

The preference replacement race has explicit behavior: checks before saving avoid normal conflicts; atomic replacement preserves the actual displaced file in a unique backup. If its bytes differ from the loaded baseline, that backup is retained and its path is displayed. The newly saved layout may occupy `layout.json` in this narrow race, but the external version is recoverable and the change is not silent. Filesystems that cannot perform atomic replacement fail saving safely; there is no overwrite-only fallback.
