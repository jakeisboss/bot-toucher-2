# BT2 0.1 design

Status: approved updater design with corrected 0.1 scope: shell/UI refinement and updater only. Implementation follows this correction.

## Accepted direction

BT2 remains a Windows-first .NET 8 WPF application, continuing the accepted 0.0 shell. Deliver only 0.1: a manual updater, Home information/activity, and the specified dark theme/right-rail refinements. No packet loading, workload runtime, executor, allocator, results model, or CPU/GPU execution.

0.1 does not implement widget hosting/runtime/contracts, external DLL loading, messaging/actions, widget lifecycle/crash handling/proof DLLs or widget persistence. It does not implement packet loading, workload execution, allocator, CPU/GPU execution or results architecture. Those systems require later designs.

The authoritative acceptance list is [DEFINITION-OF-DONE.md](DEFINITION-OF-DONE.md). [BT1-INSPECTION.md](BT1-INSPECTION.md) records inspected source and reuse decisions before implementation. Existing source is changed only for concrete 0.1 requirements.

## Approaches and proposed choice

Updater alternatives: (a) journaled in-place file replacement, (b) verified version directories and an atomic active-install record. Both need staging, hashes, receipts and recovery. Prefer (b): live binaries remain untouched until the target is complete; one small owned record controls activation. It costs local staging disk space but does not increase download size, and leaves the old installation recoverable after a process interruption. Do not use a second repair/rollback engine.

## Project responsibilities

- Existing `BotToucher2.App`: WPF shell, Home cards, lightweight Updates frontend and Windows startup/shutdown integration.
- Existing `BotToucher2.Core`: preserve hardware lifetime and preference safety; add shared activity records where portable.
- `BotToucher2.Update`: one UI-independent transaction engine, manifests, inventory validation, release transport, receipts, journal, install/rollback/recovery operations. The external updater uses this exact engine; UI has no competing file mutation path.
- `BotToucher2.Updater`: command-line/bootstrap frontend outside the active application folder, including explicit install/check/apply/rollback/recover and launcher entry points. It can work when the main WPF executable is damaged. This is a frontend, not a second authority.
- Windows smoke/transaction tests live under test paths and run only through explicit test entry points.

Keep files grouped by responsibility; do not create general plugin/provider infrastructure unrelated to these concrete contracts.

## Install and state layout

Per-user root `%LOCALAPPDATA%/BotToucher2/`:

- `install/versions/<version-and-content-id>/`: immutable full application inventories.
- `install/current.json`: small authoritative active/previous installation identity record, atomically replaced with recovery evidence.
- `control/bootstrap/`: minimal fixed-protocol launcher/bootstrap outside application versions.
- `control/versions/<engine-content-id>/`: immutable updater frontend/engine/dependency inventories, separately verified and selected by the same active-install record as the app; a damaged app does not prevent external invocation.
- `updates/staging/<transaction-id>/`: downloaded changed/new payload and reconstructed complete target; never executed before verification.
- `updates/transactions/`: bounded transaction journal and receipts/history, bound to exact paths, identities and manifest digest.
- `updates/rollback/`: rollback receipt and only state snapshots required by the retained known-good previous installation; application bytes live in its immutable version directory.
- `state/`: shell preferences only; executable behavior is never deserialized from state.
- `logs/`: bounded real shell/update activity and diagnostic records.

Existing 0.0 `layout.json` must be imported through explicit validated migration, retaining original bytes. Widgets right-tab values map to Context. Corrupt/foreign preferences still produce defaults plus a visible nonfatal notice and are not silently overwritten. Routine updates do not require administrator rights.

The initial bootstrap/full install may transfer a full application. A loose 0.0 ZIP has no trusted install receipt: do not pretend it is a verified managed source. Establish 0.1 as a clean managed install while preserving validated user state. Later managed transitions transfer changed/new data only.

## GitHub Releases and manifests

One repository: `jakeisboss/bot-toucher-2`. GitHub repository page metadata was inspected without a supplied application credential and reports `repository_public=true` / `visibilityLabel=Public`. 0.1 supports public Releases over HTTPS; private release authentication is out of scope and must fail clearly if visibility changes. One channel: consider both published normal releases and prereleases; exclude drafts. Select compatible newer numeric versions rather than relying on GitHub's latest-stable-only endpoint. Check is a deliberate user action; opening the surface never checks or downloads automatically.

Release assets:

