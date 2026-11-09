# Moving without rebuilding the landscape

The previous renderer keyed its floor, water and cliff caches on exact player
coordinates. A fractional move rebuilt the visible geometry and uploaded it.
A published-build Dolphin capture measured median terrain preparation at 74.5 ms
while moving, plus 15.7 ms animation work; the median frame interval was 116.7 ms.
Stationary frames reused meshes and took 16.6 ms. Those stationary results did
not establish walking performance.

## Streamed sections and camera motion

The active renderer uses 8×4-cell sections in 256 bounded slots. Floor, water,
foam, river banks, soil and cliff caps share the existing materials and texture
ownership. A shared cylinder rotation and translation move cached geometry;
logical world UVs and row chords remain aligned. Visibility selection follows
half-metre camera buckets with a guard band and retains partial sections while they build. Visible work can preempt an
offscreen build and resume it later; prefetch follows the direction of travel.

New sections target a shared two-millisecond/eight-stage background
budget. One terrain stage is guaranteed even if masks consumed the deadline,
preventing sustained prefetch starvation. Initial loading and cold teleports warm the full twelve-metre prefetch
band. Once presented, ordinary travel keeps input, simulation and world drawing
active even when background work is pending. It never reenters landscape loading
because a tile or section boundary was crossed. Recovery reserves up to eleven
stages per unfinished visible section, including upload, before drawing if
prefetch falls behind. This work-derived bound may exceed the background time
target; a fixed sixteen-stage cutoff left one gap in a native diagonal route.
Terrain/town/material changes reset the stream. Flat dry ground and deep ocean use four row stages and three publication stages, retaining the original row
chords and UVs. Other sections generate ground and water together per row and
skip wall rows proven to have no exposed edges. All sections share cached
cylinder rows. Deep-ocean strips merge only where every depth sample is fully
deep and no shore, foam or river mouth can exist. The cliff gallery remains separate.
See [render performance](render-performance.md) for the active stream details.
The earlier 8×8 patch renderer is superseded; its numerical reference helpers
remain covered by the movement rendering checks.

The prefetch pool shares native handles with models, textures, UI and masks.
The game-owned native patch raises that bounded table from 512 to 768 slots
(about 35 KiB of additional static storage), retaining the SDK's 16 MiB byte
budget and ten-bit token format. A native fixture checks full-table rejection,
unique handles, generation-safe reuse and pinned release during console builds.

## Animation and simulation

Gameplay, movement, clocks, dialogue and villager navigation still tick at 30 Hz.
AnimationFrameClock accumulates those ticks and the renderer consumes their
elapsed time in one ModelInstance.Update call per actor per rendered frame.
Intermediate catch-up poses are not skinned. Clip changes discard time belonging
to the previous clip; offscreen villagers retain the existing frozen-time policy.
The initial resident walking pose is retained until its first navigation transition. SDK Play/Stop controls still evaluate transition poses when a clip changes.
Skinning is now outside the measured simulation phase, so it is not subtracted
from that phase again. Terrain, collision and save codecs are unchanged.

## Continuous movement validation

Run the same copied regression card and ISO for each comparison:

```sh
docker run --rm --init --shm-size=512m \
  --mount type=bind,src="$PWD",dst=/work -w /work animaltown-dolphin \
  python3 tools/iso-smoke.py out/AnimalTown-VERSION.iso out/movement \
  --movement-profile --profile-seconds 8
python3 tools/read-frame-profile.py out/movement/user/Dump/Frames/*.avi --check-movement
```

The scenario records stationary, east/west, north/south, diagonal running and
ramp movement, then saves. Telemetry v5 keeps the 32-word checksummed layout and
16 timing phases. Word 23 now stores streamed build stages in bits 0..7, explicit animation
update calls in bits 8..10, actual player movement in bit 12 and running in bit
13. It replaces the former rejected-cell field. The decoder remains compatible
with v1..v4 and labels stationary/walking/running separately; v5 cached/rebuilt
classification uses build-stage counts rather than a rounded preparation duration.
The movement check requires actual walking and running samples and p95 frame
intervals at or below 50 ms. This is a regression gate, not a hardware FPS claim.

