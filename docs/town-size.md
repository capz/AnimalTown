# Compact town bounds

New towns contain **128×120 one-metre ground cells**: 15,360 cells, exactly 60%
of the previous 160×160 town's 25,600 cells. Buildings, individual cells,
20×20-cell acre templates, movement speed and cylinder curvature retain their
scale. Seven acre columns (the easternmost only eight cells wide) and six rows
cover the rectangular map.

Generator version 5 uses complete inland acres for rivers, both bridges, ponds
and three diagonal-corner terraces. Services, six resident homes and the starter
plot keep their anchors. Vegetation falls from 320 to 192 trees. The southern
ocean retains its 32-cell depth, so water occupies a larger share of this town.
Terrain and elevation each use 15,360 bytes, saving 20,480 bytes together;
per-cell navigation and terrain archive coverage also follow the smaller bounds.
Fixed visible-window caches and other object capacities do not shrink by 40%.

The full map keeps equal horizontal and vertical cell scale, with a 280×262.5
pixel terrain rectangle. Acre lines and labels include the partial east column.
Its cached two-cell-stride terrain texture is 64×64, using 16 KiB each for the
RGBA upload buffer and native texture; only 64×60 texels hold map samples.
Minimap UVs and markers use the separate width and height.

**No old-size conversion is supported.** Combined and split save records must
declare exactly 128×120 cells. Implicit-dimension schemas 1–3 and larger-town
records are rejected before decoding their terrain. The old cropping, home
relocation and outlying-item recovery paths have been removed. Delete existing
town data using title Options → Data → Delete town data, then create a new town.
No automatic deletion or regeneration occurs on load; personal video settings
remain independent of town data.

Checks cover exact area, both rectangular boundaries, multi-seed land and
service reachability, river/pond topology, generated cliff geometry/navigation,
edge-position save round trips and rejection of old dimensions in both codecs.

For focused native validation, run `tools/iso-smoke.py ISO OUTPUT --compact-town
--dsp-hle` in the Dolphin environment with a disposable empty card. It creates a
town through onboarding, checks committed dimensions, captures the map and
generated ramp, climbs the ramp and saves again without changing terrain.
Copy that disposable card to a second output directory and use
`--compact-town-resume --dsp-hle` to check loading and map rendering while
retaining the exact saved generation. Physical-console validation is separate.
