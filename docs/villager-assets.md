# Current villager assets

## Four additional appearances supplied on 2026-11-27

| Appearance | Prepared source | Source archive prefix |
|---|---|---|
| Panda | `villager_panda.glb` | `b8428be7` |
| Pig | `villager_pig.glb` | `faf2e7ad` |
| Axolotl | `villager_axolotl.glb` | `74687edf` |
| Fox in green jacket | `villager_foxjacket.glb` | `c9c5fd54` |

Each archive splits one character's animation exports across two GLBs. Their
meshes, nodes and skins match exactly. The imports append appearance IDs
14–17, preserving all existing appearance and saved species IDs.
Every embedded map is reduced to at most 128×128. Villager cooking now caps
both runtime package variants at 128px, and the texture audit rejects larger
villager maps, including in packages whose compatibility filename denotes
the 256 variant. Unreferenced high-resolution sidecar images stay in the
original download archives and are not imported or packaged.
The axolotl supplies three joint/weight sets; all twelve influences are
combined before retaining and normalizing each vertex's strongest four.

Panda, axolotl and the jacket fox retain their supplied idle, walking and
running clips. The pig's custom export still runs its legs; it is retained
as `RunningInPlace` in the prepared source. Its gameplay `Idle` is a static
standing pose using bind hips/legs and the relaxed upper-body pose from its
own walk. Every animated TRS channel is keyed to clear prior locomotion.
`idleDerivedFromStandingPose` records this fallback in the provenance.
The thirteen real Dolphin swaps cover all twelve added appearances and an NPC.

```sh
python3 tools/import-villagers.py /path/to/downloads /path/to/AnimalTown --only panda pig axolotl foxjacket
```

## Additional appearances supplied on 2026-11-27

`base_basic_shaded (2).glb` is a crocodile in an orange tropical shirt;
`base_basic_shaded (3).glb` is a capybara in a blue wave-pattern shirt.
They are prepared as `villager_crocodile.glb` and `villager_capybara.glb`.
Both contain their own authored running (`human_000029`) and walking
(`human_000089`) clips. Crocodile uses `human_000093` for Idle; capybara
uses `human_000013`. Their rigs, topology and UVs follow the same import
policy below, with 128px embedded maps and horizontal locomotion in place.

The resupplied `base_basic_shaded (1).glb` is byte-identical to the existing
grumpy pug officer and adds no duplicate. The fourteen appearance IDs retain
all twelve previous entries, appending crocodile and capybara at 12 and 13.
The six saved species IDs remain unchanged.

Import just these additions without requiring the older source archives:

```sh
python3 tools/import-villagers.py /path/to/downloads /path/to/AnimalTown --only crocodile capybara
```

`--only` preserves the other prepared GLBs and their provenance records.

## Additional appearances supplied on 2026-11-26

Five distinct characters are present in the seven supplied ZIPs. The two
`4073a55c` archives are byte-identical. The `f8e7f757` GLB and USDZ exports
represent the same raccoon. The `adb5cd87` and `1f2f9207` archives each split
one character's animations across two exports. The additional supplied
`base_basic_shaded (1).glb` provides the sixth character, a grumpy pug police officer.

| Appearance | Prepared source | Source archive prefix |
|---|---|---|
| Penguin | `villager_penguin.glb` | `4c8507cd` |
| White tiger | `villager_whitetiger.glb` | `4073a55c` |
| Lion | `villager_lion.glb` | `adb5cd87` |
| Robin | `villager_robin.glb` | `1f2f9207` |
| Raccoon | `villager_raccoon.glb` | `f8e7f757 (1)` |
| Grumpy pug officer | `villager_pugofficer.glb` | `base_basic_shaded (1).glb` |

Each prepared GLB has `Idle`, `Walking` and `Running` clips on its own rig.
The officer's source also contains bow and sit clips, retained in its GLB.
No walk was supplied for the officer, so its `Walking` clip is a half-speed
copy of its own run; the three runtime clips use its own rig.
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
