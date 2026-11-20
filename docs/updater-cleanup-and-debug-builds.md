# Updater cleanup and in-game development build selection

Status: technical proposal, 20 November 2026. The wireframes below are for
design approval before UI implementation. This change documents the approach;
it does not enable deletion, network browsing, or build switching.

Interpretation: “upload to that version” means **update this console to a
selected published non-release build**. Publishing a new build remains a CI task.

## Verified starting point

- AnimalTown source inspected: `9b5bc78`, with the proposal based on main
  `1670545`. Both pin updater `4adce94e59d77b12d56093795a4a9fa0f980dbb4`
  in `build/updater.lock.json`.
- The production updater reads `dvd:/version.md`, checks the running identity,
  unmounts the disc, downloads to `gcloader:/games/<published filename>`, verifies
  size/SHA-256/header/version, selects the image with physical DVD readback,
  and hands off to a prepared Swiss. It currently preserves every prior image.
- `StableImageDiscovery.ScanAsync` and `ImageFiles.TryReadVersion` can recognize
  renamed ISO/GCM files by embedded identity. The scanner returns only the
  highest numeric version, excludes development identities, and is deliberately
  unreachable from production. `tools/build.py` and `tests/test_native_scan.py`
  enforce that exclusion. Detection must be extended and reconnected for cleanup.
- `docs/updater-build.md` still describes the older scan/delete-selected flow.
  The updater source and its README describe the current version-file flow;
  reconcile that guide when implementing this proposal.
- In-game Settings opens the gated Debug/testing tab in
  `game/AnimalTown.GameCube/Program.cs`. Its panel already supports paging,
  `MenuNavigation`, IPL body text, Gorditas headings and rounded selection rows.
  `UpdaterLaunch.Request()` currently supports title-screen stable updates only.
- Stable discovery uses GitHub `/releases/latest`, bounded to 32 KiB and 64
  assets. `ReleaseInfo.Parse` can validate one hash-bearing release in an array,
  but there is no development-build browser or exact-target launch protocol.

## 1. Clean up old game copies after installation

### Eligibility and retention

Recommend removing superseded **stable** copies after a successful stable
installation, while retaining one previous stable image as a recovery candidate.
This leaves the newly installed game plus one fallback instead of accumulating
all releases. The fallback is not a proven bootable rollback unless its complete
digest has also been verified; a recognizable header alone cannot prove that.

| File/case | Default action |
| --- | --- |
| Newly selected target, including FAT case aliases | Always retain |
| Highest pre-existing stable version below target | Retain one deterministic recovery candidate |
| Other recognized stable images numerically below target | Eligible for deletion |
| Equal-version copies | Retain; defer duplicate-content removal |
| Stable images above target | Retain |
| Hash-bearing development images | Retain; hashes do not establish age |
| Preview installation, including an intentional downgrade | Skip automatic cleanup entirely |
| Foreign/truncated/invalid images, unknown files, saves | Retain |
| `.bak`, `.update`, SDK staging and updater recovery paths | Retain; separate recovery ownership |

Use the existing `AMTE01` ID, GameCube magic and `AnimalTown / <version>`
header checks, not a filename pattern or filesystem timestamp, to identify a
candidate. Search ISO/GCM files recursively on the GC Loader volume, including
renamed `.nkit.iso` files. Never delete directories or other volumes. Normalize
paths using the mounted filesystem's canonical spelling and FAT comparison;
reject traversal, ambiguous aliases and paths outside `gcloader:/`.

Choose the retained candidate by highest numeric version below the target,
then the existing case-insensitive ordinal path tie-break. Prefer an image
whose updater receipt and full digest can be verified when available. Record
which selection rule was used; do not describe an unverified candidate as
verified. If no distinct candidate exists, cleanup is a no-op.

### Ordering and failure behavior

1. Read and validate the launched disc's version, release `dvd:/`, mount GC
   Loader, and prepare Swiss using the current startup sequence.
