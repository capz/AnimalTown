# Supplied structure assets

`assets/models/world-stuctures/structure_tent.glb` is the canonical guest-tent
source, supplied on 2026-11-03. It has 1,510 triangles and one base-color material.
The Linux build normalizes it with the other structures and imports it as
`dvd:/tent.dndmodel`, with 128-pixel textures. ISO validation requires that file;
the release texture audit records its package/payload fingerprints.

There was no previous tent GLB or tent placement renderer in this checkout.
The supplied asset is packaged for the guest-tent system; player houses and
shops retain their existing models. Guest tent persistence, placement and
rendering remain part of the outstanding online implementation. The tent is
not eagerly loaded into console memory while unused.

`assets/models/interiors/interior_shop_small.glb` is the supplied shop interior,
with 2,596 triangles and one base-color material. It is stored in source as
requested; it is not yet imported into the ISO or substituted for house rooms.
Both original GLBs are copied byte for byte. No replacement artwork is generated.
