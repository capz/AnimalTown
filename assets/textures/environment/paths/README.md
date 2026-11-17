# Painted dirt path

Original artwork generated for AnimalTown’s selected approach B, November 2026.
Warm ochre earth, broad painted wear and sparse pebbles; no procedural noise overlay.
The source image was reduced offline to 64×64 and its opposite edge texels matched
with a four-pixel correction. `path-import` is the authoring step; ordinary builds
only validate and cook the committed tile.

`PathAssets.Build` emits `path-dirt.dndtex`: one opaque native CMPR page with the
64, 32, 16, 8 and 4 pixel mip levels (2,752 pixel bytes, 2,784 bytes on disc).
The PNG is an authoring asset and is never packaged on the game disc. Corners,
ends and junctions share this tile and get their outlines from terrain geometry.
