# Offline resident jobs implementation

Implementation and qualification record for [the full phased plan](villager-jobs-offline-plan.md).
The UI set was explicitly approved on 20 November 2026; see
[the mockups](concepts/villager-jobs-ui-mockups.md).

## Implemented behavior

The domain owns complete offers, personal errands, shared favors, bindings,
obligations, receipts, mail, stable identities and time reconciliation. Presentation
uses explicit commands and reads snapshots; rendering never rolls an offer or
pays a reward. The normal game now connects all twelve templates.

| Phase | Implementation | Current verification |
| --- | --- | --- |
| 1 | Explicit contracts, catalog pools, bounded state, memory/storage limits | Domain, native fixture and resource qualification |
| 2 | Nonce-based town namespace; monotonic NPC/player/job/item/mail identities; keyed relationships | Identity, profile reuse and full codec roundtrips |
| 3 | 15 delivery / five retrieval slots per owner; six shared slots; 48 stored offers; 128 reward slots; protected parcels | Acceptance, replay, capacity, parcel restrictions and save checks |
| 4 | Dated second-resolution RTC; exact expiry/season boundaries; chronological mail catch-up; separate active cooldown clock | Calendar, rollback, timing and live-overlay checks |
| 5 | Reward pools, independent outfit geometry, floor/wall rewards, owed payouts | Content/payout tests; six-species clothing and room-covering controller captures |
| 6 | Ask/accept/decline, reminders, jobs list, protected parcels and contribution selectors | Approved UI; controller acceptance, protected parcels and actual item selectors |
| 7 | Clothing/property deliveries; recipient outfit and payment | Actual domain handoffs, full-pocket and wallet retries |
| 8 | Saved referral decisions, four-borrower maximum, exact pickup and return | Multi-stage/reload/retry tests |
| 9 | Ordinary fruit/fish/insect handoffs and net flower planting | Actual inventory/planting API tests, claimant protection |
| 10 | Two balls, four seasonal snowballs, rolling/collision/combining, persistent snowmen and exact destruction | Actual fixed-step physics and save evidence tests |
| 11 | Composed queued letters, attachments, NPC grading/replies, player mail and notice-board posts | Unicode/rank tests, full mailboxes, offline delivery and full text roundtrips |
| 12 | Two-card departure/return, fifth visitor capsule, canonical decisions, recovery, explicit NPC exchanges | Foreign/moved deliveries and write-boundary fault injection |
| 13 | Four-profile ownership, deletion guards, legacy request retirement, protected opening parcels and notice-board employment task | Profile, migration, opening and save qualification |
| 14 | Managed/native/console qualification, captures, performance and same-commit delivery | Managed/native AOT, controller/save qualification and measured software-emulator results below |

The six existing employment payments remain 1,000 debt credit each. The new final
notice-board task adds no seventh payment. Published catalog-v1 saves with a
completed six-stage tutorial migrate to completed; in-progress stages retain
prior credit. Existing generic parcels are bound only for the fixed opening
sequence. Ordinary generic parcels cannot replace a resident-job instance.

## Ownership and travel

Four permanent resident slots stay intact. A separate fifth job-owner partition
and visitor profile carry the traveller's identity, pockets, personal jobs,
bindings, relationships, command sequence and portable earned entitlements.
The same cumulative 15/5 allowance applies abroad. Shared occupancy stays in its
requesting town. Stored offers remain at their NPC's town, keyed by player/NPC,
within the 48-offer budget, including offers belonging to prior visitors.

The source is locked before destination preparation. The source's durable
outbound decision permits destination activation. Return preparation is saved
before the destination's durable return decision permits unlocking home.
Recovery requires both matching towns and replays the existing transfer;
removing a card never unlocks the source. Another local resident may still play.
Copied saves with identical town IDs are refused as destinations.

Both selected NPCs are explicitly confirmed when two full six-resident towns
exchange neighbours. Their actual identity, personality, cooldown/patience,
species, outfit and keyed friendships survive; their new home belongs to the
chosen destination slot. Pending moves disable interactions with those slots.

Letters and catalogue ordering are home services while visiting; local letter
favors require a permanent resident sender. Offline travel requires saving.
Play without saving retains the complete local job system in memory.

## Storage and memory architecture