- Small bounded release metadata JSON: schema/application identity, version/build identity, notes summary, compatible source inventory IDs, transition asset names, declared transfer sizes, optional initial/bootstrap full asset.
- Small transition manifest JSON: exact source and target inventory identity; canonical relative paths; each file's size and SHA-256; deterministic unchanged/replace/add/delete operation set; per-file asset name/hash/size for changed/new files.
- Changed/new file assets (or a source-specific archive containing exactly those files). Unchanged and deleted files contribute zero download bytes. Deduplicate identical content where practical without creating a separate package system.
- Initial/full bootstrap asset and compiled Windows test distribution as GitHub Release assets only.

Prefer full-file replacement for changes; binary patching is deferred. Hashes verify inventory/payload integrity, not a publisher signature. The initial source trust boundary is HTTPS GitHub Releases controlled by the configured repository. Do not invent signing keys, compile credentials into releases, follow unbounded arbitrary URLs, or silently treat local JSON as authenticated publisher metadata. Bound metadata size, file count and total sizes; reject case-insensitive path aliases, traversal, rooted/UNC/drive/alternate-stream paths, Windows reserved names, trailing-dot/space aliases, symlinks/reparse traversal and undeclared files.

Cloud Git read access does not establish Releases API or asset publication access. A fresh API read currently returned Forbidden. Live access/publication and any required deployment permissions remain external prerequisites; report them honestly. Do not use binary Git commits as a workaround. No authentication subsystem is introduced in 0.1. Private-release distribution would require a separately approved credential design.

## Lightweight status versus installation

Settings → Updates reads only bounded local status/receipt summaries. It does not enumerate/hash the install, open payload archives, unpack anything, or materialize rollback backups. Check fetches only bounded release and manifest metadata. Show current/available version, exact declared required transfer size and notes. Install alone verifies source bytes, downloads required assets and stages the target. Transfer accounting is derived from required operations and includes metadata separately.

Packet Center's existing Update Center entry becomes a navigation link to Settings → Updates. There is one view/workflow/engine.

## One updater transaction authority

All mutation commands share one cross-process lease and transaction state machine. Paths and approved manifest/handoff digests are revalidated at each process boundary. The UI cannot write install records directly.

1. Explicit Install selects a target manifest; source version and complete managed inventory/hashes must match the current receipt. Unexpected executable files are rejected rather than silently adopted.
2. Determine the required delta; stream changed/new assets to owned staging files with byte limits and hash verification. Copy unchanged files locally from verified source; omit deletions from the target. Never patch unknown bytes.
3. Verify the complete reconstructed staged target and reserve sufficient disk space. Any failure leaves the live install and known-good rollback unchanged.
4. Bind the staged transition to a helper handoff. Request orderly BT2 shutdown; wait for the actual parent process to exit. The helper holds/reacquires the shared lease and re-verifies source/stage before mutation.
5. Journal the pending transition and retain the existing previous-known-good rollback. Snapshot only mutable state touched by this transition. Publish the verified version directory and atomically activate a pending target record; keep recovery evidence for an interruption.
6. Verify the installed target, relaunch it, and await a transaction-bound readiness acknowledgment after real shell initialization, with no tests or update operations started automatically. A byte-correct but unlaunchable candidate is not accepted as known-good.
7. Commit the receipt/known-good active record, then rotate rollback to the just-replaced known-good version and clean obsolete managed bytes. No earlier rollback is destroyed before commit. Failed launch/verification restores the prior active record and retains the older rollback; record/report the failure.

Interrupted transitions are inspected by the same engine using durable phases and exact receipt identities; unknown/tampered states are rejected, never guessed. Rollback verifies current inventory, receipt and backup before switching, verifies the restored target, relaunches and records the result. A damaged-current recovery must be an explicit full-replacement/recovery mode of this same engine, preserve evidence of mismatched bytes, and never silently relax normal source verification.

Before 1.0 retain exactly one previous known-good rollback after a successful operation. Do not retain a growing version chain. Once 1.0 exists preserve its verified immutable installation/receipt as a durable anchor; the broader post-1.0 policy remains deferred. Temporary pending/quarantine recovery evidence is distinguished from accepted rollback versions and cleaned only when safe.

The bootstrap/control component must remain independently invocable and bind its supported engine/manifest protocol. Updating a running helper requires using a verified worker copy outside the target directory; no attempt to overwrite loaded updater binaries. Version/protocol incompatibility fails clearly instead of launching a guessed helper.

## Transaction decisions resolved by the builder check

These decisions define the approved version-directory/atomic-record updater, subject to a concrete implementation flaw being documented before a necessary change.

### Updater/control self-update

