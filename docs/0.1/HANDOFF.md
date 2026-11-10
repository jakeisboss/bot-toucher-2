# Bot Toucher 2 — corrected 0.1 handoff

**0.1 scope: shell/UI refinement and one manual updater only. Windows and live GitHub Release verification remain explicitly UNVERIFIED in this cloud environment.**

## 1. Corrected 0.1 scope

The trusted external DLL claim and all W01–W11 acceptance requirements were removed before production implementation. No widget host/runtime, DLL loading/contracts, messaging/actions, lifecycle/crash/proof/persistence, packet loading, workloads, allocator, CPU/GPU execution or results architecture was added. Lab stays a content boundary for later execution/results. The corrected [Definition of Done](DEFINITION-OF-DONE.md) is authoritative.

## 2. Implementation structure

- Existing `BotToucher2.App`: cohesive dark WPF controls, Context + built-in Hardware Monitor right rail, Home Application Info/Recent Activity, Settings-only manual update frontend, safe shutdown and candidate readiness.
- Existing `BotToucher2.Core`: preserved hardware lifetime and safe shell preferences, compatible old-tab migration, real bounded activity log.
- New `BotToucher2.Update`: one portable update authority, paired inventory/delta planner, exact source/target verification, staging, atomic committed record, journal/receipts/history, rollback/recovery and public GitHub transport.
- New `BotToucher2.Updater`: external command/bootstrap/packaging/process frontend to that same engine, independently usable without loading WPF.
- Three portable test projects; explicit Windows shell smoke and real disposable transaction scripts. Production startup references none of the test executables.

## 3. BT1 reused pieces

The existing 0.0 hardware snapshot/sampler reuse remains intact. 0.1 retains useful dark navy colors and adapts BT1's Application Info field choices and bounded activity-list/detail presentation. Later BT1 updater source was inspected for deterministic delta classification, inventory bounds, hash verification, owned-path checks and process-shared locking. Proven concepts were retained where clean; [inspection/provenance](BT1-INSPECTION.md) identifies exact reference commits and paths.

## 4. BT1 rejected pieces

No migration of AppServices, StoragePaths, Bridge, updater legacy receipt/version bindings, in-memory whole-payload archives, separate recovery implementation, or widget/card refresh/runtime coupling. A BT1 path normalizer that stripped leading slashes was not reused. BT2 instead rejects unsafe/noncanonical Windows paths and uses immutable paired version directories.

## 5. Updater architecture

There is one `UpdateEngine`; UI and CLI delegate mutations to it. One cross-process update lease and an app-instance lease coordinate operations. Exact source version/inventory/hash checks precede delta transfer. New/replacement assets stream with size/hash bounds, duplicate content downloads once, and unchanged bytes copy locally. The complete staged target verifies before activation.

App and updater engine live in one immutable paired bundle. This inspection-driven refinement avoids duplicated publication/recovery mutations across two physical roots while keeping engine files outside `app/`. Verified workers run outside mutation targets. The sole atomic `current.json` commit selects current/previous/anchor identities. Durable receipts bind readiness and inventory; the journal distinguishes before/after commit and makes cleanup retryable. Prepared state restores and worker copies verify outside live destinations before publication.

Failed candidate launch/readiness restores the prior selection/state and preserves known-good rollback. Successful updates retain exactly one previous known-good pair before 1.0; the 1.0 anchor is protected. Explicit full recovery never promotes damaged bytes into rollback. Recovery uses this same authority and relaunch/readiness boundary; it is not a separate repair engine.

## 6. Install/state paths

Default root: `%LOCALAPPDATA%/BotToucher2`, without elevation. Immutable paired bytes: `install/versions/<inventory-id>/{app,engine}`; sole selection: `install/current.json`; worker/app locks: `control/`; shell preferences: `state/layout.json`; staging/journal/receipts/snapshots/readiness/recovery: `updates/`; activity: `logs/activity.jsonl`. [OPERATING.md](OPERATING.md) gives the full path table and external commands.

