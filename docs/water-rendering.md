# Shallow river water on GameCube

Rivers and ponds use a stationary opaque riverbed, sparse faceted rocks, a
translucent turquoise surface, and a separate narrow shoreline accent. The
surface has smaller broken ripples; moving caustic light belongs to the bed.
Every component has a procedural default, so the renderer works without any
new supplied artwork. See [water-art-assets.md](water-art-assets.md) for the
five optional PNG replacements, exact formats and the import commands.

This is a stylized shallow-water treatment inspired by the Smalltown Forest
wallpaper. It does not implement a physical fluid simulation or framebuffer
refraction. The indirect map displaces the surface pattern's texture
coordinates; it does not bend the image of the riverbed.

## Draw order and depth

`TerrainRenderer.Draw` submits depth-writing riverbeds, rocks, sea, soil and
ground before the ordinary opaque world objects. After buildings, bridge
decks, actors and box batches, `TownGame.SubmitFramePart` calls
`TerrainRenderer.DrawWater`. That call draws the translucent inland surface,
then shoreline foam, before foliage, weather and overlays.

| Component | Blend | Depth test | Depth write | Archive kind |
|---|---|---|---|---:|
| Riverbed and submerged bank skirts | Opaque | On | On | 108 |
| Generated rocks | Opaque | On | On | 109 |
| Sea and optional rough-sea patch | Opaque | On | On | 107 / dynamic patch |
| Inland river/pond surface | Source alpha | On | Off | 104 |
| Shoreline foam | Source alpha | On | Off | 105 |

This order gives the surface real background geometry to blend with. Bed
pixels and submerged rock pixels receive the turquoise overlay; above-water
rock caps and bridge decks occlude it through the existing depth buffer.
Shoreline geometry is lifted 8 mm to avoid coincidence with the surface.
The legacy recreated-terrain comparison follows the same opaque/transparent
separation and retains its river shadow/light scope.

Meshes are grouped by shared material. This split does not introduce a
scene-wide transparency sorter. The horizontal inland cells form one
nonoverlapping layer, while the existing bank foam avoids overlapping corner
strips. General intersecting transparent objects still need their own ordering.

## Geometry and world semantics

`Terrain.Water` (ID 1) remains inland water, and `Terrain.Sea` (ID 8) remains sea.
Water surfaces keep their existing level, 0.50 m below local terrain elevation.
Bridges retain their deck, collision and traversal behavior. None of the new
bed depths or rock placements is stored in the town save or changes navigation.

`WaterBedGeometry` maps the existing shore-distance field into a separate
render-only depth, ranging from 0.16 m near banks to 0.66 m farther out. That
field is shoreline proximity, not a sampled scene-depth buffer. The bed has
two triangles per inland cell, including the continuous space beneath bridges.
Additional skirts close the gap from the existing bank toe to the lowered bed,
including cardinal and corner joins. Sea cells do not allocate a bed.

Bed corners share logical coordinates across sections. Bed, bank skirts,
water and rocks all use the same cylindrical row-chord projection as the ground.
Skirt bottoms and stone placement sample the actual two bed triangles, so a
second approximation cannot leave visible holes between the layers.

Rocks are generated deterministically from the town seed and fixed logical
2-by-2-metre blocks. Candidates are rejected on bridges, dry cells and narrow
bank margins. Accepted shapes are combined into each section's rock mesh;
there are no individual per-frame rock objects or random-placement updates.
Most rise 10–20 cm from their buried bases and stay submerged. Candidates can
protrude 8–14 cm above water only where the physical bed is at most 30 cm deep;
deeper stones never stretch into posts to reach the surface. Broad shoulders
keep their silhouette low. Placement is decorative and adds no collision.

The existing sloped banks, coast orientations, beach wet-sand tint, estuary
water level and historical save migration remain in place. This change does
not regenerate a town or add a terrain-editing system.

## Materials and animation

The inland material uses the pinned SDK's custom GX TEV program rather than
using texture intensity as its transparency. Every authored alpha stage
explicitly preserves the depth-dependent raster alpha. This matters because
an I8 texture exposes intensity through alpha as well as RGB: ordinary texture
alpha multiplication would make the river disappear between ripple strokes.

Two independently scrolling samples of the fine ripple mask provide a subdued
teal layer and a pale cyan highlight. The indirect distortion stays restrained.
Its RGB565 source is a displacement field: blue drives horizontal displacement
and green drives vertical displacement. The custom GX indirect matrix uses
those channels explicitly, avoiding RGB565's constant alpha channel.

The riverbed's color texture remains fixed in world coordinates. A separate
low-contrast caustic mask moves across it through another UV transform. The
caustic contribution is multiplied by the vertex/material lighting so it does
not remain a bright additive glow at night. Rocks use the same light pattern
with their own faceted vertex colors. Their triangles are split at the dry
waterline during construction, and vertex alpha masks the caustic contribution
below that line. The rock material itself always writes opaque alpha.

The shallow and deep palette still derives from the existing seasonal floor
metadata. `SurfaceTint` supplies the inland opacity, while `OceanTint` always
returns opaque alpha. This separation also keeps the optional rough-sea wave
patch opaque when the inland material changes.

