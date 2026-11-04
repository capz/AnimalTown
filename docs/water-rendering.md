# Opaque GameCube water

Outdoor `Terrain.Water` cells now use `WaterRenderer` and a shared `WaterMaterial`
instead of the neutral grass/floor texture. This includes rivers, ponds, the sea,
and ocean outside town bounds. Floor metadata patches cannot turn dry ground into
water; collision, saves, generation and bridge footprints keep their existing rules.
Interior floors continue to use the terrain importer.

The surface has two triangles per visible cell, with world-coordinate UVs (one
base repeat per four metres). It sits 6 cm below the ground/bridge surface and
follows the existing cylindrical world and terrain elevation. No vertices move
for waves. Geometry updates only when the terrain, camera focus, room or season
invalidates the terrain cache. Water authored on raised sections inherits their
height; use level cells for rivers/ponds, not terrain ramps.

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
cells (4,096 surface triangles); each cell can add at most four foam quads.
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
