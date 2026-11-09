# Buildings, interiors and villager journeys

The cottage kit and its shared doorway system run on DolphinDotNet/hypercube-engine.
Both player and NPC transitions use `PortalSystem`; rendering never owns actor
locations. Source assets are original generated low-poly geometry. Supplied shop,
tent and furnishing models remain intact.

## Play

Approach a front doormat and press A when **Enter** appears. Indoors, approach the
visible exit and press A at **Exit**. The door opens, the actor walks through its
threshold and the player view fades briefly. NPCs animate the local doorway
without fading the player's view. The doorway is reserved until its actor clears;
opposite-direction arrivals have separate landing points and queue safely.

C-stick left/right turns the indoor view by 90 degrees. Release the stick before
another turn. L recentres it. Movement uses the current interpolated camera basis.
Camera-facing walls are hidden; interiors have no ceiling. Visibility uses the
indoor camera, not the outdoor cylinder's fixed frustum. Outdoors retains its
existing camera and curvature.

Villagers visit homes independently of the player, navigate around furnishings,
wait at activity spots, leave, and resume their saved outdoor waypoint. All six
continue their journeys while the player visits a house. An inaccessible visit
is abandoned after bounded stalled time; long walks are not timed out while the
actor is making progress. New furniture cannot obstruct doorway clearance.
Existing saved possessions are retained even if they overlap a new doorway.

The developer chord L+R+A focuses successive resident entrances outside. Indoors
it focuses that room's exit. It never performs a portal transfer. This is also
available from the title for focused emulator scenarios. Normal entry and exit
still require A and run through the same validation and animation.

## Assets and replacement workflow

```
assets/definitions/buildings/cottage_small.json
assets/definitions/interiors/cottage_small.json
assets/models/buildings/cottage_small/{exterior,door}.glb
assets/models/interiors/cottage_small/{floor,north,east,south,west,door}.glb
assets/textures/buildings/cottage_small/cottage-atlas.png
```

Building definitions reference interior definitions independently. Definitions
compile by explicit numeric type, with cottage_small occupying stable type 0.
Append new contiguous type IDs after existing IDs; do not rename/reorder shipped IDs because
saves retain per-building type indices. Keep existing files as compatibility
aliases when replacing art. Runtime model aliases are defined by the prefix.

Add a building JSON and a referenced interior JSON, assign a unique prefix and
provide all seven static model parts. `buildings ROOT` validates/cooks the catalog
and outputs `out/building-assets.json` plus `out/disc/buildings.bin`. The build
imports all listed models automatically. No changes to doors or NPC logic are
required for a compatible new model. `buildings-generate ROOT` explicitly rebuilds
the original starter artwork; normal builds never regenerate artist sources.

The current town reserves 3x3-metre house footprints: exteriors must fit that
collision frame (small roof overhangs allowed). Author in metres, +Y up, +Z toward
the front, lower-left floor pivot at zero. The separate door's pivot is its lower
hinge, with the panel extending +X. Exterior metadata supplies hinge and approach
coordinates; approaches face +Z and lie outside the footprint. Models with larger
footprints need a future town-placement/portal-orientation extension, not a silent
scale change to collision.

Interior shell parts use a normalized 4x4 frame and scale in X/Z to the definition's
4..16-metre floor size. Walls remain 2.8m tall. The +Z wall contains a centred door
opening. Metadata fixtures use actual room metres, not normalized shell coordinates.
Up to 16 fixture boxes can be declared: kind 0 is collision-only for furnishings
already in the artist mesh, kind 1 draws a simple bed, kind 2 a writing desk.
Built-in fixtures are used in resident homes; player rooms retain their own saved
furniture. The player home retains its 4/6/8m expansion sizes and old room keys.

The host importer validates source confinement, identifiers, static geometry,
triangle/material limits, bounds, normals, UVs, fixture bounds and exit clearance.
Generated source GLBs embed the same 128px palette. The native renderer loads the
smallest compiled variants, with 64px textures and a 32px mip; larger variants
retain the author's resolution. Variant suffixes specify a maximum texture size.
The ISO verifier requires the catalog and every part.
Source GLBs retain standard counterclockwise winding and outward normals. The
build converts freshly imported package indices to the game's clockwise GX
submission convention. Native checks verify that winding without changing normals.

## Domain and persistence

Space 0 is town; 1..4 remain player rooms; 16..21 are six distinct resident homes;
24/25 are shop/post interiors. `BuildingWorld` maps stable building instances to
these spaces and current town placement. Furniture is keyed by space and owner,
so houses sharing one mesh do not share possessions. A room need not be rendered
to exist or host an NPC.

`VillagerJourney` stores visit/activity/dwell/exit/resume intentions above the
existing outdoor planner. `InteriorNavigation` performs local half-metre-grid
searches and dynamic occupancy checks. Player movement uses the same interior
furniture/occupancy collision. `PortalSystem` commits location once, at tick 18 of
a 36-tick transition; visual interpolation does not alter saved coordinates.

Combined saves now write version 8; split records write version 4.
The appended villager encounter history is described in [villager attention](villager-attention.md). Old saves remain
readable and give actors outdoor locations. New records persist player/villager
spaces, facing, building types, journey phases, dwell/stall timers and the next
outdoor waypoint. Paths, camera interpolation, door reservations and animations
are rebuilt. Before the commit tick a save contains the source; afterwards it
contains the destination. Unknown building art retains its type and possessions;
occupants recover to a safe exterior rather than remain in an unavailable room.

## Resource bounds and validation

There are seven actors, nine building instances and at most four shared building
art definitions. Native building resources have a combined 768 KiB residency
ceiling, a 256 KiB model-load ceiling and a 16 KiB instance ceiling per part.
The build loads every cooked native part through the production C model loader,
creates all static instances together, and enforces these budgets before linking.
Budget accounting includes retained packages and decoded texture charges, even
when the immutable texture cache shares the same atlas across parts.
One asset task runs at a time; the loading screen stays responsive between parts.
Meshes/poses are allocated before gameplay rendering; there is no room-entry allocation
or per-frame model/texture creation. A room change reuses the same kit.

Indoor search has one 1024-cell parent/queue scratch pair, seven bounded 512-step
routes and at most 128 expansions per tick across all occupants. Outdoor planning
retains its separate existing 128-expansion limit. NPC rooms require logical state
only; invisible occupants are neither rendered nor skinned. No pathfinding state
is serialized or duplicated per house.

Run `tools/test-changes.ps1`; buildings have explicit managed/tooling routing.
Host checks cover portal saves on both sides of commit, occupancy, exit, furniture,
indoor path budgets, camera bases/culling, migration and all six NPC round trips
while the player remains indoors. `tools/iso-smoke.py ISO OUTPUT --buildings
--dsp-hle` captures native exteriors, door cues, interiors, camera turns, indoor
save and exit. `--building-reload` checks the saved interior using the same card.
Physical GameCube loading, visual quality and long-session memory behavior still
require target-hardware qualification; Dolphin evidence is not hardware proof.
