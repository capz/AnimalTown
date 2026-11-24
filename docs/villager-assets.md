# Current villager assets

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
