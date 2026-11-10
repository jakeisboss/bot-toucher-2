# BT1 inspection and reuse decisions

BT1 is reference material, not authority. BT2 has its own repository, names, version and local preference directory. No BT1 assembly, checkout, configuration or process is required at runtime.

Inspected repository: `https://github.com/jakeisboss/bot-toucher`. Main at `511720045526b75b660459cd58ac152a5a5e2794` contains documentation and workflows, not application source. Application reference: `release-0.2.1.3-20261009`, exact commit `6c30c11fe60a490ebeac2f81732988595eaaefc6`. A local read-only reference checkout was held outside BT2 at `/tmp/bt1-reference`. No BT1 files were edited.

| BT1 piece | BT2 decision and adaptation |
|---|---|
| `src/BotToucher.Core/Models/HardwareSnapshot.cs` | Reused snapshot records with a BT2 namespace. Small observation-only data shape, no workload semantics. |
| `src/BotToucher.Core/Services/HardwareMonitorService.cs` | Adapted as `WindowsHardwareSampler`: retains CPU system-time deltas, RAM API, GPU engine counters, VRAM counters/registry fallback, units and unavailable states. Removed storage-path coupling and optional disk metric, cache and capture gate. One application-owned serial loop supplies serialization and lifetime instead. Capture stays on background work and must drain before disposal. Extracted byte/VRAM formatting into the portable observation helper `HardwareReadings` so unavailable VRAM is regression-tested; corrected BT1’s literal available fallback when neither usage nor capacity exists. |
| `src/BotToucher.App/App.xaml` | Retained the useful dark palette. Kept styles small, with explicit dark button hover/press and keyboard focus states. No wholesale theme migration. |
| `src/BotToucher.App/MainWindow.xaml` | Retained the useful three-column rail/splitter structure, approximate initial rail widths, hide/restore controls and navigation names. Rebuilt presentation around BT2-owned state rather than copy service coupling. |
| `src/BotToucher.App/Views/PacketCenterWorkspaceView.xaml` | Retained Project / Packets / Activity and Update Center inside Packets. Contents are honest deferred surfaces, with no packet/update actions. |
| `src/BotToucher.Core/Services/ShellPreferencesService.cs` | Retained the idea of independent shell preferences and safe asynchronous storage. Did not reuse StoragePaths or legacy schema. BT2 validates its own small state, preserves invalid data and notices failures. |
| `src/BotToucher.App/Controls/HardwareMonitorPanel.xaml.cs` | Retained snapshot-presentation separation. Did not carry over widget/card refresh-provider coupling or view timers. BT2 monitor lifetime belongs to the application. |

## Intentionally excluded

The old AppServices graph, Bridge integration, project checklist semantics, quick swaps, results widgets, packet admission/execution, CPU controls, study logic, data preparation, update staging/apply/rollback and widget library/runtime were not copied. Their dependencies or responsibilities exceed 0.0. The old UI/process policies were inspected as context and were not imported as BT2 authority.

The central workspace is a normal content surface only. No future packet/widget/session schema or plugin interface was invented. The sole sampler interface exists for the concrete 0.0 observation lifecycle, not as a compute-provider abstraction.
