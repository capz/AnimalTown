# Smaller home plots and saved edge smoothing

## Draft integration checkpoint

The shared gameplay rules, configuration migration and regression checks are
implemented. The production placement border/help text, the new title Options
row and its emulator interaction scenario await the user's required mockup
approval. This draft is not ready to merge until those UI changes are completed.

Proposed screens reuse existing assets and controls:

- [Current-tier home placement](concepts/house-plot-current-tier.svg)
- [Edge smoothing in title Options](concepts/title-edge-smoothing.svg)

## Home placement and garden construction

The starting plot is 7 x 5 tiles including the cottage. Later plots use the
resident's current garden tier: 9 x 7, 11 x 9, 13 x 11 or 15 x 13. The game no
longer reserves the final garden size when a resident first chooses a home.
The exact doorway approach and exterior portal landing remain protected; for
the smallest garden the final landing is one cell beyond the southern edge.

`GameState.CanPlaceHouse` is the shared, allocation-free preview validator.
`PlaceHouse` repeats validation before changing the town. The complete footprint
must fit on level grass and avoid other homes, accepted construction reservations,
service buildings, NPC homes and their entrances. Existing paths in newly
claimed land remain blockers. Relocation moves the resident's outdoor furniture
only after its translated destinations have also passed validation.

Only trees may be removed automatically. The tree classification is the same
one used by `TreeDrawBatch`: mature trees and planted tree saplings/fruit, with
planted flowers excluded. Placing or relocating a house removes only trees
under its cottage, fence, mailbox, doorway/exit or moved furniture. Decorative
trees elsewhere inside the garden and trees outside construction remain.
Flowers, dropped and buried items, paths, terrain, elevations, buildings and
other furniture are never deleted. A non-tree object physically obstructing
construction rejects the action without charging money or removing any tree.

`Expand(true, day, out reason, free)` checks the next garden size before charging
the usual loan and scheduling overnight construction. Its target becomes a
reservation immediately, so another resident cannot build on the accepted
extension. At rollover, construction checks the current world again and clears
trees in the newly acquired land. Existing garden paths and interior decorations
remain. If an obstacle appeared in the meantime, the same paid order stays
pending until a later rollover can finish it; it is not charged again.

Both combined and split saves validate current/pending dimensions and exact
entrance reservations. Existing save formats and former larger plots remain
readable. Late obstacles do not make a pending construction save unreadable.
The generator's original starter clearing stays unchanged to preserve generated
town layouts; runtime ownership and visitor spawning use actual home reservations.

Onboarding operates on its private draft. Preview never clears objects. Choosing
a valid spot records tree-removal indices, and returning to review restores the
exact compact ground-table ordering and any swapped lost-and-found receipt
indices. Saving publishes only the final choice; abandoning the draft preserves
the source town unchanged.

## Regular video configuration

`UserOptions.EdgeSmoothing` defaults to true. The existing 16-byte options record
becomes ATO v3 and stores it at byte 7 beside resolution, widescreen and debug
visibility. ATO v1/v2 still load. The hardware AA implementation remains the
existing native full-resolution mode; it supports 640 x 480, so 576p requires
smoothing to be off. Emulator host MSAA is unrelated.

The old title and gameplay debug controls are removed. New 44-byte ATD v7 debug
records retire slot 30 and write it as zero. A persisted AA choice in ATD v4-v6
migrates only when no ATO v3 preference exists. New video preferences always take
precedence. Missing old choices retain the new enabled default.

Loading settings remains read-only. An explicit video save writes ATO v3. If
diagnostics are saved first, the adapter writes and verifies the migrated video
record before retiring the only saved copy in the old debug record. A failed
write or full card leaves the migration retryable. Temporary play writes neither
record, and the selected memory-card slot remains authoritative.

After mockup approval, the title row will call
`VideoOptions.SetEdgeSmoothing(enabled)`, update the options only after a successful
display change, record the resulting active resolution and save with the normal
options adapter. The applied underline follows the live hardware mode.

## Validation

The regression suites cover compact adjacent and edge plots, both save formats,
terrain/path/structure rejection, safe portal exits, tree-only clearing, saplings
versus flowers, relocation, pending and inactive-resident construction, deferred
completion, draft rollback and committed lost-and-found receipt preservation.
Existing interrupted onboarding saves and zero-allocation previews remain covered.

Video checks cover both values in all AA-bearing legacy formats, current-option
precedence, dedicated/loose/archive card layouts, migration before diagnostics
writes, interruption/retry, full cards, selected Slot B and temporary play.

Focused host suites and the managed GameCube project build have passed. Physical
GameCube behavior and a new UI/emulator capture have not yet been validated for
this draft. The repository's complete PR pipeline remains the release gate.
