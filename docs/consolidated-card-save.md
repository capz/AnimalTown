# AnimalTown memory-card layout

Shared town records, manifests and deletion intent live in `AnimalTown` or `AnimalTown2`. Each occupied playable slot has its own
entry, `AnimalTown-Villager1` through `AnimalTown-Villager4`. The browser comment
identifies the slot. Each entry includes its own banner and icon and holds both
banks for that slot. Empty slots have only small records in the shared archive.
Computer-controlled residents belong to the shared town.
The explicit-save policy and game-level reference-manifest publication protocol
remain in force. No town progress is saved just by opening the title screen.

The record engine is owned by DolphinDotNet (SDK PR 187), pinned by the
`vendor/card-store-sdk` gitlink. It is source-linked until a matching SDK package
can be consumed; this avoids changing the game's pinned renderer/compiler and
engine dependency versions. Its native adapter/bridge are included by the guarded
native patch, rather than implementing a second record engine in AnimalTown.
Initialize the submodule with `git submodule update --init vendor/card-store-sdk`.

The shared archive reserves 34 standard 8 KiB card blocks: two for browser metadata and
artwork, two independent checkpoint sectors, and thirty data sectors. Small
logical records share sectors. A new town with one villager uses 36 blocks
across two entries. The villager
entry occupies two blocks. Its verified replacement journal can use three more
blocks temporarily, so the first save needs 39 free blocks and three directory
entries. The archive's fixed reservation covers normal recovery and reports
exhaustion if an edited town outgrows its available sectors.

Video and debug settings have separate one-block entries, `AnimalTown-Video`
and `AnimalTown-Debug`. Each can save before a town exists. A first settings save
needs three free blocks for the new entry and its temporary journal, or two
free blocks to replace an existing one. Existing loose and archived settings
remain readable. An explicit town save migrates both settings when space permits,
and saving either settings screen moves that screen's data. The game verifies
the dedicated file before removing any archived copy. A full card preserves
the old settings for a later retry.

Each shared archive update writes unused sectors, reads them back, then writes and verifies
the older checkpoint. Neither checkpoint's live data is overwritten prematurely.
Opening validates record checksums and falls back to the preceding complete
checkpoint if necessary. After an ambiguous I/O failure reopen; the in-memory
allocator is no longer used. Updates stage individual sectors/records rather
than allocating a whole-file replacement journal. No extra directory item is
needed for normal journal or backup data.

On the first explicit game save, archived villager banks are copied into their
own entries and verified before the corresponding archive records are removed.
An archived town with one villager needs five free blocks to migrate that slot.
Each completed slot consumes two blocks. A full card leaves any remaining
villagers readable in the shared archive until there is space to retry. The copy uses the SDK journal, and the loader resolves banks from
either location after an interruption. Title-screen loading leaves the layout
untouched.

## Existing cards

Reads also support the original loose bank files. Migration into an archive
requires the old files **plus 34 free blocks and one directory entry**. It refuses
an incomplete source town instead of guessing. It copies all recognized records,
reads them back, verifies that the decoded town matches, and publishes a durable
migration marker before deleting any source file. Cleanup interruption resumes
only after checking remaining source bytes against the verified destination.
Unrelated files stay untouched. An unreadable/different old file is preserved.
Never remove existing files to create migration workspace. Use a backed-up card
with enough space; this change does not relocate records to another physical card.

In-game town deletion removes the shared save records and all villager entries
while retaining options and debug configuration. In-game villager deletion
writes and verifies both empty banks before reclaiming that slot's entry.
For a complete backup in the GameCube card manager, copy the shared archive and
all villager entries together. The town manifest requires its referenced banks
to load a complete save.

## Validation and remaining scope

`tools/test-changes.ps1 -BaseRef origin/main -NativeInProcess` includes the actual
game adapter, SDK torn-write suite and native sector adapter. Host coverage tests
first save/reload, no-op save, full card, interrupted cleanup, successful retry,
independent options/debug changes, unrelated-file preservation and deletion.
SDK tests cut every byte of data/checkpoint/initial-checkpoint writes, retain
neighbors and test fallback, packing and capacity failure.

The separate-card town/character publication protocol, live card-location selector
and missing-character visibility/creation capacity flow are still design work;
this implementation provides their record storage, not those UI/routing features.
Physical Memory Card 251/Swiss, real flash power interruption and native console
rendering remain unqualified until tested on the corresponding hardware/build.

Traffic history requires a 32-block record payload plus two artwork blocks (34
physical blocks total). Existing 16-block payload containers are read without
modification. Their first write creates `AnimalTown2`, copies and verifies all
records, verifies the full town snapshot, and publishes its migration marker
before deleting `AnimalTown`. This requires 52 physical blocks temporarily for
the old and replacement containers together. A cut before publication retains
the old source; a cut after publication loads the verified replacement and
resumes cleanup. Lack of workspace leaves the original save intact. Repeated
changing traffic saves and interrupted copy/cleanup are covered by the host
card adapter checks. Physical-card timing and power-loss tests remain separate.
