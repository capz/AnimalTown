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
using the existing water meshes, sandy seabed, independent scrolling caustics and
bounded local ocean waves. It does not change saved terrain or collision.

The game pins DolphinDotNet SDK/Core/GameCube/Assets **0.1.0-preview.318.1**.
Its public `TevMaterialPresets.DualTextureGlints` replaces the game-owned RGB-only
program. The three-stage DNEF v4 effect adds both scrolling RGB contributions,
combines their alphas, and owns the exact GX comparison:

```text
RGB = saturate(raster + PrimaryLight * TEX0 + ReflectedLight * TEX1)
A   ≈ raster.a * (TEX0.a + TEX1.a) / 2    (GX fixed-point rounding)
pass if A <= 33 OR A >= 235               (approximately 0.13 / 0.92)
```

There is no legacy TEX0 opacity epilogue. Both the main surface and close-wave
material use opaque raster alpha; reducing it to 206 would prevent the >=235
branch from ever passing. The effect overrides the single material cutoff.
Ordinary following draws restore their own comparison through the SDK.
Materials retain alpha blending, backface culling, depth testing and disabled
depth writes. Clear near/far mips have zero output alpha even though the low
comparison passes. This is an original averaged-alpha approximation, not a
claim of the retail game's exact TEV equation. EFB refraction and screen-space
reflections are not included.

## Original authored mip chain

Five independently editable grayscale PNGs live in
`assets/textures/environment/water/sunshine-wave-mipN-SIZE.png`:

| GX level | Size | Purpose |
| --- | --- | --- |
| L0 | 64×64 | Clear near field |
| L1 | 32×32 | Irregular scrolling caustic cells |
| L2 | 16×16 | Brightest reflection band |
| L3 | 8×8 | Subdued fading tail |
| L4 | 4×4 | Clear far field |

The existing SkiaSharp cooker preserves each original PNG's luminance, derives
coverage alpha, and packs one **10,912-byte GX IA8 DNTX v2 chain**. It performs no
automatic downsampling or runtime image synthesis. L1's alpha gain is 1.55 so its
near-wave glints survive the stronger >=235 threshold; L2 retains its authored
bright reflection band. The validator requires sufficient high-branch coverage
in both levels and clear endpoints. The disc and texture audits require the
cooked `out/disc/sunshine-water.dndtex` asset.

Both texture slots borrow the same resident image. Main-surface samplers use
GX trilinear minification, levels 0–4 and +0.65 LOD bias. Equal UV scales and
independent one-axis/two-axis scrolls produce moving interference. Close waves
borrow the same texture but force both samplers to L1, with positive bias to
keep them minifying near the camera. No second image is allocated.

Only the first selection loads the cooked image and creates effects. Warm style
switches reuse resources and do not rebuild terrain. Animation changes UV
matrices; it does not upload textures. A separate bounded two-sine near-field
mesh updates only over visible open sea, independently of archived terrain.

## Seabed and draw order

The seabed uses the existing cliff-soil texture with a sandy constant and the
same immutable river/ocean geometry, transformed inward around the cylinder
axis: 40 cm for rivers and 75 cm for sea. This adds no mesh/archive records.

1. Draw opaque river/ocean beds.
2. Draw the remaining opaque terrain.
3. Draw the thin blue water body, cutout glints, shoreline foam and local waves.

Ground mip controls remain independent of the water's authored levels. The
existing Wind Waker warp material and its optional storm-wave path are retained.

## Package compatibility

Hypercube Core/GameCube remains at **preview.28.1**, the latest published engine.
Its GameCube package was compiled against SDK 315 and declares an exact 315
runtime dependency. The game and rendering checks explicitly pin runtime 318;
NuGet's direct reference wins. Only that intentional NU1608 constraint warning
is suppressed on the direct runtime reference. `resolve-packages.ps1` accepts
this specific verified 28/315→318 combination and rejects other mismatches.
The compiler, runtime, offline Assets tool and Imaging payload all use 318;
locked restore records the actual resolved graph and package hashes.

## Validation

`tools/test-changes.ps1` selects SDK-upgrade coverage, including gameplay,
rendering, importers, native helpers and backend integration. The rendering
checks consume the actual SDK-generated water package and cover all 65,536
alpha pairs, both cutout branches, full raster opacity, shared mip selection
and the existing engine drawing surface. Asset cooking checks the real source
PNGs against the new high-alpha coverage threshold.

For a full bootable ISO, the existing focused Dolphin scenario is:

```sh
python3 tools/iso-smoke.py <AnimalTown.iso> out/sunshine-ab --water-ab
```

It captures original/Sunshine/restored views and selector states, checks visible
animation and writes `water-ab-comparison.json`. A successful host test does not
establish console image quality or frame time. Dolphin capture and physical
GameCube validation of this material remain outstanding until run against the
updated build. Release builds publish a same-commit full ISO, NKit ISO and backend
through the existing release workflow.
