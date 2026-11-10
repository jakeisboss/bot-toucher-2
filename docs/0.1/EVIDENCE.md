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

This is a cloud proxy denial, not evidence of a private repository or failed Windows client. A fresh repository-page inspection reported public visibility. Live release check/asset publishing are UNVERIFIED here; no credentials were invented, no verification bypass used, and no compiled Git-blob fallback was made. The manual Windows workflow and release script provide the intended verification/publication route.

## Platform distinction

Portable tests use real temporary files/streams and controlled launch/readiness/fault boundaries. They cover transaction logic and filesystem effects on Linux. Native Windows process exit, sharing locks, WPF rendering, physical resizing, readiness, update/rollback/reapply and power interruption remain Windows-dependent and must be tested there. The provided Windows scripts have not been executed on this host.
