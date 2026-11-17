# Traffic-driven town paths

Paths come from anonymous outdoor tile entries by the player and all six NPCs,
including NPC journeys while the player is indoors. Standing still, movement
within one tile, indoor movement, onboarding previews, spawn/portal relocations
and debug-camera jumps add no visits. No actor IDs, individual routes or visit
timestamps are retained.

Each of the town's 15,360 metre tiles has one byte counter. Before a counter can
wrap, all nonzero counters are halved with rounding up. This retains relative
traffic with bounded precision over long sessions; these are relative counts,
not unlimited lifetime totals. The common movement path allocates nothing.
Renormalization is one bounded 15,360-byte pass, only on overflow.

Every 90 simulation ticks (three seconds at 30 Hz), dirty history is classified
with a reused 256-bin, 512-byte histogram. Only visited, path-eligible grass/camp
cells participate. Plaza, bridge and beach visits are stored but cannot paint
those surfaces. High outliers exceed Q3 + 1.5 × IQR, using nearest-rank quartiles.
They are excluded from the population used to select the busiest fifth and are
then included as paths themselves. The inlier quota is rounded up. Cutoff ties
retain existing selected tiles first, then use stable cell order, so ties do not
paint the whole town or shuffle unchanged paths. Never-visited tiles do not get
paths. New towns have no preplanned network or automatic doorway spurs.

The existing 3,840-byte two-bit path layer remains the cached selection. It has
its own revision; changing it never invalidates the terrain archive or shadow
receiver geometry. Shared-town record v7 and combined v11 append a versioned
15,360-byte traffic block covered by the existing checksums. Historical town
v4/v6 and combined v4–v10 remain readable; they get zero traffic history and
retain their saved road layer until actual traffic triggers a classification.
Incremental manifests remain v5 and can reference v4/v6/v7 shared town records
alongside unchanged v4 personal records. Traffic only writes to the memory card
through the existing explicit save flow, never on every step. The card archive reserves 34 physical blocks (previously
18); a verified replacement migration needs 52 blocks temporarily. See
[consolidated-card-save.md](consolidated-card-save.md).

## Rendering and cost

A native GX I4 mask holds one nearest-filtered texel per metre in a 128×128
texture: 8,192 bytes, including eight empty padding rows. It is expanded directly
from the packed selection into an SDK-charged native buffer, with no duplicate
managed image or RGBA decode. It uploads only when the path revision changes.
Point filtering keeps a selected tile from bleeding onto unvisited neighbours.
Boundaries follow the selected metre cells; the old geometry corner bevels are
replaced by the coverage mask.

The original painted tile remains
`assets/textures/environment/paths/dirt-64.png`: one 64×64 CMPR page with five mip
levels through 4×4, 2,752 native pixel bytes / 2,784 disc bytes. It contains ochre
wear and sparse pebbles, without a noise overlay. UVs remain world-aligned, with
one repeat per two metres. Three extra GX TEV stages blend this shared page onto
eligible existing ground draws, before the existing shadow/light stages.
Receiver alpha keeps paths off cliff lips. No path triangles, decals, additional
draw calls, full terrain rebuilds or per-frame texture uploads are needed.
The three added passes retain the ground raster channel, including passes that
only forward colour. Using `COLOR_NULL` there suppressed later receiver-alpha
reads on GX, hiding the paths and subsequent shadows; matched Dolphin captures
verified the retained-channel configuration.

The mask and dirt page are demanded by selected archive sections. They are
released indoors, outside path demand, on town replacement and on disposal,
after clearing borrowed material bindings. Async disc jobs return bytes only;
stale generations are ignored. While a page is loading or unavailable, its ground
keeps rendering and no busy retry loop runs.

Additional retained CPU storage is 15,872 payload bytes for counters/histogram,
plus object/array headers. The existing selection costs 3,840 bytes. Visible
paths add an 8,192-byte native mask plus the shared 2,752-byte native dirt page
(and the texture-cache metadata/package already used by that page). A 16-bit
history would double counter storage; a path mesh/decal would need vertex/index
buffers and extra submissions. The chosen mask spends a few texture/TEV stages
to keep CPU geometry work, allocations and retained memory low.

Host timings from the managed checks are diagnostic, not GameCube frame rates.
The previous fixed-network performance report at
[performance/town-paths/README.md](performance/town-paths/README.md) describes its
pinned historical implementation and is not a measurement of this renderer.
