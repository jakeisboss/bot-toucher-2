# Final whole-branch fix wave

Baseline reviewed: `9003950fdb8916bbadad2ae63eff3d2a13fb14b2`. Scope: the three findings in `whole-branch-review.md`, plus the explicitly authorized package output/input overlap guard. No future runtime or widget work. One shared UpdateEngine remains the transaction authority.

## Changes

1. **Consistent lightweight status.** Metadata opens now use `FileShare.ReadWrite | FileShare.Delete`, open before checking the bounded stream length, and tolerate files removed before open. Status brackets receipt validation and the pending flag with current-record byte reads. It retries a changed snapshot at most eight times, then returns a nonfatal pending/busy status with no stale selected directory. A stable malformed/null record or missing/bad receipt still fails. No mutation lease, payload access, installed-byte scan or snapshot materialization was added to status. Tests cover commit/pruning during receipt validation, commit between validation and pending inspection, eight concurrent readers during forty updates, stable missing receipts, bounded churn, and readiness polling while Apply owns the mutation lease.

2. **Managed external command isolation.** `apply`, `install`, `install-release`, `rollback`, `recover`, and `full-recover` invoked under the managed root independently call `PrepareWorkerAsync`. A caller already at the independently verified worker can execute there; a version-directory caller dispatches there and exits before mutation. No command-line flag alone bypasses verification: the child validates its worker bytes/path plus an owned random-token request binding parent PID and exact arguments. The child acknowledges initialization, waits for actual parent exit, and uses the original shared engine frontend. Root, working directory, arguments, stdout/stderr and final exit code are preserved.

   Windows loaded-image lifetime requires an explicit process-semantics refinement: the managed parent cannot remain loaded while synchronously waiting for an operation that may quarantine/prune its own bundle. Its JSON therefore says **Accepted, NOT completed** and provides a fixed owned result path. Completion/failure and exit/output/error appear in that durable result and activity logs. Initialization is bounded to fifteen seconds and a rejected worker is stopped; parent-exit wait is bounded to two minutes. The Windows proof bounds final-result polling to ten minutes and treats missing/stuck/error results as failure. Independently trusted bootstrap invocation outside the managed root remains synchronous and can recover a damaged selected app/engine. OPERATING documents these semantics. GUI worker/parent shutdown and candidate lifetime gates remain intact.

   Portable tests use genuine inventories for worker validation and damaged-selected-engine refusal, reject forged handoff arguments/PIDs, verify all six dispatch policies, exact argument forwarding, owned result paths and failure details. Two subprocess tests launch the actual managed CLI and verified worker, proving successful no-op recovery and a rollback refusal produce Accepted followed by the correct durable final result. The portable fixture alone grants Unix execute permission to its native test apphost; this is not production Linux updater support.

3. **Readable activity selection.** A small portable feed state retains the selected ActivityEntry across inserted rows and skips unchanged refreshes. The WPF presentation retains the details and suppresses only its own list-rebuild selection events. The portable test checks entry/message persistence and expiration from the bounded feed. The WPF smoke adds a real selection, waits across the two-second timer, inserts a real log event and checks retained selection/details.

4. **Package boundary safety.** Package rejects output/input containment in either direction before creating or copying into output. Tests cover output nested in either app or engine input.

The Windows transaction proof now invokes the retained managed engine for a third update which prunes that engine bundle, and invokes managed current full recovery which quarantines that bundle. It validates the worker is outside versions, awaits the final result, checks pruning/restored bytes and runs the concurrent status tests on Windows. Its JSON polling uses deletion-compatible sharing.

## Verification

Red evidence:

- Before the status implementation, `tools/dotnet.sh test tests/BotToucher2.Update.Tests -c Release --filter 'FullyQualifiedName~StatusRetriesWhenCommit|FullyQualifiedName~ConcurrentStatusReaders|FullyQualifiedName~StatusStillRejects' --verbosity minimal` failed 3/4. The actual eight-reader/forty-update stress reproduced multiple `InvalidDataException: Referenced receipt missing.` exceptions. The deterministic interleaving tests also lacked the required checkpoints at baseline; stable-corruption rejection passed.
- New activity-state and dispatch tests initially failed compilation because their production behavior/types did not exist.
- The first actual CLI subprocess test and an overlapping first full-suite attempt failed both subprocess cases because the Linux fixture's prepared apphost lacked execute permission after the engine's byte-copy. The test fixture was corrected to grant execute permission to its prepared native worker. Production Windows behavior was not changed to accommodate that fixture. Both subprocess cases subsequently passed.

Final green evidence on this Linux environment:

| Command | Result |
|---|---|
| `tools/dotnet.sh test tests/BotToucher2.Update.Tests -c Release --filter 'FullyQualifiedName~Status' --verbosity minimal` | 4/4 before adding the final bounded-churn and readiness-lease tests; concurrent stress passed with zero status errors |
| `tools/dotnet.sh test tests/BotToucher2.Core.Tests -c Release --filter FullyQualifiedName~Refresh_preserves --verbosity minimal` | 1/1 |
| `tools/dotnet.sh test tests/BotToucher2.Release.Tests -c Release --filter FullyQualifiedName~DispatchTests --verbosity minimal` | 12/12 before adding the two real subprocess cases |
| `tools/dotnet.sh test tests/BotToucher2.Release.Tests -c Release --filter FullyQualifiedName~ManagedCliExits --verbosity minimal` | 2/2 real subprocess outcomes |
| `tools/dotnet.sh test BotToucher2.sln -c Release --verbosity minimal` | **108/108: 60 Update, 31 Core, 17 Release; zero failures/skips**. Includes the final bounded-churn and lease/readiness cases and both subprocess outcomes. |
| `tools/dotnet.sh build BotToucher2.sln -c Release --no-restore --verbosity minimal` | **Succeeded, 0 warnings, 0 errors**, including Windows WPF app and expanded Shell.Smoke compilation |

The test runner emits its existing informational `VSTEST_ARTIFACTS_DIRECTORY` fallback notice. No test failures remain in the final suite.

## Limits

Actual Windows metadata delete/replace sharing, Windows loaded-image lifetime during managed dispatch/pruning/quarantine, production readiness/commit overlap, WPF timer interaction/rendering, helper death and full Windows transaction execution remain **UNVERIFIED** in this environment. The expanded Windows scripts/smoke are intended checks, not execution evidence. Portable process tests prove dispatch/result behavior with the native Linux test apphost only. Live Release access/publication and release readiness remain the parent's external evidence responsibility; this fix report makes no Windows or publication pass claim.
