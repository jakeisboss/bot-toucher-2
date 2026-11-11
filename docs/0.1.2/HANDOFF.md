# BT2 0.1.2 handoff

## 1. What changed

Small refinement of accepted 0.1.1 (`720301b1ebca9f2a6843c2ec5d44d88bfe43fd40`): structured installed/rollback/transaction records on Settings → Updates, consistent dark navy client surfaces, and version 0.1.2 in assembly, window title and header. No new product systems.

## 2. Significant files

- `src/BotToucher2.App/Views/UpdatesView.cs`: three read-only record cards and the existing rollback action.
- `src/BotToucher2.App/UpdateController.cs`: exposes validated status, last recorded transaction and whether history was read successfully.
- `src/BotToucher2.App/Theme.xaml`: shared system-brush fallbacks and dark scrollbar/thumb, list row, checkbox, separator, tooltip and focus resources.
- `src/BotToucher2.App/MainWindow.xaml[.cs]`: explicitly applies Window style to derived MainWindow; shows actual build version.
- `Directory.Build.props`: 0.1.2.
- `tests/BotToucher2.Shell.Smoke/RefinementChecks.cs`: real-engine record fixtures, failure/busy/corrupt/pending display checks, native control behavior and actual client renders.
- Release workflow/scripts: exact published 0.1.1 source delta and Windows verification; no engine change.

## 3. Rollback/version UI

Current, Previous known-good and Last recorded transaction are separate navy cards. The rollback button and confirmation name the actual retained previous version. Empty installation/history, failed targets, pending transactions and unavailable/corrupt metadata are explicit. An unreadable state is not called an empty installation.

Transaction records show target, outcome, time and ID. No source-version arrow is guessed: the existing history format does not store the source version. This is the implementation ruling; its cost is omission of the optional arrow, while preserving truthful display and compatibility with 0.1.1.

## 4. Exact updater state

`UpdateEngine.StatusAsync()` validates current/referenced receipts and supplies `UpdateStatus.Current.Version`, `Previous.Version`, `Pending` and `DisplacedStateDirectory`. `GetHistoryAsync()` validates the bounded history digest; its last entry supplies `Version`, `Outcome`, `TimestampUtc` and `TransactionId`. Committed maps to “Verified / successful”; failed maps to “Failed / not accepted.” Missing history supplies no sample rows.

“Verification recorded” refers to the updater's install-time verification. Opening the page does not scan installed binaries or rehash payloads. UI eligibility disables operations while metadata/history is unavailable; the same engine still performs all actual verification and policy checks. Preserved-state requirements remain enforced by that engine, not inferred from a folder's presence.

## 5. Theme/resources

The white main surface came from an implicit Window style not being applied to the derived MainWindow; it is now explicit. Existing navy palette, navigation, layout and spacing remain recognizable. Shared brushes/templates cover the actual shell controls, content hosts, tabs, scrollbars, list selection, checkbox states, separators, buttons and keyboard focus. TextBox, ComboBox, ListView and DataGrid controls are not present; no speculative views were introduced.

## 6. BT1 visual references

Inspected retained BT1 HomeView/Application Info/Activity sources and existing provenance in `docs/0.1/BT1-INSPECTION.md`. Reused the navy/background hierarchy, cyan headings, subtle borders and bounded record/card presentation. Did not carry widget placement/refresh services, AppServices coupling or deliberately light popup styling into BT2.

## 7. Verification

