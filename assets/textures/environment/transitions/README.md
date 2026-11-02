# AnimalTown terrain transition atlases

16 opaque PNG atlases grouped by center material. Each center has spring grass, fall grass/leaves, winter snow, and sand borders. The borders have visible organic fringes, contact shadows, snowbanks, or raised sand lips.

| Center material | Atlas folder | Preview folder |
| --- | --- | --- |
| terracotta | [atlases](terracotta/atlases/) | [previews](terracotta/previews/) |
| water | [atlases](water/atlases/) | [previews](water/previews/) |
| sand | [atlases](sand/atlases/) | [previews](sand/previews/) |
| stone_pavement | [atlases](stone_pavement/atlases/) | [previews](stone_pavement/previews/) |

Each atlas is **768 x 768 pixels**, containing a **3 x 3** layout of **256 x 256** slices. No gutters, labels, or guides occur in the actual atlas files. Coordinates start at the top-left.

```text
top_left      top       top_right
left          center    right
bottom_left   bottom    bottom_right
```

Slice origins are x/y = 0, 256, 512. Use standard nine-slice **stretch** mode: preserve the corners, stretch top/bottom horizontally, left/right vertically, and center along both axes. These are continuous illustrations; opposite slice edges are not designed for repeat tiling. Stretching also stretches texture features within the extended slices.

Each individual preview shows native 3x3 cuts and an enlarged rectangular stretch. Guides appear only in previews. Every atlas was decoded, checked for dimensions/opacity, split into nine regions, and reassembled byte-exactly. The catalog includes SHA-256 hashes. These are file and visual checks; no in-engine or hardware rendering validation is claimed. Use engine-side UV insets or padding if needed for filtering/mipmaps.

Artwork was created with the built-in image generation tool. Prompts are stored within each material folder. New materials use the revised terracotta atlases as edit targets and the corresponding spring material as reference.
