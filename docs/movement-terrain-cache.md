# Moving without rebuilding the landscape

The previous renderer keyed its floor, water and cliff caches on exact player
coordinates. A fractional move rebuilt the visible geometry and uploaded it.
A published-build Dolphin capture measured median terrain preparation at 74.5 ms
while moving, plus 15.7 ms animation work; the median frame interval was 116.7 ms.
Stationary frames reused meshes and took 16.6 ms. Those stationary results did
not establish walking performance.

## Streamed sections and camera motion

The active renderer uses 8×4-cell sections in 160 bounded slots. Floor, water,
foam, river banks, soil and cliff caps share the existing materials and texture
ownership. A shared cylinder rotation and translation move cached geometry;
logical world UVs and row chords remain aligned. Visibility selection follows
half-metre camera buckets with a guard band and pins sections while they build.

New sections advance in row stages under a two-millisecond/eight-stage frame
budget. Initial loading and cold teleports show landscape progress until visible
sections are ready. Terrain/town/material changes reset the stream. Shore depth
and row poses are reused within each section. The cliff gallery remains separate.
See [render performance](render-performance.md) for the active stream details.
The earlier 8×8 patch renderer is superseded; its numerical reference helpers
remain covered by the movement rendering checks.

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