All ordinary animation changes shared material UV matrices and color only.
It does not upload textures, regenerate archived geometry, move the riverbed,
or allocate per-frame rock meshes. Translations wrap to preserve precision in
long sessions. Freeze-motion inspection holds these texture animations.

### Sea compatibility

The sea keeps its existing opaque Wind Waker-inspired material, interconnected
crest mask and indirect movement. This avoids introducing a transparent deep
ocean with no underlying geometry. The optional rough ocean still creates its
two additional I8 maps only when selected and retires them when unused.

`LocalOceanWaves` remains the existing 17-by-17 vertex / 512-triangle near-sea
patch. Its four traveling waves, outer fade and 12 mm lift are unchanged; it
is a decorative overlay on the fixed sea plane. Its cached phases and lazy
buffers keep the normal calm-water path free of dynamic wave uploads.

### Shoreline

The existing 12 cm contact strip uses a shared IA8 mask with broken coverage
and a gentle opacity pulse. Bridges do not create foam edges across the river.
The strip follows existing bank toes and retains trimmed, nonoverlapping
corner joins. This is a bank contact accent; the implementation does not add
bespoke foam rings around every generated rock.

## Assets and resource bounds

Optional native texture packages load only when the shared materials are
constructed. Each role falls back independently to its procedural pattern.
Dimensions, native format, mip count and exact byte length are validated before
upload; malformed packages emit a diagnostic and retain the fallback.
Author PNGs are converted offline and never decoded in the frame loop.

The five replaceable texture roles contain 56 KiB of native pixel data in total.
The calm ocean retains a 4 KiB ripple mask and an 8 KiB distortion map, bringing
ordinary water to 68 KiB. Rough-sea textures are another 8 KiB when active.
These figures exclude geometry, shader objects, managed
staging and package headers. The maps use repeat/linear sampling with one base
level; no authored mip chain is currently accepted.

`WaterBedRenderer` is owned by `WaterRenderer` and follows the same
Begin/Append/Finish/Archive/Dispose lifecycle. The managed archive-kind table
and native `TA_KINDS` both include 110 kinds. Existing archive limits remain
8 MiB in ARAM, a 1 MiB resident cache and 256 mesh views. The new parts count
against those existing limits rather than creating a second terrain archive.

Per 8-by-4-metre section, the conservative geometry bounds are 1,728 bed/skirt
vertices and 1,296 clipped rock vertices. At the native 36-byte vertex stride,
the parts are at most 62,208 and 46,656 bytes respectively, fitting the existing
65,536-byte append staging buffer. Actual river sections usually contain
substantially less. Cold build
work is bounded and section buffers are reused; visible mesh selection and
archive caching continue through the existing terrain system.

A host-side geometry sweep of town seeds 0, 49, 7319 and 1986 measured at most
389,056 bytes of added archive payload, 192,512 bytes of conservative visible
cache payload and 52 extra visible parts before coalescing. The largest actual
half-section part was 10,368 bytes. These are incremental geometry estimates;
the combined existing-plus-new native residency is checked separately by the
Dolphin acceptance scenario, including the legacy-to-archive restoration.

This is an engineering budget, not a claim of measured physical-console frame
rate. Verify memory pressure, raster cost and distant ripple stability in the
actual game. Screen-space refraction or stronger reflection would add a
separate scene-capture and sampling cost and should be evaluated as a later
rendering change.

## Verification and review

Run the repository selector rather than assuming that a shader-only test is
sufficient for geometry, packaging or native archive changes:

```powershell
./tools/test-changes.ps1 -BaseRef origin/main -PlanOnly
./tools/test-changes.ps1 -BaseRef origin/main
```

`-NativeInProcess` is available in the Linux builder environment. Managed
checks cover bed corner continuity, all local bank topologies, bridge exclusion,
deterministic bounded rocks, and unchanged traversal. Rendering checks cover
material contracts and native texture layout. Importer checks exercise format,
channel and size constraints, stale asset retirement and matching optional
content in the disc and Depot runtime payloads. Native archive fixtures cover
the new bed/rock kinds and byte-exact resident views.

For visual acceptance, use the existing read-only river/coast tour and
terrain-material animation scenario against the built console image:

```sh
python3 tools/iso-smoke.py out/AnimalTown-VERSION.nkit.iso out/water-review --river-coast
python3 tools/iso-smoke.py out/AnimalTown-VERSION.nkit.iso out/material-review --terrain-materials --season spring --dsp-hle
```

Run these inside the configured Dolphin image as described by the release
workflow. `L+R+D-pad Left` cycles river, bank corner, bridge, coast and estuary
views without editing or saving the town. Inspection should establish that the
bed remains still, ripples and caustics move independently, stone tops retain
their waterline, bridges occlude the surface, corners have no holes, and a menu
round trip restores the same materials. Also inspect night tint and the
optional rough sea separately.

The native water probe records actual GX texture matrices, stage bindings,
constants, blend mode and depth state at a real draw. It distinguishes the new
translucent inland path from the retained opaque coast. The animation scenario
compares moving, frozen and resumed water in both archived and legacy paths;
its pixel checks supplement, rather than replace, inspection of the captures.
Physical GameCube appearance and performance require console validation.
