# Dropped-item sprite

`dropped-item.png` is the user's supplied golden pinecone, background removed
with image editing and fitted into a **64×64 transparent canvas**, including at
least two transparent padding pixels. The larger transparent edit is retained
outside the game repository; only the 64px review/source PNG is kept here.

`dropped-item.dndtex` is the committed GameCube-ready asset: GX-tiled **RGB5A3**,
64×64, seven mip levels down to 1×1. Pixel payload: **10,976 bytes**; package:
11,008 bytes. RGB5A3 keeps 3-bit alpha at the silhouette and 5-bit RGB on opaque
texels, using half the memory of RGBA8. CMPR would reduce memory further but
its one-bit alpha discards the antialiased edge. Paletted formats are not
supported by the current SDK package loader.

The offline cooker uses alpha-weighted mip filtering and bleeds RGB into fully
transparent texels to avoid dark filtering fringes. Only the native package is
copied to the disc. The game uploads that package directly, with clamp wrapping
and trilinear minification; it never decodes PNGs or builds these mips at runtime.

Recook after an approved source edit:

```
dotnet run --project tools/AnimalTown.Tools -c Release -- dropped-item-import .
dotnet run --project tools/AnimalTown.Tools -c Release -- dropped-item-check .
```

Builds reject stale native assets. `texture-audit.json` records source/native
hashes, native format, dimensions, mip count and resident pixel bytes.

Ordinary dropped ground items share this marker. Buried objects, flowers,
saplings, trees and lost-and-found records keep their existing representations.
Pickup, inventory identity, collision and saved ground records are unchanged.
