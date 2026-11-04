# Shared low-poly cliff kit

Eight GLBs cover UP, DOWN, LEFT, RIGHT, UP LEFT, UP RIGHT, DOWN LEFT and DOWN
RIGHT. Each section is 2x2m with a 2m rise, +Y up, lower northwest pivot.

Cardinals: **36 exported vertices / 18 triangles**. Diagonals: **30 vertices /
14 triangles**, including the zero-height triangular half. One of each totals
**128 triangles**. Hidden underside caps are omitted. Walls and caps have hard
outward normals. The four cardinal orientations share one runtime template.

Each diagonal consists of one level half-cube prism plus a level ground triangle:
high Y=2, low Y=0, vertical corner-to-corner wall. There are no slanted caps or
ramps. See `docs/concepts/cliff-kit-v3.md` for the orientation predicates.

## Materials and UVs

Material 0 is **Bind upper-level neighbor (top and lip)**. Material 2 is
**Bind ground-level neighbor (low half)**. Resolve these independently: sand
below a grass terrace stays sand, including the complete low triangle. Replace
each entire placeholder with its neighbor's existing ground material, including
seasonal tint; neither placeholder contains a texture image or an extra runtime
multiplier. The 6cm lip always follows the upper material. Material 1 remains soil.

The live preview checks touching north/east/south/west edges in that fixed order,
accepting only neighbors on the corresponding high or low side and at the same
height. An isolated section without a matching neighbor falls back to its own
ground material. The floor renderer binds the low half to that lower neighbor;
the separate low triangle is omitted to avoid coplanar duplicate geometry.
The preview can choose different existing material batches above and below
without loading any new ground textures. At a junction with several surface
types the fixed neighbor order is deterministic; an eventual terrain editor
should let the player explicitly select the intended surface.

For placed sections, compute U=unrolled town X/2 and V=unrolled town Z/2 before
cylinder bending. Never rotate, mirror or restart these UVs with the mesh. A
triangle uses its matching half of the regular tile's UV square. Ground and lip
thus reuse the existing texture and its spring/summer/fall/winter tint.

Material 1 is **Shared soil / stacked UVs**. Every wall chart occupies the same
0..1 UV rectangle: all soil faces overlap, with no atlas gutters or unused islands.
The diagonal uses the same rectangle across its longer face. V maps full wall
height; U repeats, V clamps. Side texture detail is deliberately broad enough for
this economical unwrap. All GLBs reference the same external PNG at
`../../../textures/environment/cliffs/cliff-soil.png`.

Default soil is 128x128; a 256x256 alternative is included. The GameCube package
uses CMPR plus one half-size mip when quality permits: default residency is
**10,240 bytes total**. The soil package is shared and loaded lazily once.

## Runtime

The game uses matching procedural templates rather than loading eight separate
model/texture packages. Existing raised terrain uses pooled exposed soil walls
and ground-material lips; hidden internal sides are omitted. Ground caps remain
in the ordinary floor renderer. Native indexed-compatible batches use white
vertices, one shared soil material and the existing ground materials.

Developer inspection: **L+R+D-pad Up** toggles an eight-piece gallery near the
player. It is a rendering preview, has no collision and does not change/save town
terrain. It draws the regular floor as the low half, avoiding coplanar duplicate
triangles. All positions follow the cylindrical world. Existing maps and their
raised-section layouts are retained; this change does not add diagonal placements
to town generation or change movement/save formats.

Regenerate geometry and select authoring texture size:

```
dotnet run --project tools/AnimalTown.Tools -c Release -- terrain-primitives . 128
dotnet run --project tools/AnimalTown.Tools -c Release -- terrain-primitives . 256
```

`TerrainCliffGeometry.cs` is the common source for exported and live geometry.
`cliff-kit.json` records counts/binding details. The asset ZIP keeps the `assets/`
directory layout so the shared external texture resolves after extraction.
