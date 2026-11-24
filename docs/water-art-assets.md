# Clear river water: artwork delivery brief

## What is required now

**No supplied artwork is required to run the clear-water renderer.** It has
procedural riverbed, ripple, caustic, distortion and shoreline masks, plus
generated rocks. Each of the five texture roles below can independently use
artist-authored artwork. Supplying one PNG does not require supplying the rest.

The target is the Smalltown Forest wallpaper's shallow turquoise river: a
stationary sandy/pebbly bed, a few larger rounded rocks, small broken surface
glints, gentle moving underwater light, and restrained shoreline accents. Keep
the bed's material pattern separate from the surface ripples. The largest rocks
provide silhouettes; the bed texture supplies the much smaller pebbles.

## Files the artist can supply

Put final PNGs in `assets/textures/environment/water/`. Filenames are exact and
case-sensitive. The cooker deliberately rejects misspelled or unknown PNG
names in that directory, incorrect dimensions, and unsuitable channels.

| Priority | Exact source filename | Required dimensions and PNG channels | Cooked runtime file / format | Resident pixels |
|---|---|---|---|---:|
| 1 | `riverbed-color.png` | 128×128, opaque RGB or RGBA with A=255 | `water-riverbed-color.dndtex`, RGB565 | 32 KiB |
| 2 | `surface-ripples.png` | 64×64, grayscale RGB, A=255 | `water-surface-ripples.dndtex`, I8 | 4 KiB |
| 3 | `bed-caustics.png` | 64×64, grayscale RGB, A=255 | `water-bed-caustics.dndtex`, I8 | 4 KiB |
| 4 | `shoreline-foam.png` | 64×64, grayscale RGB plus alpha | `water-shoreline-foam.dndtex`, IA8 | 8 KiB |
| 5 | `surface-distortion.png` | 64×64, opaque RGB or RGBA with A=255; special channel meanings below | `water-surface-distortion.dndtex`, RGB565 | 8 KiB |

Every map is a single base level with repeat sampling. The whole set has a
**56 KiB native pixel payload** plus small package/effect overhead. Authored
textures replace the corresponding procedural texture; they are not an extra
copy retained beside it. This figure excludes meshes and the separate legacy
opaque-ocean and rough-sea materials. Package dimensions and formats are shared between the cooker and
runtime in `WaterTextureCatalog`.

### 1. Riverbed color

This has the greatest effect on the finished art direction.

- Paint a top-down, seamless tile of muted sand, gravel, and small rounded
  pebbles. Mix warm pale sand with subdued gray-green stones; avoid uniform
  saturated blue, because the water surface supplies the blue/turquoise tint.
- Let patches of the base remain visible. Use small and medium pebble shapes,
  with a few loose clusters and quiet areas between them. Do not paint a grid
  or equally spaced large rocks.
- Keep local shape shading restrained. Do not bake the bridge, riverbanks,
  long directional cast shadows, surface foam, or caustic lighting into this
  texture. The bed stays fixed while the separate light mask moves.
- Do not paint a large foreground boulder into the tile. Larger rocks are
  geometry, so they can have proper silhouettes and waterlines.
- The runtime maps it continuously in world coordinates. The image must join
  at all four edges; do not draw a border or a transparent gutter.

### 2. Surface ripple mask

Supply a **mask**, not a colored water image. Black means no bright ripple;
white is maximum ripple contribution, with gray for soft transitions. R, G,
and B must be identical and the PNG must remain completely opaque. PNG alpha
does not control water transparency; the material controls that separately.

Use short, broken, gently curved strokes and separated small glints. Leave
substantial black space. Avoid long connected honeycomb outlines, broad ocean
whitecaps, or dense white noise. Several subtle marks should fit within the
apparent width of a large rock. The runtime controls world scale and slow
motion, so do not bake perspective or a scrolling animation into the PNG.

The mask repeats in both directions. Check a 3×3 tiled preview and make sure
strokes crossing an edge continue at the opposite edge without a visible seam.

### 3. Underwater caustic mask

This is a separate grayscale, fully opaque mask: black contributes no caustic
light; lighter values supply the moving light pattern. Keep soft irregular
bands and small connected patches with restrained coverage and contrast.
Favor a calm shallow-river impression over a sharp swimming-pool grid.

Do not include pebble outlines, large rock shadows, white surface glints, or
water color. Those belong to other components. Make this texture seamless in
both axes. It moves independently over the stationary bed, which is what
helps the two levels read as separate depths.

### 4. Shoreline foam/contact mask

Supply grayscale RGB plus alpha. White or pale grayscale RGB is suitable for
visible marks; **alpha** shapes their coverage. Transparent pixels mean no
contact highlight. Use broken soft bands and sparse patches, not a solid
white rectangle. Avoid long opaque contours that would turn every riverbank
into an ocean surf line.

