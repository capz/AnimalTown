# Requested world assets

Please provide the following original assets in our established visual style.
Use only types present in Animal Crossing on GameCube or New Leaf on 3DS.
These are asset requests, not a request to copy Nintendo meshes or textures.

Trees use only the supplied deciduous, palm and pine GLBs. Procedural tree
geometry, generated tree artwork, billboards and invented LODs are prohibited.
The renderer currently limits detailed trees to the eight nearest visible trees
because the supplied meshes are expensive. More tree art is not needed to fill
the town; lower-poly versions of these exact assets are the first priority.
Young trees use scaled instances of the same supplied model; the nursery packet
inventory icon contains no generated tree illustration. Any seasonal tree models
or model-derived inventory thumbnails must also be supplied by you.

## First delivery

| Asset | Specific request | Footprint / pivot | Budget per mesh |
|---|---|---|---|
| Existing tree LODs | Artist-authored lower-poly deciduous, palm and pine matching the supplied trees; preserve silhouette, species and source textures. Label `deciduous_lod1.glb`, `palm_lod1.glb`, `pine_lod1.glb`. No new species required. | Same dimensions as each current source; trunk-base pivot at ground. | 400–800 triangles, 1 material, one 128×128 texture. An optional 100–200-triangle far LOD is useful. |
| Seamless ground tile pack | Spring/summer/fall/winter grass, dry sand, dirt, wood bridge decking, stone/cobblestone pavement, terracotta pavement and river water. Start with one tile each; optional A/B variations must join with one another. Existing transition illustrations cannot substitute for these repeat tiles. | Opaque top-down PNG; 32×32 or 64×64. No baked perspective, labels or shadows from unrelated objects. | One RGB texture per tile; no PBR maps. Summer transition sheets for all four center materials are also missing. |
| Wooden river bridge | One simple original wooden bridge, including deck, low rails and bank landings. A second cobblestone variant can follow. | Span along +X, 6 m long × 2 m wide; ground/deck-center pivot; bank sockets at X=−3 and +3, Y=0. Keep the central 1.5 m walk lane unobstructed. | 600–1,200 triangles; 1–2 materials; 128×128 textures. |
| Natural ramp | Grass/dirt slope with two side cheeks; the fixed geographical type, not a New Horizons construction kit. | 2 m wide, 6 m run, 2 m rise; pivot at lower entrance center (0,0,0), upper entrance at (0,2,6). | 200–500 triangles; 1–2 materials; 128×128 textures. |
| Cliff kit | A straight segment plus matching inner and outer 90-degree corners, with a grass lip at the top. | Straight: 4 m long, 2 m high, 0.5 m deep. Corners fit a 2×2 m grid. Pivot at ground-level seam; matching seam vertices. | 100–300 triangles per piece; 1 material, shared 128×128 sheet. |
| Riverbank kit | Straight bank, inner/outer bends and an end/pond join. Natural soil/grass banks with a waterline, not concrete canals. | Straight pieces 4 m long; corners fit 2×2 m. Waterline Y=0, ground lip Y=0.3; pivot at waterline seam. | 100–300 triangles per piece; shared 128×128 sheet. |

## Following delivery

| Asset | Specific request | Size / budget |
|---|---|---|
| Waterfall | A natural waterfall lip and rock side cheeks that joins the 2 m cliff kit; separate opaque water strip for later animation. | 2 m wide, 2 m drop; 300–600 triangles, 128×128 textures. |
| Shore kit | Sand-to-water straight shoreline plus inner/outer corners, and a low beach rock cluster. | 4 m straight sections, 2×2 m corners; 100–300 triangles per piece. |
| Bush | The previously mentioned bush is not present in the model checkout. Please supply its path or provide one original bush GLB. | About 1 m wide, trunk/base at Y=0; 150–400 triangles, one 64×64 or 128×128 texture. |
| Pond bank | Four compatible curved quarters and one straight extension, joining the riverbank waterline. | 2×2 m quarters, shared bank sheet; 100–300 triangles each. |

GLB delivery: metres, +Y up, triangulated, applied transforms, ground-aligned
pivots, one UV set, embedded base-color PNGs. Avoid normal maps, metallic/roughness
textures, transparent layered foliage and complex material graphs. Include a
small JSON/text note identifying dimensions, pivots, collision boxes and entry/
exit sockets. Keep trees unrigged unless a supplied animation is necessary.
Meshes remain parented to the cylindrical world and align their local up/down
axis with the radial ground normal.

Reference scope: Nintendo's [New Leaf manual](https://csassets.nintendo.com/noaext/image/private/t_KA_PDF/manual-3DS-Animal_Crossing_New_Leaf_Welcome_amiibo_full)
describes bridges and other public works. The [incline reference](https://nookipedia.com/wiki/Incline)
distinguishes the older natural ramps, including New Leaf beach access, from the
later construction system; the [cliff reference](https://nookipedia.com/wiki/Cliff)
describes GameCube terraces and New Leaf's town/beach separation. No canals,
player terraforming, ladders, vaulting poles or modern elevated-road kits are
requested.
