# Sunshine / Wind Waker water comparison (debug preview)

## Debug UI mockup — reuse existing settings panel

No new screen, asset or menu layout. Add one final option to the existing
**START → Settings → Debug / testing**, after **Ground mip distance**:

```text
┌───────────────────────────────────────────────┐
│  Debug / testing                    page 3 / 3 │
│                                               │
│    Season preview: Auto                       │
│    Villager attention bubbles: Off            │
│    Rim intensity: 100%                        │
│    SDK performance overlay: Off               │
│    Ground mipmaps: On                         │
│    Ground mip distance: Auto                  │
│  > Water rendering: Wind Waker                │
│                                               │
│  A / Left / Right: switch  ·  Z: Wind Waker    │
│  Session options 19-25 / 25                   │
└───────────────────────────────────────────────┘
```

A switches to `Water rendering: Sunshine`; pressing again switches back.
Left/right are captured on that row (L/R continue switching menu tabs).
Z restores Wind Waker. The option is **session-only**: saved-town,
calendar, collision, geometry revisions and season metadata are untouched.

## Water rendering

Wind Waker remains the default. Sunshine is an opt-in, session-only comparison
using the existing water meshes, sandy seabed, thresholded surface glints,
separate nearby waves and screen-space refraction. Saved terrain and collision
are unchanged. The sea and near-wave programs reproduce the recorded GX state
from Sunshine's Plaza sea material and `TMapObjWave`; the art, world scale,
thin blue body and bounded wave geometry remain AnimalTown's own. This does
not claim identical retail images, lighting or camera behavior.

## Exact TEV arithmetic

The main surface follows `sea.bmd` material `_umi`, with two stages and initial
TEV colors C0=(252,255,255) and C1=(232,228,252). The source material's vertex
alpha is 255; its RGB path does not use vertex color or texture RGB.

For the products below, define the native integer interpolation helper:

```text
P(scale, a, b) = (scale * b * (a + (a >> 7)) + 128) >> 8
```

Inputs `a` and `b` are eight-bit values; scale is 1 or 2. This includes GX's
255→256 interpolation endpoint expansion, scaling before truncation and rounding.
It describes these ADD/zero-bias stages, rather than a general floating-point
shader equation.

| Stage | RGB | Alpha | Clamp |
| --- | --- | --- | --- |
| Sea0 | C0 | `A0 = P(1, rasterAlpha, TEX0.a)` | On |
| Sea1 | `P(2, C0, C1)` per channel | `A1 = P(2, A0, TEX1.a)` | Off |

The final color and alpha use their low eight bits. Sea RGB therefore becomes
**(203,200,248)** after wrapping; it must not saturate to white. With opaque
raster alpha, `A0=TEX0.a`. In particular, samples (255,128) produce final alpha 0,
while (255,255) produce 254. These overflow cases distinguish the source program
from averaged or saturated alpha.

The main comparison is **GEQUAL 235 XNOR GEQUAL 35**, accepting final alpha 0–34
or 235–255. Blend factors are **SrcAlpha / One**. Clear endpoint mips still
contribute no color because their output alpha is 0.

The near-wave overlay is a separate program from
`TMapObjWave::initDraw`: stage 0 emits RGB 0 and the same clamped `A0`; stage 1
emits `clamp(2*rasterRGB)` and `clamp(P(2,A0,TEX1.a))`. It uses
**GEQUAL 85 OR LEQUAL 35** and **SrcAlpha / SrcColor** blending. The source
raster color (200,200,255) becomes white after RGB clamping. Its per-vertex
alpha is `floor(255 * (1 − max(abs(x−8),abs(z−8))/8))` on the bounded 16-cell
patch, with 0 at/outside its border and 255 at its center.

Both surface programs use depth test LEQUAL and no depth writes. Their material
declarations retain the source's CullNone, while AnimalTown's existing native
policy still culls backfaces on all 3D polygons, as requested for the game. The
outdoor camera sees the water's front faces. The effects own their two-reference
comparison and blend factors; subsequent ordinary materials restore their own
state. There is no extra TEX0 opacity stage. The game's shadow footer reads
alpha through the eight-bit A input, preserving the sea's final wrap before
comparison instead of clamping its signed-wide intermediate.

