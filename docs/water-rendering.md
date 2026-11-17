# Opaque GameCube water

Outdoor water uses one shared `WaterRenderer` / `WaterMaterial`, with distinct
persisted inland and sea semantics. `Terrain.Water` (ID 1) means rivers and ponds;
`Terrain.Sea` (ID 8) means sea and the external ocean. Inland water ground and
visible opaque surface are exactly 0.50 m below their local elevation. Sea
uses the same current -0.50 m surface level. Bridges retain their land
plane and collision.

`TerrainWalls` emits shared soil/lip batches along dry-to-river boundaries,
including corners. Water cells never emit retaining walls against one another.
Bridge decks cover a continuous lowered water plane without becoming foam edges or retaining walls.
All wall endpoints use the same cylindrical row chords as ground and water.

CoastNorth/East/South/West (IDs 9..12) describe aligned 2 x 2 m sections. Their
landward metre is grass-topped 2 m land/cliff; the seaward metre is sea, not the
lower half of a vertical face. They reuse world-aligned ground material bindings
and the existing soil texture. New southern headlands use CoastSouth; all four
orientations are covered by regression checks. Coastal cliffs block traversal.
Beach cells touching sea slope to the sea plane and receive a wet-sand tint;
restrained surface foam supplies the surf edge. New generation permits sea to
border only water, beach or a facing coastal cliff.

The river and sea now use the same water elevation. The former 0.44 m
estuary height step was removed when sea level was unified with rivers.
This is stylized, static terrain water rather than a physical flow simulation.

The surface has two triangles per visible cell, with world-coordinate UVs (one
base repeat per four metres). Calm water uses immutable archived geometry;
camera motion selects archived mesh views, while animation changes only the
shared material's UV matrices. Terrain or season changes can rebuild the archive.

## Saves and generation

Combined saves now write v6 and read v1..v6; split records write v2 and read
v1..v2. Historical formats still reject IDs above 7. Current formats validate IDs
through 12. Loading historical records relabels only existing water in the
southern sea band (z >= 128) as sea. It never flood-fills the connected river or
regenerates a town. Dry terrain edits, elevations, structures, possessions,
resident data and progress remain intact. A historical inland pond stays inland
water; a historical water edit inside the southern sea band adopts sea semantics.
This explicit template boundary is deterministic and migration is stable after
its first write. Existing arbitrary dry shoreline edits are retained and receive
normal cliff edge geometry rather than being repainted as beach or headland.
New-town generator version 4 authors the sea boundary and half-section headlands.

## Material and patterns

The pinned DolphinDotNet SDK supports two textures, separate UV matrices and GX
TEV programs directly. The game uses its `WaterWarp` preset for calm water.

Calm water creates one original neutral 64x64 GX I8 foam/crest mask (4 KiB) and
one 64x64 RGB565 signed displacement map (8 KiB), without mipmaps. Both repeat
periodically in two dimensions and use linear filtering. The two foam layers
sample the same I8 image through independent UV matrices. Existing artist source
images and nine-slice atlases are preserved. No runtime image decoding or texture
uploads occur during animation. The optional storm material lazily creates two
additional I8 images, for another 8 KiB only while rough water is active.

The RGB program is:

1. Start with the depth-dependent vertex tint.
2. Blend toward the first pale foam color using the first displaced I8 sample.
3. Blend toward the second pale foam color using the independently scrolling
   second sample of the same I8 image.
4. Preserve RGB and resolve primary texture opacity in the SDK's final stage.

This is four physical TEV stages, two texture units and one indirect GX stage.
The ripple map stores its RGB blend weight in intensity. Surface blending is
`Opaque`, depth testing/writing stay enabled, and alpha cutoff is zero. Using I8
keeps the SDK combiner API sufficient without custom texture-alpha stages.
The current target is GameCube GX; there is no separate renderer backend needing
a shader fallback.

`WaterMaterial.Shallow` and `Deep` configure the shoreline/interior palette. On
season load, version-2 floor metadata supplies the deep tint from the existing
`water` terrain configuration; shallow tint scales proportionally from it. All
water bodies share this palette, including water inside pond metadata patches.
Version-1 metadata uses the default `#4B9ABB` blue. Vertex tint uses
distance to dry terrain, capped at three metres, sampled at shared logical corners
so neighboring cells/strips have identical edge colors. Bridges do not create
false shallow bands or foam across the river.

`BaseSpeed` defaults to `(0.012, 0.025)` UV/s; `RippleSpeed` to `(-0.018, 0.035)`.
The ripple layer has a separate 1.35x scale. Animation uses elapsed wall time,
independent of simulation ticks and camera motion. Translations wrap modulo one
to preserve precision during long sessions. Set style fields before first
preparation when reusing the material; a palette change needs a geometry rebuild.

## Optional shore foam and budgets