- `tools/dotnet.sh build BotToucher2.sln -c Release --no-restore`: PASS, zero warnings/errors.
- Windows `tools/check-windows.ps1`: all 121 automated tests PASS plus WPF smoke/refinement checks.
- New native regressions first failed against 0.1.1 (run 42978963217); busy/unreadable display regressions failed in run 42980379859 and passed after the minimal display fix.
- Windows run [42980759372](https://github.com/jakeisboss/bot-toucher-2/actions/runs/42980759372): complete records/theme/control checks, real disposable install/update/source mismatch/rollback/reapply/recovery/pruning and public index check PASS.
- Final release run [42981502164](https://github.com/jakeisboss/bot-toucher-2/actions/runs/42981502164): all final native tests/smoke, disposable real transactions, Release publication, public metadata-only check, and exact published 0.1.1 → 0.1.2 install/rollback/reapply PASS.
- `git diff 720301b -- src/BotToucher2.Update src/BotToucher2.Core src/BotToucher2.Updater` is empty: no engine/core/CLI production source change.
- Actual WPF client images from compiled source `cfc8de15502b42f12703a81dd92f47a24fd80820` are in [screenshots](screenshots/SOURCE.txt). Home, Lab, Updates, Settings and control-focus renders were inspected. They use disposable updater-generated receipts; the isolated shell does not run hardware sampling. The preference notice in the shell images is an explicit test fixture. These are actual Windows renders, not mockups or Linux cross-build images.

## 8. Intentionally unchanged

One previous known-good before 1.0; existing 1.0 anchor protection; exact source checks; staging, commit/readiness, rollback/reapply and failure recovery; receipt/history formats; manual GitHub Releases updates; app-owned hardware monitor/preferences; navigation and rail/workspace ownership. No new startup checks/tests/downloads or apply-on-close behavior.

## 9. Known limitations

Physical end-user mouse interaction, arbitrary DPI/high-contrast configurations, OS title bars/native dialogs and user visual acceptance are not claimed by client renders. No stable launcher or .btpacket importer was added. The existing version-directory launch/install model is unchanged. Last transaction shows recorded target rather than a fabricated source arrow. Other baseline recovery/evidence-retention limits remain as documented for 0.1.

## 10. Requested requirements

| ID | Requirement | Status / evidence |
|---|---|---|
| R01 | Reports 0.1.2 | PASS — assembly, header, title and native smoke |
| R02 | Visible rollback/installed-version section | PASS — three structured cards, actual render |
| R03 | Correct current version | PASS — validated receipt fixture and empty/busy/error cases |
| R04 | Correct previous known-good, including absence | PASS — real update/rollback fixtures |
| R05 | Rollback button targets real previous version | PASS — label/confirmation/engine action; failed target excluded |
| R06 | Last transaction from real records | PASS — validated bounded history target/outcome/time/ID |
| R07 | No fabricated history or success | PASS — empty, failure, corrupted and pending cases |
| R08 | One previous before 1.0; failed updates preserve it | PASS — unchanged engine plus existing native tests |
| R09 | Existing rollback/reapply behavior preserved | PASS — native disposable proof and exact published 0.1.1 → 0.1.2 rollback/reapply |
| R10 | UI remains consumer; no parallel authority/schema | PASS — no engine/core/CLI source diff |
| R11 | No default white/black normal WPF backgrounds | PASS — all six major client surfaces scanned, actual renders inspected |
| R12 | Centralized tabs/scroll/list/button/check/focus resources | PASS — shared theme, page-scroll and focus behavior verified |
| R13 | Readable theme, disabled/selected/focus states | PASS — actual renders, activity selection and cyan keyboard focus checks |
| R14 | Major surfaces/navigation/rails/window behavior | PASS — existing WPF smoke plus new render/background checks |
| R15 | Windows evidence distinguished from cross-build | PASS — actual Windows execution/images, stated limits |
| R16 | No packets/widgets/workloads/execution/results/new updater/multi-rollback | PASS — bounded source diff and unchanged engine |
| R17 | Production startup behavior preserved | PASS — no startup changes; tests live only in test projects/scripts |
| R18 | Full physical/OS/DPI/end-user visual acceptance | UNVERIFIED — outside automated client-render evidence |

## 11. Final branch

`bt2-0.1.2-refinements`; no main merge.

## 12. Full commit SHA

The final documentation commit's full SHA is supplied with the handoff response. Release tag and source SHA are recorded with the publication result below.

## 13. Working-tree status

Checked after the final commit/push and reported with the handoff response. Generated compiled payloads remain GitHub Release assets; only small actual PNG documentation images are tracked here. Stop after 0.1.2; no 0.2 work started.

## Published delivery

[v0.1.2](https://github.com/jakeisboss/bot-toucher-2/releases/tag/v0.1.2), built from `cfc8de15502b42f12703a81dd92f47a24fd80820`. Existing original 0.1.1 installations use Settings → Updates → Check for updates → Install update. The exact-source in-app delta is **778,460 bytes**. [Offline transition ZIP](https://github.com/jakeisboss/bot-toucher-2/releases/download/v0.1.2/BotToucher2-0.1.1-to-0.1.2-update.zip) contains manifest/assets, not a directly runnable EXE. The initial bootstrap is available on the Release for new installs.

The final Windows publication job verified the original published 0.1.1 bootstrap in a disposable root, offered the correct smaller delta, installed 0.1.2, rolled back to 0.1.1 and reapplied 0.1.2 successfully. Temporary screenshot-writing CI permissions/steps were removed after capturing the evidence. No main merge, packet/widget work or 0.2 implementation occurred.