Combined saves use schema 15 and catalog 2; split town records additionally
retain main’s schema-10 snow checkpoints. The two older combined schema-14
layouts are distinguished by their original catalog: catalog 1 contained snow,
catalog 2 contained the job prerelease. Frozen saves written by both actual
older implementations test those imports. Schema 15 gives snow and jobs separate
bounded length fields, so neither extension overwrites the other. Earlier
supported saves remain readable. The new economic ledger has its own strict schema and is stored in the
existing economy record, with no additional save-repository record or SQL
column. Ledger schema 12 projects its dated-second watermark into the same
committed runtime record as NPC availability. A clock-only checkpoint writes
1,336 encoded bytes across four logical records, including both publication
heads, and leaves both terrain and economic/mail records byte-identical.
The consolidated store writes an 8 KiB data page and an 8 KiB checkpoint per
logical record: a healthy clock-only checkpoint therefore writes 64 KiB of
card sectors, excluding any initial conversion/growth/cleanup. Readback verifies
each sector.
Full snapshots retain their ordinary self-contained watermark. The decoder
requires the exact referenced runtime record for projected economies; schemas
7 through 11 remain readable.

UI atlases read the existing GX-tiled source in reusable 128-pixel pages rather
than allocating the entire font or portrait package. A font that previously
needed a 524,344-byte managed read now uses a 32 KiB encoded page, a 64 KiB RGBA
scratch and a 32 KiB upload buffer. Other atlas formats stay bounded to a
128-pixel page. The existing alpha/colour conversion and native formats remain
unchanged. Exact texel/hash tests cover every GX format, partial reads and
malformed headers; the pinned AOT filesystem runs real seek/read extraction
under normal and forced collection. The complete town-session call frame also
unwinds before the next title/load cycle, releasing the previous town's roots.

The game-owned native C build omits unused unwind tables: managed exceptions use
explicit setjmp/longjmp handlers and registered GC frames. At the table-only
checkpoint (`cbbe8cf`), this removed 380,832 loaded bytes without changing the
executable code or the 6 MiB heap. Later loader code is included in the final
native-image measurements below. Ordinary debug information remains in the ELF. Native exception/recovery and
forced-collection tests run with the same compiler flags.

The core uses fixed arrays and three independent persisted RNG streams. Ordinary
fixed-step work allocates no memory and performs no card I/O. Message bodies use
UTF-8 when it is smaller, otherwise lossless 21-bit Unicode scalars, with shared
2 KB blocks for short letters and exact arrays for longer bodies. A maximum
1,000-scalar body occupies 2,632 packed bytes even when every scalar differs.
Legacy UTF-8 and 24-bit scalar bodies remain readable and are compacted directly
without constructing a temporary UTF-16 string. Mail envelopes use lazy 16-entry
pages; empty reads allocate nothing, and discarding the last letter releases its
page. Discarding letters cannot pin a large text arena.
The native adapter reclaims normalization temporaries before retaining text.
There is no ICU dependency or retained decoded-string cache for every letter. Unicode 14 normalization/classification tables
contain 46,344 immutable data bytes, with the Unicode license packaged in Depot.

Encoding/decoding economic data directly in bounded byte buffers avoids a full
intermediate word-array copy of large mailboxes. Real 128-letter mailboxes for
all four profiles, with 1,000 supplementary Unicode scalars per letter, are
exercised by the capacity fixture. Incremental readback retains the immutable
economic buffer and checks its referenced sequence without a duplicate copy.
The six-MiB native heap keeps a 1,703,936-byte payload range available for
large economic-record buffers. Small renderer and mail objects cannot fragment
that range after travel rebuilds the world. It is part of the existing heap,
not an additional reservation. Six-MiB allocations keep their exact aligned
sizes; other heap sizes retain the previous 64 KiB size classes. Collection
coalesces adjacent free blocks, and no live object is moved. A negative control
using the previous allocator fails the new fragmented-save regression; the
updated allocator completes thirty growing checkpoints with retained mail and
world allocations.
Travel identity validation streams and verifies the complete referenced record
and retains only its 732-byte identity prefix; it does not allocate another
maximum-size economy buffer. The console managed heap reserves 6 MiB; this is 2 MiB
more than the previous build, needed for maximum mail and two-card validation.
Native normal and forced-GC tests exercise both full towns and complete travel,
including retained snow dents alongside every job, reward and full mailbox.
Identical mail envelopes share pages between validated snapshots. Each owner
copies a page before changing it, so reading, gifts, discards and profile cleanup
remain independent. This avoids another complete envelope table during readback.
Each travel card reuses its own validated text bodies during legacy conversion.
The repository asks optional storage preparation to finish that verified legacy
conversion before it encodes the next economic record. This avoids retaining
both the new 1.7 MB output buffer and a second legacy-validation buffer while
both full towns are live. Identity and revision guards run before preparation;
a failed preparation does not advance the live save revision. A native
regression retains both inspected towns and performs the additional migration
read, matching the console adapter rather than assuming already-converted cards.
Archive copying and cleanup give each large encoded record its own native call
frame and collect between records; a previous mirror cannot remain a GC root
while the next is allocated. Native regressions retain a full mailbox and
an additional 2 MiB of world data through copying, repeat readback and metadata
previews. A separate maximum-economy round trip saves again after its transfer
frames unwind, retaining 2 MiB in sixty-four renderer-sized buffers. Both normal
and forced-collection executions pass.