The native IA8 format stores intensity and alpha separately. The shader tints
the texture; colored RGB channels would be discarded, so the cooker rejects
them. The existing shoreline strip geometry supplies location and width.
The map itself is periodic in both axes; there is no per-bank image atlas or
unique rock-waterline painting requirement.

### 5. Surface distortion field

This is a **two-channel displacement field, not a normal map**. It is the least
important artistic replacement; the procedural version is sufficient.

| PNG channel | Meaning |
|---|---|
| Blue | Horizontal texture-coordinate displacement |
| Green | Vertical texture-coordinate displacement |
| Red | Unused; leave at 128 |
| Alpha | Must be 255 |

Start near RGB `(128,128,128)` and use broad smooth variations around the
middle value in green and blue. Keep changes gentle and continuous at all
edges. Do not supply a normal-map convention such as red=X/green=Y/blue=Z,
and do not use black as the neutral displacement.

RGB565 quantizes the midpoint: the nearby encoded values are B=16/31 and
G=32/63, approximately 132 and 130 after expansion. A perfectly exact 8-bit
128 is not representable in both channels. The material uses a small signed
displacement and handles the channel mapping; an artist should not compensate
by swapping channels or inserting a baked water color.

## General delivery rules

- Export the exact game-size PNGs above. Larger layered masters may accompany
  the delivery separately, but they are not runtime inputs and must not be
  mixed into the five-file source directory.
- Use a top-down, seamless pattern with no perspective, borders, labels,
  logos, UI, baked camera lighting, or backdrop.
- Keep masks neutral grayscale. Use ordinary 8-bit PNG channels, rather than
  HDR data, EXR, a video, or PBR material graphs.
- Verify the appearance both at native size and tiled. Avoid single-pixel
  speckling that becomes distracting during slow movement.
- Provide each role as an independent file, not a contact sheet or atlas.
  The smaller actual world-scale ripples are configured by the renderer;
  increasing PNG resolution does not make waves physically smaller.

## Import, validate, and build

After placing any of the optional PNGs at the exact paths:

```sh
dotnet run --project tools/AnimalTown.Tools -c Release -- water-assets .
dotnet run --project tools/AnimalTown.Tools -c Release -- water-check .
```

The regular game art/build pipeline also runs the water cooker. It creates
the bounded native `.dndtex` files in `out/disc/` and writes
`out/water-textures.json`, recording which roles are authored, their original
and cooked hashes, formats, dimensions and resident bytes. Original PNGs are
never overwritten by cooking. If any supplied PNG is invalid, cooking fails
before replacing the previous cooked map set.

Remove a PNG and rebuild to restore that role's procedural fallback. The
cooker removes its stale `.dndtex`, so a previous authored map cannot silently
remain active in an incremental build.

The native game does not decode PNG files. `WaterTextureAssets.TryLoad` reads
the corresponding optional native package through `ContentPaths.Asset`, so
the mounted content source works the same way for disc and DOL-relative SD
installs. It validates the role, dimensions, format, mip count and exact byte
budget before upload. A missing map uses its generated fallback; a malformed
map also falls back and records a diagnostic instead of preventing play.

Both ISO and Depot builds use the same five explicit optional runtime names.
The Depot packager validates each supplied map and verifies that its presence
and bytes match the disc build. PNGs, larger masters, and the authoring report
do not enter the installed runtime payload.

Importer checks cover native GX block order and channels, format/dimension
budgets, opacity/grayscale constraints, deterministic cooking, source
preservation, stale-map retirement, unknown names, and matching optional
content between ISO and Depot.

## Rock meshes and additional assets

**No rock GLB is needed for this change.** The renderer supplies bounded,
reusable generated rocks. They are separate from the smaller pebbles in the
riverbed tile.

If a future art pass replaces those shapes, useful deliverables would be
three original unrigged rounded river-rock variants, triangulated, with one
opaque material, one UV set or vertex colors, +Y up, applied transforms and a
base-centered pivot. Authoring at a consistent unit diameter lets the renderer
control placement and size. Aim for roughly 48–96 triangles per variant and
at most one shared 64×64 color map; avoid transparency, PBR/normal maps and
animation. A smoother silhouette is useful; dense sculpt detail is not.

Those GLBs are a **future integration option**, not currently registered
drop-in replacements. This change does not promise that adding an arbitrary
`river-rock.glb` will be loaded. No reflection cubemap, refraction capture,
screen-space texture, unique riverbank mesh, or additional artwork is required
for the current water materials. Existing bridge and bank assets remain usable.

See [water-rendering.md](water-rendering.md) for the renderer and pass order,
and [world-asset-requests.md](world-asset-requests.md) for the broader world kit.
