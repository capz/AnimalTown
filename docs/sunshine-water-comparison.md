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

## Follow-up fix: true layered water and silent idle interactions (PR #56)

The original Sunshine prototype bound **one IA8 texture in both texture slots**
and rendered one translucent pass. It could not produce two independent
interference patterns, and its alpha control discarded the second tap's alpha.

The corrected renderer uses, in order:

1. The existing opaque underwater sand/cliff-soil texture, with archived river
   and sea meshes inset 0.4m and 0.75m radially into the curved world.
2. A separate transparent color film over the water meshes, retaining vertex
   colors for water depth/season tint; depth test on, depth write off.
3. A second **independent translucent sparkle pass** over the same geometry:
   * TEX0 uses the 64x64 IA8 authored ripple texture (five mips).
   * TEX1 uses a different 64x64 IA8 authored reflection/glint texture
     (five mips and its own periodic wave directions).
   * Each slot has its own scrolling/rotating UVs and explicit trilinear
     LinearMipmapLinear filtering and LOD bias. Mid-distance LODs are brighter
     than near and distant LODs, so the effect changes naturally with distance.
   * DolphinDotNet DNEF v4 adds both sampled RGB/alpha contributions and uses
     a real GX two-reference alpha test, passing alpha <= 78 OR alpha >= 140.
     This creates clear holes and strong highlights; unlike the earlier pass,
     the middle alpha range is actually discarded by the hardware.
4. The original bank foam continues to draw above the water.

The two authored IA8 images total 21,824 native texture bytes. Sunshine mode
adds one water-surface submission pass compared with the original mode but
no new mesh/archive geometry or per-frame texture uploads. Wind Waker stays the
default debug style, with its existing draw count, storm patch and materials.

The thresholds are deliberately calibrated to the new alpha textures rather
than blindly copying retail Sunshine's higher reference values: if the alpha
textures never reach the cutoff, the sparkle layer is invisible. The two
distinct layers also make the effect visible when their UV motions diverge.

**Interaction fix:** when A is pressed with no usable target, the player now
does nothing, indoors or outside. Unsuccessful bug-net catches are also silent.
Valid conversations, pickups, door operations and successful tool interactions
retain their previous messages.

CI checks validate authored mip packing, independent texture contents, the
DNEF v4 TEV stage selectors, the two GX alpha-test thresholds and regression
guards on the interaction handler. A visual comparison in Dolphin/real GameCube
is still required to assess glint brightness and performance.
