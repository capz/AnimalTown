# Terrain texture authoring

Edit `assets/terrain/terrain.json`, then build with `tools/build-game.ps1`.
The release workflow includes the resulting terrain on the bootable ISO.
No runtime PNG decoder, JSON parser, shader or desktop font/drawing library enters
the GameCube executable. The host tool converts authored PNGs into GX RGB565.

## Seasons and surfaces

`season` accepts `auto`, `spring`, `summer`, `fall`, or `winter`. Auto uses the
console RTC: March–May, June–August, September–November, December–February.
Sand is a surface/border environment, not a fifth season. Summer currently reuses
the supplied spring transition illustrations; a distinct summer sheet can replace
each source without changing code. Changing seasons does not alter tree models.

Base surfaces are `grass`, `water`, `wood`, `sand`, `rock`, `pavement`, `dirt`,
`floor` and `building`. Extra patch materials are `terracotta`, `stone_pavement`,
`sand_patch` and `pond`. Base definitions live under `surfaces`; a season can
replace a material definition under `seasons`. An omitted override inherits its
base definition. A definition with no sources uses its explicit flat color as a
placeholder. Large-area grass, river water, sand and wood still need seamless art;
the transition sheets supplied so far are not seamless texture tiles.

## Repeating tiles and variations

Use opaque, seamless PNGs, preferably 32×32 or 64×64. Their opposite edges must
meet. Each rendered floor cell is one logical metre; each screen tile remains a
20×20-metre square, in the unchanged 20×20 screen-tile world. Up to four repeat
variants per material are chosen by a stable coordinate hash, without per-frame
randomness or per-cell save overhead. Multiple variants should share compatible
edges so neighbouring variants also join cleanly.

For example, after supplying these PNGs, replace a grass definition with:

```json
{
  "color": "#85B86B",
  "mode": "repeat",
  "sources": [
    { "file": "assets/textures/tiles/grass-spring-a.png" },
    { "file": "assets/textures/tiles/grass-spring-b.png" }
  ]
}
```

A source can select a sheet region using `"rect": [x, y, width, height]` in
top-left pixel coordinates. This crops/resamples an authored tile; it does not
make non-periodic artwork seamless. Source paths must stay inside the repository.
Missing files, transparency, invalid bounds and exceeded atlas budgets fail builds.

## Existing nine-slice transitions

The supplied terracotta, stone-pavement, sand and water sheets are continuous
768×768 illustrations with nine 256×256 slices. Use `mode: nineSliceStretch`
with exactly one source; never repeat those centers/edges or the complete sheet.
The importer downsamples each slice independently, adds a two-pixel gutter and
uses inset UVs. Runtime preserves corner size and stretches the edges and center
over an explicit rectangular patch on the cylinder.

```json
{
  "surface": "stone_pavement",
  "terrain": "grass",
  "x": 46, "z": 32, "width": 7, "height": 6, "border": 1
}
```

`surface` selects artwork; `terrain` matches an existing base surface. Coordinates,
dimensions and border are integer logical metres. Width/height must exceed twice
the border. First matching patch wins. Patches change rendering, not collision,
walkability or save data. A `pond` patch must match `water`; it cannot silently
turn walkable grass into water. New towns contain the example pond at (82,26);
old saved terrain is preserved and only receives patches where terrain matches.
Define actual terrain in `Town.Generate` when authoring a new layout.

The default manifest uses all four provided center materials. Files prefixed
`sand_` describe sand-bordered versions; select them as the material's source for
sandy environments. They are available for authoring, not automatically substituted
for grass-bordered sheets without a matching layout. No artwork is regenerated.

## Hardware budget and verification

Default `tilePixels: 32`, `atlasSize: 256` gives 49 padded slots and **128 KiB**
resident RGB565 texture storage. The default four nine-slice materials plus nine
base surfaces occupy 45 slots. Each repeat variant uses one slot; a nine-slice
source uses nine. To add many variants, author `atlasSize: 512` for 196 slots at
32 pixels, costing **512 KiB**. `tilePixels: 64` needs the 512 atlas for the default
material set. Larger sizes are rejected. Disable unused transition overrides to
recover slots. Only the active season's texture is retained; season changes load
outside a render pass. Generated assets are under `out/terrain` and `out/disc`.

Ground uses reusable textured dynamic meshes, no frame allocations, one material
submission, and no redundant rebuild while the camera/room is stationary. The
68-row outdoor band has 8,036 triangles, with row widths bounded around the camera frustum; its edges remain outside the
viewport. This and up to eight detailed source trees still require physical
frame-time/memory profiling; emulator boot is not hardware performance evidence.

Build checks exercise native atlas swizzling/gutters, source cropping, variants,
opacity, bounds and source confinement. Shared checks cover seasonal mapping,
negative-coordinate repeat stability and continuous nine-slice coordinates.
