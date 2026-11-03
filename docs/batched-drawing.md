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
for the next. The original triangle layout and nondegenerate rectangles are
retained. Preparation cost is charged to the UI telemetry phase.

`UiFont` precomputes proportional IPL/Gorditas glyph regions, including slices
across the existing 128-pixel pages. Forty-eight layouts cache positions for
text up to 128 characters; longer dialogue streams without entering that cache.
Ordered chunks of at most 128 sprites use DrawSprites. Color, scale and shared
heading pivots are applied while drawing. Texture-page switches still flush GX,
but each glyph no longer crosses to native code separately. Atlases stay unmipped.

## Map and world

The map samples the original terrain centers into a lazy 128-square RGBA8 image.
Upload occurs only when sampled terrain or Town identity changes. Nearest
filtering preserves flat colors; grid and live markers remain above the image.
Native pixels use 64 KiB, reusable CPU upload pixels another 64 KiB, and sampled
cell comparisons about 6.3 KiB. Data is checked while open so direct terrain
edits cannot leave a stale background.

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
`--backpack-layout` also checks slot/action navigation.

Telemetry v4 retains v3 phase timings and completes imported primitive/texture
counts. Fields 26/27 contain drawing API entries/material setups instead of
cumulative cache hits/misses. Scenario IDs add 16 for baseline; map uses 10/26.
The decoder rejects mixed counter versions and drops five transition samples.
Reported texture-cache residency still excludes shape/generated-map allocations.

Publish raw AVI, JSON, screenshots and checksums with the exact ISO release.
Compare static geometry, glyphs, corners, title pivots, map alignment and popovers;
live actors/title rotation prevent naive whole-frame pixel comparison. Guest
timings are phase costs, not physical-console GPU timing or predicted savings.