`FoamEnabled` adds a 12 cm pale band along exposed dry banks, enabled by default.
It gently pulses in opacity. It is a separate alpha material with depth testing
and no depth writes; the water underneath remains fully opaque. Corner strips
are trimmed to avoid overlapping alpha, and there are no foam edges against
bridges or the external ocean. Foam interpolates on the water's row chord with
an 8 mm radial lift, so it does not float above the curved surface.
A shared 64x64 GX IA8 shoreline mask (8 KiB) supplies moving opacity
through a per-frame UV matrix; no shoreline vertices are changed.
The mask repeats seamlessly and scrolls along the bank independently
of the opaque surface layers.

The floor visibility window bounds selection. Water is limited to 2,048 visible
cells (4,096 horizontal surface triangles); each cell can add at most four foam quads and four two-sided mouth curtains.
Retained mesh buffers grow geometrically and are reused; animation only updates
material transforms/color. Empty geometry is not submitted. The normal scene
requires one opaque water draw plus one optional foam draw, sharing textures and
effect across every visible water body. The triangle counter includes both.

## Wind Waker-inspired water (2026-11-10)

The ordinary water material remains compatible with the currently pinned
DolphinDotNet SDK. `WindWakerWaterPattern.Foam` builds a 64x64 GX I8
texture with four-by-four periodic jittered Voronoi sites. Smooth periodic
coordinate warping makes interconnected irregular foam lines; fixed intensity
is blended by the existing two UV/TEV layers. CPU work happens once at startup.
`WaterSurface.StormTexture()` preserves the more forceful optional storm
whitecaps, while shore breakers use the new translucent IA8 mask.

A separate `LocalOceanWaves` is active only for the existing opt-in
`DebugWeather.RoughOceanWhenRaining` condition near deep water. It checks an
8x8 m area for pure sea tiles, and then updates a 17x17 vertex / 512-triangle
grid with four travelling cosine waves. The wave patch is positioned 6 m
towards the outer sea; the wave amplitude fades over three outer grid cells.
A 12 mm positive lift from the archived water avoids coincident depth.
The patch follows the same rigid cylindrical scroll transform and does not
change archived ARAM terrain or collision. It is a decorative overlay, not
a replacement for the fixed sea plane. Profiling should verify the cost
of `DynamicMesh.SetVertices` and possible overlap at its edge.

The coordinated DolphinDotNet 305 / Hypercube 15 package update enables GX
indirect texture sampling in the default Wind Waker material. The game uses
`TevMaterialPresets.WaterWarp` directly: two independent foam UV samples (TEX0)
and one signed 64x64 RGB565 displacement map (TEX1, 8 KiB). The SDK owns DNEF v3
validation and serialization; the game's hand-written package/failure probe and
unused v2 calm-water fallback have been removed. The warp changes UVs on the GPU,
without per-frame texture uploads or changes to archived terrain vertices.

The old storm material and its two I8 images are created between frames only
when rough weather is first selected. This avoids 8 KiB of unused native image
data and obsolete effect work compared with keeping both the v2 fallback and
new v3 material. This is the sole water renderer.

The optional rough geometry retains exactly the four travelling waves and edge
fade. `WindWakerWaveGrid` caches spatial phases when the patch origin changes;
steady frames compute four sine/cosine pairs instead of 1,156 cosines. Tests
compare every grid sample against the previous height function, including long
sessions, with a 0.0000001 m tolerance and no warm-frame allocations. The small
phase cache adds 3,264 payload bytes once rough water is used. Vertex/index and
cylinder-row staging are now lazy, deferring 16,428 payload bytes during ordinary
play. Terrain levels, collision and save formats remain unchanged.

### Cost model

- Calm river / ocean: no geometry changes and no per-frame image uploads;
  material UV/opacity updates only.
- Shoreline: one shared 8 KiB IA8 mask, bringing ordinary water texture data to
  20 KiB including the foam and indirect maps.
- Rough sea opt-in: 289 vertices and 512 triangles; one small dynamic
  vertex upload and draw per frame while a pure deep-sea patch is active.
- DNEF v3 on the pinned SDK: two textures (foam + indirect mask), three
  authored TEV stages plus a base-alpha stage, and one indirect GX stage.
  This is the active calm-water path; there is no automatic fallback gate.

The normal world remains one batched opaque surface, one sea pass and
an optional shoreline pass using existing archive-bound mesh views.
The original `d_a_sea.cpp` source in `zeldaret/tww` informed the
separation of near-field wave heights from texture-based distant water;
these masks and numbers are an original AnimalTown approximation, not copied
Nintendo textures or a bit-identical GameCube pipeline.

### Legacy river shadow composition

The optional **Recreate terrain meshes** and **Ground ring comparison** debug
paths also apply the shared cloud/shadow/light footer to river water. That footer
previously reused GX constant K1 for lamp color, replacing the second foam layer's
pale highlight with black at noon or lamp gold at night. This affected blending
in those legacy comparison paths; it did not stop their UV matrices.

