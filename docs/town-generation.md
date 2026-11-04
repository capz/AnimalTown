# Seeded acre-based town generation

Current update: [raised sections, water coverage, villagers, clipping and clock](terrain-villager-update.md). The description below records the initial flat generator; the update documents v2/v5 changes.

The GameCube generator is a constrained, staged acre assembler. Research reference:
ACreTeam/ac-decomp commit `e4926f73b0c8097b91a149c1b9c068901985bedb`:

* [River tracing, required features, heights and acre selection](https://github.com/ACreTeam/ac-decomp/blob/e4926f73b0c8097b91a149c1b9c068901985bedb/src/game/m_random_field_ovl.c)
* [River continuation and travel-gate lookup tables](https://github.com/ACreTeam/ac-decomp/blob/e4926f73b0c8097b91a149c1b9c068901985bedb/src/game/m_random_field.c)
* [Original 5x6 playable acres with 16x16 cells](https://github.com/ACreTeam/ac-decomp/blob/e4926f73b0c8097b91a149c1b9c068901985bedb/include/m_field_make.h)

The original first chooses a two-/three-tier scheme, forms cliffs and a river,
assigns crossings and landmarks, builds acre heights and selects actual compatible
acre variants. Its river tracer chooses allowed continuations and checks downstream
feasibility; invalid configurations are retried. Travel gates and river-flow types
are separate data, not interchangeable concepts.

## Implemented adaptation

AnimalTown retains **8x8 screen acres, each 20x20 fine cells**, and its cylindrical
display. `TownGenerator` implements this staged template approach for features the
current flat terrain system supports. It is not a binary port, original seed/RNG
reproduction, or Nintendo's acre library. No Nintendo map/model/texture data ships.

1. Choose shoreline templates with shared edge heights and shallow bay variants.
2. Trace a north-to-south river through compatible north/east/south/west sockets.
   The path descends or moves sideways without revisiting acres. Authored straight,
   bent and horizontal variants retain three-cell-wide boundary sockets. Allowed
   continuations preserve the existing northwest service area.
3. Assign two mandatory straight-river crossing acres, one in each map half.
   Temporary crossing art is the existing repeating wooden floor, with real
   walkable collision and clear land approaches on both banks.
4. Assign two or three pond templates to otherwise-flat inland acres, inset from
   acre seams and separated from the river.
5. Retain service entrances, resident-house anchors, plaza, camp and starter plot.
   These are required acre constraints in the current template set. Moving services
   requires data-driven entrance/render/save positions first.
6. Populate 320 trees from an independent seeded stream. Trees avoid the starter
   reserve, acre travel corridors, water, structures and neighbouring trees.
   Place two shells on actual beach cells away from the river mouth.

Construction rules guarantee the river reaches the sea and reserve the crossing
acres, so this reduced template set needs no unbounded retry loop. Ponds use a
bounded permutation of candidate acres; vegetation uses bounded attempts.
Generation is startup-only. The existing renderer, cylindrical parenting, culling,
shared tree meshes and rendering limits remain in use.

`GameState(seed)` stays deterministic on client/server. The server already uses
random seeds. The console mixes its timing clock and RTC calendar for fresh towns
instead of always using 7319. Seed, terrain and ground objects remain in v4 saves;
decoded terrain is loaded intact, not regenerated. Generation refuses a played
town so it cannot silently reroll a saved world. Legacy migrations still pass.

## Tests and developer previews

Tests sweep 68 seeds including zero, negatives and signed limits. They flood-fill
all playable land with tree collision enabled, check residents and required
entrances, bounded crossings, river/ocean connectivity, separate ponds, terrain
diversity, exact terrain/vegetation reproduction, object budgets, regeneration
guards, save round trips and preservation of edited terrain.

```powershell
dotnet run --project tools/AnimalTown.Tools -c Release -- town-preview . out/town-previews 7319 4
```

This writes four 640x640 PNG maps and `generation-audit.json` containing seeds,
generator version, dimensions and terrain SHA-256 identities. Grid lines mark
acres; dark green marks trees, blue is water, brown crossings are bridges. Service
footprints/plaza/camp use brown/cream. The pale dotted rectangle is the starter
reserve; the white point is arrival.

## Missing for full terraced towns

The renderer/collision system currently has one ground height. `Terrain.Cliff`
is a blocking floor category, not an elevation layer. Full two-/three-tier tracing
needs height data, slope traversal, cliff faces, waterfall joins and coordinated
render/culling/collision/save support. These are implementation tasks.

Original art still needed (see [world-asset-requests.md](world-asset-requests.md)):

| Asset | Required pieces / dimensions |
| --- | --- |
| Wooden bridge | Along +X, 6 m span x 2 m width; bank sockets at +/-3 m, Y=0; clear central walk lane. |
| Riverbank/pond kit | 4 m straights, 2x2 m inner/outer corners and pond joins; waterline Y=0, ground lip Y=0.3 m. |
| Natural ramp | 2 m wide, 6 m run, 2 m rise; lower pivot (0,0,0), upper socket (0,2,6). |
| Cliff kit | 4 m straight x 2 m high, matching 2x2 m inner/outer corners, aligned grass lip and seam vertices. |
| Waterfall | 2 m wide, 2 m drop; lip, rock cheeks and separate opaque water strip joining cliff and riverbank. |
| Shore kit | 4 m sand/water straights and matching 2x2 m corners; one low beach-rock variant. |

Deliver triangulated GLB, metres, +Y up, applied transforms, ground-aligned pivots,
one UV set and embedded base colour. Prefer 100-500 triangles per terrain piece,
600-1200 for a bridge, one shared atlas per kit with default maximum 256 pixels.
Include named sockets and collision/footprint notes. Keep to natural GameCube/3DS
ramps, bridges and terrain, without New Horizons construction/terraforming scope.
Flat generation works without these meshes; banks and crossings currently use
existing floor materials.
