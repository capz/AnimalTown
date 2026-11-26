# Current villager assets

## Additional appearances supplied on 2026-11-26

Five distinct characters are present in the seven supplied ZIPs. The two
`4073a55c` archives are byte-identical. The `f8e7f757` GLB and USDZ exports
represent the same raccoon. The `adb5cd87` and `1f2f9207` archives each split
one character's animations across two exports. A sixth distinct character
was requested but its archive has not been supplied yet.

| Appearance | Prepared source | Source archive prefix |
|---|---|---|
| Penguin | `villager_penguin.glb` | `4c8507cd` |
| White tiger | `villager_whitetiger.glb` | `4073a55c` |
| Lion | `villager_lion.glb` | `adb5cd87` |
| Robin | `villager_robin.glb` | `1f2f9207` |
| Raccoon | `villager_raccoon.glb` | `f8e7f757 (1)` |

Each prepared GLB has `Idle`, `Walking` and `Running` clips on its own rig.
Split exports are merged only after verifying identical meshes, nodes and
skins. Clip durations, rotations and vertical motion retain their supplied values.
Walking/running hips tracks remove horizontal travel so the rig loops in place
under the existing world navigation; cyclic sway is retained.
Embedded images are reduced from 2048px to 128×128 with Lanczos filtering;
the runtime uses the same 128px base plus 64px mip as existing villagers.
The explicit 256px package also retains those original 128px imported images.

The SDK accepts four skin influences. The importer combines duplicate joints,
keeps each vertex's strongest four influences, and normalizes their weights.
`new-villagers-provenance.json` records archive/export/prepared hashes, clip
aliases and maximum discarded weight. Positions, topology, UVs, rigs and
inverse binds are preserved. Regenerate with Pillow installed:

```sh
python3 tools/import-villagers.py /path/to/downloaded/zips /path/to/AnimalTown
```

The new appearances ship in both disc and Depot payloads. They are available
through the [debug visual swap control](villager-visual-swaps.md), without
adding saved species slots or changing the resident roster.

## Existing saved species

The bunny, giraffe, Maine coon and hamster use the animated GLBs supplied on
2026-11-22. The stable species slots keep existing villagers and save data
associated with the same species.

| Save slot | Species | Source under `assets/models/villagers/` | Triangles | Rig | Supplied clips |
|---|---|---|---:|---|---:|
| 0 | Fox | `Meshy_AI_Rigged_Fox_All_Animations.glb` | Original | Original | 2 |
| 1 | Bunny | `villager_bunny.glb` | 1,489 | 38 joints | 5 |
| 2 | Giraffe | `villager_giraffe.glb` | 1,572 | 34 joints | 6 |
| 3 | Maine coon | `villager_mainecoon.glb` | 1,548 | 37 joints | 4 |
| 4 | Hamster | `villager_hamster.glb` | 1,562 | 28 joints | 4 |
| 5 | Cocker spaniel | `villager_cockerspaniel.glb` | 1,574 | 42 joints | 0 |

The four replacements retain their meshes, UVs, skinning and supplied animation
tracks. Their embedded textures are 128×128, including maps the GameCube
material path omits. Both cooked variants for each replacement therefore fit
within 128×128. GX renders the base color with the existing diffuse
approximation. The importer normalizes model height and places the bind pose
at ground level.

Players and residents share these models. The GameCube build packs the supplied
locomotion clips and adds `Idle` for bunny, giraffe, hamster, and Maine coon.
The idle clip loops when a villager stands still. Fox and spaniel use their
standing pose. Constant tracks store one sample at the original final key
time. Cooked tracks reduce redundant keys to within 0.5 degrees of local rotation
and 0.0005 model units of translation at the source sample times. The original
clip duration is preserved.
Cooked models keep the textures sampled by GX materials. The source GLBs
retain the full animation and texture data. The picker portrait bake uses the
same framing and lighting recipe.