Travel previews read verified town identity, seed, revision and six NPC names
without constructing another town or decoding its mail. Every referenced record
is checksum-validated. Complete semantic/capacity checks still occur after the
renderers have unwound, before any transfer decision is published.
Normal towns retain their existing 32-block
backing reservation. Mail grows it when required; a CRC-protected root intent,
verified alternate copy and epoch publication precede deleting the old backing
file. Growth needs spare card workspace and preserves the old save on failure.
All 89 physical block writes in the dynamic-growth fixture have been torn and
retried, preserving the original records and ending with one backing item.

The game-owned record-store extension preserves the pinned SDK's DNDREC01 wire
format while extending its bounded backing-file ceiling to 2,048 blocks. Native
creation/read limits are patched and tested together. Actual card capacity still
limits allocation; the game does not reserve the maximum at startup.

No per-job character model or character texture is created. Clothes and world
objects share small vertex-colour meshes; new inventory categories reuse the
existing icon atlas. All gameplay character textures remain 128 pixels, with
256 pixels the maximum allowed character variant. The shared vertex-colour
geometry costs 17,280 native bytes in the measured six-species scene. Texture
residency and terrain ARAM are unchanged in that comparison.

## Controller and saved-state qualification

Dolphin controller runs exercise the production game, rather than calling the
completion API directly. The host fixture prepares disposable towns and stored
offers; acceptance, movement, handoffs, inventory selectors, letters, savings,
travel and saves use actual controller input. `--job-verify` independently loads
the exported committed records and rejects an incomplete objective. Controller
reports use `controls_completed`; a final pass also requires the independent
`jobs-state.json` result. Reusable modes live in `JobFixture.cs` and
`tools/jobs_smoke.py`.

| Required real-engine case | Evidence |
| --- | --- |
| Clothing/property deliveries | Actual recipient outfit/item and successful terminal receipt |
| Four-borrower retrieval | Mid-chain save, title reload, full-pocket refusal, ordinary item drop, exact pickup/return |
| Fruit, fish and insect | Actual contribution selectors and successful saved completion |
| Ball | Physical player push and actual collision with the requesting NPC |
| Snowman | Two snowballs combined, reported, saved/reloaded and destroyed with the shovel |
| Flowers | Four net planted flowers in the actual 20-tile home acre |
| Letter | Controller-composed body, scheduled 08:00 delivery, real reply, read/present collection |
| Full mailbox | Pending reply retained until an older letter is discarded, then collected |
| Full wallet | Earned 197-currency obligation saved/reloaded; 1,000 deposited; exact wallet 99,196 after collection |
| Foreign and moved-recipient deliveries | Actual departure, remote handoff, return, and independently verified records from both cards |
| NPC exchange | Explicit pair confirmation; identities, species and both cards checked |
| Interrupted departure | Committed home lock, failure before destination publication, UI recovery and safe return |
| Room rewards | Actual backpack actions apply floor/wall items and persist both coverings |

The screenshot manifest records the source version and SHA-256 for each capture.
It distinguishes development captures from the final release's matching-source
controller captures. See [captures](performance/offline-jobs/captures.json),
[clothing](screenshots/offline-jobs/clothing-final.png),
[retrieval](screenshots/offline-jobs/borrower-reload.png),
[snowman](screenshots/offline-jobs/snowman.png),
[letter](screenshots/offline-jobs/letter-composition-final-112b.png), and
[travel recovery](screenshots/offline-jobs/trip-recovery.png).

