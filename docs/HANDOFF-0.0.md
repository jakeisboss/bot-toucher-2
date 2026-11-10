# Bot Toucher 2 — 0.0 handoff

**Overall: source implementation delivered; 0.0 acceptance is incomplete pending actual Windows execution. No 0.1 work was started.**

## 1. What was created

An independent .NET 8 / WPF solution in the existing `jakeisboss/bot-toucher-2` repository:

- `src/BotToucher2.App`: application startup/shutdown, `MainWindow`, dark resources, central destination surfaces, Packet Center, shell Settings/Help, built-in hardware monitor and Windows native sampler.
- `src/BotToucher2.Core`: validated `ShellState`, preservation-first `ShellPreferences`/atomic commit, telemetry snapshot records/formatting and a serial application-lifetime monitor.
- `tests/BotToucher2.Core.Tests`: 26 portable behavior cases.
- `tests/BotToucher2.Shell.Smoke`: targeted Windows WPF integration executable, compiled but unrun.
- `tools`: verified SDK bootstrap, cloud CLI wrapper and Windows check script.

The central workspace is only a normal content boundary. It is not a universal widget host. The monitor runs independently of view/rail visibility; cancellation and draining precede counter disposal.

## 2. What was reused from BT1

Reference commit `6c30c11fe60a490ebeac2f81732988595eaaefc6` (`release-0.2.1.3-20261009`): hardware snapshot records, useful Windows telemetry sampling, dark palette, three-column rail/splitter layout ideas, navigation names and Packet Center hierarchy. These fit 0.0 without carrying workload authority. Exact paths and adaptations are in [BT1-REUSE.md](BT1-REUSE.md).

## 3. What was intentionally not reused

AppServices and storage coupling, Bridge, project checklist behavior, packet/workload views, CPU execution controls, results widgets, widget runtime/library and updater implementation. The old monitor’s card refresh provider and view timers were replaced by an app-owned lifetime. BT1 policies were inspected as reference, not imported as BT2 authority.

## 4. What changed

The previously README-only repository now contains the solution/projects above. Important entry points are `App.xaml.cs`, `MainWindow.xaml[.cs]`, `Views/PacketCenterView.xaml[.cs]`, `Controls/HardwareMonitorPanel.xaml[.cs]`, `Hardware/WindowsHardwareSampler.cs`, and the Core `Shell`/`Hardware` folders. The approved design status was changed to the exact requested text before implementation; the implementation plan, provenance and verification documents record subsequent work.

Review corrections reject incomplete/foreign preference documents, retain/report displaced bytes in an atomic-replacement race, record keyboard as well as drag splitter widths, and avoid claiming VRAM availability without actual usage or capacity.

## 5. Verification performed

- Clean Release solution cross-build: PASS, four projects, 0 warnings, 0 errors.
- Portable Release tests: PASS, 26 passed / 0 failed / 0 skipped. Test-first evidence: original 13 preference checks, original 4 lifetime checks and 9 review regression cases each failed before implementation/correction and passed afterward.
- Self-contained Windows x64 publish: PASS. Native `.exe` is a Windows PE32+ x86-64 GUI executable.
- Whole-branch independent source review: performed; four Important findings addressed, no Critical or deferred Minor findings.
- Windows smoke: compiled, NOT RUN. Actual app launch, physical interactions, live Windows telemetry, responsiveness and visual acceptance: NOT VERIFIED.

Full commands, coverage limits and the Windows checklist are in [VERIFICATION.md](VERIFICATION.md). Portable checks cannot establish WPF acceptance.

## 6. 0.0 Definition of Done

PASS for an existence/structure requirement means source and cross-build evidence. FAIL (unverified) means a required runtime check was not available, rather than an observed Windows failure.

