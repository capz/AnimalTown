# Level cliff tiles — revision 3

Generated with built-in image generation. This replaces revision 2 as the
modeling specification; these are concept drawings, not implemented meshes.

## Shape and height

Each section occupies the same complete 2 x 2 m square as a regular section.
Cardinal pieces are one cube with a flat top at local height 2 m. A diagonal
piece retains exactly half that cube, cut by a vertical plane between opposite
corners. Its raised triangular top is flat at 2 m; the other triangular half
is an ordinary flat ground surface at 0 m. Both halves together cover exactly
one square footprint, without a hole, extra pedestal or additional cube.

All vertices of the high cap have the same height. All vertices of the low cap
have height zero. The intervening wall is vertical, with equal 2 m height at
both endpoints. There is no slope interpolation, bevelled cap or ramp. Level
is defined in the unrolled terrain coordinates; the complete section follows
the cylindrical world in the same way as ordinary ground.

Let x,z be section-local coordinates in [0,2], with +x east/right and +z
south/down. The low half faces the named orientation:

| Orientation | Raised half (height 2 m) | Ground half (height 0 m) |
| --- | --- | --- |
| UP LEFT | x + z >= 2 | x + z < 2 |
| UP RIGHT | x <= z | x > z |
| DOWN LEFT | z <= x | z > x |
| DOWN RIGHT | x + z <= 2 | x + z > 2 |

The diagonal needs separate high/low edge vertices at identical X/Z coordinates
and a vertical wall between them. It blocks traversal; ramp pieces are separate.

## Seamless ground and materials

Both horizontal halves use the existing neutral outdoor tile, at the same scale,
phase, season and tint as adjacent regular sections. Neither triangle receives
a complete image squeezed into its bounds: together they sample the appropriate
two halves of one regular tile's UV square.

Use the unrolled town position **before cylindrical bending**:

```
u = townX / 2
v = townZ / 2
```

Rotating or mirroring the cliff geometry must not rotate, mirror or restart
these coordinates. Derive them from the placed vertex position, not from the
rotated mesh's local UVs. A shared edge always has matching UV coordinates
regardless of height or cliff orientation.

Every vertical face uses the same soil appearance. A narrow 5-10 cm spillover
lip follows every upper edge; its tint must be the exact same terrain/season
tint used for the top. The soil itself does not inherit the grass tint. Do not
assign the triangle-pattern grass-top material across a vertical wall. Keep the
top planar; the lip does not require a sloping cap or extruded grass geometry.

## Delivery

See `cliff-kit-v3.png` for the concept and `cliff-kit-v3.prompt.txt` for the exact
final generation prompt. Revision 2 remains historical reference. The diagram
illustrates the construction; seamless runtime sampling must use the real
neutral tile and the coordinate rule above, not pixels cut from the drawing.