## Acceptance traceability

The supplied specification's sections 14.3–14.8 are exercised by
`VillagerJobChecks`, `OfflineJobChecks`, `JobWorldChecks`, `JobMailChecks`,
`JobLetterChecks`, `JobRecoveryChecks`, `JobTravelChecks`,
`VillagerJobIdentityChecks`, `VillagerJobSaveChecks`,
`JobMailboxCapacityChecks` and `IncrementalSaveChecks`. Independent Unicode
oracles live in `JobTextBodyChecks` and `JobLetterChecks`; the native fixture
runs the production compiler, allocator and card adapter with normal and forced
collection. Opening employment, four-profile switching/deletion and live-world
menu behavior are included in the managed life/save suites.

The focused verification ran 4,713,196 gameplay/render/texture assertions,
347,358 rendering-service assertions, 889 card-adapter checks and 57,856 SDK
record-store checks. Backend integration also passed. The machine-readable
[qualification record](performance/offline-jobs/qualification.json) separates
encoded save bytes, physical card writes, host native measurements and emulator
observations. Controller/save reports cover section 14.9 and the plan's real
interaction gates; screenshots are supporting evidence rather than the success
criterion.

## Measured resource costs

Each scene measured 240 frames, excluding the first five from frame/CPU means,
at native 640×480, AA off, runtime profile 4. Runs were sequential, with no
other emulator or local native build. These are short Dolphin 2503 OpenGL/llvmpipe
samples, not hardware limits.

| Scene | Guest FPS | CPU mean | Frame p50 / p95 | Native resources | Managed reservation |
| --- | ---: | ---: | ---: | ---: | ---: |
| Prior-main baseline | 57.03 | 10.83 ms | 16.68 / 16.68 ms | 2,890,252 B | 4,194,304 B |
| Current, outfits off | 55.90 | 11.18 ms | 16.68 / 33.37 ms | 2,907,532 B | 6,291,456 B |
| Current, all outfits | 54.18 | 11.91 ms | 16.68 / 33.37 ms | 2,907,532 B | 6,291,456 B |
| Current, snowfall + outfits | 54.18 | 11.51 ms | 16.68 / 33.37 ms | 3,183,500 B | 6,291,456 B |
| Current, returned maximum economy | 58.92 | 5.11 ms | 16.68 / 16.68 ms | 2,907,532 B | 6,291,456 B |

The prior-main source is `1670545`; it includes earlier main features. The
difference from that baseline is a whole-build comparison, not an isolated
causal cost of jobs. Outfits-off/on are the same implementation and scene. The returned maximum-economy case uses the preserved migrated town, a later RTC and a slightly moved player; it is an observation of that loaded town, not an isolated estimate of mail CPU cost.
No managed collection occurred during any of the five captures.

| Scene | Terrain preparation | Actor submission | Animation/skinning |
| --- | ---: | ---: | ---: |
| Prior-main baseline | 4.077 ms | 0.968 ms | 1.870 ms |
| Current, outfits off | 4.097 ms | 1.005 ms | 1.931 ms |
| Current, all outfits | 4.189 ms | 1.550 ms | 1.977 ms |
| Current, snowfall + outfits | 2.655 ms | 1.579 ms | 1.992 ms |
| Current, returned maximum economy | 1.433 ms | 0.295 ms | 0.006 ms |

Current, outfits off: MEM1 24,454,968/25,165,824 B, leaving 710,856 B; whole ARAM
10,502,144/16,777,216 B. Texture bytes 1,034,272; terrain ARAM 2,811,136. Managed extent
2,133,144 B, peak 2,134,360 B; extent includes holes and is not retained/live usage.
Checkpoint totals: none requested in this timing-only case.

Current, all outfits: MEM1 24,454,968/25,165,824 B, leaving 710,856 B; whole ARAM
10,502,144/16,777,216 B. Texture bytes 1,034,272; terrain ARAM 2,811,136. Managed extent
2,133,144 B, peak 2,134,360 B; extent includes holes and is not retained/live usage.
Checkpoint totals: 31.89 s, 10.85 s.