2. Download/publish the target with the current contiguous writer. Verify its
   complete card bytes, length, digest and embedded version. An existing
   destination can be reused only after the same checks; never truncate it.
3. Close HTTP/TLS and stop networking. Prepare Swiss's exact target arguments.
   Honor cancellation before selecting the image.
4. Call `Storage.SelectGcLoaderDiscImage(target)` and require successful
   physical boot-header readback. Before this returns, delete no old image.
   “Installation complete” here means verified publication and activation,
   not proof that the next game session will boot successfully.
5. Discover candidates in a read-only pass after activation. Reuse one
   1,088-byte header buffer and bounded directory cursors, yielding for input.
   Complete discovery and select the recovery candidate before deleting anything.
   Any scan error, excessive depth or identity ambiguity skips cleanup.
6. Run a second bounded traversal to delete eligible files. Close enumeration
   handles before mutating a directory: use a bounded batch with stable path
   checkpoints, reopen/rescan that directory after each batch, and never rely on
   a FAT directory cursor remaining valid across deletions. Recheck each file's
   canonical path, length and header fingerprint against its planned record
   immediately before deleting it. Revalidate the protected target before the
   first batch and after storage errors. Stop on changed media or identities.
7. Keep counters for removed files/bytes and retained/skipped files. A cleanup
   failure must not relabel a successful installation as a failed download.
   Stop further deletion, retain the verified target and prepare its normal
   handoff if storage remains usable. Media/target validation failure must stop
   handoff as well. Report partial cleanup through existing status/diagnostics.

Use a bounded, flushed cleanup receipt under an updater-owned directory to
record the target path/version/digest, retained path, policy/schema and completion
state. Persist it before the first deletion. A malformed/incomplete receipt
never authorizes deletion on its own: a later updater run must revalidate the
target bytes, activate it and rescan before another cleanup attempt. No deletion
runs automatically at game startup. Missing old files count as already removed.
An interrupted receipt write therefore loses cleanup progress, not an install.

Cancellation during cleanup stops between file operations and keeps the new
target; it cannot restore files already removed. Before Swiss handoff, offer the
same normal completion/exit behavior if the user stops cleanup. Never reclaim
old images before downloading to make space: insufficient room for target plus
existing copies is a pre-install failure.

FAT deletion/flush is not a power-loss transaction. Both images exist until
activation, and the target plus retained candidate remain outside deletion, but
filesystem corruption from removal/power loss still needs hardware acceptance.

### Implementation boundaries

Extend `StableImageDiscovery.cs` into reusable bounded identity traversal plus a
separate `CleanupPolicy`/`ImageCleanup` component. Do not reuse
`InstallTransaction.ActivateAndDeleteSelected`: its single guessed installation
does not represent a volume-wide policy. Keep the version-file source identity
and fixed `/games` destination in `Updater.cs`/`LaunchRequest.cs`.

Replace the blanket AOT “no scan” assertion with an ordering invariant: no
installed-image traversal/deletion during initial version/latest-release checks,
and cleanup reachable only after successful target verification and activation.
Keep existing depth/handle/memory bounds; exceeding them preserves files.

First ship the traversal in report-only mode and inspect candidate decisions on
representative cards. Enable deletion only after fault-injection and real-media
validation. Recognizing header-bearing copies is an install policy, not a
general-purpose duplicate-file cleaner.

## 2. Select a published development build from in-game debug options

### Discovery and bounded networking

Add `Development builds...` to the existing Debug/testing tab. Entering it
starts an explicit, cancelable read-only request; normal gameplay and title
startup make no new release-list calls. Enable installation for disc/GC Loader
setups where `UpdaterLaunch.Available` is true. Depot installations show the
existing Depot update guidance, since an ISO updater cannot update their SD app.

Use `GET /repos/capz/AnimalTown/releases?per_page=1&page=N`. One release per
request preserves the tested 32 KiB response budget and avoids buffering many
release bodies/assets in the console heap. Browse lazily: at most eight inspected
publications per load action, four visible build rows, and 24 retained build
descriptors. Offer “Load more” even when an inspected window contains only stable
or incompatible releases. An empty API page ends discovery. Display counts as
loaded results, never as a guaranteed total of all builds.

