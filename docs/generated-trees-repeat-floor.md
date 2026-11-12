# Generated trees and repeating ground

The user's latest instruction authorizes temporary generated low-poly trees,
superseding the earlier supplied-tree-only restriction.

## Trees

The game generates three meshes: deciduous (84 triangles), palm
(68 triangles), and pine (60 triangles). Four uniform-color parts provide a
trunk and flat canopy shading. Geometry is shared across placements and uses
the SDK's unlit GX path. Up to 32 visible trees are submitted in
material groups through DrawMeshes; this is shared geometry and batched CPU
submission, not hardware instancing. Saplings reuse the same meshes at smaller
scale. Trees retain their planting/collision data, world parent and radial pose.
Deciduous colors follow seasons; palms and pines stay green, with winter snow
color on suitable upper faces.

All three species, including trunks and saplings, multiply their existing
seasonal vertex colors by one generated 32×32 I8 noise texture in the same
material pass. Fixed-seed soft mottling and fine grain use tree-local planar
UVs, repeat wrapping and a complete box-filtered trilinear mip chain to reduce
distant shimmer. The texture is shared across species and seasons: 1,440 native
pixel bytes (1,472 including its temporary package header). It is generated once
per tree renderer, updated only when its debug strength changes, and disposed
with the renderer. [Debug strength and scale controls](tree-noise-debug.md)
include persistent Save/Restore actions.
The textured vertices add UV bandwidth and one texture sample; triangle counts
and submission counts stay unchanged. No overlay pass or per-tree texture is used.

Original tree GLBs remain in source for reference. Their converted DNMDs are
retired from disc staging and rejected by ISO verification. They are no longer
loaded or drawn. The eight imported console models are the six villagers,
building and packaged guest tent.

## Floor artwork and authoring

The supplied floor sheets are under `assets/textures/environment/transitions`:
four materials, each with four 768-square source sheets. The repeating renderer
uses their center cells for stone, sand, water and terracotta. Seasonal grass
uses explicit 64-square corner crops. Summer currently reuses spring artwork.
These crops are identified in `assets/terrain/terrain.json`; no new raster art
is generated. Mirrored hardware repeat joins crops continuously without storing
duplicated tiles. Plain-color wood/indoor-floor fallbacks remain where no source
art is specified.

To use a dedicated tile pack, add `repeatSources` to a base or seasonal style:

```json
"grass": {
  "color": "#85B86B",
  "sources": [],
  "repeatSources": [
    { "file": "assets/textures/my-grass.png" },
    { "file": "assets/textures/my-grass-variants.png", "rect": [0, 0, 256, 256] }
  ]
}
```

One to four opaque variants are supported. Variant choice is stable in 8-metre
blocks, allowing long strips to merge. Existing patch rectangles still select
surface styles; legacy nine-slice declarations provide their center image to
the repeating renderer. The saved world is 8×8 square screen tiles; existing
larger towns crop on import as described in [town-size.md](town-size.md).

## Load-time texture policy

Host import crops and converts source images to RGB565 DNTX, retaining source
resolution up to 1024 pixels and using power-of-two dimensions required by GX.
The final reduction happens on the console: box-filter to at most 64×64, build
two levels total (base plus half-size, stopping at 4×4), and select CMPR when decoded mean squared RGB error is at
most 256 per channel. Higher-error images use RGB565 mipmaps. A 64-square tile
uses 2,560 bytes with CMPR mipmaps or 10,240 bytes with RGB565 mipmaps. Solid
4-square fallbacks use 32 bytes. The current seven unique sources per season
use 12,864 bytes in total, versus 13,824 with the previous full mip chains
and the older 32 KiB active atlas.

Sampler state uses mirrored repeat and trilinear minification when mipmaps are
present. Cache keys include the source disc URI, build identity and processing/
sampler policy. Original dimensions and source fingerprint survive; originals
and temporary decode/encode arrays leave scope before explicit collection.
`texture-audit.json` includes runtime format, dimensions, mip count and resident
bytes, plus original PNG SHA-256 and crop provenance. Identical source packages
are deduplicated within each season. The application cache remains limited to
4 MiB of native pixels, with 256 source slots and lease-aware eviction.

## Geometry and memory

Ground uses curved one-metre rows merged horizontally wherever material/variant
matches. Texture coordinates repeat every two metres. Indoors use one flat
quad. Only visible rows/cells enter the plan; frustum and cylinder-backside
culling remain active. There is no mesh per world cell or duplicated tile image.

Per-material dynamic buffers grow geometrically and are reused while visible;
inactive material meshes are released. Unused capacity contains degenerate
white vertices, preserving the uniform-color indexed GX fast path. The plan
is capped at 1,365 strips; rounded active buffer capacity stays below 16,380
vertices. Typical views use far less geometry. All allocation/upload work is
outside render passes, and a stationary camera reuses prepared meshes.

Releases contain a compact NKit ISO with title/hash/disc/save banners, the separate
server build, and texture audit. Dolphin measurements are diagnostic; physical
GameCube performance and GC Loader/Swiss/media testing remain outstanding.