Current, snowfall + outfits: MEM1 24,731,064/25,165,824 B, leaving 434,760 B; whole ARAM
10,502,144/16,777,216 B. Texture bytes 1,034,272; terrain ARAM 2,818,752. Managed extent
2,135,696 B, peak 2,136,912 B; extent includes holes and is not retained/live usage.
Checkpoint totals: 31.89 s, 10.41 s.

Current, returned maximum economy: MEM1 24,454,968/25,165,824 B, leaving 710,856 B; whole
ARAM 10,502,144/16,777,216 B. Texture bytes 1,034,272; terrain ARAM 2,811,136. Managed
extent 3,782,168 B, peak 6,291,456 B; extent includes holes and is not retained/live
usage. Checkpoint totals: 244.42 s, 114.40 s, 114.40 s.

The winter capture leaves only about 425 KiB of MEM1 free. This is a tight margin; physical-hardware validation remains necessary. Summer outfits off/on share the same scene and implementation. The snowfall scene is at 21:00 rather than noon, so it includes different shadow and lighting work.

The current DOL loads 7,963,968 code bytes, 503,904 data bytes and 7,228,564 BSS bytes (15,696,436 B total). The prior-main total was 12,616,100 B; the increase includes the extra 2 MiB managed reservation. The reservation is already inside BSS and must not be added twice. No new character textures are required.

The host NativeAOT fixture uses the actual pinned compiler and managed runtime,
with both normal and forced collection. Its live core allocation delta is
68,312 bytes, including the fifth visitor partition: 2,776 bytes above the
proposed 64 KiB target, below the enforced 72 KiB bound. Fully allocated 576-envelope
pages add a conservative 76,296 host-layout bytes; retained bodies are
charged separately. These x64 object-header measurements are not an exact PPC
resident-layout measurement. Unicode tables add 46,344 immutable image bytes.

Normal native jobs reach a 967,032-byte extent with forced collection.
Large-mail scenarios expose the reserved high-address range, so their extent
high-water mark reaches the full 6,291,456-byte reservation even while unused
holes remain. This diagnostic is an address extent, **not a retained/live-byte
count**. After each completed maximum scenario and collection, the fixture
retains 435,040 bytes of extent with forced collection, or 440,504 bytes in
normal collection mode. Both normal and forced-collection modes pass
full mail, two full towns, dense snow, all five owner partitions, legacy
validation, return and the subsequent world-loaded checkpoint. No heap increase
was needed for the final fragmentation fix. Ordinary clock ticks are allocation
free; capture-specific allocation and collection counts are in the resource
report.

## Card capacity, failures and migration

Normal first towns reserve 32 internal blocks plus two artwork/header blocks.
Maximum four-profile mail contains 512 real 1,000-scalar supplementary-Unicode
bodies; its self-contained combined snapshot is 1,447,176 bytes. The larger
simultaneous economy fixture is 1,702,736 bytes. Neither truncates bodies or
reduces the required capacities. Maximum-mail qualification uses disposable
16 MiB cards; the smallest official cards cannot hold that worst case and the
copy-on-write/growth workspace. A full card preserves the previous committed
snapshot and reports failure rather than dropping letters.

Legacy conversion sizes its first reservation from logical directory lengths
and copy-on-write workspace. It shares immutable text when validating the live
snapshot and verifies copied bodies through sector streaming. Initial file
creation can be interrupted before either root exists, including halfway
through zero filling. Reads continue from the complete legacy town. A write
validates that source before replacing only the unverified temporary archive;
an incomplete source never authorizes replacement. Both old files remain until
publication/readback proves the new copy. Corruption checks, selected-slot
saving, Play without saving and complete-town/profile deletion remain covered.

The card adapter has 889 passing host checks, including every one of 89 dynamic
growth writes torn/retried and 391 writes in maximum legacy conversion. Ordinary
reference publication has before/torn/after interruptions at every write;
two-card transfers cover 16 departure and 20 return publication writes. Each
recovers a complete old or new state and retains the same obligation/receipt.

The record store can retire pages referenced only by its older durable root
after publishing a verified clone of the current directory. If fragmentation
still prevents a contiguous record allocation, it streams an intact extent to
free space, verifies each sector, and publishes both directory checkpoints
before reusing the source. Packed neighbours move together. Torn writes and
retries cover both retirement and relocation; live sectors are never overwritten
while either durable root still references them.

