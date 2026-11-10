# BT2 0.1 evidence

## Actual packaging delta, independent of WPF launch

The compiled external CLI was invoked against real temporary source/target directories. Fixture executables were inert byte files for packaging only and were **not executed**. Both app and engine entry paths existed. The source contained an unchanged 1 MiB file, unchanged app/engine bytes, one replaced text file and one deleted text file. The target added a new text file.

Commands used the production entry point:

```text
tools/dotnet.sh run --project src/BotToucher2.Updater -c Release --no-build -- package --app <source/app> --engine <source/engine> --out <p0> --version 0.1.0 --root <unused-root>
tools/dotnet.sh run --project src/BotToucher2.Updater -c Release --no-build -- package --app <target/app> --engine <target/engine> --out <p1> --version 0.1.1 --source <p0/transition.json> --root <unused-root>
```

Observed target:

| Measurement | Observed |
|---|---:|
| Complete target bytes | 1,048,609 |
| Required payload bytes declared | 8 |
| Actual generated payload bytes | 8 |
| Actual unique payload assets | 2 |
| Unchanged operations | 3 |
| Replacement operations | 1 |
| New operations | 1 |
| Deletion operations | 1 |
| Unchanged bytes transferred | 0 |
| Deleted bytes transferred | 0 |
| Unchanged bytes avoided | 1,048,601 |

The script asserted exact operation counts, unique asset bytes and that the unused install root was not created/mutated. Portable engine tests additionally count actual stream opens and transferred bytes, including duplicate app/engine content downloaded once and reused locally. No claim of actual Windows execution is made by this packaging proof.

## Lightweight status/check

The real external CLI `status --root /tmp/bt2-01-status-proof` returned current/previous null, directories null and Pending=false with no install payload. Its `help` entry point ran independently of WPF. The release transport test models a 200,000,000-byte unchanged runtime entry and an unavailable local app directory. Check performs exactly three HTTP requests (bounded release index, bt2-release.json, transition manifest), does not touch that directory or request the payload, and reports the 7-byte required replacement. Explicitly opening the asset adds the fourth HTTP request and yields the actual expected bytes. Additional tests reject manifest tampering and a target lacking its external updater.

Metadata/status limits are based on record bytes/file count, not full application payload size. Install is deliberately the expensive boundary where source/target bytes are verified and staging is constructed.

## Live GitHub limitation

A real manual CLI check was attempted. It exited nonzero with:

```text
The proxy tunnel request to proxy 'http://proxy:8080/' failed with status code '403'.
```

This is a cloud proxy denial, not evidence of a private repository or failed Windows client. A fresh repository-page inspection reported public visibility. This Linux attempt did not verify live release check/asset publishing; later actual Windows verification below supersedes that limitation. no credentials were invented, no verification bypass used, and no compiled Git-blob fallback was made. The manual Windows workflow and release script provide the intended verification/publication route.

## Platform distinction

Portable tests use real temporary files/streams and controlled launch/readiness/fault boundaries. They cover transaction logic and filesystem effects on Linux. Native Windows process exit, sharing locks, WPF rendering, physical resizing, readiness, update/rollback/reapply and power interruption remain Windows-dependent and must be tested there. The provided scripts cannot execute on Linux; their actual Windows results are recorded below.

## Native Windows validation and sharing regression

