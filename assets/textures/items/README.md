# Dropped-item world billboards

Six 64×64 transparent source PNGs live here, each with at least two pixels of
transparent padding and a baked-in soft ground shadow:

| Source | Drop appearance |
| --- | --- |
| `dropped-item.png` | Original generic fallback for all other items |
| `dropped-money.png` | Acorn currency bags (catalog IDs 22–24) |
| `dropped-tool.png` | Tools/equipment (`ItemFlags.Tool`) |
| `dropped-clothing.png` | Clothing (`ItemFlags.Wear`) |
| `dropped-quest.png` | Quest-related letter/lost umbrella (IDs 19 and 20) |
| `dropped-present.png` | Parcels (ID 18), or any explicitly wrapped item |

The five new icons use a soft, rounded, New Horizons-inspired visual style with
acorn motifs. Their original object illustrations are retained as source PNGs,
not used as UI sprites. The generic item remains the original supplied golden
pinecone marker for unclassified drops.

All six sprites are cooked by `DroppedItemAssets.Build` as **GX-tiled RGB5A3**
native texture packages, exactly **64×64, seven mip levels down to 1×1**,
**10,976 pixel bytes** (11,008 bytes per package). This is the format and
resolution of the original `dropped-item.dndtex` committed fallback.
The offline cooker alpha-weights mip filtering and bleeds RGB into transparent
texels to avoid black edges; the engine uses trilinear minification with clamp
wrapping. Only the six native packages are placed into the disc's asset root,
so the PNG sources are not loaded on the console.

The fallback `dropped-item.dndtex` remains checked in and validated against
its PNG source to prevent regressions. The other five packages are reproducibly
generated directly into `out/disc` by `art` / the normal ISO build.

Check native textures and all mip levels:

```sh
dotnet run --project tools/AnimalTown.Tools -c Release -- dropped-item-check .
```

After changing the *generic fallback*, recook its tracked native package with:

```sh
dotnet run --project tools/AnimalTown.Tools -c Release -- dropped-item-import .
```

The texture audit records all source/native hashes, format, dimensions, mip
levels, and resident pixel bytes.

The base ground item identity is unchanged. An optional `GroundItem.Wrapped`
flag selects the present appearance for **any** ordinary dropped item; it is
independent of `ItemFlags.Gift`, which means *giftable*, not already wrapped.
Wrapping is stored in an unused high bit of the existing six-integer ground
kind field and does not increase the save record size. Legacy saves decode the
same way, with `Wrapped=false`.

Buried objects, flowers, saplings, trees, and lost-and-found records retain
their existing world representations and gameplay behavior. The generic icon
is used for drops outside the five explicit categories.