Transition manifests include separately identified **application and updater-engine inventories**, including their target hashes. Stage/verify both under immutable version/content IDs. `current.json` selects an app/engine pair; rollback selects the corresponding previous pair. A worker copy of the current verified updater engine runs outside both target directories, so no loaded executable/DLL needs in-place replacement. The same engine owns these operations; the minimal bootstrap only selects/launches an owned verified entry point and never stages/applies/rolls back files itself.

The initial bootstrap launcher protocol stays fixed through 0.1; manifests declare a minimum bootstrap protocol and incompatible targets are refused. Changing that bootstrap protocol itself requires explicit bootstrap replacement, not an invented second recovery engine. Routine engine changes use the same managed transition and paired receipt. Stage failure preserves both current app and current engine.

### Exact commit and interruption rule

One atomic replacement of `current.json` is the **sole commit point**. That committed record contains the transaction ID, current app/engine inventory IDs, previous-known-good pair, associated state snapshot identity and known-good receipt identity. A candidate/pending record is not a committed known-good version. Before replacing it, persist the complete verified receipt and readiness acknowledgment so the referenced artifacts already exist. Append human/history events and perform pruning after commit; they are not alternate authorities.

Recovery compares the journal's transaction ID with the atomic record. If committed, finish idempotent cleanup and do not rotate again. If pending/uncommitted, preserve the existing previous-known-good pair, restore the pre-transaction app/engine selection and state where needed, record failure and offer explicit retry/recovery. A damaged/mismatched record or receipt blocks automatic mutation and preserves evidence. Never infer success from a directory name or process exit alone.

### Mutable state and candidate readiness

Only `state/` contains managed mutable shell/widget-layout state in 0.1. External widget binaries/data and activity logs are outside that snapshot. After the old app exits and before activating a candidate, snapshot the full bounded managed state directory, including exact bytes and a hashed inventory. Stage preparation does not migrate the live state.

The candidate loads/migrates preferences in memory for readiness, runs no tests or update operations, and does not persist state or accept user edits until the updater records commit. If candidate readiness fails, restore the verified pre-transition state snapshot using the same journaled authority; retain any unexpected differing state bytes for recovery rather than discard them.

A later explicit rollback first preserves the user's **current** state as one reported displaced-state recovery snapshot, then restores the state snapshot paired with the previous known-good version. Show this consequence before user confirmation. This prevents incompatible preferences from being fed to older code without silently losing subsequent changes. Keep one displaced-state recovery snapshot, distinct from the single executable rollback version; if unresolved recovery evidence prevents safe rotation, refuse rather than silently delete it. No schema for future workload/session data is defined.

## Activity and startup safety

One bounded activity service supplies Home Recent Activity, Packet Center Activity, updater events and persistent logs. Record real startup, preference fallback, hardware initialization/failure/recovery, updater phases/errors. Do not append periodic identical telemetry-success noise or fake sample events.

Home Application Info adapts BT1's useful read-only card: actual assembly version, supplied build/source identity or unavailable, actual install/state/log paths, real release/build state and launch timestamp. Project/Bridge are deferred, never fabricated.

**Production startup runs no tests, demonstrations, benchmarks, diagnostics workloads, update checks or installs.** Only required shell services start (hardware observation, lightweight activity/preferences). Test harnesses are separate explicit entry points.

## Shell/UI refinement

Keep the accepted three-column window, navigation and both rail controls. Replace bright default tab/content templates with cohesive dark navy surfaces, readable hierarchy, modest cyan accents and visible keyboard focus. Right rail is Context plus the built-in Hardware Monitor only. Home has app-owned Application Info and Recent Activity cards. Lab remains a clean content boundary for future execution/results, without implementing either system. Settings contains the sole Updates surface.

## Verification and delivery

Targeted portable tests cover deterministic delta generation and exact transfer counts; source/payload mismatch; path/reparse rejection; no payload reads on status/check; staged/installed verification; failed apply/health acknowledgment preserving rollback; rollback rotation/anchor policy; interrupted transaction recovery. No widget, packet or execution tests/architecture are introduced.

Windows tests explicitly launch production app and external updater in a disposable per-user test root; install/update/rollback/reapply and interrupt selected phases. Exercise no automatic checks/installs/tests at launch, dark Home/Settings/right-rail visuals, Application Info, real Recent Activity, physical resizing and restarts. Never mutate the user's actual installation during automated tests. Linux cross-builds cannot establish those passes.

Publish source/docs to the feature branch, compiled assets to GitHub Releases if access permits, and provide real GitHub links. If Release publication or Windows execution remains unavailable, name the blocker and mark each affected acceptance item UNVERIFIED. Final handoff uses the user's fourteen corrected handoff sections and stops at 0.1.
