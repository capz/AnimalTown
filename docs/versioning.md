# Incremental main and PR versions

The frozen starting version is **0.11.38**, corresponding to main commit
`7e3dd9599709f553d13d538a320a13f3d410e861`. `version.json` stores this checkpoint
and `increment: minor`. Major stays fixed. Minor advances once and patch resets
to zero for every PR merged to main,
including documentation, features, bugfixes and reverts. Commit titles and the
number of implementation commits have no effect.

| Build | Version example |
| --- | --- |
| Current main baseline | `0.11.38` |
| First PR merged after the checkpoint | `0.12.0` |
| Next PR merged to main | `0.13.0` |
| PR built while main is `0.13.0` | `0.13.0+a1b2c3d4e5f6` |

PR versions use current main's numeric version plus `+` and the first 12 lowercase
hexadecimal characters of the **actual feature-head commit**. They never predict
the next minor, count feature commits, or use GitHub's synthetic test-merge hash.
A stale feature branch still uses the current main snapshot fetched for that build.
Pushing more commits to a PR changes only its hash while main stays unchanged.
Version configuration comes from the selected main commit, so editing it on a
feature branch cannot alter that PR's numeric version. The one-time adoption PR
uses its new schema-2 checkpoint while main still has the legacy schema.

Only change the frozen baseline or major/minor components at the user's explicit
request. Conventional titles such as `feat:`, `fix:` and `docs:` remain useful for
readability but do not control versions or block version calculation.

## Counting integrations correctly

The resolver uses GitHub's merged PR records for base `main`, then includes only
integration boundaries on the selected main snapshot's first-parent history after
the checkpoint. Each PR number is counted once. GitHub reports the merge commit,
squash commit, or final rebased commit in `merge_commit_sha`; therefore normal,
squash and multi-commit rebase merges all increment minor exactly once. See
[GitHub's integration-boundary contract](https://docs.github.com/en/rest/pulls/pulls#get-a-pull-request).
Unmerged PRs, PRs targeting another branch, baseline history, and integrations after
the selected snapshot are excluded. Duplicate or invalid records are rejected.

No counter commits are pushed by CI. Parallel branch jobs cannot consume minor
numbers, and retries do not increment anything. Main releases are numbered by
merged history rather than successful build counts, timestamps, tags or job order.
A failed release can be retried at the same number. Direct commits to main do not
count as PRs and cannot publish over an existing numbered release; integrate
changes through a PR. Missing history or API failures fail rather than guessing.

The first merge under this policy publishes **0.12.0**. To make a release, merge
the reviewed PR into main; the existing workflow creates the tag and publishes
the matching NKit ISO and backend after checks pass. No manual tag is needed.
Numeric main builds publish as regular GitHub releases. Hash-suffixed PR builds
remain prereleases, so GitHub's latest-release endpoint identifies the main channel.
Every release includes `update.json`, a maximum 2 KiB document containing only
the version and ISO name, URL, byte length and SHA-256. The updater can fetch
`https://github.com/capz/AnimalTown/releases/latest/download/update.json` without
loading release notes, author metadata or preview builds. The feed is included
in release checksums and generated from the exact packaged ISO before publication.
All new merges use the minor policy, including fixes and documentation. Major
changes remain explicit. Historical configurations without `increment` keep
their original patch-counting behavior, so older builds remain reproducible.

## Main snapshots and local builds

The exact source and main commits are recorded separately in `out/version.json`.
Rebuilding the same pair produces the same identity. Rebuilding a feature commit
after main advances deliberately uses main's newer numeric version.

Use a full clone. Before a local branch build, refresh main and prepare metadata:

```powershell
git fetch origin main
./tools/write-version.ps1 -Refresh
```

Local `main` checkouts resolve against their own HEAD; branch checkouts use
`refs/remotes/origin/main`. Detached CI checkouts explicitly select `main` or
`branch` through `ANIMALTOWN_VERSION_BUILD_KIND` and pin the main ref through
`ANIMALTOWN_VERSION_MAIN_REF`. An older main rebuild uses that older main commit,
not whichever commit happens to be latest on the remote.

GitHub history is normalized into ignored `out/version-context.json`, keyed by
main commit and configuration SHA-256. Console, backend and packaging reuse the
same context without another API call or a token in the compilation container.
Changing main or the frozen configuration invalidates the cache. The resolver can
read public PR history anonymously; CI reads the same API with its job token when
preparing the context. That token is not passed into compilation containers.
`-Refresh` discards the cached history. Offline builds require an
already prepared context for the same snapshot.

## CI and published artifacts

Every PR runs version-policy tests. Same-repository PRs also build and publish
the full console/backend prerelease with the source-head hash suffix. Fork PRs
run read-only policy checks, without publishing access. Rebase older feature
branches onto main after adopting this policy so their version tools and
configuration use schema 2.
New PR heads cancel obsolete PR builds; main releases are serialized and are
not canceled by newer main pushes.

Main merge builds publish the numeric version, including documentation-only PRs.
The regular path-based validation planner still selects relevant suites;
`-ForceRelease` requests artifacts without forcing unrelated test suites.
Console/backend/source consistency, clean-checkout checks, disc verification,
uploaded asset hashes and idempotent publication remain enforced.

The game display, disc metadata, archives and release tags use the same identity:

```text
AnimalTown-0.11.12.nkit.iso
AnimalTown-server-0.11.12.zip
v0.11.12

AnimalTown-0.11.12+a1b2c3d4e5f6.nkit.iso
AnimalTown-server-0.11.12+a1b2c3d4e5f6.zip
v0.11.12+a1b2c3d4e5f6
```

Validation-base discovery recognizes numeric main tags, branch `+hash` tags,
legacy four-part `vxx.yy.zz.hash` tags and `build-<hash>` tags. Hash-bearing tags
must contain the tagged commit's actual hash. Main history uses first-parent
ancestors, so unrelated branch releases cannot hide pending main changes.

`tests/versioning.ps1` builds disposable Git histories covering the checkpoint,
feature/stale branches, normal/squash/rebase integration boundaries, historical
snapshots, direct commits, duplicate records, overflow, bad configuration/history,
REST pagination/filtering, offline cache reuse and old/new release anchors.
