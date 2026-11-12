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
- **Underwater floor:** draw separate, merged **solid dark-blue** bed meshes
  before the translucent surface. The unlit opaque material is RGB (18, 48, 82),
  with no texture or custom TEV effect. River floor is shallower than open sea.
  Bed geometry is baked once; saved terrain and surface-water geometry are unchanged.
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

The seabed uses `WaterMaterial.Bed`, an ordinary untextured material with opaque
blending, depth testing/writing and unlit shading. White vertex colors and zero
UVs keep the tint independent of surface-water gradients, seasonal floor
materials and the cliff-soil texture. Sunshine selection no longer loads the
cliff-soil texture on behalf of the bed or allocates a bed TEV program.

`WaterSurface.BedRunEnd` finds the longest adjacent run in each curved row that
shares an X-flat near/far height profile and the same river/sea depth. Dry banks,
height changes and X-sloped cells break the run; bridges remain covered. Material
variant boundaries do not split it. Rows retain their exact cylinder chords:
merging across Z would flatten the curved bed or create T-junction gaps. A flat
8x4 m patch therefore takes four row quads (eight triangles) instead of 32 cell
quads (64 triangles). A 12-seed sweep covers 122,639 water/bridge cells using
19,453 quads, about 84% fewer than one quad per cell.

`SunshineWater.BedScale` and `BedAxisOffset` bake the inward radial positions into
the bed vertices: inland 40 cm inward, offshore 75 cm. All beds share one mesh per
section and the normal terrain scroll transform, with no per-frame bed geometry
updates or extra bed transforms. Archive kind 108 stores this compact payload;
the native coalescer combines the two scratch halves within each 8x8 m page.
Wind Waker skips binding the bed entries. Switching styles rebinds existing
archive views without rebuilding terrain, loading textures or changing the fixed
256-view / 1 MiB MEM1 cache budgets. Visible triangle counters include bed draws
only when Sunshine is selected.

The depth-correct order is:

1. Opaque river and ocean beds at their inward radial positions
2. Existing opaque soil, cliffs and ground
3. Translucent Sunshine surface and existing translucent foam

Wind Waker continues its original ordering and optional storm-wave patch.
The local storm overlay is suppressed in Sunshine mode to avoid alpha
accumulation across two overlapping water surfaces.

Only the first switch to Sunshine generates the mipmap and creates the wave
TEV program. Subsequent A/B switches reuse the same GPU resources,
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
