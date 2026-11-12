# Incremental memory-card saves

A save compares the exact encoded content of each component with the committed
revision. Sequence and CRC fields are excluded from the content comparison.
There are no mutable dirty flags to miss edits or to lose when recreating a
repository. Only changed components are written. A healthy unchanged save writes
zero files and does not increment the save sequence.

## Components and cost

The shared static town record contains terrain, elevation, calendar, NPC names,
world objects, building types and furnishings. Home plots, shops and each of the
four player profiles remain separate records. An eighth record, `AT-Residents0/1`,
stores NPC positions, headings, journey state/timers and the active profile.
Ordinary NPC walking therefore changes only a 300-byte runtime record.

A component update normally writes four files: its two copies and two 192-byte
reference manifests. Player-only and NPC-only regression fixtures write under
4 KiB of record payload, including both manifests. A large, edited terrain record
is left byte-for-byte untouched by player-only saves. NPC simulation can still
make runtime data change between two menu saves; this is real saved content.

These numbers measure record payload, not physical card traffic. Every file also
has SDK artwork and block rounding, and transactional writes use temporary files.
Initial saves and migrations write eight component pairs plus two manifests
(18 files). They need enough free directory entries and card blocks, including
transaction overhead. Existing combined migration files are retained unchanged.

## Publication and recovery

Each schema-5 `AT-Commit0/1` manifest has the normal ATSP header and CRC, followed
by eight ordered references. Each reference records kind, preferred copy, record
sequence, length and CRC. Data records retain schema-4 headers; component sequences
may be older than the manifest sequence. Reuse requires exact kind, town seed,
sequence, length and checksum. An alternate copy is accepted only for that same
referenced revision.

For changed data, save writes the copy opposite its current verified primary,
then reads it back byte-for-byte. All referenced components are reconstructed as
a complete logical snapshot and pass the existing strict semantic validation
before the first new manifest is published. The prior publication remains
recoverable until that boundary. Save then repairs changed or damaged mirrors,
publishes the second manifest and verifies both copies. A successful save keeps
two complete references to the latest state, so fallback cannot resurrect a
deleted resident. Both heads may reference records from different copies and
publication sequences; unreferenced records are never combined during loading.

A failure before publication preserves the old snapshot. A failure after
publication may already have committed the complete new snapshot. Retrying the
exact same snapshot adopts that publication and finishes redundancy repair;
a different stale snapshot is rejected. Reloading also picks the newest complete
publication. Healthy unchanged records are never rewritten during repair.

## Compatibility and memory

Old split schemas 1–4 and combined saves remain readable. The next successful
save migrates them without regenerating terrain, possessions, structures or
progress. The public seven-record schema-4 snapshot and combined export remain
unchanged, including backend storage and validation.

On card, the town record is a canonical projection with 67 runtime integers
zeroed; `AT-Residents` supplies those values on reconstruction. Stored bytes are
never modified in place. The reconstructed snapshot receives a common sequence
and CRC before passing the existing decoder. Bounded parsing and semantic checks
reject invalid projections, references and runtime fields.

Cold terrain baking uses a 96 KiB coalescer scratch buffer; the full rendering
cache is allocated only after baking scratch meshes have retired. This reduces
startup peak MEM1 use by 928 KiB without changing the gameplay cache budget.

Native persistence functions are compiled for size with explicit function
boundaries. This keeps save/load code out of gameplay inlining and preserves
MEM1 for card buffers and the full terrain rendering cache. Gameplay keeps its
normal optimization level.

During the blocking in-game Save action, the renderer releases its rebuildable
1 MiB GX terrain working cache before card mount/I/O. Immutable geometry stays
in ARAM. The card is closed before restoring that cache; visible views rebind
on the next frame without rebuilding or regenerating the town.

## Validation

The save suite covers exact no-ops, individual component edits, large edited
terrain, profile switching/deletion, legacy migration, readback corruption,
single-file damage, stale writers, and interruption before/during/after every
actual write. It verifies complete old-or-new snapshots and idempotent recovery.
The native archive fixture checks cache suspension/resume, live-view guards,
byte-exact ARAM retention and repeated cycles without resource growth.
