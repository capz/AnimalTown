# Release versions: xx.yy.zz.bb

AnimalTown uses four parts, for example `0.3.2.a1b2c3d4e5f6`:

| Part | Meaning | Changes when |
| --- | --- | --- |
| `xx` | User-controlled major | Only when the user explicitly requests it |
| `yy` | Feature counter | A `feat:` change lands |
| `zz` | Bugfix counter | A `fix:` change lands |
| `bb` | Source identity | First 12 lowercase hexadecimal characters of the full Git commit |

The initial baseline is `0.0.0`; no major bump has been requested. Counters are independent: features do not reset
bugfixes, and changing `xx` does not reset `yy` or `zz`. Numbers are not padded
or limited to two digits. This is the project's requested scheme, not SemVer.
Breaking-change markers (`!`) never change `xx` automatically.

## Describe each integrated change

Use a conventional PR title and retain that title when merging:

```text
feat(water): add river bank meshes
fix(water): close shoreline corner gaps
docs: describe terrain authoring
```

`feat:` increments `yy`; `fix:` increments `zz`. The other accepted prefixes are
`docs:`, `chore:`, `test:`, `ci:`, `build:`, `refactor:`, `style:` and `revert:`.
They change the hash without changing the numeric counters. Optional scopes and
`!` are accepted. Split independent feature additions and bugfixes into separate
PRs so each has its own classification; supporting fixes within a feature PR
belong to that feature's integration unit.

The GitHub workflow checks PR titles on open, edits, new commits and reopening.
For a normal GitHub merge commit, versioning reads the PR title in the merge
message body and counts the PR once. For squash merges/direct commits it reads
the commit subject. Rebase merges count each first-parent `feat:`/`fix:` commit;
use one classified commit per logical change and non-feature prefixes for support
commits. Preserve the default PR title in merge messages. Unknown post-adoption
integration titles fail version resolution rather than silently missing a bump.
History-sync merges are not counted as another feature/fix.

## Reproducible calculation

`version.json` stores the starting counters, user-controlled `xx` and baseline
commit. `tools/version.ps1` walks first-parent integration history after that
baseline, counting classified changes. History before this policy was introduced
is retained as the baseline and is not retroactively classified. CI never pushes
counter commits, and calculation does not depend on the order of release jobs,
successful uploads, run numbers or tag discovery. Retrying the same source commit
produces the same version. A documentation commit changes `bb` only.

Only change `xx` in `version.json` after an explicit user request. Leave the
baseline commit and starting `yy`/`zz` unchanged when doing so. CI does not infer
major changes from code, PR labels, commit markers or workflow dispatches.

Use a full clone (`fetch-depth: 0` in Actions). Missing baseline/history is an
error, never a fallback to zero counters. Inspect a local identity with:

```powershell
./tools/write-version.ps1
```

The resolver writes `out/version.json` with the version, full commit, dirty flag,
baseline and classified changes. Local dirty builds display `-dirty` separately
from the clean version identity and cannot be released. Branch builds have their
own hash and integration-history preview; the merged main commit gets its final
identity from that history.

## Build and release identity

The game display, disc title/banner and ISO filename use the four-part version.
`BUILD.txt` inside the ISO, the server and release records the version alongside
the full source revisions. Archive names include it, for example:

```text
AnimalTown-0.3.2.a1b2c3d4e5f6.iso.zip
AnimalTown-server-0.3.2.a1b2c3d4e5f6.zip
AnimalTown-tools-0.3.2.a1b2c3d4e5f6.zip
```

GitHub release tags use `v0.3.2.a1b2c3d4e5f6`. Releases include `version.json` and
its checksum. Packaging rejects console/backend outputs whose source commit,
version or clean state differs from the checkout. Existing draft/upload hash
checks and idempotent publishing remain in force.

Validation-base discovery recognizes both new `vxx.yy.zz.bb` and legacy
`build-<hash>` tags on first-parent ancestors. Tags must contain the tagged
commit's actual 12-character hash. A rerun excludes its own commit when choosing
the previous release, so it cannot skip checks by discovering its own tag.

`tests/versioning.ps1` exercises classification, merge/rebase-compatible history,
independent counters, explicit major control, deterministic rebuilds, metadata,
bad history/configuration and old/new release anchors. It runs with build-policy
checks through `tools/test-changes.ps1` and the release workflow.