0.0 root preferences are retained during import. Invalid/foreign JSON uses defaults plus a visible nonfatal notice and does not silently overwrite originals. Candidate preferences are inspected/migrated in memory before readiness; state edits/saving wait for commit. Rollback preserves current state in a reported displaced-state snapshot before restoring its paired previous state, and requires explicit resolution before later rotation.

## 7. GitHub Release asset format

One public HTTPS repository and numeric release channel, including prereleases; no automatic background checks/downloads/installs. `bt2-release.json` is bounded lightweight metadata. Source-specific transition JSON binds exact source version/ID and complete target inventory/operations/hash/size. Assets named by SHA-256 contain changed/new bytes only; initial bootstrap contains a source-null full manifest and content-addressed assets. Binary patches are deferred. Compiled payloads/bootstrap ZIPs belong only in GitHub Release assets.

`tools/publish-release.ps1` and the explicit manual Windows workflow implement the publication route. Live API access is denied by the cloud proxy, so no release upload or working binary download link is claimed. No new compiled Git blobs were used. The historical 0.0 download branch was not rewritten/deleted by this task.

## 8. Delta/update-size evidence

The actual packaging CLI generated a **1,048,609-byte complete target with only 8 payload bytes**, stored as two assets. Operations: 3 unchanged, 1 replaced, 1 new, 1 deleted. Unchanged/deleted payload bytes: zero. Unique app/engine content also transfers once in the engine tests. See [measured evidence](EVIDENCE.md); inert fixture executable names were used only for packaging and were not launched.

## 9. Lightweight updater-load evidence

Status reads bounded receipt metadata without installed-file/payload scanning. Manual Check's HTTP test models a 200,000,000-byte unchanged runtime, performs exactly 3 metadata/manifest requests, and makes no payload request until explicit asset opening. The fourth request returns the expected changed bytes. The real independent CLI status/help entry points were also run. No payload size is used to allocate memory or trigger hash scans on open/check.

## 10. Exact verification

Local source verification at `034d100` passed 111/111 tests: 60 updater, 33 core and 18 release/dispatch tests, with zero failures/skips. Release build passed with zero warnings/errors. Native Windows verification is being requested separately and is not inferred from these results. The reproducible commands are:

```text
tools/dotnet.sh clean BotToucher2.sln -c Release
tools/dotnet.sh build BotToucher2.sln -c Release
tools/dotnet.sh test BotToucher2.sln -c Release --no-build --logger trx --results-directory <fresh-results-directory>
tools/dotnet.sh publish src/BotToucher2.App -c Release -r win-x64 --self-contained true -o <app-output>
tools/dotnet.sh publish src/BotToucher2.Updater -c Release -r win-x64 --self-contained true -o <engine-output>
```

External CLI status/help, real packaging generation and a live manual check were executed. The live check failed nonzero with a proxy-tunnel HTTP 403, which was reported rather than interpreted as successful access. No Windows GUI, physical interaction or actual Windows transaction result is inferred from these commands.

Independent reviews covered frontend and shared-engine paths, followed by whole-branch integration. Findings were addressed with targeted regressions: interrupted state/worker preparation, paired entrypoints, recovery relaunch/retention, candidate close/helper lifetime and concurrent metadata/cleanup consistency. Exact final results are recorded in the accompanying verification summary.

## 11. Every corrected 0.1 DoD item

PASS identifies the stated structural/portable evidence, not a hidden Windows acceptance claim. UNVERIFIED identifies a required native/UI/live-service check not executed here. There are no widget acceptance items.