## Original art and native textures

The five editable opaque grayscale PNGs in
`assets/textures/environment/water/` remain the original AnimalTown art. Each
surface source expands exactly 2× with nearest sampling, then rounds intensity
to a multiple of 17. GX I4 supplies that decoded intensity as both RGB and alpha;
there is no separate alpha gain curve.

| Package | Source | Native levels | Payload |
| --- | --- | --- | --- |
| `sunshine-water.dndtex` | Independent 64/32/16/8/4 PNGs | 128/64/32/16/8 | 10,912 bytes |
| `sunshine-wave.dndtex` | L1 PNG, resized separately | 128×256 through 8×16, five levels | 21,824 bytes |
| `sunshine-distortion.dndtex` | L1 PNG, resized separately | 64/32/16/8 | 2,720 bytes |

All packages use DNTX v2 and native I4 tiles (8×8, high nibble first). The main
surface's clear/caustic/bright/fade/clear levels are independently authored;
they are never generated from its blank base level. Near waves and distortion
use ordinary filtered mip chains instead. Validation checks dimensions, payload
sizes, clear surface endpoints and source contrast. The three required disc
assets total **35,456 native image bytes**.

The main and near-wave programs each bind their own resident image to both
samplers. Main samplers use trilinear minification, linear magnification,
repeat wrapping, LOD 0–4 and biases 0/−0.5. Near waves use LOD 0–4 and bias 0 on both
samplers. Distortion uses LOD 0–3, repeat and bias 0; the captured scene uses
clamp and linear filtering without mipmaps.

At 30fps, the source 1200-frame BTK repeats every 40 seconds. For
`u = (seconds mod 40)/40`, the main translation is
`f(u) = −u³ + 1.5u² + 0.5u`, from its recorded Hermite tangents. With source
UVs (s,t), zero rotation and center (0.5,0.5), the implemented transforms are:

```text
Sea0:       (5s + f,       4t + f + 0.5)
Sea1:       (6s − 0.5,     4t + f + 0.5)
Distortion: (5s,           4t + 3u + 0.5)
```

These omit only integer translations, which are identical under repeat
wrapping. Basic mapping does not invert T. Near waves advance
`(0.045*seconds) mod 1`, the source 0.0015 repeats per 30fps frame. Their current
AnimalTown UV scales are (0.48,0.48) and (0.48,0.6), scrolling S and T respectively.
The public demonstration's 5× BTK playback is deliberately not used.

Only the first selection loads these packages and creates effects. Warm style
switches reuse them; UV animation does not regenerate or upload textures.
The small two-sine near-wave mesh updates only over visible open sea, separately
from immutable terrain. Ground mip controls remain independent of water mips;
the existing Wind Waker warp and optional storm-wave paths remain available.

## EFB refraction, seabed and draw order

The separate `_ind` technique captures the main scene once, then projectively
samples it on visible water. It does not render a mirrored camera. Capture is
half the viewport width and height in RGB565: **153,600 bytes at 640×480**,
bringing texture/capture image payload to **189,056 bytes** at that resolution,
excluding object metadata and transient loading buffers. Resolution changes
recreate the capture; no per-frame capture allocation is needed.

The recorded indirect setup has one 8-bit stage, STU bias, coordinate scales 1:1,
no indirect wrapping, no addPrevious and no originalLOD. Its matrix is
`[0,0,0; 0,0.001,0.015]`, exponent 2. The direct stage outputs the displaced
capture without blending, with LEQUAL depth test and no depth writes. The game
swaps capture/distortion map and coordinate slots together to fit the SDK's
screen-projection API while preserving those lookup roles. Terrain in front of
water fails the depth test; the pass does not classify or redraw land.

The seabed reuses the cliff-soil texture and existing river/ocean meshes,
transformed inward around the cylinder axis by 40cm/75cm. No duplicate terrain
mesh or ARAM archive records are needed. Outdoor Sunshine draw order is:

1. Opaque water beds, then land and cliffs.
2. Thin blue body, main sea glints, nearby waves and shoreline foam.
3. Remaining world objects and actors.
4. End scene submission, capture once, resume the same camera and depth buffer,
   then draw refraction on visible river/ocean geometry.