The maximum-capacity qualification preserves the actual interrupted cards
from the first failed departure. The save-preparation fix recovers the same
traveller, activates the fifth owner partition, and completes the return.
Independent decoding verifies all **1,024 full 1,000-scalar bodies**, the actual
visitor job at slot 99, the home personal partition, no destination guest
remaining and both finished/unlocked trip journals. That executable subsequently
failed its final checkpoint because the world reload fragmented free memory.
It is recorded as a failed checkpoint, not a passing complete controller case.
See the [protocol observations](performance/offline-jobs/final-preparation-recovery-timings.json)
and [fragmentation regression](performance/offline-jobs/maximum-travel-fragmentation-regression.json).

The final executable uses independent exact copies of the next failed title-reload
case: the reserved-buffer build had already saved these returned cards twice,
then ran out of contiguous space for the heading font while reopening them.
The streamed-atlas and scoped-session fixes address that additional failure.
See the [title-reload regression](performance/offline-jobs/maximum-title-reload-fragmentation-regression.json). Its controller run completes an explicit checkpoint, Save & Quit, title
reload and another checkpoint. Strict saved-state verification again checks both
towns, all 1,024 bodies and the preserved actual visitor checkpoint. The native
production-runtime fixture separately completes the entire maximum-economy trip
with the final allocator and saves after rebuilding a 2 MiB world working set.
No mailbox or job capacity was reduced. Each final town occupies 1,026 physical
8 KiB blocks (8,404,992 bytes including the two artwork/header blocks), as read
from both actual card directories. This worst simultaneous economy exceeds an
official 1,019-block card; the test uses 16 MiB cards. Normal sparse towns retain
the much smaller reservation described above. See the [final checkpoint observations](performance/offline-jobs/final-stream-post-travel-timings.json).

These extreme saves have substantial I/O costs: the original home conversion
took 528.28 seconds; recovering into the unconverted destination took
19.62 minutes, and returning took
25.86 minutes. Those intervals used the prior
save-preparation executable, include validation/conversion and were not isolated
CPU samples. Final-executable checkpoint totals were
244.42 s, 114.40 s, 114.40 s.
These intervals measure save waits; frame timings are reported separately.
The first checkpoint also rebased dated jobs after the preserved card's clock
was ahead of this fixture RTC: both 1,669,720-byte economic mirrors changed
from sequence 7 to 10. The next Save & Quit took only the unchanged-economy
route. See the [record comparison](performance/offline-jobs/final-stream-first-checkpoint-record-changes.json).
Mount/readback validates whole referenced records; small clock-only *writes*
still require substantial reads when every mailbox is full. These costs do not run on each simulation tick.

## Release and qualification limits

The release workflow builds and validates NKit ISO, independently compiled
standalone Depot app, backend and tools from one source/main-version snapshot.
It recreates `update.json` for every release. Controller captures include an ISO
hash/source identity, and the publisher requires both completed controls and a
passing committed-state verifier before placing captures under the Depot
archive's `release/scenarios/jobs/`. ISO retains its updater; Depot excludes it.

Production source `1d59404cb5aa45ef8e2f6e6d3370bf5499624548` was built cleanly as the ISO console profile,
independently compiled standalone Depot profile and backend, then packaged by
`publish-release.ps1 -PackageOnly`. Final runtime captures use the official
SHA-256-identified NKit. The matching CI pipeline passed and published all five
release assets. See the [local manifest](performance/offline-jobs/local-release-final.json)
and [GitHub release evidence](performance/offline-jobs/github-release-final.json).
Documentation/capture-only follow-up commits preserve this production-source
label; their CI runs additionally validate and publish their own matching-head
release. The failed earlier console scenarios remain identified separately.

All runtime screenshots and frame timings here come from Dolphin 2503 with
OpenGL/llvmpipe, native 640×480, AA off. They are emulator observations, not
GameCube performance guarantees. Actual console profiling and standalone
SD/launcher boot remain hardware qualifications: this Dolphin version does not
emulate SD Gecko/SD2SP2. Fourteen production-bootstrap AOT cases validate real
host-volume reads, path/argv selection, closed handles, asset identity and GC
stress; they do not replace physical SD boot. Online job authority and live
network visits remain the separate project described in the plan.
