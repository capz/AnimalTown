# Opaque GameCube water

Outdoor water uses one shared `WaterRenderer` / `WaterMaterial`, with distinct
persisted inland and sea semantics. `Terrain.Water` (ID 1) means rivers and ponds;
`Terrain.Sea` (ID 8) means sea and the external ocean. Inland water ground and
visible opaque surface are exactly 0.50 m below their local elevation. Sea keeps
the existing -0.06 m offset. Bridges retain the land plane and collision.

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

At a mouth, river -0.50 m and sea -0.06 m remain distinct. An opaque, two-sided
water curtain joins their shared edge over the exact 0.44 m step. Its endpoints
meet both surface chords and the bank endpoints, closing the estuary without
flattening the river or adding a soil wall between water cells. This is a stylized
static tidal step, not a physical flowing-water simulation.

The surface has two triangles per visible cell, with world-coordinate UVs (one
base repeat per four metres). No vertices move for waves. Geometry updates only
when terrain, camera focus, room or season invalidates the terrain cache.

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
New-town generator version 3 authors the sea boundary and half-section headlands.

## Material and patterns

The pinned DolphinDotNet SDK supports two textures, separate UV matrices and GX
TEV programs directly; no native patch or engine submodule change is needed.

`WaterSurface.Texture` creates two original neutral 64x64 GX I8 patterns once at
startup: broad low-contrast modulation and sparse curved ripple weights. Each
uses 4 KiB, for 8 KiB resident texture data total, without mipmaps. Both repeat
periodically in two dimensions and use linear filtering. Existing artist source
images and nine-slice atlases are preserved. No runtime image decoding or texture
uploads occur during animation.

The RGB program is:

1. `base = vertexTint * baseTexture`.
2. `result = lerp(base, Highlight, rippleIntensity)`.
3. The SDK's required two-texture final stage preserves RGB and primary opacity.

This is three physical TEV stages and two texture units. The ripple map stores
its blend weight in intensity, with an implicit alpha of 255: surface blending is
`Opaque`, depth testing/writing stay enabled, and alpha cutoff is zero. Using I8
keeps the simple SDK combiner API sufficient without custom texture-alpha stages.
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

The floor visibility window bounds selection. Water is limited to 2,048 visible
cells (4,096 horizontal surface triangles); each cell can add at most four foam quads and four two-sided mouth curtains.
Retained mesh buffers grow geometrically and are reused; animation only updates
material transforms/color. Empty geometry is not submitted. The normal scene
requires one opaque water draw plus one optional foam draw, sharing textures and
effect across every visible water body. The triangle counter includes both.

## Verification

`./tools/test-changes.ps1` selects the terrain/core checks for water files. The
terrain suite covers aliased floor materials, land/bridge exclusion, interior
transitions, bounded ocean selection, continuous corner depth, periodic GX tile
packing, ripple density and long-running/negative UV scrolling.

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
GameCube appearance/performance remains unverified.

## River/coast inspection

L+R+D-pad Left cycles a read-only camera tour: river, bank corner, bridge,
coastal headland/beach, mouth, then normal play. It uses the actual loaded town,
restores player coordinates after every draw, and blocks normal input during
inspection. It never modifies terrain or saves. L+R+D-pad Up still opens the
existing eight-piece cliff gallery.

```sh
docker run --rm --init --shm-size=512m --mount "type=bind,src=$PWD,dst=/work" -w /work animaltown-dolphin python3 tools/iso-smoke.py out/AnimalTown-VERSION.iso out/river-coast-smoke --river-coast
```

Inspect river.png, river-corner.png, bridge.png, coast-beach.png, estuary.png,
restored.png and river-coast-menu.png. Automated height, footprint, shore-adjacency,
68-seed generation, combined/split round-trip and historical migration checks run
through `tools/test-changes.ps1`. Physical console testing remains outstanding.
The handoff branch is explicitly enabled in the existing release workflow so its
clean ISO/backend/tools/cliff-kit manifests and checksums publish as a prerelease.
