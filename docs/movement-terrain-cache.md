# Moving without rebuilding the landscape

The previous renderer keyed its floor, water and cliff caches on exact player
coordinates. A fractional move rebuilt the visible geometry and uploaded it.
A published-build Dolphin capture measured median terrain preparation at 74.5 ms
while moving, plus 15.7 ms animation work; the median frame interval was 116.7 ms.
Stationary frames reused meshes and took 16.6 ms. Those stationary results did
not establish walking performance.

## Terrain patches and camera motion

Terrain is cached as 8×8-cell patches in 96 slots. Floor, water, foam, river banks,
soil and cliff caps share the existing materials and texture ownership. Opaque
patch meshes are grouped by material with the SDK batch API; alpha foam keeps its
own pass. The app still consumes the pinned Hypercube/DolphinDotNet NuGet packages.

Each vertex is baked on the existing cylinder at focus Z=0. Moving the camera
changes a shared X rotation and translation, exactly reproducing the previous
projection. Camera X changes visibility, not vertex positions. Logical world UVs,
row chords, river/sea levels and cliff ground bindings are unchanged. Cached sine
and cosine values cover the bounded row range; water depth is calculated once
per shared corner and terrain revision rather than once per rebuilt vertex.

A patch is rebuilt when it is newly needed, its town/terrain revision changes,
its seasonal material revision changes, or the developer gallery toggles. No
camera coordinate is part of its content key. The gallery is anchored at its
activation position so walking around it does not move or rebuild its sections.
Indoor floors retain their separate house-tier cache and identity transform.

Visibility uses conservative patch spheres that include the terrain's -0.50 m
to 2 m range and gallery caps crossing an origin patch boundary. Required patches
are pinned. A bounded nearest guard band is prefetched one patch per frame, with
spare slots for turnover. An unbounded guard set would cause continuous eviction
and rebuilding even at rest. An LRU target of 3 MiB counts CPU vertex capacity and
native mesh bytes, excluding shared textures and bookkeeping. Prefetch stops at
85% of that target; unused chunks are trimmed when necessary. Visible chunks
stay pinned, while slot and per-patch geometry limits provide hard bounds.
Startup, inspection teleports and actual terrain edits can still require a cold
visible-set rebuild. Ordinary camera motion reuses warm geometry.

## Animation and simulation

Gameplay, movement, clocks, dialogue and villager navigation still tick at 30 Hz.
AnimationFrameClock accumulates those ticks and the renderer consumes their
elapsed time in one ModelInstance.Update call per actor per rendered frame.
Intermediate catch-up poses are not skinned. Clip changes discard time belonging
to the previous clip; offscreen villagers retain the existing frozen-time policy.
SDK Play/Stop controls still evaluate transition poses when a clip changes.
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
16 timing phases. Word 23 now stores patch builds in bits 0..7, explicit animation
update calls in bits 8..10, actual player movement in bit 12 and running in bit
13. It replaces the former rejected-cell field. The decoder remains compatible
with v1..v4 and labels stationary/walking/running separately; v5 cached/rebuilt
classification uses patch counts rather than a rounded preparation duration.
The movement check requires actual walking and running samples and p95 frame
intervals at or below 50 ms. This is a regression gate, not a hardware FPS claim.

Numerical checks compare the transformed terrain and shared-edge chords against
the old formula, verify conservative patch bounds and exact cell coverage, test
terrain-edit invalidation and test coalesced animation time. Dolphin results for
the final commit and downloadable build are recorded in the review PR. Physical
GameCube testing remains outstanding.
