# Current villager assets

On 2026-11-03 the five non-fox species were replaced with the user-supplied
lower-poly GLBs. The original fox GLB and its clips remain unchanged.

| Stable save slot | Species | Source under `assets/models/villagers/` | Triangles | Rig | Clips |
|---|---|---|---:|---|---|
| 0 | Fox | `Meshy_AI_Rigged_Fox_All_Animations.glb` | Original | Original | Walking, Running |
| 1 | Bunny | `villager_bunny.glb` | 1,575 | None supplied | None |
| 2 | Giraffe | `villager_giraffe.glb` | 1,572 | 28 joints | None |
| 3 | Maine coon | `villager_mainecoon.glb` | 1,548 | 34 joints | None |
| 4 | Hamster | `villager_hamster.glb` | 1,562 | 25 joints | None |
| 5 | Cocker spaniel | `villager_cockerspaniel.glb` | 1,574 | 42 joints | None |

Slots stay stable so existing saves keep their inventories, houses and progress.
Former geckos become bunnies; former prairie dogs become hamsters. Player creation
and residents both use the replacement model table. Old animated GLBs remain in
source history/reference assets but are not imported into the game disc.
Stale generated gecko/prairie model files are moved out of disc staging by the
build script. A clean Linux build imports only the current six villagers.

All five new files are used as supplied, retaining their UVs, materials and
available skinning data. Normalization preserves their bind pose and places the
feet at ground level. Texture import defaults native images to 256 pixels, with explicit 128/256/512/1024 variants. Because
the replacement files use metallic/roughness PBR materials, their base-color
maps use the SDK's explicit diffuse approximation on GX; normal maps and PBR
metallic/roughness response are omitted rather than requiring a programmable shader.
The new files contain no clips, so they move through the world in their authored
pose; the fox retains walking/running animation. Their `Bone_###` rigs differ
from the old Mixamo rigs, so the old clips cannot simply be copied by bone name.
Their bind pose is evaluated at instance creation; static villagers skip the
otherwise redundant animation/skinning update each simulation tick.

To restore animation, please supply these same five meshes with in-place
`Walking` and `Running` clips, with no root translation, on each mesh's own rig.
The bunny needs skin weights and a rig as well. Keep the current silhouettes,
roughly 1,600-triangle budget, one primary material and one 128-square texture;
24–30 fps source clips are sufficient. An optional `Idle` clip would also help.
