# Traffic-driven town paths

Paths come from anonymous outdoor tile entries by the player and all six NPCs,
including NPC journeys while the player is indoors. Standing still, movement
within one tile, indoor movement, onboarding previews, spawn/portal relocations
and debug-camera jumps add no visits. No actor IDs, individual routes or visit
timestamps are retained.

Each of the town's 15,360 metre tiles has one byte counter. Counts through 100
are exact and never reduced. Before a counter can wrap, only excess above 100
is halved with rounding up. This preserves the minimum and opacity milestones
when traffic elsewhere is busy, while retaining bounded relative traffic above
full opacity. Counts above 100 are not unlimited lifetime totals. The common
movement path allocates nothing. Renormalization is one bounded 15,360-byte
pass, only on overflow.

Every 90 simulation ticks (three seconds at 30 Hz), dirty history is classified
with a reused 256-bin, 512-byte histogram. Only visited, path-eligible grass/camp
cells participate. Plaza, bridge and beach visits are stored but cannot paint
those surfaces. High outliers exceed Q3 + 1.5 × IQR, using nearest-rank quartiles.
They are excluded from the population used to select the busiest fifth and are
then included as paths themselves. The inlier quota is rounded up. Cutoff ties
retain existing selected tiles first, then use stable cell order, so ties do not
paint the whole town or shuffle unchanged paths. Never-visited tiles do not get
paths. A ranked tile, including an outlier, also needs at least **35 visits**
before it can show a path. This gate does not change the percentile population:
all visited eligible cells still participate in ranking. New towns have no
preplanned network or automatic doorway spurs.

The existing 3,840-byte two-bit path layer remains the cached selection. It has
its own revision; changing it never invalidates the terrain archive or shadow
receiver geometry. Shared-town record v7 and combined v11 append a versioned
15,360-byte traffic block covered by the existing checksums. Historical town
v4/v6 and combined v4–v10 remain readable; they get zero traffic history and
retain their saved selection until actual traffic triggers a classification;
the renderer's minimum-visit gate hides selections lacking sufficient history.
Incremental manifests remain v5 and can reference v4/v6/v7 shared town records
alongside unchanged v4 personal records. Traffic only writes to the memory card
through the existing explicit save flow, never on every step. The card archive reserves 34 physical blocks (previously
18); a verified replacement migration needs 52 blocks temporarily. See
[consolidated-card-save.md](consolidated-card-save.md).

## Rendering and cost

A native GX I4 mask holds one nearest-filtered texel per metre in a 128×128
texture: 8,192 bytes, including eight padding rows outside world coverage. It is expanded directly
from the packed selection and borrowed counter bytes into an SDK-charged native
buffer, with no duplicate managed image or RGBA decode. It uploads only when
selection or a selected cell's quantized opacity changes. Unselected traffic,
visits within the same opacity step, and visits beyond full opacity cause no
extra upload.
Four padding bytes carry a publication serial so Dolphin's default sampled
texture hashing detects even sparse edits, including after texture recreation.
The serial adds four native state bytes, with no additional texture allocation.
Point filtering keeps a selected tile from bleeding onto unvisited neighbours.
Boundaries follow the selected metre cells; the old geometry corner bevels are
replaced by the coverage mask.

The same I4 texels now carry traffic opacity: zero below 35 visits, 2/15
(roughly 13%) at 35, 5/15 at 50, 9/15 at 70, and 15/15 at 100 or more.
The ground shader already interpolates between normal grass and shared dirt
using this mask. Fading adds no shader stages, textures, geometry or draw calls.

The original painted tile remains
`assets/textures/environment/paths/dirt-64.png`: one 64×64 CMPR page with five mip
levels through 4×4, 2,752 native pixel bytes / 2,784 disc bytes. It contains ochre
wear and sparse pebbles, without a noise overlay. UVs remain world-aligned, with
one repeat per two metres. Three extra GX TEV stages blend this shared page onto
eligible existing ground draws, before the existing shadow/light stages.
Receiver alpha keeps paths off cliff lips. No path triangles, decals, additional
draw calls, full terrain rebuilds or per-frame texture uploads are needed.
The added passes explicitly select the ground receiver channel and preserve the
base material alpha through the later shadow/light stages.

The mask and dirt page are demanded by selected archive sections. They are
released indoors, outside path demand, on town replacement and on disposal,
after clearing borrowed material bindings. Async disc jobs return bytes only;
stale generations are ignored. While a page is loading or unavailable, its ground
keeps rendering and no busy retry loop runs.
Ground mip-band previews preserve the same path coverage using the diagnostic
dirt palette. Its page and borrowed bindings retire with the dirt source, so
debugging cannot keep path textures resident after demand ends.

Additional retained CPU storage is 15,872 payload bytes for counters/histogram,
plus object/array headers. Fading adds two four-byte revision fields (managed
and native), with allocator alignment additional. The existing selection costs 3,840 bytes. Visible
paths add an 8,192-byte native mask plus the shared 2,752-byte native dirt page
(and the texture-cache metadata/package already used by that page). A 16-bit
history would double counter storage; a path mesh/decal would need vertex/index
buffers and extra submissions. The chosen mask spends a few texture/TEV stages
to keep CPU geometry work, allocations and retained memory low.

Host timings from the managed checks are diagnostic, not GameCube frame rates.
The previous fixed-network performance report at
[performance/town-paths/README.md](performance/town-paths/README.md) describes its
pinned historical implementation and is not a measurement of this renderer.

Dolphin's sampled texture hashing can miss sparse live I4 mask edits. The
publication serial fixes that with the default cache, while full-hash captures
(`SafeTextureCacheColorSamples = 0`) provide an independent check. Console mask
uploads remain revision-gated; players need no emulator accuracy adjustment.
