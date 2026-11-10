# BT2 0.1 Definition of Done

Written before implementation. This is the acceptance checklist for 0.1 only. Each item must receive PASS, FAIL or UNVERIFIED with concrete evidence in the final handoff. Unverified Windows behavior must not be inferred from Linux cross-builds. The user has accepted the 0.0 shell as the baseline.

## Shell/UI

- S01: Cohesive dark navy palette across workspace, rails, cards, borders, text, selection and focus; preserve the recognizable shell structure.
- S02: Remove the right-rail Widgets tab.
- S03: Right rail contains Context and the application-owned Hardware Monitor; it is not a widget runtime/results surface.
- S04: Home Application Info displays real available application/build/path data and honest unavailable/deferred values.
- S05: Home Recent Activity displays real shell/system/update events without fabricated production examples.
- S06: Lab remains the structural home for future execution/results; no real execution/result model is added.

## Updater

- U01: Exactly one update engine/transaction authority, shared by app frontend and external invocation.
- U02: Manual GitHub Releases metadata check works; one channel includes development builds and normal releases.
- U03: Opening Settings → Updates and checking status use only bounded lightweight metadata, with no payload download/extraction/hash scan or rollback-archive loading.
- U04: Actual source install inventory/hashes are verified before applying a transition; mismatch preserves the install and reports an error.
- U05: Transition manifest records exact source/target identity, inventories/hashes, unchanged/replaced/new/deleted files and required payload identities/sizes.
- U06: Required delta is determined from verified identities rather than version labels alone.
- U07: Transfer contains only required new/replacement files; unchanged and deleted files cause no payload download.
- U08: Downloaded payload hashes and sizes are verified.
- U09: Target transition is fully staged before switching the live install.
- U10: Complete staged target inventory/hashes are verified.
- U11: Apply uses the shared safe transaction, coordinates app shutdown and supports restart/interruption recovery.
- U12: Installed target verifies before success/known-good acceptance is recorded.
- U13: Durable transaction receipt/history is written and failures are recorded.
- U14: Before 1.0 exactly one previous known-good rollback version is retained after successful completion; 1.0 is protected as a durable anchor once it exists.
- U15: Failed download/stage/apply/verification does not rotate or destroy the existing known-good rollback.
- U16: Rollback verifies current state, receipt, backup integrity and restored target, and records the result.
- U17: The same update authority can be invoked externally without requiring a working main app.
- U18: Successful update and rollback relaunch BT2; failure to relaunch is reported rather than claimed successful.
- U19: Failure states are visible in the UI and Recent Activity/logs, with applicable recovery choices.
- U20: Per-user non-admin installation separates installation, mutable state, staging, logs and rollback storage; final paths are documented.
- U21: Only explicit user Install/Roll back actions initiate those operations. No background automatic download/install or apply-on-close behavior.
- U22: Compiled payloads/bootstrap artifacts belong to GitHub Release assets, never new normal Git binary blobs. Source/docs/manifests/small assets remain in the source branch.

## Widget foundation

- W01: One universal blank widget host, with no workload-specific content or semantics.
- W02: App/workspace owns widget position, size, visibility, arrangement and persistence.
- W03: Explicit user loading of trusted external .NET widget DLLs works through a narrow compatible contract; runtime owns widget lifecycle, independent of global layout authority.
- W04: Start/stop/dispose cleanly, including failure/cancellation and lifecycle-safe subscription cleanup.
- W05: Named publish/subscribe works with explicit namespace/group and widget identity boundaries.
- W06: An action gateway separates informational messages from requests to registered app actions; unknown actions fail clearly.
- W07: Two external generic proof widgets coexist and communicate without direct instance references or packet loading.
- W08: Persisted layout restores geometry/visibility without persisting widget functional/runtime state as layout data.
- W09: No packet loading, packet execution, allocator, CPU/GPU execution, final results or workload architecture is introduced.

- W10: Production startup/layout restore invokes no tests, demos, update operations or widget code; widget starts require deliberate action.
- W11: Caught widget faults and best-effort fatal/unclean-exit detection produce real crash/activity reports without claiming in-process isolation.

## Verification and handoff

- V01: Targeted automated behavior and failure-path tests pass.
- V02: Required actual Windows launches/interactions are verified or explicitly UNVERIFIED.
- V03: Real update/rollback/reapply transaction and relevant failure cases are proven on Windows or explicitly UNVERIFIED.
- V04: No claim of Windows UI/update behavior rests only on Linux cross-compilation.
- V05: Significant source mismatch, payload mismatch, staging/apply interruption, rollback preservation and lifecycle failure paths are exercised.
- V06: Documentation includes design, BT1 reuse/rejection, install paths, release formats, operating instructions, update-size and lightweight-status evidence.
- V07: Final handoff marks every checklist item PASS/FAIL/UNVERIFIED, includes exact verification commands/results, limitations, 0.2 readiness, final branch/full SHA/working-tree state, and stops at 0.1.

## Definition of completion

Implementation plus a green cross-build alone is insufficient to claim verified Windows acceptance. Any required external permission/access blocker and any unrun check must be named. No 0.2 implementation is authorized.