| ID | Status | Evidence / limit |
|---|---|---|
| S01 | UNVERIFIED | Cohesive dark templates compile; Windows visual/focus/selection review unrun |
| S02 | PASS | Right-rail Widgets tab removed from compiled shell |
| S03 | PASS | Right rail contains Context and separate built-in monitor |
| S04 | UNVERIFIED | Real assembly/path info implemented; actual Windows view unrun |
| S05 | UNVERIFIED | Real bounded activity tests pass; Windows Home/Activity interactions unrun |
| S06 | PASS | Lab remains empty content boundary; no execution/results model |
| S07 | PASS | Settings owns Updates; old entry routes there |
| S08 | PASS | Startup call graph contains no checks/installs/tests/benchmarks/workloads |
| U01 | PASS | One engine shared by UI/external CLI, one transaction record/lease |
| U02 | UNVERIFIED | Manual public release client fixture-tested; real API proxy denied |
| U03 | PASS | Metadata-only check/status tests and CLI evidence; no payload scan |
| U04 | PASS | Exact source version/hash mismatch tests preserve installation |
| U05 | PASS | Canonical source/target identities, hashes and four operation kinds |
| U06 | PASS | Deterministic required delta plus actual CLI accounting |
| U07 | PASS | Only new/changed unique blobs transfer; unchanged/deleted zero |
| U08 | PASS | Payload size/hash failure and cleanup tests |
| U09 | PASS | Complete paired target staged before live activation |
| U10 | PASS | Full staged inventory checked again before apply |
| U11 | UNVERIFIED | Portable transaction/recovery passes; native shutdown/locks/apply pending |
| U12 | PASS | Installed hashes/readiness-bound receipt precede commit; native readiness U18 |
| U13 | PASS | Durable hashed receipt/journal and bounded history tests |
| U14 | PASS | One previous pair retained; durable 1.0 anchor protected in tests |
| U15 | PASS | Failure cases preserve existing known-good rollback |
| U16 | UNVERIFIED | Portable restore/hash/readiness proof; actual Windows rollback unrun |
| U17 | PASS | Real standalone CLI executed without WPF; native managed-worker checks pending |
| U18 | UNVERIFIED | Process/readiness frontend cross-built; real Windows relaunch unrun |
| U19 | UNVERIFIED | Console/log errors verified; Windows native/UI error presentation unrun |
| U20 | PASS | Separate per-user paths and no elevation request; Windows ACL check pending |
| U21 | PASS | Explicit user actions only; no apply-on-close or startup work |
| U22 | PASS | Publication scripts use Release assets; no new binary Git blobs/upload claim |
| V01 | PASS | Final portable tests/build results below |
| V02 | UNVERIFIED | Actual Windows launch/physical interaction not run |
| V03 | UNVERIFIED | Actual Windows update/rollback/reapply not run |
| V04 | PASS | All Windows claims explicitly bounded; cross-build distinguished |
| V05 | PASS | Source/payload/stage/restore/interruption/retention/readiness failures tested |
| V06 | PASS | Corrected design, provenance, paths/formats, commands and evidence documented |
| V07 | PASS | Every corrected item listed; final Git state in accompanying response |

## 12. Known limitations

- This host cannot launch WPF. Native Windows process exit/sharing, actual GUI/helper handshake, physical resizing, ACLs and UI acceptance remain UNVERIFIED.
- Cloud proxy denies the live GitHub API request; Releases check, upload and a published executable link remain UNVERIFIED. The manual workflow/release script is the intended next route, not Git binary commits.
- Physical power-loss persistence and hostile same-user directory swaps are not proven by managed path checks/flush calls. No sandbox or publisher-signature system is claimed.
- Abrupt preparation interruption can leave orphan staging data; distinct verified worker caches and intentionally quarantined unexpected bytes remain recoverable evidence. They are not a growing accepted rollback chain and are not blindly deleted.
- A damaged selected 1.0 anchor must be repaired by same-inventory recovery before changing it; ordinary rotation never discards the anchor.
- Rollback's state consequence is explicit: later changes are preserved in displaced-state evidence, not silently merged into older schemas. Acknowledge only after saving/resolving them.
- The .NET test SDK emits a nonfatal artifact-directory fallback notice; fresh TRX counts/exit status remain authoritative.

## 13. 0.2 readiness

The shell content boundary, real activity and manual updater are available as a base. No widget, packet or execution contracts were invented. 0.2 requires its own design and approval, and Windows/live-service acceptance of 0.1 is still outstanding. No 0.2 implementation was started.

## 14. Final Git state

Branch: `bt2-0.1-foundation`. Full final commit SHA and working-tree status are supplied in the accompanying response after committing documentation. No merge into main is performed. All generated binaries/tests/results remain ignored artifacts; only source/docs/scripts/small metadata are committed.