Numerical checks compare the transformed terrain and shared-edge chords against
the old formula, verify conservative patch bounds and exact cell coverage, test
terrain-edit invalidation and test coalesced animation time. Dolphin results for
the final commit and downloadable build are recorded in the review PR. Physical
GameCube testing remains outstanding.

## No-loading travel regression

The frame-time decoder formerly skipped landscape-loading frames because the
loading panel omitted telemetry. The panel now draws telemetry too. Word 30
retains its scenario in bits 0..12; bit 13 marks bounded repair, bit 14 marks
missing visible terrain and bit 15 marks landscape loading. The decoder rejects
loading or missing terrain with `--check-streaming`.

Use a copy of an existing regression card and run a route without inspector
teleports:

```sh
python3 tools/iso-smoke.py out/AnimalTown-VERSION.nkit.iso out/travel \
  --stream-travel --profile-seconds 8 --season spring --dsp-hle
python3 tools/read-frame-profile.py out/travel/user/Dump/Frames/*.avi \
  --check-streaming --check-movement
```

The managed stress test traverses three generated towns at full run speed,
including diagonal travel and immediate reversals. It permits eight finer normal
stages per frame and no normal work every seventh frame. Bounded urgent repair
keeps all visible sections ready; separate checks prove the section bounds
include fractional camera motion, both eye heights, river bottoms and cliff
caps, and that skipped wall rows contain no exposed edge.
Another 720 full-speed diagonal frames deliberately allow no normal background
stages, including turns, and verify work-derived recovery still covers every section.
The emulator route also checks frame-counter progress after every direction;
a frozen framebuffer cannot pass simply by retaining a valid telemetry header.

## Allocation and recycling follow-up

The pinned indexed cache allocated a new native mesh on each changed commit.
Terrain ground now updates validated indexed payloads in place when native
capacity fits, retaining its handle; growth creates a replacement before releasing
the old mesh. Invalid indices, nonfinite vertices and active-frame updates leave
old content intact. Empty ground staging arrays survive slot recycling, while
unused native handles still release to respect the 768-slot bound. Ground, water
and wall publication use separate stages; dry/deep rows no longer generate four
rows in one indivisible call.

A game-owned generated-runtime patch maintains a real intrusive free-block chain
and append tail. Allocation no longer searches live blocks or rebuilds/coalesces
the whole heap. Collection retains the existing tracing/root rules, coalesces free
blocks and rebuilds the chain. Free links occupy unused payload; the heap adds one
tail pointer. It chooses the smallest fitting free block so small retained
objects do not consume the only contiguous space for town/save arrays. The
managed heap and native resource byte budgets are unchanged.
Fixtures cover rooted graphs, split/reuse/coalescing, reclaimed tails and GC stress.
The SDK checkout and package versions are unchanged.

Frame completion already waits for retrace. The default extra frame wait now
services runtime callbacks without allocating a one-shot timer/task. HUD version
text is retained and profiling labels refresh every thirty frames.

An optional checksummed diagnostic bar at y=432..447 supplements v5. Sixteen
16-bit words contain magic 0x4144, matching frame ID, five 0.1ms phase timings
(selection, generation, publication, material animation, native masks), allocation
count, packed free-search count (low 12 bits)/GC count (high 4 bits), packed texture
operations (low byte)/shadow-page updates (high byte), mesh creates/updates/releases,
staging growths, recycled sections and XOR checksum. Counts saturate to their
fields. The decoder accepts older captures without this bar. Diagnostic timing
separates UV/material updates from texture payload operations and native mask work.

Use `--stream-travel --freeze-textures` to freeze UV/material animation, or
`--stream-travel --recreate-terrain-meshes` to force the old allocate/release policy
for ground. Both use the published ISO and copied regression card; the scenario
records its flags and frame progression in travel-scenario.json. Developer chords
L+R+Z+DpadDown and L+R+Z+X toggle these controls. Normal play uses motion and mesh
reuse. Published-build comparison results are recorded in the PR.