Accept published, non-draft prereleases with a valid current `M.m.p+12hex`
identity or supported legacy `M.m.p.<12/40hex>` identity, plus one valid direct
NKit ISO. Numeric versions remain stable even if an old publication has the
prerelease flag. Skip unrelated releases and label incompatible/missing assets
without making them installable. Preserve GitHub's publication order; do not
sort commit hashes or imply that branch counters determine recency.

Retain only release ID, exact tag/version, publication date, asset ID, size and
digest, plus a short sanitized label. Free the JSON/body before the next request;
clip printable ASCII labels for the current fonts. Deduplicate by release ID
when publications change between pages. Refresh restarts traversal and clears
stale entries. This is a live paginated listing, not a publication snapshot.

Handle offline/DNS/TLS errors, timeouts, 403/429 rate limits, empty results and
oversized metadata as explicit browser states with retry/back. Respect server
retry limits and avoid automatic polling. No GitHub write token belongs on the
console: only published releases are available, excluding drafts and private CI
artifacts. Share the updater's clock, entropy, CA and hostname validation rules.

Extract a small source contract from the pinned updater checkout: build
identities, bounded release parsing, launch-envelope validation and public trust
bootstrap data. Have `tools/build-updater.ps1` stage the allowlisted sources and
notice into `out/updater-contract` for game compilation, checking their pinned
source/hash. Both binaries consume that exact revision. Confirm game/updater SDK
compatibility before sharing HTTP bootstrap code; keep SDK-specific lifecycle
wrappers local. Reuse existing font/shape assets; this needs no new art.

### Pin the selected target through a new protocol

Add v4 alongside the existing v1-v3 readers. Proposed separate argv entries:

```text
<updater DOL path>
--animaltown-update=4
<running BuildInfo.Version>
preview
<numeric release ID>
<exact tag>
<numeric asset ID>
<expected SHA-256>
```

Validate exact argument count, scalar bounds and supported tag/hash syntax;
the existing native bridge has a 4 KiB envelope. Pass identities, not arbitrary
download URLs or filesystem targets. Updater independently fetches
`/repos/capz/AnimalTown/releases/<release ID>`, checks the selected tag/asset
ID/digest, requires a published development build, then derives the allowlisted
repository/tag/asset URL from verified metadata. A removed release or changed
asset fails explicitly, with no silent substitution of “latest.”

Factor the existing install pipeline to accept a validated release target.
Stable v3 keeps numeric no-downgrade semantics. Explicit preview v4 permits
different branches and lower numeric versions after user confirmation; equal
installed identities show “Current” and do not offer redundant installation.
Both modes keep size, SHA-256, embedded identity, contiguous publication,
collision protection, verified physical selection and Swiss autoload checks.
Preview mode always disables automatic old-image cleanup.

### Save, unwind, then launch

Selection opens a confirmation showing current and target versions, size and
restart behavior. “Save & update” is the action; Back is the default focus.
Explain that older builds may not read current saves. Do not migrate or rewrite
save formats as part of switching versions.

Refactor `SaveFromMenu` to return a structured success result, including memory
card close success. Its current `void` return and caught errors are insufficient
for deciding that it is safe to launch. Block the handoff on save failure,
damaged saves or sessions with saving disabled; preserve the live session and
show its error. A discard-progress alternative would require a separate design.

After a successful save, set a pending immutable update request. Exit
`RunSession`, dispose `TownGame`, release card/render/audio/streaming resources,
and handle that request in the outer application loop before showing the title.
Extend `UpdaterLaunch` to prepare the DOL and set v4 arguments there. Stop and
dispose browser networking before this transition. Prepare/launch failures
return to a usable title/error screen with the saved progress intact. Selecting
a version starts this flow directly from debug options; there is no second
manual Update choice on the title screen.

