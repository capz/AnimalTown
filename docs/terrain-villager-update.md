# Raised terrain and village life

The regular/9-slice transition request is deferred at the user's request until
the separate regular texture pack is available. Existing floor materials remain.

## Terrain

Generator v2 produces 20-30% water cells with an expanded southern ocean, a
connected river and ponds. Bridges count as land. Two crossings remain inland.
The 68-seed sweep enforces coverage and connected playable land. Existing saved
terrain is retained; the new ratio and raised sections apply to new towns.

Three compatible acres receive 2-metre raised sections and matching ramps.
The [cube/ramp source GLBs](../assets/models/terrain/README.md) use 2x2x2m bounds.
Native geometry batches exposed sides and uses the regular floor for tops;
internal faces disappear. Side/floor corners use identical cylinder coordinates.
Player travel samples continuous height: vertical faces block, ramps traverse.
Objects and trees follow height; the camera follows the player's ground height.

Clipping uses actual camera elevation and current curvature, a wider/longer
terrain envelope and two-cell viewport margins. Raised tile bounds include their
height range. CPU culling retains partial intersections for GX viewport clipping.
Ground is bounded to 2048 strips; the expanded checkerboard sweep retains at
most 876 in current tests. GPU backface culling remains enabled. Horizon culling
uses observer height and a conservative object margin.

## Villagers

Each model slot receives a unique name generated from town seed and model ID.
The actual names are saved, so later generator changes do not rename residents.
Pre-v5 saves receive generated names once when migrated. Home anchors are now
separate from walking positions, preventing moving villagers from moving houses.

Destinations include shops, other homes, beach, a chosen terrain type and land
beside water for future fishing. Activities remain out of scope. Initial category
assignments are seed-shuffled; subsequent goals are independent random choices.
Villagers face travel, animate while moving/visible, and pause under menus/dialogue.

One shared incremental BFS expands at most 128 terrain nodes per tick. Initial
routes account for water/elevation while ignoring trees, buildings and crowds.
Only when an obstacle enters a 1.2m look-ahead does a 13x13 local detour search run.
Trees, buildings, outdoor furniture, the player's house and nearby people block
local movement. If that fails, newly observed static obstacles enter a compact
per-agent bitset and the incremental planner retries the journey. Crowds yield
and retry; people are never permanently recorded as walls. Navigation allocates
no route arrays or strings per tick and is timed as simulation, not skinning.

## Clock and saves

Developer inspection: hold L+R and press START outdoors to move to the foot of
the first generated ramp. This changes only the test player's position; it does
not regenerate terrain. Old flat saves have no inspection target. The ISO smoke
test's `--terrain-life` mode uses this chord to capture low/high ramp views and
saved positions, alongside clock and wandering-villager screenshots.

The outdoor HUD displays console-local date, weekday and 24-hour time using the
GameCube RTC through the existing native bridge. It samples once a second and
reuses text until the minute/date changes. Menus/dialogue hide the clock.

The [split storage format](split-saves.md) now persists town, house positions,
shops and player separately. Its legacy import/export format, v5, appends RLE elevation, saved model-tied names, fixed homes and fractional
NPC coordinates. v1-v4 remain readable and retain terrain/progress. Older maps
remain flat rather than being rerolled. New water ratios/raised sections apply
to new towns; navigation and the clock work with existing towns as well.

Tests cover migration/round trips, 68 seeds, ramp/cliff traversal, clock caching,
6000 navigation ticks, collision-free travel, fixed homes, goal diversity and
bounded work. Physical hardware validation remains outstanding.