5. Attention indicators, rain and HUD, outside the capture.

## Dependencies and validation

The repository still pins DolphinDotNet **preview.318.1** and Hypercube
**preview.28.1**. The new source requires unpublished SDK additions in
[DolphinDotNet PR 172](https://github.com/capz/DolphinDotNet/pull/172): complete
stage RGB state, blend factors, the signed indirect matrix and projected
`SceneCapture`. A normal build against published 318 cannot compile these new
calls. Publication, then an actual package/version-lock update, is required;
no future package number is assumed here. [Hypercube PR 18](https://github.com/capz/hypercube-engine/pull/18)
has merged; engine package availability must be checked when updating the pins.

With the private SDK source, managed compilation and AOT generation passed,
as did **196,629 water rendering checks**. Those checks inspect generated effect
packages and independently evaluate all 65,536 alpha pairs, wrapping versus
clamping, compare boundaries, color overflow, LOD state and refraction matrix.
The domain terrain suite passed **2,594,599 checks**, and all three texture cooks
passed using the checked-in PNGs. These host results establish program and
asset behavior, not console appearance or frame time. Dolphin and physical
GameCube validation of this implementation have not run.

For a full bootable ISO, the existing focused Dolphin scenario is:

```sh
python3 tools/iso-smoke.py <AnimalTown.iso> out/sunshine-ab --water-ab
```

It captures default/Sunshine/restored views and selector states, checks visible
animation and writes `water-ab-comparison.json`. Run it after resolving the SDK
dependency and building the updated ISO. Release delivery remains a same-commit
full ISO, NKit ISO and backend through the existing release workflow.

Idle A interactions remain silent indoors and outdoors, including unsuccessful
bug-net catches. Conversations, pickups, doors and successful tool interactions
retain their feedback.

## Reference identities

- [Original water breakdown](https://blog.mecheye.net/2018/03/deconstructing-the-water-effect-in-super-mario-sunshine/).
- Noclip source at `cc5abc92b5b087e50e219cfc498a3912e036cf5f`:
  [demo and accelerated timing](https://github.com/magcius/noclip.website/blob/cc5abc92b5b087e50e219cfc498a3912e036cf5f/src/InteractiveExamples/SunshineWater.ts),
  [material/BTK interpretation](https://github.com/magcius/noclip.website/blob/cc5abc92b5b087e50e219cfc498a3912e036cf5f/src/Common/JSYSTEM/J3D/J3DLoader.ts),
  [scene/refraction ordering](https://github.com/magcius/noclip.website/blob/cc5abc92b5b087e50e219cfc498a3912e036cf5f/src/j3d/sms_scenes.ts).
- Numeric `_umi`/`_ind` material and BTK records checked from the public Plaza
  demo archive, SHA256 `cbc2969b1498249e6a69a5363e93a042b06dd528ae041db8cf16f701f462c03a`.
  Only functional numbers informed this implementation; retail art is not shipped.
- Sunshine decompilation at `840109f9cc7c2e5c0d213088ddc38d58f4e68b0d`:
  [near-wave program](https://github.com/doldecomp/sms/blob/840109f9cc7c2e5c0d213088ddc38d58f4e68b0d/src/MoveBG/MapObjWave.cpp),
  [capture format](https://github.com/doldecomp/sms/blob/840109f9cc7c2e5c0d213088ddc38d58f4e68b0d/src/MarioUtil/ScreenUtil.cpp),
  [capture setup](https://github.com/doldecomp/sms/blob/840109f9cc7c2e5c0d213088ddc38d58f4e68b0d/src/System/MarDirectorSetupObjects.cpp),
  [runtime capture binding/projection](https://github.com/doldecomp/sms/blob/840109f9cc7c2e5c0d213088ddc38d58f4e68b0d/src/Map/MapStaticObject.cpp).
- [Dolphin's integer TEV implementation](https://github.com/dolphin-emu/dolphin/blob/master/Source/Core/VideoCommon/UberShaderPixel.cpp)
  independently documents the rounding, input expansion and final byte handling.
