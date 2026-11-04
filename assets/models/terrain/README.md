# Terrain prototype sections

The production [cliff kit](cliffs/README.md) supplies eight orientations, shared
soil textures, fixed world-ground UV bindings and a live eight-piece preview.

`section_cube_2m.glb`: a closed **2 x 2 x 2 metre** cube, 8 vertices / 12 triangles.
`section_ramp_2m.glb`: a closed single-section wedge, **2 x 2 metre footprint**,
**2 metre rise**, 6 vertices / 8 triangles. North edge (Z=0) is Y=2;
south edge (Z=2) meets Y=0. Rotate around Y for other approach directions.

Both use +Y up, bounds (0,0,0)..(2,2,2), outward glTF winding, an earth-colour
material and a lower northwest corner pivot. A section covers 2x2 fine cells;
ten sections fit a 20-metre acre. The generator validates signed volume.

Regenerate: `dotnet run --project tools/AnimalTown.Tools -c Release -- terrain-primitives .`.

The console batches equivalent exposed faces, omits internal faces, and uses
regular grass on top. Every corner follows the same cylinder/elevation equation
as the floor, avoiding seams between rigidly positioned section models. The GLBs
are editable source/reference assets; per-section model loads/draws are unnecessary.