The footer now preserves both foam constants K0/K1, keeps environment/shadow
colors in K2/K3, and places lamp RGB in an unused regular TEV color register.
Selection excludes every authored RGB read and destination, including packed
destinations, so the water program cannot overwrite the lamp before the appended
stages execute. The initial alpha stays zero; shadow/light alpha calculations use
their independent channels. WaterWarp uses only PREV and therefore leaves REG0
available. The change adds no texture, render pass or TEV stage. Ordinary materials
with zero or one constant keep their existing footer. This supports WaterWarp's
two-constant composition; other layouts retain the existing footer policy.

## Verification

`./tools/test-changes.ps1` selects the terrain/core checks for water files. The
terrain suite covers aliased floor materials, land/bridge exclusion, interior
transitions, bounded ocean selection, continuous corner depth, periodic GX tile
packing, connected foam density, shoreline IA8 native tile order,
positive wave displacement and long-running/negative UV scrolling.

Build with `./tools/build-game.ps1`. For the targeted Dolphin check, use a fresh
output directory/card:

```powershell
docker run --rm --init --shm-size=512m --mount "type=bind,src=$PWD,dst=/work" -w /work animaltown-dolphin python3 tools/iso-smoke.py out/AnimalTown-COMMIT.iso out/water-smoke --water
```

The scenario creates a player, reaches the western ocean, checks that a stationary
water region changes over eight seconds, moves the camera, and captures the menu
and return to the world. Inspect `water-a.png`, `water-b.png`, `water-camera.png`,
`water-menu.png`, `water-return.png`, and emulator logs for seams, depth/foam edges,
unchanged geometry and subsequent material state. Pixel animation alone does not
prove visual quality or distinguish the individual scrolling layers. Physical
GameCube appearance/performance remains unverified. The existing Dolphin
water smoke test does not automatically enable rainy-ocean wave mode; inspect
that mode separately with the in-game debug weather setting and measure it
against the calm ocean baseline.

For combined mip and water acceptance, use `--terrain-materials`. Its fixed river
and coast views compare interior crest/chromaticity changes, freeze landscape
motion as a control, and resume it. It also toggles the existing **Recreate terrain
meshes** shortcut (L+R+Z+X), repeats the river controls, and restores the archived
path. Sampling waits for observed rendered frames and UV displacement rather
than a fixed host delay. This avoids counting moving
cloud shadows or a slow emulator's wall-clock delay as proof of texture animation.

During the existing water inspection tour, `NativeWaterProbe` emits
`ANIMALTOWN WATER DRAW` at the first actual water primitive and every 30 rendered
frames thereafter. The read-only wrappers forward GX commands unchanged and
record the actual loaded primary/secondary UV matrices, selected matrix IDs,
foam stage texture-coordinate/map bindings, TEV/indirect stage counts, resolved
texture sizes and the final K0/K1 RGBA constants (`c4e4deff` / `e5f5edff`). Matrix
coefficients are signed integers scaled by 1,000,000 in
`m00,m01,m10,m11,offsetU,offsetV` order. The probe stays silent outside inspection.
The patched-native suite runs a recording transport fixture that verifies call
forwarding, draw gating, cadence, scene reset and frozen matrix reporting. A
recording shadow fixture also checks both foam colors and the resulting RGB/alpha
over noon/night environment, cloud and lamp masks, plus register read/write
exclusion. A native trace proves submission state; the associated Dolphin screenshots and
pixel comparisons establish the visible result.

## River/coast inspection

L+R+D-pad Left cycles a read-only camera tour: river, bank corner, bridge,
coastal headland/beach, mouth, then normal play. It uses the actual loaded town,
restores player coordinates after every draw, and blocks normal input during
inspection. It never modifies terrain or saves. L+R+D-pad Up still opens the
existing eight-piece cliff gallery.

```sh
docker run --rm --init --shm-size=512m --mount "type=bind,src=$PWD,dst=/work" -w /work animaltown-dolphin python3 tools/iso-smoke.py out/AnimalTown-VERSION.nkit.iso out/river-coast-smoke --river-coast
```

Inspect river.png, river-corner.png, bridge.png, coast-beach.png, estuary.png,
restored.png and river-coast-menu.png. Automated height, footprint, shore-adjacency,
68-seed generation, combined/split round-trip and historical migration checks run
through `tools/test-changes.ps1`. Physical console testing remains outstanding.
The handoff branch is explicitly enabled in the existing release workflow so its
clean ISO/backend/tools/cliff-kit manifests and checksums publish as a prerelease.

## Renderer resources

Calm and rough water share the Wind Waker renderer. Rough-sea textures and the
small dynamic wave patch are prepared only when needed. All ordinary water,
river banks, bridge underpasses and shoreline foam use the immutable terrain
archive. The renderer uses no separate seabed mesh or screen-space water capture.
The previous alternative mode, its debug selector and authored texture packages
have been removed. Existing saved debug settings and town formats are unchanged.
