# Water bump lighting and refraction: implementation handoff

This document is the complete implementation plan and source contract for
AnimalTown's inland-water bump lighting and underwater refraction. It records
the exact data layout, native bridge, GX resources, draw order, lifetimes,
fallbacks, authoring format and validation procedure. The validation-status
section distinguishes local host evidence from console and hardware evidence.

## 1. Objective and scope

Extend AnimalTown's shallow inland water with two separately controllable visual
effects:

1. An animated small-scale surface-direction field that changes specular
   highlights and the environment sheen. A bright scrolling colour mask alone
   does not satisfy this requirement: the sampled direction must affect the
   lookup used for the lighting response.
2. Refraction of the rendered riverbed and submerged rocks. The apparent image
   beneath the water must move independently of the surface pattern. Distorting
   only the ripple texture does not satisfy this requirement.

Retain the existing stationary riverbed, deterministic rocks, turquoise depth
tint, underwater caustics, shoreline foam, bridge occlusion, and opaque ocean.
Use restrained motion appropriate to the existing stylized art direction. The
feature must remain functional with procedural assets alone.

The environment sheen is a stylized directional lighting/reflection response,
not a second rendering of mirrored actors, buildings or the sky. The separate
screen capture is used for underwater refraction only. No reflective-scene
render pass or reflection cubemap is required.

This is a world-rendering feature. It does not introduce a menu, HUD panel,
editor, terrain-editing tool, save-field migration or physics simulation. The
existing landscape freeze and water inspection controls are the validation
controls. Any later user-interface design requires its own approved mockup.

### Source baseline

