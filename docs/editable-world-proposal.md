# Editable town terrain proposal

This is a proposed extension inspired by the freedom to reshape a town, with
an original presentation and bounded GameCube implementation. It is not an
implementation of Nintendo's terrain algorithm. Existing generation, saves and
collision remain unchanged by the cliff-material update. Building a terraforming
feature would be a separate scope decision; existing asset requests remain
GameCube/3DS-era natural ramps, banks, cliffs, bridges and waterfalls.

## In-game experience

Enter a dedicated **Town Works** mode from a rounded tool palette. Walk normally
and aim at the neighboring section; show a thin opaque outline and a small
patterned preview at the proposed height. Avoid large translucent volumes and
full-screen overlays. The preview follows the cylinder's radial ground normal.

Tools: paint ground, raise/lower terrace, cut/restore a diagonal corner, carve/fill
water, and position a natural ramp or bridge footprint. Start with one 2x2m
section per action; keep ordinary paths on the existing fine-cell grid. Limit
terraces initially to ground plus two raised tiers, each 2m high. Keep the
existing square 8x8-screen world and camera; edit coordinates are unrolled town
coordinates, then transformed for display.

Suggested controls while Town Works is active:

| Input | Result |
|---|---|
| Stick | Walk and face the target section |
| A | Apply the previewed action |
| Y | Alternate action: lower/fill/restore |
| X | Rotate corner, ramp or bridge orientation |
| L / R | Cycle tool |
| D-pad | Choose surface or terrace level in a small nearby selector |
| Z | Undo last local edit |
| B | Close selector, then leave Town Works |

The targeting marker shows an icon and text for invalid edits, rather than
relying on red/green alone. Protect occupied houses, trees, villagers and door
approaches; say what blocks the edit beside the target. Check accessible ramps,
bridge landings and water continuity before confirmation. Keep water percentage
as a generation rule, not a reason to reject every subsequent player edit.

Use our bulging rounded panels, softly tilted panel titles, Gorditas Bold for
major headings and the native IPL font for controls, descriptions and menus.
The tool palette and compact material popover draw only while visible. Show the
selected ground material separately for **Upper terrace** and **Ground level**;
default each to its directly adjoining terrain at the same height. A grass top
can border a sand lower half. The grass lip inherits the upper surface; the tall
face always uses soil. Give the player a choice when multiple different surface
types meet, rather than silently changing a whole connected terrace.

Apply a small edit immediately with a brief tool animation. Reserve confirmation
for costly construction or relocating a building. A larger planning view can
follow later, after basic targeting and undo are comfortable on a controller.

## Terrain representation and materials

Retain the existing fine-cell terrain data and add section records on the 2m
grid. A minimal record needs four bytes: elevation tier, corner shape, upper
surface ID and lower surface ID. The 80x80 section grid therefore costs 25,600
bytes before bookkeeping, independently of visible geometry. Reserve an escape
shape for ramps and keep bridge placements in the existing placement system.
Do not allocate a voxel volume or duplicate textures per section.

Material IDs reference the shared floor texture and seasonal tint table. Neighbor
inheritance resolves to IDs when an edit is committed; subsequent deliberate
painting changes the addressed surface, not an unbounded chain of neighbors.
Reconcile both sides of a shared edge within the same transaction. Render the
low half once through the floor mesh or a low cap, never both. World X/Z divided
by the repeat scale supplies all ground UVs before bending, so rotating geometry
does not rotate/restart the ground pattern. Soil keeps stacked wall UVs and the
single shared 128px texture, about 10KiB with its half-size CMPR mip.

Derive exposed faces from adjacent tier/shape records. Cull internal faces and
bottoms. Use the shared 18-triangle cardinal and 14-triangle diagonal templates;
those are source counts including lips and the diagonal low triangle, not a
promise of measured frame time. Eight source sections total 128 triangles.
Partition into small fixed chunks, initially 8x8 sections, with material batches
and pooled native indexed buffers. Bound each chunk's capacity from worst-case
exposed walls, including stacked tiers. Cap queued rebuild work per frame and
measure it alongside houses and villagers before choosing final chunk sizes.

An edit marks its chunk and directly affected neighbors dirty. Rebuild only those
visible mesh batches; a seasonal tint change updates material bindings. Keep
curved-space bounding volumes conservative for upper tiers. Reuse the same
height/shape queries for drawing, player movement, NPC navigation and targeting.
Reject inaccessible cliff crossings; paths use ramp and bridge links. Invalidate
only routes intersecting changed cells and replan a bounded number of NPCs per
tick. Preserve fixed-capacity arrays and avoid allocations in the frame loop.

## Saves and backend

Represent edits as commands containing town revision, affected cells, previous
values and proposed values. Validate footprints, occupancy, reachable exits and
water/land constraints before atomically applying them. An undo ring stores
bounded inverse commands; building purchases need their own economic rules.

Persist section records in the town-data save with a versioned migration from
current rectangular terraces. Keep house positions, shop state and player state
in their existing separate save components. An edit involving a moved house
must commit its terrain and house changes together through the save commit
record. A map migration must retain existing playable towns, not regenerate them.

Keep the server a separate build. For shared towns, it validates the same domain
commands against an expected revision and returns a compact delta or conflict.
Local preview may be immediate; authoritative edits and undo must never overwrite
a newer town revision. Save only accepted edits. Offline play uses the identical
command path locally, without a network dependency.

## Delivery order and assets

1. Section data, migration and common collision/height queries; no editing UI yet.
2. Ground painting and one-section targeting with undo and protected footprints.
3. Raise/lower plus diagonal corners and local mesh/navigation invalidation.
4. Water banks, natural ramps and bridges after matching art and placement rules.

Useful missing art: straight/inner/outer riverbanks, pond joins, shore joins,
one matching natural ramp, a wooden bridge with landing sockets, and a waterfall
lip/strip. These fit the older-game asset scope. No placeable incline-kit assets
are required for this proposal.

Validate only the changed terrain, movement, generation/save and relevant UI
paths at each step. Measure native CPU submission, GPU completion, rebuild
spikes and memory on representative dense views. Dolphin captures demonstrate
behavior; physical-console timing and validation must be reported separately.