Windows Actions runs [42915341972](https://github.com/jakeisboss/bot-toucher-2/actions/runs/42915341972) and [42916246532](https://github.com/jakeisboss/bot-toucher-2/actions/runs/42916246532) failed the concurrent-status stress test before WPF/transaction execution. The second run exposed the precise `UnauthorizedAccessException` at overwrite-move publication of `current.json`. Native evidence, rather than the green Linux result, identified the Windows sharing difference.

Commit `ba1609d2b24500b581e8107ca7b7c5807154705e` uses `File.Replace` for existing metadata and non-overwriting `File.Move` for first creation. The same correction applies to externally polled command results. Added regressions hold an old metadata/result reader open throughout publication, verify its old snapshot remains intact and verify fresh readers observe the new record. Portable tests after this fix passed **113/113** (61 Update, 33 Core, 19 Release); the full Release solution build passed with zero warnings/errors. Actual Windows confirmation is a separate required result, not inferred from this portable pass.

The next native run, [42917066666](https://github.com/jakeisboss/bot-toucher-2/actions/runs/42917066666), passed publication with held readers but exposed a transient sharing violation when a status reader opens `current.json` during replacement. The stress assertion remained enabled and failed, so Windows transaction execution and publication were again skipped. This required bounded handling of that precise sharing condition; corruption and ordinary access failures remain errors.

Commit `b418643d9032f418c9cdb3c3c9d52ae53afe2fe3` bounds sharing/lock retries (exact HRESULTs 0x80070020/21) across all status reads, reporting pending after eight 5-ms attempts. External result polling reports pending under its existing deadline. Other I/O/access/corruption errors propagate. Injected sharing/lock regressions failed before the fix and passed afterward; other-error regressions also passed. The portable suite passed 119 with two Windows-exclusive-handle tests explicitly skipped (121 total). Those two native regressions and the unchanged concurrency stress remain required in Windows CI.

[Windows run 42917924226](https://github.com/jakeisboss/bot-toucher-2/actions/runs/42917924226), source `70c970d52d88346e2493fad2a2cd6bbc32198d78`, passed the targeted tests and real WPF smoke step. This executes all **121 tests** on Windows, including the two exclusive-handle regressions skipped on Linux, and the eight-reader/40-update stress. WPF smoke covers launch, destinations, both rail toggles, minimum-size workspace, mouse/keyboard splitter state, right-rail cleanup, Home actual version/install path, and activity refresh/detail/error recovery. Physical visual review remains separate.

That run's subsequent real-transaction step failed before installation at its first self-contained publish. The recorded wrapper arguments split the unquoted PowerShell/MSBuild version option into `-p: Version=0.1.0`. Commit `db5e98c` quotes the entire version argument in both Windows validation and Release publishing and retains native command output in diagnostic annotations. No install/rollback acceptance is claimed from this failed preparation step.

Run [42918425239](https://github.com/jakeisboss/bot-toucher-2/actions/runs/42918425239) again passed native tests/WPF smoke, then remained in real transactions. Source inspection identified a concrete output-handle wait cycle: .NET's Windows CreateProcess path inherits handles; the managed caller's redirected output can remain open in its worker and relaunched app, while the proof waits for EOF before closing that app. A verified `.exe` Windows shell-launch boundary is used to isolate process output ownership; the existing real EOF/transaction assertions remain enabled. Newer explicit runs cancel older runs of this same feature branch so a superseded candidate cannot publish.

Commit `59dccb5000b45164a705bef248ea4c12422fed4c` makes the minimal Windows-only worker/app ShellExecute change; Linux worker fixtures remain direct. Exact argument/CWD/readiness/process ownership are retained. The targeted CLI suite passed 20 with its one native-only sharing test skipped. Superseded run 42918425239 was explicitly cancelled before publication. Run 42919892817 uses source `326b169` for the native confirmation and gated Release publication.

Run 42919892817 passed native tests/WPF smoke, all six self-contained publishes, package generation, and the real initial install/readiness with BT2 remaining open. The public wrapper annotation initially exposed only an install failure. The archived log later established that update and rollback also succeeded, then reapply failed at readiness; publication did not run. The following diagnostic run preserves product code and includes native updater output in the public error annotation; no success is inferred for the failed transition.

Run [42921415861](https://github.com/jakeisboss/bot-toucher-2/actions/runs/42921415861) reproduced the intermittent failure with exact native output: access to the final `.ready` file was denied because it was still held by the writer. Source confirms the helper's default reader cannot share that outstanding write handle. App commit `ec82808082e9cfc9c87c2e339b13693288c3d951` writes/flushed/closes a random same-directory temporary file and only then publishes the final nonce name without overwrite, cleaning temporary data on failure. App Release build passed with zero warnings/errors. Native verification retains real launches and EOF checks. An escaping typo in the temporary CI log-filter diagnostic was corrected and YAML parsed before the final native request; no product behavior changed for that diagnostic correction.

## Final native result

[Run 42922016748](https://github.com/jakeisboss/bot-toucher-2/actions/runs/42922016748), source `9d61772`, passed all 121 Windows tests, real WPF smoke, actual initial install/update/source-mismatch refusal/rollback/reapply, retained-engine pruning, managed full recovery, readiness and the public Release-index check. The archived run-6 log was successfully retrieved (terminal escapes stripped before publication) and confirms the same readiness sharing violation specifically during reapply, after update and rollback had succeeded. The atomic nonce publication fixes that observed cause; no unclassified native failure is being hidden. Temporary prior-log access steps/permissions were removed from the final workflow.

## Published distribution and live check

The same final Windows run published [v0.1.0](https://github.com/jakeisboss/bot-toucher-2/releases/tag/v0.1.0) from full source SHA `9d6177256ffd59fab49f0f161b1b64c95fc7befd`. The publication job's real public metadata check passed: it fetched release/metadata/manifest and found 0.1.0 without opening payload assets. An independent HTTPS read confirmed the public Release page and exact [bootstrap asset](https://github.com/jakeisboss/bot-toucher-2/releases/download/v0.1.0/BotToucher2-0.1.0-bootstrap-win-x64.zip); `git ls-remote origin refs/tags/v0.1.0` matched that full source SHA. No compiled assets were committed to the source branch.
