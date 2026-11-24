# Shallow river water on GameCube

Rivers and ponds use a stationary opaque riverbed, sparse faceted rocks, a
translucent turquoise surface, and a separate narrow shoreline accent. The
surface has smaller broken ripples; moving caustic light belongs to the bed.
Every component has a procedural default, so the renderer works without any
new supplied artwork. See [water-art-assets.md](water-art-assets.md) for the
six optional PNG replacements, exact formats and the import commands. The
complete native implementation plan, register assignments, equations and
validation procedure are in [water-bump-implementation.md](water-bump-implementation.md).

This is a stylized shallow-water treatment inspired by the Smalltown Forest
wallpaper. Its optional native optics add two moving slope samples, bumped
sunlight highlights, a restrained procedural environment sheen, and
framebuffer refraction of the bed and submerged rocks. These are stylized
surface effects; there is no fluid simulation or live reflection of scenery.
The original transparent surface remains the fallback if native resources are
unavailable.

## Draw order and depth

`TerrainRenderer.Draw` first submits depth-writing riverbeds and, when a
capture is available, the submerged portions of rocks. It copies that image
to a half-resolution RGB565 texture, then draws complete rocks, sea, soil and
ground before the ordinary opaque world objects. The capture excludes dry
rock caps, land, bridges and actors. After buildings, bridge
decks, actors and box batches, `TownGame.SubmitFramePart` calls
`TerrainRenderer.DrawWater`. That call draws the inland surface,
then shoreline foam, before foliage, weather and overlays.

| Component | Blend | Depth test | Depth write | Archive kind |
|---|---|---|---|---:|
| Riverbed and submerged bank skirts | Opaque | On | On | 108 |
| Wet-only rock pass before capture | Opaque, dry fragments discarded | On | On | 109 reused |
| Generated rocks | Opaque | On | On | 109 |
| Sea and optional rough-sea patch | Opaque | On | On | 107 / dynamic patch |
| Inland river/pond surface | Opaque internal composition with valid capture; source-alpha fallback | On | Off | 104 |
| Shoreline foam | Source alpha | On | Off | 105 |

With a valid capture, TEV combines the sampled bed image with the surface
using the existing depth-dependent opacity and writes the already-composited
color without another framebuffer blend. Without a capture, the surface uses
the existing source-alpha blend. Bed pixels and submerged rock pixels receive
the turquoise overlay; above-water
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

The native optics append three TEV stages for bumped lighting. Two separate
scrolling samples of a dedicated IA8 derivative map perturb a lighting lookup
derived from the cylindrical world position. Its intensity contains a narrow
Blinn highlight; its alpha contains a Fresnel-shaped sheen weight. Highlight
strength follows sunlight, while the sheen blends toward the current
environment-tinted sky clear color. The fixed light and view directions are
deliberate approximations, not a moving-sun or per-pixel view-vector model.

Five additional TEV stages sample the captured background through the
existing independent B/G displacement field. A nearest-filtered alias detects
captured clear color and rejects displacement into uncovered background. A
shore fade further reduces distortion in shallow water. The final background
color uses linear filtering and is composed once with the surface color.
This is a bounded coverage guard, not a scene-depth reconstruction: inspect
bilinear shore borders and submerged silhouettes in Dolphin and on hardware.
Lighting and refraction have separate amplitude controls.

The riverbed's color texture remains fixed in world coordinates. A separate
low-contrast caustic mask moves across it through another UV transform. The
caustic contribution is multiplied by the vertex/material lighting so it does
not remain a bright additive glow at night. Rocks use the same light pattern
with their own faceted vertex colors. Their triangles are split at the physical
waterline during construction. Wet vertices reserve alpha 1 when the caustic
fade would otherwise be zero; dry triangles keep alpha 0. This lets the capture
include the full submerged silhouette while the same channel still modulates
caustics. Boundary vertices are duplicated across the wet/dry split, so dry caps
cannot inherit wet coverage. The ordinary rock material always writes opaque
alpha. The existing caustic fade formula is retained, with a negligible minimum
on wet vertices and a slightly different interpolation near the physical cut.

The shallow and deep palette still derives from the existing seasonal floor
metadata. `SurfaceTint` supplies the inland opacity, while `OceanTint` always
returns opaque alpha. This separation also keeps the optional rough-sea wave
patch opaque when the inland material changes.

Ordinary wave animation changes shared material/native UV matrices and color.
It does not upload textures, regenerate archived geometry, move the riverbed,
or allocate per-frame rock meshes. Translations wrap to preserve precision in
long sessions. Refraction adds one synchronized EFB copy per rendered frame
part. Freeze-motion inspection holds wave phase while camera projection,
capture freshness and lighting continue to follow the rendered view.

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

Optional native texture packages load only when the shared materials or the
first visible-water optics resources are constructed. Each role falls back
independently to its procedural pattern.
Dimensions, native format, mip count and exact byte length are validated before
upload; malformed packages emit a diagnostic and retain the fallback.
Author PNGs are converted offline and never decoded in the frame loop.

The six replaceable texture roles contain 64 KiB of native pixel data in total.
The calm ocean retains a 4 KiB ripple mask and an 8 KiB distortion map, bringing
authored/procedural water maps to 76 KiB. The native lighting lookup adds 8 KiB,
and the half-resolution RGB565 capture adds 150 KiB at a 640-by-480 display.
The complete ordinary-water pixel allocation is therefore 234 KiB at that
display size. Rough-sea textures are another 8 KiB when active.
These figures exclude geometry, shader objects, managed
staging and package headers. The maps use repeat/linear sampling with one base
level; no authored mip chain is currently accepted. The lookup and capture
clamp at their edges. All native optics textures use the SDK's existing
resource accounting. The capture is retired when water is inactive, indoors,
during terrain rebuilds and before save pressure; the two 8 KiB optics maps
remain until their terrain owner is disposed. Capture allocation failure
keeps bumped transparent water and is retried only after an inactive/display
transition. Failure to initialize the maps keeps the original surface.

The shader uses 12 TEV stages normally, 13 with the ordinary environment
footer, and all 16 with the legacy shadow/light footer. Capture fallback uses
7, 8 or 11 stages respectively. It uses three indirect stages, six texgens
without a capture or seven with it, and at most seven texture-map slots.
There is no vertex-format change, NBT stream or CPU-expanded normal path.

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
rate. Verify memory pressure, raster cost, copy synchronization and distant
ripple stability in the actual game. The split-AA path copies each resident
band into the same logical capture and requires separate seam inspection.

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
material contracts, the analytic derivative field and native texture layout.
Native production-code fixtures cover stage/register composition, capture
lifecycle and fallbacks. Importer checks exercise format,
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
constants, blend mode and depth state at a real draw. Its optics record also
tracks texture formats, dimensions, capture generation/age and resource bytes.
It distinguishes refracted inland water, transparent fallback and the retained
opaque coast. The animation scenario
compares moving, frozen and resumed water in both archived and legacy paths;
its pixel checks supplement, rather than replace, inspection of the captures.
Physical GameCube appearance and performance require console validation.