The source was inspected at AnimalTown commit
`69eaee06b3babd426052ab1c7552580679a7482f`, the shallow-water branch
`feat/shallow-water-wallpaper` associated with
[PR #160](https://github.com/capz/AnimalTown/pull/160). That prerequisite merged
to `main` as commit `37665a5713e3cbe925b8cfb527fb8a9736c9af92` during this
implementation. Both commits have the identical source tree
`9430889f2c8320c501a78aa05211ee1cbf2b3cd5`, so the pinned inspection links
remain valid.

The working branch is `feat/water-bump-lighting-refraction`, based directly on
that `main` merge commit. Its new PR targets `main`; no stacked open-PR
dependency is required. Preserve the merged shallow-water feature and review
only the additional optics changes against this integration base.

Pinned build dependencies at that source revision:

| Component | Pinned value | Source |
| --- | --- | --- |
| .NET SDK | `10.0.404`, `latestFeature` roll-forward | `global.json` |
| DolphinDotNet MSBuild SDK | `0.1.0-preview.376.1` | `global.json` |
| Hypercube GameCube engine | `0.1.0-preview.52.1` | `game/AnimalTown.GameCube/AnimalTown.GameCube.csproj` |
| Card-store source dependency | Git submodule `vendor/card-store-sdk` | `.gitmodules` and the recorded gitlink |

Use the SDK native source included in that exact package when matching or
testing patch anchors. A different `main` checkout of DolphinDotNet is not a
substitute for verifying the source that this game actually bundles.

## 2. Current renderer and exact extension points

### Managed source map

| File | Existing responsibilities | Relevance to this implementation |
| --- | --- | --- |
| `game/AnimalTown.GameCube/WaterMaterial.cs` | Owns shared water, ocean, riverbed, rock and foam materials and textures; `Animate` changes UV transforms and foam colour; `Dispose` retires borrowed-texture effects before textures. | Preserve its four-stage shallow material and raster-alpha contract as the fallback. Add or attach the native enhancement with an explicit lifetime and scope. |
| `game/AnimalTown.GameCube/WaterRenderer.cs` | Builds inland surface, ocean and foam meshes; archives kinds 104, 107 and 105; computes shared corner colours and the exact cylindrical row-chord positions. | Supplies water geometry and any surface-orientation data required by lighting. Never use a fixed world-up normal for the entire curved world without a documented approximation. |
| `game/AnimalTown.GameCube/WaterBedRenderer.cs` | Builds and archives opaque bed/skirt and rock meshes; splits rock triangles once at the physical waterline; uses vertex alpha for wet coverage and caustic strength. | Includes the entire submerged rock in the refraction source while preserving dry caps and the existing geometry budget. |
| `game/AnimalTown.GameCube/TerrainRenderer.cs` | Owns `WaterMaterial`; chooses archived or legacy terrain; draws opaque bed/rocks/sea before the ordinary ground; draws inland surface and foam later via `DrawWater`. | Provides the capture and water-draw lifetime boundaries for both terrain paths. |
| `game/AnimalTown.GameCube/Program.cs` | `DrawScene` prepares camera/terrain/lighting; `SubmitFramePart` draws terrain, opaque world objects and actors, then `terrain.DrawWater(renderer)`, foliage/weather and UI. | The capture must be valid for the actual frame and camera and must exclude foreground actors/UI as required by the selected capture design. |
| `game/AnimalTown.GameCube/NativeWaterProbe.cs` | AOT-patched inspection-only bridge. | Keep observational diagnostics separate from feature enablement and avoid treating a setup call as proof of an actual draw. |
| `game/AnimalTown.GameCube/WaterBumpEffect.cs` | New managed owner for the optional native enhancement, one reusable settings array and initialization/fallback state. | Initializes only when inland water is visible, updates animation parameters, scopes native draws and retires resources. |
| `game/AnimalTown.GameCube/NativeWater.cs` | New AOT-patched native optics bridge. | Explicit lifetime, capture and draw-scope boundary; no resource allocations from draw callbacks. |
| `shared/AnimalTown.Domain/WaterSurface.cs` | Existing surface height, UV scale, depth and shoreline rules. | Preserve water levels, bridge traversal and geometry limits. |
| `shared/AnimalTown.Domain/WaterBedGeometry.cs` | Deterministic bed/rocks, physical submersion distance, caustic fade, shared wet alpha and conservative vertex count. | Separate actual underwater coverage from the one-centimetre quiet-caustic margin. |
| `shared/AnimalTown.Domain/ShallowWaterPattern.cs` | Procedural native-layout ripple, displacement, caustic and riverbed textures; opacity ramp. | Preserve the existing displacement-field meaning. New lighting-direction data needs its own explicit encoding or generation rule. |
| `shared/AnimalTown.Domain/WaterBumpPattern.cs` | New deterministic periodic derivatives and GX IA8 packing. | Generates the exact 8 KiB fallback for the sixth optional texture role, `surface-slopes`. |
| `shared/AnimalTown.Domain/WaterTextureCatalog.cs` | Shared authoring/runtime contract and exact payload validation; five roles at the baseline, six with `surface-slopes`. | Keep every authored role optional and give the new IA8 slope payload an exact format/length contract. |
| `game/AnimalTown.GameCube/WaterTextureAssets.cs` | Bounded optional `.dndtex` loader; validates dimensions/format/mips/length and falls back after errors. | Reuse this behaviour if introducing a new authored texture role. |
| `shared/AnimalTown.Domain/WorldLighting.cs`, `TownDaylight.cs` | Expose environment RGB tint, sunlight strength and lamp strength, including astronomical daylight presets. | They currently do not provide a full sun-direction vector. The new highlight direction must be explicitly defined rather than inferred from an API that does not exist. |
| `shared/AnimalTown.Domain/CylinderWorld.cs`, `CylinderRows.cs` | World curvature, row basis and scroll transform. | Match these transformations when constructing lighting and projected refraction coordinates. |

### Native source map

| File | Existing responsibilities |
| --- | --- |
| `tools/patch-native.ps1` | Patches freshly generated AOT/native sources, copies game-owned native includes and runs host fixtures. Anchors fail closed when pinned SDK source changes. |
| `tools/patch-water-optics.py` | Replaces all seven `NativeWater` AOT stubs; includes the optics implementation after the pinned vertex-state declaration; appends identical immediate/indexed hooks after the existing environment/shadow footer; records the frame clear colour. |
| `tools/native-water-optics.inc` | Owns SDK texture handles, immutable direction/lighting maps, optional frame capture, exact GX stage construction, per-pass freshness and diagnostics. |
| `tools/patch-water-probe.py` | Adds the `NativeWaterProbe.Enable` AOT bridge and wraps actual GX calls in generated `source/graphics_gx.c`. |
| `tools/native-water-probe.inc` | Records real matrix, TEV, indirect, texture, konst, blend and depth state at `GX_Begin`; forwards original GX calls. |
| `tools/native-shadow-union-gx.inc` | Appends environment/shadow/lamp stages and manages shared GX registers, texture slots and matrices. |
| `tools/native-terrain-archive.inc` | Owns the fixed ARAM archive and MEM1 views used by archived terrain. |
| `tests/native-water-probe.c` | Checks exact GX-call forwarding, actual-draw gating, frozen values, selected surface and bounded logging. |
| SDK `source/graphics_gx.c` and `include/dnd_indexed_mesh.inc` | The actual immediate and indexed draw backends patched by the game. Both routes must execute the same water enhancement and state cleanup. |

Baseline source links are pinned to the inspected revision:

- [WaterMaterial.cs](https://github.com/capz/AnimalTown/blob/69eaee06b3babd426052ab1c7552580679a7482f/game/AnimalTown.GameCube/WaterMaterial.cs)
- [TerrainRenderer.cs](https://github.com/capz/AnimalTown/blob/69eaee06b3babd426052ab1c7552580679a7482f/game/AnimalTown.GameCube/TerrainRenderer.cs)
- [Program.cs](https://github.com/capz/AnimalTown/blob/69eaee06b3babd426052ab1c7552580679a7482f/game/AnimalTown.GameCube/Program.cs)
- [Native patch integration](https://github.com/capz/AnimalTown/blob/69eaee06b3babd426052ab1c7552580679a7482f/tools/patch-native.ps1)
- [Existing water design](https://github.com/capz/AnimalTown/blob/69eaee06b3babd426052ab1c7552580679a7482f/docs/water-rendering.md)
- [Existing asset contract](https://github.com/capz/AnimalTown/blob/69eaee06b3babd426052ab1c7552580679a7482f/docs/water-art-assets.md)

### Existing draw order and depth contract

At the baseline, the frame submits:

1. Opaque riverbed/skirt and rock geometry, then opaque sea.
2. Soil, ground, cliffs and other terrain.
3. Buildings, bridge decks, actors, trees, placed and dropped objects, and the
   other opaque world submissions.
4. Translucent inland water, followed by shoreline foam.
5. Foliage, weather, overlays and UI.

| Component | Blend | Depth test | Depth write | Archive kind |
| --- | --- | --- | --- | ---: |
| Inland water | Source alpha | On | Off | 104 |
| Shoreline foam | Source alpha | On | Off | 105 |
| Ocean | Opaque | On | On | 107 |
| Bed and submerged skirts | Opaque | On | On | 108 |
| Rocks | Opaque | On | On | 109 |

The archived and legacy paths have different submission methods but must
produce equivalent visible effects. The late water depth test is what keeps
bridges, banks, actors and dry rock caps in front of the water. Preserve that
occlusion even if the enhanced shader composes a captured background itself.

The existing shallow material has four RGB stages, konst colours K0/K1 and
temporary RGB register 0. Its explicit alpha stages preserve vertex alpha
through every I8 ripple read. The environment/shadow footer uses additional
registers and stages, including K2/K3. Any expanded program needs a complete
resource allocation table for the final native draw, including that footer.

### Enhanced pass order

`TerrainRenderer.PrepareWaterOptics` checks for actual visible inland surface
meshes after terrain preparation. Archived mode checks kind 104; legacy mode
checks visible sections' `SurfaceMesh`. `Program.DrawScene` calls it with the
current focused Z, sunlight strength and environment colour before the frame
starts. `WaterBumpEffect` owns the optional native state for that terrain
renderer.

At the beginning of each actual outdoor terrain pass, `waterBump.Begin()`
invalidates any prior capture. The draw order then becomes:

1. Draw the original opaque bed and skirts.
2. If a refraction buffer was allocated, enter native scope 1 and draw only
   the wet portion of the existing rock geometry. Always leave the scope in a
   `finally` block.
3. Capture that underwater image before dry caps, ordinary land, sea, bridges
   or actors are submitted.
4. Draw the original complete rock geometry, opaque sea, ordinary terrain and
   world. This preserves visible dry caps and all existing opaque occluders.
5. Enter native scope 2 around the late inland surface. Apply the enhanced
   material and, when the capture is fresh, perform the underwater composition.
   Leave the scope in a `finally` block, then draw foam normally.
6. Continue with foliage, weather and UI using their ordinary material state.

The wet-rock prepass reuses the same archived/direct geometry as the full rock
draw; it does not allocate a separate capture mesh or rebake the archive per
frame. Vertex alpha carries physical wet coverage as well as caustic strength,
rather than the rock's visible framebuffer opacity. Scope 1 keeps alpha
greater than zero.

### Physical rock coverage and the caustic margin

The baseline caustic mask deliberately remains zero in the upper centimetre
below the water. Reusing that mask directly as capture coverage would omit a
real submerged band of an emerging rock. The later opaque water composite
would then replace that band with the bed captured behind it. Physical
coverage therefore needs a distinct zero/nonzero meaning within the existing
vertex-alpha channel.

`WaterBedGeometry` defines the following logical-height functions, with depths
in metres:

```text
waterHeight = WaterSurface.HeightAt(town, cellX, cellZ, point.X, point.Z)
SubmersionDistance = waterHeight - point.Height
CausticDistance   = waterHeight - 0.01 - point.Height
SubmersionMask    = byte(255 * clamp(CausticDistance / (0.12-0.01), 0, 1))
RockAlpha        = wetTriangle ? max(1, SubmersionMask) : 0
```

`WaterBedRenderer.RockTriangle` uses **SubmersionDistance** for its one split,
moving the cut from the old caustic-margin plane to the physical waterline.
An entirely nonnegative triangle is wet; an entirely nonpositive triangle is
dry, with the nonnegative check first for a coincident triangle. A crossing
triangle rotates its vertices without changing winding, computes edge
intersections with `t=distanceA/(distanceA-distanceB)`, and emits the same
three triangles as the previous one-cut algorithm. The wet/dry decision is
attached to each emitted triangle, not recalculated from a slightly rounded
intersection height.

Boundary vertices are duplicated: their wet-side copy has alpha at least 1,
while their dry-side copy has exactly 0. Every physically wet triangle is
therefore included by the prepass even when its caustic mask is zero; every
dry triangle stays excluded and receives no caustic contribution. The
per-vertex caustic fade formula remains the same, with the minimum wet alpha
reserving only 1/255 of its range. Interpolation within the new wet triangles
can introduce a tiny below-water caustic contribution near the surface; it
does not brighten the separate dry triangles.

`RockVertexCount` uses the same physical-distance classifier as the renderer.
There is still one cut per input triangle, at most nine output vertices for
that triangle, and at most **162 vertices per six-sided rock**. Archive kind
109, vertex layout, part count, `RockCapacity` and the existing 64 KiB part
limit remain unchanged. No second split, additional mask texture, new archive
part or additional mesh budget is introduced. Geometry is baked normally
when a terrain section is created; the capture pass merely reuses it.

The domain regression fixture samples flat, raised and north/east ramp water,
including points 0.1 mm above/below the surface and exactly on it. A test
stone with bottom `waterHeight-0.005`, top `waterHeight+0.005`, two 0.2 m
radii and angle zero must produce exactly **126 vertices**: it crosses the
physical waterline even though its entire submerged half lies inside the
quiet-caustic margin. `ShallowWaterMaterialChecks` also evaluates the emitted
rock program with raster alpha 1 across six receiver shades and three caustic
intensities; that marker alone rounds to no visible RGB addition while the
ordinary rock output remains opaque. These checks complement the native
prepass alpha-comparison fixture.

`Begin` is a per-render-pass operation, not merely a simulation-tick operation.
It runs again if the frame renderer submits more than one part of a frame.
This prevents a split-AA pass from reusing another part's capture. The current
native display implementation renders full-AA mode in two 248-line passes,
with logical Y origins 0 and 232 in a 640×480 image. A correctly sized
full-image capture allocation alone does not make both bands current.

## 3. Representation and mathematical contract

### Surface direction and bump lighting

The implementation must distinguish these data types:

- A height texture contains a scalar elevation.
- A two-channel derivative/perturbation field contains changes in direction
  along the surface basis.
- An ordinary tangent-space RGB normal map contains all three normal
  components and is not automatically interchangeable with the two-channel
  field.
- The existing `surface-distortion` texture contains UV displacement, with
  blue driving horizontal movement and green driving vertical movement. It is
  explicitly not a normal map.

For a perturbation field with components `p` and `q`, the conceptual surface
direction is proportional to `N + p*T + q*B`, subject to the selected sign and
axis conventions. The actual position-derived approximation and lookup
normalization are defined below; they should not be confused with a general
tangent-space shader. The CPU fixture evaluates the emitted TEV arithmetic
independently from the GX command generator.

AnimalTown's geometry curves around the X axis. Vertices in one row chord
share an interpolated position between `CylinderRows.At(z)` and `.At(z+1)`;
`ScrollTransform` then maps the archived reference coordinates into the live
view. The lighting basis and refraction projection must follow the same
object/view transformation. A forward-facing patch must keep its highlight
attached when the camera scrolls, rather than shifting because the renderer
assumed that every water tile had world normal `(0,1,0)`.

### Direction field and exact defaults

The new field is 64×64 **IA8**, with one eight-bit component per channel and
an exact neutral byte value of 128. The first byte of a GX IA8 texel is alpha
and stores tangent-X; the second is intensity and stores tangent-Z. Each
signed component is decoded by subtracting 128. A texture filled with
`0x80,0x80` represents a flat perturbation. GX IA8 uses 4×4 tiles; at texel
`(x,z)` the byte offset is:

```text
tile = (z / 4) * 16 + (x / 4)
offset = tile * 32 + ((z % 4) * 4 + (x % 4)) * 2
bytes[offset]     = tangentX + 128
bytes[offset + 1] = tangentZ + 128
```

The divisions above are integer divisions for nonnegative 0–63 coordinates.
The complete payload is 64×64×2 = **8,192 bytes**. It contains no mip levels.
It is sampled twice with independent scrolling coordinates; the maps
themselves remain immutable during ordinary animation.

The procedural source `WaterBumpPattern` evaluates, for `u=x/64`, `v=z/64`:

```text
a  = 2*pi*(2*u + v)
b  = 2*pi*(u - 2*v)
c  = 2*pi*(3*u + 2*v)
dx = 0.50*sin(a) + 0.25*sin(b) + 0.15*sin(c)
dz = 0.25*sin(a) - 0.50*sin(b) + 0.10*sin(c)
X  = 128 + round(100*dx)
Z  = 128 + round(100*dz)
```

These are the scaled negative derivatives of one continuous height field:

`h(u,v) = 0.25*cos(a) + 0.25*cos(b) + 0.05*cos(c)`.

The finite-difference test derives the gradients from that independent height
function, which catches swapped axes or a ridge direction inconsistent between
the two channels. Samples wrap in both axes and the full periodic field has
zero average tilt. The generated values remain inside 0–255 without clipping.

The existing mesh UV is `(logicalX,logicalZ) * 0.25`. The new layers multiply
that UV by 2.2 and 3.1 respectively, then apply wrapped translations:

| Layer | UV scale | Scroll U, repeats/second | Scroll V, repeats/second |
| --- | ---: | ---: | ---: |
| Wave 0 | 2.2 | +0.016 | +0.021 |
| Wave 1 | 3.1 | -0.011 | +0.014 |

Those scales correspond to repeat periods of approximately 1.818 m and
1.290 m in logical world coordinates. Translation wrapping uses
`WaterSurface.Scroll`, preserving phase precision during long sessions. Both
sampled derivative vectors contribute equally and are added, without a
one-half averaging factor. Each layer's signed byte divided by 128 is
multiplied by the slope amplitude. The existing ripple and displacement maps
keep their original coordinates and meanings.

Default optical parameters are slope amplitude **0.14**, refraction strength
**1.5 full-resolution pixels**, specular strength **0.18**, and environment
sheen strength **0.10**. They are separate fields in the single reusable
settings array described below. They are internal material controls, without
new saved settings or UI.

### Lighting lookup and cylinder approximation

`Initialize` generates a separate 64×64 IA8 lookup, also **8,192 bytes**. It
is immutable after initialization; neither animation, camera motion nor a
lighting change regenerates it. At lookup texel `(x,y)`:

```text
nx = (x + 0.5) / 32 - 1
nz = (y + 0.5) / 32 - 1
r2 = nx*nx + nz*nz
ny = sqrt(max(0, 1-r2))
L  = normalize((-0.25, 0.70, -0.67))
V  = (0, 0.60, 0.80)              # already unit length
H  = normalize(L+V)
specular = max(dot((nx,ny,nz),H),0)^64, when r2<=1; otherwise 0
facing   = 1 - clamp(dot((nx,ny,nz),V),0,1)
fresnel  = 0.04 + 0.96 * facing^5
IA8 alpha     = round(255 * clamp(fresnel,0,1))
IA8 intensity = round(255 * clamp(specular,0,1))
```

The lookup has clamp addressing and linear filtering. It reconstructs the
upper normal hemisphere inside the unit disk. Outside the disk, specular is
zero and the sheen formula uses `ny=0`; this is a bounded stylized response,
not a normalized extension onto a lower hemisphere. Raising the clamped
specular base to the 64th power uses six squarings, with no per-frame power
function.

For each draw, let `W` be the actual column-major world matrix and let
`P=(x,y,z,1)` be an original mesh position. The undistorted lookup coordinates
are:

```text
uBase = 0.5
vBase = 0.5 + ((W*P).z - focusZ) / (2*radius)
```

The two signed IA8 samples then add to those coordinates. Ignoring fixed-point
and filtering quantization, the normal components addressed by the lookup are:

```text
nx = slopeAmplitude * ((wave0.alpha-128)+(wave1.alpha-128)) / 128
nz = ((W*P).z-focusZ)/radius
     + slopeAmplitude * ((wave0.intensity-128)+(wave1.intensity-128)) / 128
```

The position contribution follows the archived world transform, so scrolling
the cylindrical world does not slide the base normal independently of the
geometry. It approximates the cylinder's normal-Z component directly from
position and reconstructs positive normal-Y in the lookup. It does not carry
per-vertex tangents or rotate each derivative by the exact local tangent
basis. Row-chord interpolation, water height and large angles away from the
focus therefore introduce a basis approximation; inspect near and far visible
rows when changing the camera or cylinder radius. No managed or native vertex
layout changes are required.

Let `S` be the existing water RGB after its environment/shadow footer and
`E` the current frame-clear RGB quantized and expanded as RGB565. The three
lighting stages compute, with ordinary TEV clamping:

```text
gSpec = round(255 * specularStrength * sunlight/255) / 255
gEnv  = round(255 * sheenStrength) / 255
S1    = clamp(S + lookup.intensity * gSpec, 0, 1)
litSurface = lerp(S1, E, lookup.alpha * gEnv)
```

Specular is a white highlight, reduced by sunlight; the environment sheen is
a directional blend toward the scene's sky/clear colour. `Program.DrawScene`
already tints its clear colour by the current environment tint and selects a
different rainy sky colour, so the sheen follows those changes without a
second multiplication. The `environmentRgb` bridge argument records the
lighting provenance in diagnostics; the actual sheen colour comes from the
clear-colour hook. Neither the lookup nor this blend samples reflected world
objects.

This is a fixed artistic light and view response. It does not use an
astronomical light azimuth, a camera-dependent reflection vector or a local
point-light direction. A zero slope amplitude removes the animated direction
offset while retaining cylinder-dependent lighting. Zero specular or sheen
strength removes that contribution independently. A zero refraction strength
selects undisplaced capture coordinates; it does not disable allocation or
the capture pass.

### Managed/native bridge

`NativeWater` declares the following AOT-patched methods. Their host C# bodies
throw `PlatformNotSupportedException`; the native bundle patch replaces the
stubs and fails if any expected generated signature is absent or ambiguous.

| Managed method | Call timing | Result contract |
| --- | --- | --- |
| `Initialize(byte[] slopes)` | Between frames, once on first visible inland water | 1 on complete initialization; 0 on failure with original water retained. |
| `Prepare(float[] settings, int active, int sunlight, int environmentRgb)` | Between frames after actual visibility/camera preparation | 0 inactive/unavailable; 1 bump resources ready; 3 bump resources plus capture buffer ready. |
| `Begin()` | Beginning of each actual outdoor terrain pass | 1 when active; 0 otherwise. Invalidates previous capture regardless of whether a new one will be produced. |
| `Scope(int mode)` | Immediately around a draw group | Returns previous mode; mode 0 ordinary, 1 wet-rock capture prepass, 2 enhanced inland surface. |
| `Capture()` | After bed/wet rocks, before remaining world | 1 when a valid current-pass capture is available; 0 otherwise. |
| `Shutdown()` | Between frames during owner disposal | 1 after resource retirement and state reset. |
| `Stats(int kind)` | Diagnostics/tests | Exact selector meanings are listed with the native instrumentation contract below. |

The reusable `float[12]` settings ABI is fixed as follows:

| Index | Meaning | Default/source |
| ---: | --- | --- |
| 0 | Focus Z | Current draw/inspection focus `state.Z`. |
| 1 | Cylinder radius | `CylinderWorld.Radius`. |
| 2 | Wave 0 U offset | Wrapped `seconds * 0.016`. |
| 3 | Wave 0 V offset | Wrapped `seconds * 0.021`. |
| 4 | Wave 0 UV scale | 2.2. |
| 5 | Wave 1 U offset | Wrapped `seconds * -0.011`. |
| 6 | Wave 1 V offset | Wrapped `seconds * 0.014`. |
| 7 | Wave 1 UV scale | 3.1. |
| 8 | Surface slope amplitude | 0.14. |
| 9 | Refraction displacement in full-resolution pixels | 1.5. |
| 10 | Specular strength | 0.18. |
| 11 | Environment sheen strength | 0.10. |

`environmentRgb` is packed `0xRRGGBB`; `sunlight` uses the game's 0–255
strength convention. The array is owned by `WaterBumpEffect`; no new array is
created per frame. The native side must copy the needed values rather than
retain a pointer into temporary managed storage.

The native bridge requires exactly twelve non-reference four-byte elements,
all finite. Radius must be at least 1 and both layer scales must be positive.
It copies the twelve floats into static native settings, clamps amplitude to
0–0.35, refraction to 0–4 full-resolution pixels, and both lighting strengths
to 0–1. Sunlight is clamped to 0–255 and environment colour is masked to
24 bits. The managed owner supplies already wrapped offsets; the native
bridge does not wrap arbitrary caller offsets. An invalid preparation disables
the active effect and invalidates the current capture. Initialization and
preparation return 0 if attempted during an active frame or raw GX scope.

### Refraction composition

The optical composition is a depth-dependent mixture of the apparent
underwater scene and the lit surface response. With baseline opacity `a`,
the result is:

`result = (1 - a) * refracted_underwater_scene + a * lit_water_surface`

The final TEV stages perform this composition and emit alpha 255 with
`GX_BM_NONE`. Applying source-alpha blending afterward would mix the
background twice. The draw retains the managed water depth test (`LEQUAL`)
and disabled depth writes.

The selected source is captured after the bed and wet-only rock prepass.
Above-water actors, bridges and dry caps have not been drawn into that image.
The normal full rocks/land/world submissions happen afterward and preserve
their regular occlusion against late water. This choice prevents refraction
from pulling a character or bridge silhouette into adjacent water merely
because it was present in an all-opaque-scene screenshot.

#### Capture geometry, projection and synchronization

Read logical dimensions from `dnd_device_width()` and `dnd_device_height()`.
Capture is supported when both are multiples of eight, at least eight,
width ≤640 and height ≤528, and `width*height/2 ≤168960`. Allocate an RGB565
texture of `(width/2,height/2)` through `dnd_texture_allocate_native`. At
640×480 this is 320×240 and **153,600 bytes**; at 640×528 it is 320×264 and
**168,960 bytes**. Unsupported dimensions retain bump lighting with the
ordinary transparent framebuffer composition.

Capture uses the existing
`dnd_draw_backend_copy(texture,0,0,sourceWidth,sourceHeight)` helper. It sets
`GX_SetTexCopySrc` and `GX_SetTexCopyDst` for half-size downsampling and then
issues `GX_CopyTex(destination,GX_FALSE)`, `GX_PixModeSync`, `GX_DrawDone`,
and `GX_InvalidateTexAll`. The copy preserves EFB colour and depth. It uses
the current SDK copy filter established for the display mode; it does not
introduce a CPU readback or change the display's pixel format. The explicit
GPU completion is a real synchronization cost and must be included in timing
measurements. The material cache is invalidated around the copy.

The capture is bound twice: map 5 uses linear filtering for colour and map 6
uses nearest filtering for validity. Both views use clamp addressing, no mip
levels, and the **same** texture allocation. For each water draw compute:

```text
clip = projection * view * world       # column-major matrices
screen[0][c] = 0.5 * (clip[c*4+0] + clip[c*4+3])
screen[1][c] = 0.5 * (clip[c*4+3] - clip[c*4+1])
screen[2][c] = clip[c*4+3]              # c=0..3
```

Load this 3×4 matrix into `GX_TEXMTX5` and generate texcoord 6 from
`GX_TG_POS`. GX interpolates S/T/Q and divides by Q, giving
`u=0.5*(clipX/clipW+1)`, `v=0.5*(1-clipY/clipW)`. This follows the SDK's
`dnd_screen_projection_matrix` convention without an extra half-texel offset.
Projection is regenerated from the actual object, camera and projection on
every submitted water draw, including while wave motion is frozen. The
current integration uses the full logical viewport; changing that assumption
requires a matching viewport transform.

In full-AA mode the SDK copies each 248-line EFB band into the corresponding
part of one full logical 640×480 capture: source origin 0 for part 0 and 232
for part 1, destination row 0 or 116 at half resolution. The global logical
projection above is correct; applying the band's adjusted projection would
incorrectly stretch a half frame over the full capture. Only rows 0–239 from
part 0 and 240–479 from part 1 survive presentation. Their eight-row overlap
covers the maximum four-pixel displacement and filter footprint, so no
additional texcoord band clamp is applied. Do not increase that displacement
limit without revisiting this guarantee. Screen boundaries clamp to the
capture's edges.

`Begin` advances a nonzero per-pass sequence and clears capture validity.
`Capture` requires an active 3D device pass, no raw GX scope, a resolvable
texture, unchanged dimensions and the current sequence. A second `Capture`
in the same pass returns 1 without copying again. A normal frame therefore
copies at most once; a full-AA frame copies at most once for each submitted
part. The draw requires `capturedPart==currentPart` before using refraction.
The identifiers are pass sequences, not merely the SDK's 0/1 AA index.

#### Displacement, shoreline fade and clear-pixel guard

Actual underwater colour is displaced by the **existing independent**
`surface-distortion` map, sampled by indirect stage 0 at its existing ripple
coordinates. The new two-wave IA8 slopes drive lighting; they do not replace
the displacement map. With signed blue/green components `b-128`, `g-128`,
indirect matrix 2 supplies:

```text
deltaCaptureTexels = (b-128, g-128) * refractionPixels/256
deltaFullPixels    = 2 * deltaCaptureTexels
```

The native signed range is therefore approximately ±`refractionPixels` in
full-resolution pixels. The small asymmetry at +127 and original RGB565
channel quantization are retained.

Let `B0` be the unwarped linear capture sample, `B1` the displaced linear
sample, and `Q1` the displaced nearest sample. Let `alphaByte` be the original
water vertex opacity after interpolation, ordinarily 72–144. The guard is:

```text
shoreWeight = clamp(4*(alphaByte-96), 0, 255) / 255
validOffset = 0 when Q1.rgb == quantizedFrameClear.rgb; otherwise 1
weight = shoreWeight * validOffset
background = lerp(B0, B1, weight)
result = lerp(background, litSurface, alphaByte/255)
outputAlpha = 255
```

Equality compares all three expanded RGB565 channels exactly using
`GX_TEV_COMP_BGR24_EQ`; there is no colour tolerance or EFB-alpha dependency.
The frame-clear RGB is converted to five/six/five bits and expanded by bit
replication, matching the texture sample. The original draw state disables
dither. The nearest alias prevents the validity comparison itself from
blending clear and underwater colours. A rejected displaced texel selects the
unwarped captured bed, retaining visible transparency through the internal
composition. At the shallow edge the weight is zero; at deepest opacity 144
the maximum weight is 192/255, retaining an undistorted share.

This is a conservative colour guard and geometry-based source selection,
not a depth buffer or thickness ray trace. It can reject a valid underwater
texel that exactly matches sky colour; that only removes local displacement.
Linear colour samples near a clear boundary can still mix adjacent texels,
which the shoreline fade reduces. The bed/skirt geometry must continue to
cover the unwarped visible inland water; changing that geometry requires
renewed edge/estuary checks. Foreground objects are excluded by capture timing
and the later water depth test, rather than by this colour comparison.

If the capture is absent or stale, the native path still adds the three
lighting stages, restores raster opacity, and keeps source-alpha framebuffer
blending. If the direction/lookup resources or expected material contract are
unavailable, it leaves the original water program intact. Neither fallback
uses another frame's captured scenery.

## 4. GX resource allocation and state ownership

GX supplies at most sixteen TEV stages and four indirect stages in a draw
configuration. Those limits include any stages appended by the game, not only
the material package. The enhancement is appended after the existing
environment/shadow footer. The lighting stages and the additional captured-pixel
validity guard must both fit the final table below. The original ocean path
remains unchanged.

### Complete program budgets

Let `b` be the stage count after the original water program and footer.
Archived daytime water has `b=4`; an environment-tinted archived draw has
`b=5`; legacy water with the four-stage shadow/lamp footer has `b=8`.

| Draw path | TEV stages | Indirect stages | Texcoord generators |
| --- | ---: | ---: | ---: |
| Original archived fallback | 4 or 5 | 1 | 2 |
| Original legacy fallback | 8 | 1 | 3 |
| Bump/sheen, no valid capture | 7 / 8 / 11 for the three bases | 3 | 6 |
| Bump/sheen plus guarded refraction | 12 / 13 / 16 for the three bases | 3 | 7 |

The full legacy program uses all sixteen stages. An additional effect cannot
be appended without redesigning the program or selecting a fallback. The
native hook only enhances scope 2, an active four-stage effect with two
uniform colours, nonzero indirect strength, a secondary texture, alpha blend
and disabled depth writes, with `b<=8`. This explicit scope plus material
contract separates inland water from ocean, foam and unrelated effects.

### Texture, coordinate and indirect assignments

| Texture map | Image | Format and size | Sampling | Ownership |
| ---: | --- | --- | --- | --- |
| 0 | Existing surface ripples | I8, 64×64, 4,096 bytes | Repeat, linear | Existing `WaterMaterial`. |
| 1 | Existing surface displacement | RGB565, 64×64, 8,192 bytes | Repeat, linear | Existing `WaterMaterial`. |
| 2 | Legacy packed shadow/lamp page | Existing IA4 page | Existing footer clamp/linear | Existing shadow system; only legacy. |
| 3 | Surface slopes | IA8, 64×64, 8,192 bytes | Repeat, linear | Native optics SDK texture handle. |
| 4 | Specular/Fresnel lookup | IA8, 64×64, 8,192 bytes | Clamp, linear | Native optics SDK texture handle. |
| 5 | Underwater capture colour | RGB565, half logical width/height | Clamp, linear | Optional native optics SDK texture handle. |
| 6 | Nearest validity view | The same capture bytes as map 5 | Clamp, nearest | Alias only; no additional allocation. |

All new samplers have one level, zero LOD range/bias and anisotropy 1. They
are initialized explicitly on enhanced draws. Their filtering does not
inherit a previous material's sampler.

| Texcoord | GX source | Matrix | Meaning |
| ---: | --- | --- | --- |
| 0 | `GX_TG_TEX0` | `GX_TEXMTX0` (30), 2×4 | Original lower ripple transform. |
| 1 | `GX_TG_TEX1` | `GX_TEXMTX1` (33), 2×4 | Original upper ripple/displacement transform. The backend supplies the same mesh UV to TEX0 and TEX1. |
| 2 | Existing footer source; otherwise identity `GX_TG_TEX0` | `GX_TEXMTX8` (54) for legacy | Reserved for original shadow/lamp reads. Do not reuse it for slopes. |
| 3 | `GX_TG_TEX0` | `GX_TEXMTX2` (36), 2×4 | Diagonal scale `p[4]`, translation `p[2],p[3]`. |
| 4 | `GX_TG_TEX0` | `GX_TEXMTX3` (39), 2×4 | Diagonal scale `p[7]`, translation `p[5],p[6]`. |
| 5 | `GX_TG_POS` | `GX_TEXMTX4` (42), 2×4 | Position-derived base lighting coordinates. |
| 6 | `GX_TG_POS` | `GX_TEXMTX5` (45), 3×4 | Perspective underwater capture S/T/Q; only with refraction. |

For coordinate 5 use `s=0.5/radius` and the exact rows:

```text
[ 0,        0,        0,         0.5                         ]
[ W[2]*s,   W[6]*s,   W[10]*s,   0.5+(W[14]-focusZ)*s       ]
[ 0,        0,        0,         1                           ]
```

All new coordinates use automatic texture scaling and disabled S/T bias.
The two unused affine Z entries in wave matrices preserve the ordinary
`[0,0,1,0]` third row; 2×4 texgen uses only the first two rows.

| Indirect stage | Source | Bias/channel use | Matrix |
| ---: | --- | --- | --- |
| 0 | Texcoord 1, map 1 | Existing `.abg` fetch; `TU` makes blue/green signed. | Original ripple stages use matrix 0; refraction stages use matrix 2. |
| 1 | Texcoord 3, map 3 | IA8 `.abg=(X,Z,Z)`; signed `ST`. | Matrix 1, first slope layer. |
| 2 | Texcoord 4, map 3 | IA8 `.abg=(X,Z,Z)`; signed `ST`. | Matrix 1, second slope layer. |

Every stage uses 1:1 indirect coordinate scaling and eight-bit indirect
samples. The matrices are independent:

```text
matrix 0: [[0,0.035,0], [0,0,0.035]], exponent -1   # unchanged ripple warp
matrix 1: [[a/4,0,0],  [0,a/4,0]],    exponent 0    # a=slope amplitude
matrix 2: [[0,r/256,0],[0,0,r/256]],  exponent 0    # r=full-res pixels
```

Changing indirect matrix 0 would also change the earlier ripple stages in
the same draw. Similarly, matrix and texcoord assignments are global to the
draw, so declaring an assignment after the footer does not make a conflicting
assignment safe for earlier stages.

### Existing stages and register ownership

For the tables below `lerp(A,B,C)=A*(1-C)+B*C`, `T` means the current texture
sample, `R` raster colour and `P` the previous RGB result. The ordinary TEV
operation is `D+lerp(A,B,C)`, clamped, scale 1 and zero bias. RGB and alpha
registers can be written independently.

| Stage | Texture coordinate/map | RGB result | Alpha result |
| ---: | --- | --- | --- |
| 0 | No required sample | `P=R.rgb` | `APREV=R.a`. |
| 1 | 0 / 0, indirect 0/matrix 0 | `C0=T.rgb*0.5` | `APREV=R.a`. |
| 2 | No required sample | `P=lerp(P,K0.rgb,C0)` | `APREV=R.a`. |
| 3 | 1 / 0, indirect 0/matrix 0 | `P=lerp(P,K1.rgb,T.rgb)` | `APREV=R.a`. |
| 4, archived tint only | None | `P=P*K2.rgb` | Preserve `APREV`. |
| 4, legacy | 2 / 2 | `P=P*K2.rgb` | `A2=shadowRed*R.a`. |
| 5, legacy | None | Preserve `P` | `A2=A2*K2.a`. |
| 6, legacy | 2 / 2 | `P=lerp(P,K3.rgb,A2)` | `A2=lampMask*R.a`. |
| 7, legacy | None | `P=P+C1*A2` | Preserve `APREV`. |

The baseline explicit alpha stages are essential: I8 ripple intensity must
not become water opacity. The legacy footer preloads `C1` with lamp RGB and
consumes it at stage 7. The optics hook preserves that value until its own
later refraction stages no longer need the lamp input.

| Global register | Before/through original stages | Optics use |
| --- | --- | --- |
| K0 RGB | `(54,127,145)` lower tint | Preserved exactly. |
| K0 alpha | Unused by original program/footer | Quantized specular gain; 46 at default strength and full sunlight. |
| K1 RGB | `(211,242,236)` glint tint | Preserved exactly. |
| K1 alpha | Unused by original program/footer | Quantized sheen gain; 26 at default strength. |
| K2/K3 | Existing environment and shadow colours/strength | Never repurposed. |
| C0 / A0 | Stage 1 writes lower-ripple RGB; alpha is unused | Preload A0=96 for shore fade; C0 becomes captured colour only after the footer. |
| C1 / A1 | Legacy lamp RGB; alpha initially zero | After lighting, A1 holds shore weight and C1 holds the guard weight. |
| C2 / A2 | RGB unused by base/footer; footer uses only A2 | Preload C2 with quantized clear RGB. Lookup later overwrites A2 with Fresnel×sheen gain. |
| PREV / APREV | Current surface colour and original opacity | Preserved until the final composition. |

K0/K1 alpha changes are intentionally visible in the probe. At default noon
their packed RGBA values are `367f912e` and `d3f2ec1a`, rather than the original
values ending in `ff`.

### Appended stages, exact operands and outputs

Every stage initially uses swap 0 and `GX_COLOR0A0`; all unlisted alpha
operations preserve `APREV`. `NONE` means a null texture/coordinate, not a
read of stale texture state. `C0/C1/C2` and `A0/A1/A2` name the RGB/alpha
halves of TEV registers 0/1/2.

| Stage | Coordinate / map | RGB inputs `(A,B,C,D)` and output | Alpha override | Indirect state |
| --- | --- | --- | --- | --- |
| `b+0` | 5 / `GX_TEXMAP_DISABLE` | `(0,0,0,CPREV)` → PREV | None | Stage 1, matrix 1, signed ST, wrap S/T to zero, no previous-coordinate addition. Seeds the first slope offset. |
| `b+1` | 5 / 4 | `(0,TEXC,K0.a,CPREV)` → PREV | `(0,TEXA,K1.a,0)` → A2 | Stage 2, matrix 1, signed ST, no wrapping, add previous offset. Samples the lookup using both slopes. |
| `b+2` | NONE | `(CPREV,C2,A2,0)` → PREV | If capture unavailable, set APREV to raster alpha. | Direct. Environment sheen. |
| `b+3` | 6 / 5 | `(0,0,0,TEXC)` → C0 | SUB `(0,A0,1,RASA)`, scale 4, clamp → A1 | Stage 0, matrix 2, signed TU, no wrapping/accumulation. Displaced linear colour and shore weight. |
| `b+4` | 6 / 6 | COMP_BGR24_EQ `(TEXC,C2,A1,0)` → C1 | None | Same displacement as `b+3`. Produces shore weight when the displaced nearest texel equals clear colour. |
| `b+5` | NONE | SUB `(0,C1,1,A1)` → C1 | None | Direct. Inverts the clear guard: valid-offset weight is A1−C1. |
| `b+6` | 6 / 5 | `(TEXC,C0,C1,0)` → C0 | None | Direct, unwarped. Mixes stationary and displaced captured colour. |
| `b+7` | NONE | `(C0,CPREV,RASA,0)` → PREV | Set APREV to konst 1. | Direct. Final background/surface composition. |

Only `b+0` through `b+2` exist without a valid capture. Except the documented
SUB/compare cases, operations use ADD, scale 1, zero bias and clamping.
Indirect stages use `GX_ITBA_OFF` and do not enable the unmodified-coordinate
LOD option. The completed program sets its final stage count explicitly.

### EFB and cache boundaries

Scope 1 overrides the final rock stage's alpha with original raster alpha and
sets alpha compare `GREATER 0`, keeping only wet geometry. It leaves the
normal rock blend/depth configuration intact. The ordinary rock submission
after the scope rebinds its own opaque alpha state.

The enhanced surface either retains alpha blend or, with a capture, sets
`GX_BM_NONE/GX_BL_ONE/GX_BL_ZERO/GX_LO_CLEAR`. Its depth remains
`GX_TRUE/GX_LEQUAL/GX_FALSE`; the copy does not clear or replace depth. Colour
write, alpha write, culling and the RGB8/Z24 or RGB565/Z16 EFB format remain
owned by the SDK's ordinary draw state. No usable EFB alpha plane is assumed.

`Prepare`, `Begin`, `Scope`, `Capture` and `Shutdown` invalidate the SDK
material cache. Both the immediate and indexed hooks run after
`town_shadow_ground` and before the primitive. Every managed scope ends in a
`finally` block; the next ordinary draw then restores its complete material
state. No water setup may leak into foam, actors, sea or UI.

The enhancement must be scoped to the intended inland material. Do not
identify it solely by a generic texture size or the fact that it is
transparent. Ocean, foam, other transparent objects, actor rim lighting and
subsequent UI must retain their own state. Cached SDK draw state must be
invalidated or restored whenever game-owned native state changes outside the
SDK's knowledge.

The legacy river path has a shadow/light scope. Avoid multiplying a captured,
already environment-tinted riverbed by the environment tint a second time.
Similarly, a sun specular contribution must be reduced by the configured
sunlight response instead of appearing as a constant bright emission at night.
The current game exposes `Tint`, `Sunlight` and `Lamps`, but no astronomical
sun vector. Any fixed artistic light direction must be stated explicitly.

## 5. Lifetime, memory and fallback contract

### Existing fixed budgets

| Resource | Baseline payload/budget |
| --- | ---: |
| Original five optional/procedural inland water textures | 57,344 bytes (56 KiB) |
| Calm ocean ripple/displacement textures | 12,288 bytes (12 KiB) |
| Optional rough-ocean textures | 8,192 bytes (8 KiB), lazy |
| Terrain archive | 8 MiB in ARAM |
| Resident terrain cache | 1 MiB |
| Terrain mesh views | 256 |
| Archive kinds | 110 |
| Native draw-resource slots | 768 |

These are separate accounting categories. Texture-cache accounting can overlap
SDK native-resource accounting; do not add overlapping totals and present them
as physical MEM1 usage. Incremental native, managed, ARAM and transient staging
costs are separated below.

The enhancement adds one 8,192-byte slope texture and one 8,192-byte lighting
lookup, plus the optional capture. Its persistent pixel budgets are:

| Mode/state | Additional native pixel bytes | Total water pixel bytes, including the original 56 KiB inland and 12 KiB calm ocean |
| --- | ---: | ---: |
| Bump only, or capture retired | 16,384 (16 KiB) | 86,016 (84 KiB) |
| 640×480, with 320×240 capture | 169,984 (166 KiB) | 239,616 (234 KiB) |
| Maximum 640×528, with 320×264 capture | 185,344 (181 KiB) | 254,976 (249 KiB) |

The six-role water asset catalog contains 65,536 bytes (64 KiB), already
including the slope texture; do not add it twice. Optional rough-ocean maps
add their existing 8 KiB separately. The nearest capture sampler is an alias
and adds no pixel storage.

Resources use `dnd_texture_allocate_native`, the existing SDK draw-resource
table, its 16 MiB graphics byte cap and AnimalTown's 768-handle limit. The
feature uses two texture handles without capture or three with capture. GX
pixel storage is aligned to 32 bytes. SDK allocation records pixel bytes in
`total_bytes`, separately from small alignment slack, malloc metadata and the
pre-existing resource table; `Stats(0)` reports these pixel bytes, not total
physical MEM1 use. No new ARAM allocation or terrain mesh storage is added.

Managed persistent state is one owner object and one 12-float array, whose
data payload is 48 bytes plus object/array headers. Procedural initialization
temporarily needs an 8,192-byte managed payload. An authored load can briefly
hold the 8,224-byte package and the extracted 8,192-byte payload together,
**16,416 bytes** plus headers, before they become collectible. The bounded
payload copy uses an ordinary loop supported by the pinned AOT compiler.
The lighting lookup is generated directly into its native allocation and
needs no additional managed pixel buffer.

Allocation, deallocation and failure behaviour must satisfy these rules:

- Allocate persistent enhancement resources outside an active frame and before
  drawing their first use. Charge allocations to the existing native accounting
  and caps.
- Reuse bounded buffers. No per-frame managed arrays, procedural texture
  regeneration, rock objects or terrain rebuilds are permitted for wave motion.
- Dispose only after the last GPU use has retired. Release resources on session
  teardown; document whether shorter indoor/visibility transitions retain or
  release the allocated maps/capture.
- Handle partial initialization atomically. Free successfully allocated
  intermediates, invalidate capture state, and retain the ordinary shallow
  material if an optional enhancement cannot initialize.
- A missing or invalid capture must select the fallback for that frame; it
  must not draw stale underwater scenery from a different camera or town.
- Freeze motion stops the time-driven ripple/direction/caustic transforms.
  Frame validity, view-dependent projection and capture freshness still track
  the actual scene. Do not freeze the camera's capture matrix accidentally.
- Indoor, loading, title and cruise frames must not reuse an outdoor capture or
  pay an unnecessary per-frame copy cost.
- A restore from legacy terrain to the archive must still fit the same fixed
  cache and model residency budgets. The existing water tour exercises this
  memory-pressure case.

`WaterBumpEffect` attempts initialization once when inland water first becomes
visible. It loads the optional native `surface-slopes` data or generates the
fallback, passes it to `Initialize`, and logs a diagnostic on failure. It does
not repeatedly allocate during every failed frame. Owner disposal is
idempotent. `Prepare` updates visibility and lighting even when wave animation
is frozen, and `CaptureReady` checks capability bit 2 of the native return.
`Suspend` calls `Prepare` with `active=0`, then clears the managed feature bits.
Terrain reset, save suspension and outdoor-cache suspension call it before
changing the scene's resident geometry. A completed indoor `DrawScene` also
prepares the effect as inactive. Neither path creates a new settings array.

### Native lifetime and failure transitions

`Initialize` validates a non-reference byte array of exactly 8,192 elements.
After validation, it retires any existing resources, allocates/uploads the
slope texture, allocates/builds the lookup, and publishes CPU writes with
`dnd_draw_backend_flush(...,2)`. That path flushes the CPU data cache,
invalidates GX textures and invalidates cached material state. SDK allocation
returns a zero handle on failure; the native bridge does not require a
managed exception to select fallback. A partial initialization releases both
handles, increments the failure counter, and returns 0.

`Prepare(active=0)` immediately invalidates the capture and scope, retires
the capture texture and resets its attempted-dimension epoch. It retains the
16 KiB immutable slope/lookup pair for reuse. While active, a changed display
size retires the old capture before attempting a correctly sized one. A
capture allocation is attempted only once for a given active dimension pair;
a failure leaves bump-only water instead of allocating again every frame.
Leaving the active state or changing dimensions allows a later retry. An
unsupported mode is also remembered for that epoch but is not counted as an
allocation failure.

`Begin` clears per-pass capture validity and last-draw feature/stage fields;
it does not allocate or release resources. `Capture` copies only once per
pass, as described above. Neither draw hook allocates, uploads immutable
texture data, rebuilds geometry or reads image data back to the CPU.

`Shutdown` resets active/scope/capture state, releases capture then lookup then
slopes, and invalidates cached material state. It uses `dnd_draw_release`,
whose normal between-frame path waits for prior GPU work before freeing
storage; the SDK also has deferred release for active resources. The managed
owner calls it between frames and makes repeated disposal harmless. Capture
retirement on indoor/save/reset transitions lets the terrain and save systems
reuse its larger allocation while the two small immutable maps survive.

There are three distinct runtime outcomes:

| Outcome | Cause | Rendered result |
| --- | --- | --- |
| Full optics | Initialized maps, supported/allocated capture, fresh pass | Bump lighting, sheen and guarded internal underwater composition. |
| Bump only | Maps available but capture unsupported, failed, absent or stale | Bump lighting and sheen with the original transparent framebuffer blend. |
| Original water | Initialization unavailable, inactive effect or incompatible draw contract | Original shallow water material and normal alpha/depth behaviour. |

### Diagnostic selectors

`NativeWater.Stats(kind)` is observational. Counters are process-lifetime
totals; initialization/shutdown do not reset them. Draw-specific fields reset
at `Begin` and then describe the most recent scoped water draw.

| Kind | Meaning |
| ---: | --- |
| 0 | Accounted slope + lookup + currently allocated capture pixel bytes. |
| 1 | 1 only when the capture is valid for the current pass sequence. |
| 2 | Completed capture-copy count. |
| 3 | Enhanced bump draw count. |
| 4 | Enhanced draw count that used refraction. |
| 5 | Draw fallback count, including bump-only draws without refraction. |
| 6 | Final TEV stage count for the most recent scoped surface draw. |
| 7 | Final indirect-stage count for that draw. |
| 8 / 9 | Allocated capture width / height, otherwise zero. |
| 10 | 1 when both immutable native map handles exist. |
| 11 | Current native draw scope, 0–2. |
| 12 / 13 | Allocated capture's full logical source width / height. |
| 14 / 15 | Current pass sequence / captured pass sequence. |
| 16 | Last-draw feature flags: value 1 bump path, value 2 nonzero sheen strength, value 4 refraction. This differs from `Prepare`'s capability mask. |
| 17 | Stage count after the original water program and footer, before optics. |
| 18 | Native initialization or capture-allocation failure count. |
| 19 | Prepared sunlight strength, 0–255. |
| 20 | Prepared environment RGB provenance. |

Unknown selectors return zero. Native
`animaltown_water_setting(index)` exposes the copied/clamped float setting to
the probe, returning zero outside 0–11. It is not an additional managed API.
Startup reports the prepared feature bits and native bytes, or logs
`ANIMALTOWN WATER OPTICS unavailable; keeping transparent water` if map
initialization fails. A zero return for deliberate inactivity must not be
reported as allocation failure.

## 6. Assets and authoring

Preserve the five existing optional inputs and add one independently optional
direction-field input in
`assets/textures/environment/water/`:

| Input | Dimensions | Native format | Meaning |
| --- | --- | --- | --- |
| `riverbed-color.png` | 128×128 | RGB565 | Stationary bed albedo. |
| `surface-ripples.png` | 64×64 | I8 | Surface colour mask, fully opaque grayscale source. |
| `bed-caustics.png` | 64×64 | I8 | Independently moving underwater illumination mask. |
| `surface-distortion.png` | 64×64 | RGB565 | UV displacement, blue horizontal/green vertical; red 128, alpha 255. |
| `shoreline-foam.png` | 64×64 | IA8 | Grayscale colour and independent alpha coverage. |
| `surface-slopes.png` | 64×64 | IA8 | New signed derivative field; opaque PNG with R=128, B=tangent-X, G=tangent-Z. |

The source names are case-sensitive. The current cooker rejects unknown PNG
names and unsuitable dimensions/channels. Every role has one mip level and
repeat sampling. Each optional map independently replaces its procedural
fallback. The original PNG is never decoded in the frame loop or shipped as a
runtime substitute for the cooked package.

### New optional `surface-slopes.png`

Use an ordinary opaque 8-bit RGB/RGBA PNG. Every source alpha must be 255 and
every source red must be 128. Blue stores the tangent-X derivative and green
stores the tangent-Z derivative, both centred on 128. Smooth variation above
and below 128 tilts the lighting response in opposite directions. Use a
seamless, low-frequency field, avoiding isolated pixel noise. A flat test map
is RGB `(128,128,128)` throughout.

This source is deliberately opaque so image tools cannot premultiply vector
components by a varying PNG alpha. The cooker maps source blue to native IA8
alpha and source green to native IA8 intensity without quantizing either
eight-bit component. The cooked file is
`water-surface-slopes.dndtex`, exactly **8,224 bytes** including its 32-byte
package header. The header describes format 3 (IA8), 64×64 dimensions, version
2 and one level. The native payload uses the tile order specified above.

All header fields are little-endian unsigned 32-bit values; texels themselves
remain in GX native byte order:

| Byte offset | Value |
| ---: | --- |
| 0 | `0x58544e44` (`DNTX`). |
| 4 | 2, package version. |
| 8 | 3, native IA8 format. |
| 12 / 16 | 64 / 64, width and height. |
| 20 | 8,192, native payload bytes. |
| 24 | 1, mip level count. |
| 28 | 0, reserved. |
| 32 onward | Exactly 8,192 tiled IA8 bytes. |

The source is a derivative map, not an RGB tangent-space normal map. Do not
drop a conventional purple/blue normal texture into this slot. Convert it to
the chosen derivative convention and preserve its tangent orientation first.
The existing `surface-distortion.png` continues to use its independent RGB565
displacement semantics and is not repurposed by this feature.

`WaterTextureCatalog.Count` becomes 6 and `TotalNativeBytes` becomes 64 KiB.
The existing catalog-driven cooker, runtime validation, audit and ISO/Depot
payload paths include the sixth role. `WaterTextureAssets.TryLoadNativeData`
returns the validated native payload for `WaterBumpEffect`; it does not create
an extra managed `Texture2D` copy beside the native-owned slope map. Source
absence and malformed runtime packages retain the procedural fallback.

Current import and validation commands remain:

```sh
dotnet run --project tools/AnimalTown.Tools -c Release -- water-assets .
dotnet run --project tools/AnimalTown.Tools -c Release -- water-check .
```

**No new artwork is required.** `surface-slopes.png` is optional; the lighting
lookup is generated natively and the underwater image is captured at runtime.
No reflection cubemap, painted lighting texture or pre-rendered refraction
image needs to be supplied.

## 7. Implementation sequence

For a clean reimplementation, create a new branch from the pinned baseline,
initialize `vendor/card-store-sdk`, and use the checked-in .NET/package locks.
The implementation files in this PR are the executable counterpart to the
contract above. Apply work in the following order so every intermediate state
retains the established water fallback.

1. **Establish the source and failing acceptance cases.** Start from the exact
   baseline above, inspect the pinned SDK, and capture baseline water and native
   command traces. Add tests for the intended directional-light response,
   refraction composition and capture invalidation before claiming the effect.
2. **Add bounded data and controls.** Implement the finalized direction/lookup
   resources and separate strengths. Define zero-strength behaviour and
   resource-failure fallback. Keep rendering controls session-only or internal;
   do not introduce a new saved setting or UI as part of this work.
3. **Integrate the native material path.** Use explicit game-owned bridge and
   draw scopes; handle both immediate and indexed submissions. Implement and
   verify every stage against the final resource table, including footers.
4. **Integrate the refraction capture boundary.** Hook the actual underwater
   source pass, preserve the ordinary depth buffer and opaque-world draw order,
   reject stale/invalid captures and restore any changed copy/GX state.
5. **Integrate animation, lighting and cleanup.** Match cylindrical coordinates,
   animation freeze, day/night lighting, town changes, indoor transitions and
   teardown. Validate allocation failure and cache restoration.
6. **Extend observed draw validation.** Update the probe and smoke parser to
   assert the new actual hardware contract. Keep the old ocean and fallback
   assertions. Add evidence that would fail if only surface UVs were warped.
7. **Validate and publish the PR.** Run the repository selector, fix relevant
   failures, append a four-line request summary, commit, push and open the new
   PR with accurate validation and remaining hardware limits. Run the release
   pipeline on the same commit used by the evidence.

The implementation changes are confined to these groups:

| Work item | Files |
| --- | --- |
| Direction field and optional art | `shared/AnimalTown.Domain/WaterBumpPattern.cs`, `WaterTextureCatalog.cs`; `game/AnimalTown.GameCube/WaterTextureAssets.cs`; `tools/AnimalTown.Tools/WaterTextureArt.cs` and its checks. |
| Managed ownership/pass ordering | `game/AnimalTown.GameCube/NativeWater.cs`, `WaterBumpEffect.cs`, `TerrainRenderer.cs`, `Program.cs`. |
| Complete submerged-rock capture | `shared/AnimalTown.Domain/WaterBedGeometry.cs`, `game/AnimalTown.GameCube/WaterBedRenderer.cs`, `tests/AnimalTown.Checks/WaterBedChecks.cs`. |
| Native optics and patching | `tools/native-water-optics.inc`, `patch-water-optics.py`, `patch-native.ps1`. |
| Production native arithmetic/capture fixture | `tests/native-water-optics.c`, `tools/check-water-optics.py`. |
| Observed GX diagnostics | `tools/native-water-probe.inc`, `tests/native-water-probe.c`. |
| Managed/rendering checks | `tests/AnimalTown.RenderingChecks/WaterBumpPatternChecks.cs`, `WaterEffectChecks.cs`, `ShallowWaterMaterialChecks.cs`; the domain rock coverage checks listed above. |
| Runtime evidence and routing | `tools/iso-smoke.py`, `tests/water-optics-smoke.py`, `tools/test-changes.ps1`, the water PR workflow and release capture selection. |
| Handoff/release metadata | This file, `docs/water-rendering.md`, `docs/water-art-assets.md`, `version.json`. |

The water/bed/rock vertex format, surface material definition, save formats,
terrain archive format and sea program need no structural change. Rock
tessellation moves its existing single cut to the physical waterline and
reserves wet alpha 1 as described above. The native scopes attach to the
existing draw boundaries.

## 8. Test and acceptance matrix

| Area | Required test/evidence | Failure it must catch |
| --- | --- | --- |
| Direction-field encoding | Native-layout sampling, signed neutral point, periodic seams, bounded values, both independent scrolls. | Incorrect channels, unsigned bias, wrapping seams or one layer accidentally unused. |
| Lighting mathematics | Flat-field reference; opposite slopes; cylinder/view placement; zero/full strengths; night sunlight zero. | Brightness mask masquerading as bump lighting, inverted axes, screen-space drift or night glow. |
| TEV state | Record complete final immediate and indexed GX calls; evaluate important colour/alpha paths against an independent reference. | Stage overflow, register collision, double tint, lost opacity, cached-state leakage. |
| Refraction source | Render a distinctive bed/rock test pattern; compare displaced and zero-displacement samples while surface colour is held fixed. | Only surface texture movement, stale or empty capture, incorrect projection. |
| Occlusion | Bridge deck, actor on bank, submerged rock, exposed cap, upper-centimetre wet rock band, narrow river corner and estuary. | Foreground silhouettes copied into water, omitted submerged rock bands, blue-tinted dry tops, shoreline bleed. |
| Wet rock representation | Physical-waterline intersections, wet boundary alpha 1, dry alpha 0, unchanged caustic fade samples and conservative one-cut vertex counts. | Treating the quiet-caustic margin as dry coverage, lighting above-water caps, or exceeding existing geometry budgets. |
| Freeze/resume | Animated, frozen and resumed captures at the same camera; a separate camera move while frozen. | Time-driven motion continues while frozen, or capture/projection freezes with the camera. |
| Terrain paths | Archived water, legacy water, return to archived water. | Missing native hook in one path, incorrect shadow footer, memory shortage on restoration. |
| Lifetime | Empty water selection, indoor/title/loading transitions, town replacement, session disposal and forced allocation failures. | Copies without visible use, stale pointers, leaks, double-free, stale scene content. |
| Existing water | Opaque coast, rough-sea patch, original caustics, foam and depth tint. | Unintended ocean transparency or global material changes. |
| Asset path, if changed | Optional absence, malformed package, one authored role at a time, stale cooked-map removal, ISO/Depot parity. | New mandatory art, invalid loads, old cached assets or package disagreement. |
| Performance | Matched water-heavy route with enhancement on/off; copy count/bytes, frame intervals, native/MEM1 peaks and cache restoration. | Unbounded GPU work, hidden allocations, misleading emulator-only frame-rate claims. |

Current water inspection cycles river, corner, bridge, coast and estuary with
`L + R + D-pad Left`. Existing terrain-material smoke checks compare moving,
frozen and resumed water in both archive and legacy paths. The probe observes
the actual `GX_Begin` submission and forwards the original call unchanged.
Its `ANIMALTOWN WATER DRAW` line retains the original texture/ripple evidence,
while a matching `ANIMALTOWN WATER OPTICS` line adds the new state. The pair
must have the same scene/frame identifiers. The enabled probe emits at most
once per thirty outdoor frames; setup alone cannot produce a draw record.

The optics line contains:

- `ready`, `scope`, `part`, `captured`, `valid`: initialized maps, active
  scope and per-pass freshness.
- `size`, `source`, `bytes`, `copies`: actual capture dimensions, original
  logical dimensions, accounted pixel bytes and completed copies.
- `draws`, `mask`, `base`, `failed`: bump/refraction/fallback counters, actual
  feature flags, footer stage count and allocation failures.
- `sun`, `environment`: prepared lighting provenance.
- `textures`, `indirect`, `stages`, `generators`: actual GX bindings for
  maps 3–6, indirect stages 1–2, all eight possible appended stages, and
  texcoords 3–6.
- `wave0`, `wave1`, `controls`: actual wave matrices and the copied strengths,
  represented as integers multiplied by 1,000,000 for deterministic logging.

The smoke reader requires the exact stage and generator budgets, preserved
base RGB constants, quantized lighting gains, distinct capture sampler views,
correct full/half dimensions and a fresh pass sequence. Supported-display
acceptance requires the complete feature; a safe fallback is reported as
fallback rather than accepted as a successful optics test. The old opaque
ocean and original fallback contracts remain separate checks.

Because refraction introduces bed/stone colours, the enhanced screenshot
metric compares local RGB edge directions rather than forcing every water
pixel into the original two-colour ripple model. Frozen controls and exposure
changes guard against accepting lighting flashes as motion. This metric
establishes interior pattern motion; it does not by itself isolate the
specular lookup or prove refraction source ownership. The native mathematical
and state fixtures establish those complementary properties.

### Local commands

Use the merged prerequisite's exact `main` commit as the reproducible
comparison base:

```powershell
./tools/test-changes.ps1 -BaseRef 37665a5713e3cbe925b8cfb527fb8a9736c9af92 -PlanOnly
./tools/test-changes.ps1 -BaseRef 37665a5713e3cbe925b8cfb527fb8a9736c9af92 -NativeInProcess
```

Use `-NativeInProcess` only in an environment with the required native host
tools, packages and dependencies installed; otherwise the runner uses the
configured Linux builder Docker image. If integrating a newer `main`, use
the actual PR comparison ancestor. The selector includes working changes.

Native build/patch, SDK, validation-map and workflow changes deliberately select
broad coverage. Do not edit routing merely to avoid a relevant failing gate.
The existing water rendering suite can also be run during focused iteration:

```sh
dotnet run --project tests/AnimalTown.RenderingChecks -c Release -- --suites water
python3 -m py_compile tools/iso-smoke.py tools/patch-water-optics.py
python3 tests/water-optics-smoke.py
```

The canonical selector resolves the pinned packages and populates
`out/sdk-native` when native checks are selected. To prepare that source
explicitly for focused host work, run from the repository root in PowerShell:

```powershell
git submodule update --init --recursive
dotnet tool restore
. ./tools/resolve-packages.ps1
dotnet build game/AnimalTown.GameCube -c Release --no-restore
```

This uses the `global.json` SDK, installed .NET 8 runtime, PowerShell 7,
Python 3 and a C11 host compiler. The complete console toolchain is provided
by the pinned image described in `docs/linux-build.md`; use
`./tools/build-game.ps1` for both ISO and Depot outputs, or its
`-NativeInProcess -RunNativeTests` mode inside that configured Linux builder.
Do not use a managed DLL build as evidence that the PowerPC/native renderer
compiled or that a console image booted.

Against the pinned native SDK source at `out/sdk-native`, run:

```sh
python3 tools/check-water-optics.py out/sdk-native
cc -std=c11 -Wall -Wextra -Werror -Iout/sdk-native/include tests/native-water-probe.c -lm -o out/native-water-probe-check
out/native-water-probe-check
```

`check-water-optics.py` extracts the real pinned SDK copy helper into a
temporary include, compiles `tests/native-water-optics.c` with that helper,
the real base TEV backend, the game shadow footer and the production optics
include, then executes the fixture. It independently evaluates recorded
RGB/alpha operations for seven opacity values, clear/valid guard outcomes
and day/night/legacy programs. It also checks gains and neutral offsets,
projected camera/aspect transforms, wet-rock alpha, allocation failure/retry,
resize/disposal, no-clear copy ordering and split-AA offsets/freshness.
This is executable host evidence of logic and submitted commands, not a
software rasterizer for the entire scene.

`tools/patch-native.ps1` applies `patch-water-optics.py` before the existing
probe patch, then runs both native fixtures on Linux unless `-SkipTests` is
explicitly selected. The optics patch fails closed if any of seven AOT
signatures, either post-footer draw hook, the vertex-state include boundary
or the frame-clear hook no longer matches the pinned source. Non-console
native fixtures receive linkable no-op bridge stubs.

The water PR workflow compiles against the pinned SDK/engine and selects
managed/rendering suites, then runs the smoke-parser fixture. Native changes
also select the wider native/build path through the repository selector.
The release workflow includes the new optics files in its water-capture
selection. Do not bypass those paths when changing a resource assignment.

For a built console candidate, the established visual scenarios are:

```sh
python3 tools/iso-smoke.py out/AnimalTown-VERSION.nkit.iso out/water-review --river-coast
python3 tools/iso-smoke.py out/AnimalTown-VERSION.nkit.iso out/material-review --terrain-materials --season spring --dsp-hle
```

Run them in the configured Dolphin image as specified by the release workflow.
Replace `VERSION` with the exact candidate version. Check the image hash and
source commit before treating captures as evidence for the PR head. A valid
native trace proves command submission; a screenshot comparison proves only
the visual property it actually measures. Neither alone establishes physical
GameCube performance.

### Validation status for this implementation

The final complete local selector passed after the physical rock-coverage
correction, using the pinned SDK, a full repository history and a disposable
PostgreSQL 16 database. The command selected all managed, rendering, tooling,
native and backend gates:

```powershell
./tools/test-changes.ps1 -BaseRef 37665a5713e3cbe925b8cfb527fb8a9736c9af92 -Full -NativeInProcess
```

`ANIMALTOWN_TEST_CONNECTION` supplied the local test database. The normal
runner can provision its disposable database through Docker instead. The
successful run included a fresh game AOT bundle and the complete native patch
fixture sequence, followed by server integration; it ended with
`Selected host checks passed.` The earlier history-resolution failure was
resolved by fetching the full checkout and rerunning the complete selector.

The following results describe this implementation:

| Check | Evidence and limit |
| --- | --- |
| Complete managed gameplay suite | 4,749,955 checks passed, including physical-waterline coverage, quiet-rim clipping, geometry budgets, saves and traversal. |
| Complete rendering suite | 392,597 checks passed across all eight rendering groups, including derivative representation, water materials and alpha-one wet coverage. The initial focused water run passed 45,189 checks before the final boundary cases were added. |
| Water-art tooling check | 36,927 checks passed, including the sixth role's lossless packing and validation. |
| Game C# build | Compiled successfully against the pinned SDK/engine; this is not a console-native build. |
| Fresh AOT bundle and native patch anchors | The pinned 376.1 compiler generated fresh C sources and all seven bridge signatures plus native hooks patched successfully. |
| `tools/check-water-optics.py` against pinned SDK 376.1 | Production TEV/footer arithmetic, capture commands and lifecycle fixture passed on the host. |
| `tests/native-water-probe.c` | GX forwarding, actual texture/matrix/stage metadata, pass age, fallback reporting and disabled cadence fixture passed on the host. |
| `tests/water-optics-smoke.py` | Exact parser positive/negative cases and synthetic image-motion/frozen/exposure controls passed. Its images are synthetic, not Dolphin screenshots. |
| `tests/frame-profile-streaming.py` | Existing streaming-report fixture passed. |
| `tests/release-bundle.py` | All 20 release-bundle tests passed. |
| Card and record-store suites | 922 card-adapter and 57,856 record-store checks passed. These are host models, not physical memory-card tests. |
| Depot packaging | 201 checks passed, including explicit optional texture payloads and source identity. |
| Complete selected native sequence | Fresh game/content/job AOT plus archive, capture, shadow, card, content, job, texture, UV, voice and Python fixtures passed. |
| Backend integration | Split-storage/migration checks and real local PostgreSQL server scenarios passed: authentication, ownership, retries, friendship/blocking, purchases, visitor permissions, custody, rewards, restoration and concurrent claims. |
| Python syntax and `git diff --check` | Passed for the changes reviewed at that point. |

Console compilation, Dolphin captures and same-commit release packages must
be read from the PR's current-head checks and attached evidence. They are
separate from the complete local host results above. At initial PR publication, CI results
and visual acceptance can still be pending; report their actual state rather
than promoting these local fixtures into a console pass. No physical
GameCube appearance, frame-time or MEM1-pressure test is claimed here.

## 9. Release and completion requirements

Follow repository `AGENTS.md` and `docs/versioning.md`. Append one uniquely
identified `requestSummaries` entry to `version.json` with exactly four lines;
the first is a short player-facing change. Do not manually reinterpret version
numbers from the PR title.

A console release must include both the compact bootable NKit ISO and the
standalone `AnimalTown-VERSION-depot.zip` SD app, with the same source, version
and cooked assets, plus the matching backend and tools packages and release
feed. Put diagnostic captures/manifests/checksums inside the Depot ZIP's
`release/` directory rather than shipping them in the installed app payload.
The existing Build and release workflow owns the same-commit pipeline. Do not
publish an ISO from one head with backend/assets or evidence from another.

The feature is ready for review when the implemented contract is fully
documented, no coordination placeholders remain, the PR diff is isolated from
its prerequisite, all applicable checks pass or are honestly identified as
blocked, and the captures distinguish actual bump lighting/refraction from the
pre-existing surface-UV animation. Original GameCube appearance and timing are
separate acceptance evidence and must be reported explicitly.

## 10. Hardware references

These references explain hardware capabilities; the pinned game/SDK source
above remains authoritative for the integration:

- [Nintendo patent: environment-mapped bump mapping](https://patents.google.com/patent/US7205999B2/en)
  describes the two-component surface perturbation and object-space variants,
  coordinate accumulation, lighting/environment lookup and result reuse.
- [libogc GX API documentation](https://bot.libretro.com/doxygen/a06329.html)
  documents `GX_SetTevIndBumpST` and `GX_SetTevIndBumpXYZ`. Their three-stage and
  one-stage costs cover the bumped lookup, not the complete water material.
- [Factor 5's GameCube shader integration account](https://www.gamedeveloper.com/programming/shader-integration-merging-shading-technologies-on-the-nintendo-gamecube)
  distinguishes emboss shading from indirect bumped diffuse/specular/reflection
  lookup and explains the limitation of a direction-only lightmap for nearby
  positional lights.
- [TEVSL implementation and documentation](https://github.com/crtc-demos/tevsl)
  provides a source-level account of TEV operations and indirect coordinate
  accumulation across stages.
