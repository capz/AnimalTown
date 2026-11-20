# Offline resident jobs implementation

Work in progress against [the full phased plan](villager-jobs-offline-plan.md).
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
| 5 | Reward pools, independent outfit geometry, floor/wall rewards, owed payouts | Domain content and payout tests; console appearance qualification pending |
| 6 | Ask/accept/decline, reminders, jobs list, protected parcels and contribution selectors | Approved UI implemented; controller qualification pending |
| 7 | Clothing/property deliveries; recipient outfit and payment | Actual domain handoffs, full-pocket and wallet retries |
| 8 | Saved referral decisions, four-borrower maximum, exact pickup and return | Multi-stage/reload/retry tests |
| 9 | Ordinary fruit/fish/insect handoffs and net flower planting | Actual inventory/planting API tests, claimant protection |
| 10 | Two balls, four seasonal snowballs, rolling/collision/combining, persistent snowmen and exact destruction | Actual fixed-step physics and save evidence tests |
| 11 | Composed queued letters, attachments, NPC grading/replies, player mail and notice-board posts | Unicode/rank tests, full mailboxes, offline delivery and full text roundtrips |
| 12 | Two-card departure/return, fifth visitor capsule, canonical decisions, recovery, explicit NPC exchanges | Foreign/moved deliveries and write-boundary fault injection |
| 13 | Four-profile ownership, deletion guards, legacy request retirement, protected opening parcels and notice-board employment task | Profile, migration, opening and save qualification |
| 14 | Managed/native/console qualification, captures, performance and same-commit delivery | In progress; no hardware or FPS claim yet |

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

Combined saves use schema 14 and catalog 2. Earlier supported saves remain
readable. The new economic ledger has its own strict schema and is stored in the
existing small economy record, with no additional save-repository record or SQL
column. NPC availability remains in the independent runtime record. A clock
checkpoint does not rewrite terrain or mail text.

The core uses fixed arrays and three independent persisted RNG streams. Ordinary
fixed-step work allocates no memory and performs no card I/O. Message bodies use
packed UTF-8 or three-byte scalars, with shared 2 KB blocks for short letters and
exact arrays for longer bodies. Discarding letters cannot pin a large text arena.
The native adapter reclaims normalization temporaries before retaining text.
There is no ICU dependency or decoded-string cache
for every resident is retained. Unicode 14 normalization/classification tables
contain 46,344 immutable data bytes, with the Unicode license packaged in Depot.

Encoding/decoding economic data directly in bounded byte buffers avoids a full
intermediate word-array copy of large mailboxes. Real 128-letter mailboxes for
all four profiles, with 1,000 supplementary Unicode scalars per letter, are
exercised by the capacity fixture. Incremental readback retains the immutable
economic buffer and checks its referenced sequence without a duplicate copy.
Large native allocations use 64 KB size classes so a slightly larger save can
reuse its previous block. The console managed heap reserves 6 MiB; this is 2 MiB
more than the previous build, needed for maximum mail and two-card validation.
Native normal and forced-GC tests exercise both full towns and complete travel.
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
256 pixels the maximum allowed character variant. Final retained-byte, heap,
ARAM, frame-time and checkpoint measurements will be added after qualification.

## Evidence still required before completion

Finish console/controller captures, qualify maximum capacity and recovery under
the actual AOT runtime, record frame time and native/main/ARAM memory, and obtain
green same-commit ISO, Depot, backend, tools and update.json delivery. The plan is
not considered complete until these qualification gates are recorded.
