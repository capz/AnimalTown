# Cliff kit concept, revision 2

Superseded by [revision 3](cliff-kit-v3.md): retain the low half at ground level
to complete the square tile, and keep all ground UVs world-aligned.

Generated with the built-in image-generation tool, using the first cliff sheet
as a visual reference. This is a modeling concept, not runtime model geometry.

Generation brief / prompt:

Preserve the warm cream 3x3 compass sheet, clear brown orientation labels,
low-polygon GameCube-era painted earth style, and 2m grid / 2m rise. Arrange
UP LEFT, UP, UP RIGHT / LEFT, central specification key, RIGHT / DOWN LEFT,
DOWN, DOWN RIGHT.

Every vertical face must have the same tan/clay earth treatment, horizontal
soil bands and darker soil toe. Add only a thin 5-10cm grass-colored spillover
lip below the top edge on every face. Never cover a vertical face with the
triangle-pattern grass top material, including back/connecting/cut faces.

For every diagonal, cut a full 2x2x2m cube along a vertical plane joining two
opposite corners, remove exactly half, and retain a right-triangular prism.
The top is horizontal and triangular, not a ramp, small chamfer, pentagon or
tetrahedron. Show triangular footprints inside dashed square grid outlines.
Directions identify the exposed face toward the removed half:

- UP LEFT: remove northwest; retain northeast/southeast/southwest vertices.
- UP RIGHT: remove northeast; retain northwest/southwest/southeast vertices.
- DOWN LEFT: remove southwest; retain northwest/northeast/southeast vertices.
- DOWN RIGHT: remove southeast; retain northwest/northeast/southwest vertices.

Each triangular prism has three full-height soil walls; the cut wall spans
2*sqrt(2)m. Cardinal pieces remain square-topped blocks with earth on all sides.
Use outside three-quarter views that show the diagonal wall and triangular top.
Avoid decorative bases obscuring the footprint, high-poly bevels, geometry for
individual soil bands, trees, houses or other props. Center key: MODULAR CLIFF
KIT / 2 m grid / 2 m rise / Diagonals: half-cube prisms / Earth on every vertical
face / Thin grass lip on every edge / Low-poly / shared materials. Do not print
unverified triangle counts.

Modeling note: bands and lip should primarily be painted. Cube and triangular
prism prototypes can be rotated for the eight orientations, with shared indexed
buffers, matching seam heights, opaque materials and radial world parenting.

A basic closed cube is 12 triangles; a closed half-cube triangular prism is
8 before optional bevels. Paint the soil bands and thin lip rather than modeling
them as extra rings. Every vertical face uses the soil material, including the
diagonal cut: its top strip can depict the lip, but the grass-top material must
never cover a vertical wall. These are primitive starting counts, not certified
triangle counts for an eventual finished mesh.