### Review mockup

Reuse the existing 550x350 panel at `(45,75)`, television safe area, rounded oat
paper, fern ink text, Gorditas heading, IPL labels and moss selection pill from
`docs/visual-identity.md`. The wireframe shows hierarchy and controls; build IDs
are illustrative. Use text labels as well as colour for status.

```text
╭──────────────────────────────────────────────────────╮
│ Development builds                                   │
│ Current: 0.30.0                                      │
│                                                      │
│ > 0.30.0+a1b2c3d4e5f6       20 Nov      89 MiB        │
│   0.30.0+112233aabbcc       19 Nov      88 MiB        │
│   0.29.0+ffeeddccbbaa       18 Nov      88 MiB        │
│   0.29.0+123456abcdef       18 Nov      87 MiB        │
│                                                      │
│   Load more                                          │
│ Published development builds / 4 loaded               │
│ A: details     X: refresh     B: back                  │
╰──────────────────────────────────────────────────────╯

╭──────────────────────────────────────────────────────╮
│ Update to this build?                                │
│ 0.30.0  ->  0.30.0+a1b2c3d4e5f6                      │
│ Download: 89 MiB                                     │
│ Your town will be saved. The game will restart.       │
│ Older builds may not read newer saves.                │
│                                                      │
│ > Back                                               │
│   Save & update                                      │
│ A: select     B: back                                 │
╰──────────────────────────────────────────────────────╯
```

Loading, empty, offline, rate-limited, save-error, current-build and Depot states
use the same panel and readable action labels. Final implementation must measure
the longest version/legacy identity with actual IPL metrics, wrap details and
keep controller hints visible at 640x480. Review a rendered mockup using the
existing assets and obtain design approval before implementing the UI, as
required by the supplied AGENTS instructions.

## Delivery and acceptance

1. Implement updater traversal/policy/receipts and v4 contract first, keeping
   stable behavior intact. Update the README, native ordering guards and stale
   game guide. Publish the updater through its existing CI.
2. Pin the validated updater commit in AnimalTown; wire contract staging and
   read-only catalog discovery. Review the rendered picker/confirmation mockup.
3. After design approval, implement the menu, explicit save result and outer-loop
   handoff. Add the four-line request summary to `version.json` when the feature
   is implemented, without claiming player behavior in this proposal-only change.
4. Use `tools/test-changes.ps1` for affected checks and update validation routing
   for the new paths. Existing release CI must package ISO, Depot app, backend
   and tools from the same source/main snapshot. Merge only after green checks.

| Area | Required evidence for implementation |
| --- | --- |
| Cleanup policy | Mixed stable/preview versions; renamed ISO/GCMs; numeric ordering; equal/newer preservation; retained candidate; FAT aliases; staging exclusions |
| Ordering/recovery | Download/hash/readback/helper failures delete nothing; cancellation; insufficient space; scan failure before deletion; changed candidate; receipt corruption; interruption between each delete; retry idempotence |
| Native limits | Real SDK AOT at 1 MiB heap, wide/deep/candidate-heavy trees, bounded handles and allocation, responsive worker/UI and cursor-safe FAT deletion |
| Catalog/selection | Current and legacy tags, numeric prerelease exclusion, paging/deduplication, response/asset caps, missing digest, literal/%2B URLs, 403/429, deleted/changed target and exact release/asset pinning |
| In-game lifecycle | Debug gating, Back/refresh navigation, save and close failures block launch, disabled-save/Depot behavior, resources unwind before argv handoff, title stable update remains intact |
| Emulator visuals | Actual assets/fonts, safe area, long identities and every error/loading state; approval against the rendered mockup |
| Hardware | GC Loader on FAT32 and exFAT, BBA HTTPS, renamed/fragmented existing copies, physical mapping/readback, Swiss autoload, removal/power interruption on disposable cards |

Dolphin can validate UI and handoff structure. It cannot establish GC Loader
deletion durability, BBA performance or physical recovery behavior.
