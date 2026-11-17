# One AnimalTown memory-card item

AnimalTown now stores its logical save records inside one physical `AnimalTown` (or migrated `AnimalTown2`)
file with one banner and icon. Town records, player records, manifests, options,
debug configuration and deletion intent keep their existing independent schemas.
The explicit-save policy and game-level reference-manifest publication protocol
remain in force. No town progress is saved just by opening the title screen.

The record engine is owned by DolphinDotNet (SDK PR 187), pinned by the
`vendor/card-store-sdk` gitlink. It is source-linked until a matching SDK package
can be consumed; this avoids changing the game's pinned renderer/compiler and
engine dependency versions. Its native adapter/bridge are included by the guarded
native patch, rather than implementing a second record engine in AnimalTown.
Initialize the submodule with `git submodule update --init vendor/card-store-sdk`.

The file reserves 34 standard 8 KiB card blocks: two for browser metadata and
artwork, two independent checkpoint sectors, and thirty data sectors. Small
logical records share sectors. A new generated Emi/Fox town with both save banks
uses this single 34-block allocation, including recovery capacity. Settings/debug
records use that same reservation. This fixed-size allocation is not guaranteed
to fit every future edited town: exhaustion retains the last verified save and
reports an error; growing it requires a separately verified copy. A settings-only
card also reserves these 34 blocks so that adding the first town can use the same
container. File-count consolidation is not a promise of lower usage for every
possible card layout.

Each replacement writes unused sectors, reads them back, then writes and verifies
the older checkpoint. Neither checkpoint's live data is overwritten prematurely.
Opening validates record checksums and falls back to the preceding complete
checkpoint if necessary. After an ambiguous I/O failure reopen; the in-memory
allocator is no longer used. Updates stage individual sectors/records rather
than allocating a whole-file replacement journal. No extra directory item is
needed for normal journal or backup data.

## Existing cards

Reads retain the old multi-file format until the first explicit write. Migration
requires the old files **plus 34 free blocks and one directory entry**. It refuses
an incomplete source town instead of guessing. It copies all recognized records,
reads them back, verifies that the decoded town matches, and publishes a durable
migration marker before deleting any source file. Cleanup interruption resumes
only after checking remaining source bytes against the verified destination.
Unrelated files stay untouched. An unreadable/different old file is preserved.
Never remove existing files to create migration workspace. Use a backed-up card
with enough space; this change does not relocate records to another physical card.

Town deletion uses logical records and a durable deletion intent. It removes both
game-level fallback banks while retaining options and debug configuration. Deleting
the physical AnimalTown item from IPL/Swiss removes **everything on that card**
inside this container. Native card copy/move likewise handles the complete item.

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
