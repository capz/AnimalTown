# Bounded drawing batches

L+R+X switches batching against the original submission paths inside one ISO.
L+R+Z enables telemetry; L+R+Y independently switches indexed/direct model
geometry. Batching and indexed drawing default on. Rounded corners, side bulges,
rotating headings, slot-anchored popovers, source textures and visibility remain.

## UI geometry and text

`UiShapes` retains white native rounded/bulged geometry, replaying shadow and
face with their original tint/offset. Keys include dimensions, radius and bulge.
Sixty-four entries and 384 KiB bound residency. Creation and eviction happen
before BeginFrame; misses draw immediately for one frame and request geometry
for the next. A bounded pending queue makes steady preparation an O(1) early
return; screen/tab changes or disabling batches cancel unbuilt requests from
the previous view. Only actual visible misses trigger builds or reclamation.
Hash lookups replace scanning resident entries while drawing the HUD,
so cached shapes from closed menus do not add a linear lookup cost. Resident
geometry stays available for reuse within the same memory bound. The original
triangle layout and nondegenerate rectangles are retained. Preparation cost is
charged to the UI telemetry phase.
Bulged-shape fallback draws its rounded component directly without queuing a
second, redundant cache build for that component.

`UiFont` precomputes proportional IPL/Gorditas glyph regions, including slices
across the existing 128-pixel pages. Sixty-four flat, direct-mapped slots cache positions for
text up to 128 characters; longer dialogue streams without entering that cache.
The flat arrays avoid per-glyph/per-layout object graphs. Ordered chunks of at
most 128 quads use DrawTexturedQuads with primitive arrays, avoiding repeated
SpriteDraw packing/validation. Rotation is evaluated once per text draw. Color, scale and shared
heading pivots are applied while drawing. Texture-page switches still flush GX,
but each glyph no longer crosses to native code separately. Atlases stay unmipped.
Dialogue now wraps its complete message once on opening and draws a visible
character count from that immutable string. This eliminates per-frame substring,
single-character arrays/strings and quadratic concatenation. A one-value cache
also wraps the current item title and save status only when their text changes.
The legacy comparison renderer respects the same visible character count.
Unrotated text no longer measures an unused heading pivot for uncached long text.
See [dialogue-performance.md](dialogue-performance.md).

## Map and world

The map samples the original terrain centers into a lazy 32-square RGBA8 image
for the current 8×8 town.
Upload occurs only when sampled terrain or Town identity changes. Nearest
filtering preserves flat colors; grid and live markers remain above the image.
Native pixels use 4 KiB and reusable CPU upload pixels another 4 KiB. A terrain
revision check avoids re-reading 1024 cells on unchanged frames. Use SetTerrain
for individual edits, or NotifyTerrainChanged after a bulk write to Tiles.
Generation/save loading invalidate automatically; terrain strip caching observes
the same revision. Town-size migration is described in town-size.md.

Visible boxes/fences keep culling before submission. `BoxBatch` snapshots up to
128 transforms and flushes contiguous compatible colors through DrawMeshes;
it never queues shared mutable Transform/Material values. Phase boundaries are
preserved. Structures/actors use bounded model queues and stable placement
transforms. SDK opaque grouping reuses compatible material state; translucency
keeps global depth order within each model group. Indexed geometry/evaluated
skins remain. Existing terrain strips and tree batches are unchanged.

See the SDK's `docs/batched-model-state.md` for native keys, invalidation,
counter definitions and regression fixtures. Shape/map budgets are additional
to the application's existing source-texture cache.

## Comparison

Run `iso-smoke.py ISO CAPTURE_DIR --resume --profile --profile-seconds 10
--compare-batches` with a copied regression card and 512 MiB Docker shared memory.
It records world modes, movement, backpack modes and map modes.
`--backpack-layout` also checks slot/action navigation. L+R+D-pad Down isolates
legacy font submission while retaining other batches (scenario offset 32).

Telemetry v4 retains v3 phase timings and completes imported primitive/texture
counts. Fields 26/27 contain drawing API entries/material setups instead of
cumulative cache hits/misses. Scenario IDs add 16 for baseline; map uses 10/26.
The decoder rejects mixed counter versions and drops five transition samples.
Reported texture-cache residency still excludes shape/generated-map allocations.

Publish raw AVI, JSON, screenshots and checksums with the exact ISO release.
Compare static geometry, glyphs, corners, title pivots, map alignment and popovers;
live actors/title rotation prevent naive whole-frame pixel comparison. Guest
timings are phase costs, not physical-console GPU timing or predicted savings.