| Requirement | Status | Evidence / remaining check |
|---|---|---|
| BT2 exists as an independent project/repository | PASS | Separate BT2 repository and assembly identities; no BT1 runtime reference |
| Application builds successfully | PASS | Clean Release cross-build, 0 warnings/errors |
| Application launches successfully | FAIL — unverified | Must run on Windows |
| Shell exists and runs | FAIL — unverified | Shell compiles; Windows running behavior unverified |
| Left rail works | FAIL — unverified | Toggle/navigation/splitter handlers implemented; Windows interaction check pending |
| Right rail works | FAIL — unverified | Context/Widgets, toggle and splitter implemented; Windows interaction check pending |
| Central widget/test workspace exists | PASS | Home/Lab central content surfaces compile; no widget runtime |
| Built-in hardware monitor is present | PASS | Separate app-owned panel and sampler; native readings unverified |
| Every planned tab/navigation destination exists | PASS | Six primary destinations, Project/Packets/Activity, Update Center, Context/Widgets |
| Navigation between shell surfaces works | FAIL — unverified | Windows smoke covers handlers but has not run |
| Surfaces ready for later functionality | PASS | Clear content/view boundary, independent from future subsystems |
| No workload/test implementation required | PASS | No workload or domain-specific test behavior added |
| No premature updater/widget runtime/packet runtime/allocator | PASS | Those systems are absent; placeholders only |
| Necessary actual 0.0 checks pass | FAIL — unverified | Portable checks pass; required Windows checks outstanding |
| Structure supports 0.1 without whole-app reorganization | PASS | App/Core boundaries, navigation/layout ownership and documentation are explicit |

Additional required Windows verification: resize/restore/maximize and splitter keyboard/drag behavior; readable contrast/focus; no visible UI blocking; native monitor rendering and shutdown. These remain unverified.

## 7. Known issues and limitations

- No Windows executor was available. Do not call 0.0 accepted or complete until Windows build/launch, targeted smoke and manual checks pass.
- The smoke harness exercises `MainWindow` under an isolated generic WPF application. It does not execute the production `App` lifetime, real telemetry or preference file UI recovery; the manual real-app checklist covers these.
- GPU/VRAM depend on Windows counters and registry information; unavailable is an intentional honest state. Temperatures have no trusted provider and remain unavailable. No resource-control policy exists.
- Native counter enumeration cannot be forcibly interrupted safely. Normal close requests cancellation and awaits the in-flight native sample before disposal.
- Invalid preferences are preserved and saving stays disabled for that session. Repair/archive them manually with the app closed. A narrow concurrent-replacement race can leave the newly saved layout at the main path; the displaced external bytes are retained in a uniquely named conflict backup and its path is shown. There is no silent discard.
- The test runner prints a nonfatal temporary-directory fallback notice; executed results remain 26/26 with exit zero.
- The published folder is a self-contained x64 build, not an installer or updater. Generated artifacts are ignored by Git.

## 8. 0.1 readiness

| Future system | Structural readiness |
|---|---|
| Updater | An Update Center destination exists; updater services, trust and apply flow remain undefined/unimplemented |
| Universal blank widget host | Central content boundary and Widgets surface exist; the host itself is intentionally absent |
| Widget runtime | Presentation and background monitoring are separated; runtime contracts still need 0.1 design |
| Widget messaging/actions | No competing message/action authority exists; contracts are intentionally undefined |
| App-owned widget layout | Shell owns rail/navigation preferences; later widget placement/size/visibility can be added under app ownership without moving workload logic into the shell |

These are extension boundaries, not a claim that any 0.1 system is implemented. Windows acceptance of 0.0 is still required. Stop here; 0.1 requires explicit approval.

## 9. Final commit

Branch: `work`. All implementation, tests and handoff documents are committed locally; no push or merge is performed. The final full commit SHA and working-tree status are reported in the accompanying handoff response. Use `git rev-parse HEAD` and `git status --short` to independently confirm them. The Windows artifact records its exact source commit separately in `BUILD-INFO.txt`.
