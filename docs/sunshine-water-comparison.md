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

## Rendering intent

- **Wind Waker:** identical current opaque/tile-textured surface + optional
  local wave mesh. No underwater floor pass.
- **Sunshine:** transparent, low-depth-write, two independently scrolling
  samples of a small shared wave texture. Individual hand-authored mip levels
  change highlight brightness and opacity with LOD, rather than being normal
  downsampled copies. Foam remains an independent bank overlay.
- **Underwater floor:** draw the **same immutable water meshes again**
  with an inward cylindrical transform, *before* the translucent surface.
  Share the game's existing cliff-soil/sand texture and apply a pale sandy tint.
  River floor is shallower than open sea. No additional water geometry, archive
  entries, per-frame texture uploads or modifications to saved water tiles.
- **Ordering:** floor before other opaque terrain; transparent water and
  existing shoreline foam after other opaque terrain, with depth testing
  retained and water depth writes disabled.
- **Resource policy:** allocate the Sunshine texture/effects only when
  the option is first selected. Keep the assets through the session for instant
  A/B switching. Original mode has no extra draws. Compare with the existing
  frame-performance debug overlay. The style is an original approximation,
  not copied game textures or an exact Sunshine alpha-test implementation.

## Implementation

The selector uses the existing 25-row paginated Debug / testing menu,
with its original parchment-colored highlight and controls. It does not
introduce any HUD or permanent settings.

The Sunshine surface has a **64×64 GX IA8, five-level DNTX texture chain**
using exactly **10,912 native texture bytes** (64², 32², 16², 8², 4²).
Intensity and alpha intentionally change across levels: the level-2 mip
has a stronger bright reflection band, while level 0 is clear and subtle.
No transparent render targets, scene captures, shaders, indirect GX effects,
or copies of Nintendo textures are involved. The two moving stages bind
the same resident image to both GX texture slots. A four-stage DNEF v2
TEV program retains the primary texture's alpha; Sunshine surface alpha
blends with **depth test on** and **depth writes off**.

The seabed reuses `TerrainSoil.Material.Texture` (the existing
`dvd:/cliff-soil.dndtex` texture) with a dedicated sandy TEV constant.
`SunshineWater.BedScale` scales the already archived water vertices
around the exact cylinder axis: inland 40 cm inward, offshore 75 cm.
Two shared `Transform` instances and draw-transform arrays replace all
separate seafloor mesh allocations. No save, collision, terrain height,
UV, archive geometry or texture-cache entry is modified by switching.

The depth-correct order is:

1. Opaque river and ocean beds at their inward radial positions
2. Existing opaque soil, cliffs and ground
3. Translucent Sunshine surface and existing translucent foam

Wind Waker continues its original ordering and optional storm-wave patch.
The local storm overlay is suppressed in Sunshine mode to avoid alpha
accumulation across two overlapping water surfaces.

Only the first switch to Sunshine generates the mipmap and creates two
TEV programs. Subsequent A/B switches reuse the same GPU resources,
reassigning the original water materials with **zero terrain rebuilds**.
Ground material mipmap toggles remain independent of Sunshine mipmaps.

### Verification

- `dotnet run --project tests/AnimalTown.Checks -c Release -- --suites terrain`
  validates mip packing, explicit LOD contrast, transparency, same-batch
  cylinder depth, SaveCodec independence and the two state transitions.
- `dotnet run --project tests/AnimalTown.RenderingChecks -c Release`
  verifies the existing immutable geometry and draw-path contracts.
- The existing `Water renderer PR validation` workflow compiles
  `game/AnimalTown.GameCube` against the locked DolphinDotNet SDK.
- On a complete, bootable local ISO, capture reproducible A/B screenshots:

```sh
python3 tools/iso-smoke.py <AnimalTown.iso> out/sunshine-ab --water-ab
```

The script walks to a water view, captures original/sunshine/restored
frames and the debug selector, records pixel differences in
`water-ab-comparison.json`, and checks that Sunshine continues animating.
It does not alter the saved game or modify a pixel of either source
texture during rendering.

**Differences from retail Sunshine:** this is a compact visual
approximation of its mipmapped transparency and two scrolling passes.
The SDK's current managed material does not expose Sunshine's
two-sided alpha-comparison threshold; the effect uses standard GX
alpha blending with authored per-mip alpha instead. Its bed is generated
by re-projecting one existing mesh, not a detailed independent bathymetry.
No physical GameCube frame-time or Dolphin screenshot comparison has
yet been run for this PR. Verify depth ordering, nearshore visibility,
shader output and draw-submit overhead before production rollout.

## Rebuilt Sunshine material and authored water texture

The original A/B preview generated five slight variations of one procedural
straight-line pattern and blended them using a TEV that was tuned for a soft
color overlay. That could only look like faint diagonal marks on the water.

The improved preview is now built from **five separately editable PNGs** in
`assets/textures/environment/water/sunshine-wave-mipN-SIZE.png`:

| GX level | Native size | Original-authored purpose |
| --- | --- | --- |
| L0 | 64×64 | Completely clear; no wave/shimmer pixels in the near field |
| L1 | 32×32 | Irregular caustic cells, thin connecting glints at moderate distance |
| L2 | 16×16 | Brightest, wider caustic reflections; apparent distant sky/glare band |
| L3 | 8×8 | Near-black tail with soft, distant fading |
| L4 | 4×4 | Completely clear; no bright horizon checkerboard |

These are independent grayscale **source** PNGs, not copies of Nintendo's
assets. Editing any single mip changes *only that mip*. The existing SkiaSharp
asset-cooking pipeline reads and validates every PNG on building an ISO and
packs each image into a **10,912-byte GX IA8 native texture chain**. No image
resampling, box filtering or runtime sine-wave art is permitted in the cook.
The derived alpha channel maps authored brightness to a specular-coverage
threshold while preserving each RGB intensity.

`SunshineWaterAssets.Build` writes `out/disc/sunshine-water.dndtex`.
The disc validator requires this file. The texture audit verifies both the
5-level native header and per-pixel authored alpha, and CI actually performs
the PNG-to-GX cook. Runtime reads the prepared package only on the first
selection of Sunshine mode.

The game now explicitly requests GX trilinear filtering for both water
texture bindings, with native LOD range 0–4 and a +0.65 LOD bias to position
the bright L2 band within the curved AnimalTown horizon. The primary and
secondary texture coordinates have equal scale; the first scrolls in two
axes and the second scrolls predominantly in one, producing changing moiré
caustics. The TEV **adds** both glint contributions rather than blending
them into the old constant color. Material alpha comparison requires
`alpha >= 0.68` before writing a specular pixel; water depth writes remain
off. A thin blue transparent body shows above the opaque sandy seabed.
Unlike the former static preview, the open ocean additionally uses a
bounded two-sine near-field wave patch that fades to the fixed sea.

The existing Wind Waker renderer is unchanged by the A/B selector.

**Technical limitation:** DolphinDotNet's currently pinned DNEF v2 alpha
program always computes opacity as vertex alpha × TEX0 alpha. Therefore
the first sample controls the glint silhouette; the second contributes to
the moving *RGB* highlights. Sunshine's original double-sided 0.13/0.92
alpha test and the full two-sample alpha formula would require an upstream
SDK effect/alpha-comparison extension. Likewise, original EFB refraction
and screen-space environment reflections are not reproduced here.
This patch achieves the artistic LOD structure, deliberate GX mip sampling,
two-texture moiré animation, high-threshold highlights, local wave geometry
and separate seabed within the published SDK. It does not claim pixel
identity with the retail game.
