# Sunshine water textures

These are original, small, **artist-editable grayscale PNG sources** used by
AnimalTown's GameCube water preview. No retail model or texture payload is
included. The material state and animation timing follow Sunshine's Plaza
water; the artwork and bounded geometry remain AnimalTown's own.

The individual PNGs are **not** ordinary reductions of a high-resolution
texture. Their distinct content is the key visual effect: when a surface
minifies with viewing angle and distance, GX's hardware texture sampler
selects a different authored level, faking a reflection/brightness band.

| Source 64 → native 128: clear | Source 32 → native 64: caustics | Source 16 → native 32: reflection | Source 8 → native 16: fade | Source 4 → native 8: clear |
| --- | --- | --- | --- | --- |
| ![L0 clear](sunshine-wave-mip0-64.png) | ![L1 caustics](sunshine-wave-mip1-32.png) | ![L2 sky glints](sunshine-wave-mip2-16.png) | ![L3 fade](sunshine-wave-mip3-8.png) | ![L4 clear](sunshine-wave-mip4-4.png) |

## Editing

1. Edit *only* the source image for the distance range you want to change.
   Leave L0 and L4 completely black (no glints). L1 should contain thin
   irregular connected caustics, L2 broader and brighter irregular
   glints, L3 very subdued remnants.
2. Keep each image **opaque 8-bit grayscale** (RGB channels equal, alpha 255).
   The cooker expands each source exactly 2× using nearest sampling and rounds
   intensity to a multiple of 17 for GX I4. The hardware supplies this decoded
   intensity as texture RGB and alpha; there is no independently amplified
   alpha curve. Do not premultiply or store transparency in the source PNG.
3. Do **not** use an image editor's automatic mipmap generation, nor resize
   the 64px blank image to fill the chain.
4. Run `dotnet run --project tools/AnimalTown.Tools -c Release -- sunshine-water .`
   to generate the three packages below in `out/disc`. The normal ISO build
   runs this cook automatically and checks their native texture headers.

| Package | Native dimensions | Mip levels | GX bytes | Art source |
| --- | --- | --- | --- | --- |
| `sunshine-water.dndtex` | 128×128 | 128/64/32/16/8 | 10,912 | Five independent PNGs above |
| `sunshine-wave.dndtex` | 128×256 | 128×256 through 8×16 | 21,824 | L1 caustic PNG, resized separately |
| `sunshine-distortion.dndtex` | 64×64 | 64/32/16/8 | 2,720 | L1 caustic PNG, resized separately |

The near-wave and distortion textures have ordinary filtered mip chains. They
need nonclear base levels and must not alias the main surface chain. All three
packages use I4, with two intensities per byte in native 8×8 tile order; total
GPU payload is 35,456 bytes.

## GPU path

- Both main surface samplers bind the same 128×128 I4 texture. Independent UV
  matrices use scales (5,4) and (6,4). Their 40-second animation uses the source
  BTK Hermite curve `f(u) = -u³ + 1.5u² + 0.5u`, where `u` is elapsed cycle
  fraction. Texture 0 translates S/T by `f`; texture 1 translates only T.
- Texture minification: GX trilinear (LinearMipmapLinear), min LOD 0,
  max LOD 4, with source sampler biases 0 and −0.5.
- Main TEV RGB first loads C0=(252,255,255), then multiplies it by
  C1=(232,228,252) at 2× without clamping. Native eight-bit output wrapping is
  intentional. Alpha first multiplies sample 0 by raster alpha, then sample 1
  by that result at 2× without final clamping. GX rounding, intermediate widths
  and output wrapping apply; this is not an average of the samples.
- Main comparison is `GEQUAL 235 XNOR GEQUAL 35`, retaining final alpha 0–34
  or 235–255. Blend is SrcAlpha/One. The near-wave material instead clamps its
  two alpha stages, uses `GEQUAL 85 OR LEQUAL 35`, and blends SrcAlpha/SrcColor.
  Its vertex alpha fades linearly by maximum axis distance around the player.
- Screen-space refraction uses a separate water-only projective pass after a
  half-resolution RGB565 scene capture. The 64px displacement texture scrolls
  three repeats per 40 seconds. This is refraction, without a mirrored camera.
- The sandy floor is a separate reused/offset mesh pass underneath all
  transparent water. It uses the existing cliff-soil texture.

See [technical implementation](../../../../docs/sunshine-water-comparison.md)
and [Sunshine's original visual technique breakdown](https://blog.mecheye.net/2018/03/deconstructing-the-water-effect-in-super-mario-sunshine/).

Original game timing is 30fps. The public breakdown's demo deliberately runs its
BTK animation five times faster; that accelerated timing is not used here.
