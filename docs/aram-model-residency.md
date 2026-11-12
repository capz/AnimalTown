# Shared model residency

AnimalTown keeps gameplay, navigation, saves, transforms and animation intentions
in MEM1. `ModelResidency` automatically pages inactive **shared species models**
to ARAM after initial model loading and terrain baking. It uses SDK
`UnloadToAramAsync` and `PrepareAsync`; it does not destroy/recreate villagers or
change texture resolution. Buildings keep their existing compact resident kit.

## Admission and ownership

All instances of a model must be outside demand before it can be demoted. The
player's selected species stays pinned, including its NPC's shared resource.
Current-room species and doorway users remain pinned. Demotion only starts
while the player is indoors, has not moved for five seconds, and no player
doorway, UV inspection or water inspection is active. Each model also needs
five seconds without demand and three seconds of warm grace after restoration.
This avoids camera-driven eviction during ordinary outdoor travel.

The model backing budget is 4 MiB, including snapshots retained after restore.
Admission checks the rounded package size against that budget, the global ARAM
free space, the largest free block and allocation-slot count. At least 512 KiB
and eight allocator slots are left for other owners. No model demotion occurs
before the terrain's 8 MiB backing exists, preserving space for initial terrain
baking as well as the separate audio/platform reservation.

Only one model transition is owned at a time. Demanded models restore before
optional stores; actual drawable/destination users precede speculative prefetch.
Foreground demand cancels an unrelated hidden prefetch or an in-progress
demotion and drains its owner before another transition starts. If cancellation
arrives after the SDK's irreversible retirement commit, the complete cold result
is restored; no partially resident model becomes drawable.

Hot backing snapshots can be released to admit another cold model. A cold
model's only backing is never discarded. Disabling the internal manager restores
all models before releasing their ARAM copies. The terrain checkbox is separate
and defaults off; it does not disable model paging.

Returning to the title stops new admissions, cancels and drains the owned model
transition, and observes its terminal result before disposing instances and
shared models. This explicit teardown boundary keeps a manually driven transfer
from losing its progress owner while it still retains native or ARAM storage.

## Prefetch and scene gates

Moving indoors, approaching its exit, and requesting a doorway trigger outdoor
prefetch. The outdoor envelope expands the existing animated sphere by 24 m,
giving more than four seconds of maximum player sprint distance. This envelope
starts restoration; it does **not** block the current scene while a hidden actor
is still cold. The original drawable envelope separately gates native animation
and drawing. Entry/exit admission uses the exact outdoor destination coordinate,
including the one-metre landing offset, and doorway visual interpolation.

`PortalSystem.ResourcesReady` defaults true for existing callers. AnimalTown
checks destination resources before a door opens and again before the atomic
location commit. NPC transitions between invisible spaces need no rendering
resources. Sources and destinations remain distinct from saved location until
the normal commit tick. A failed prepare leaves the current scene and backing
intact and retries later; it cannot silently omit a visible model.

Final draw readiness is recomputed after input and effective debug camera
changes, even on frames without a simulation tick. UV inspection waits for the
complete model set before invoking native topology code; water inspection pins
all species. Unpredictable teleports use the existing loading surface until
required models are ready. They do not call native model APIs on cold resources.

## Bounded work and evidence

Models disable automatic residency advancement and receive explicit work credit
before and after drawing. Both opportunities share a one-millisecond **soft CPU
budget**, including terrain residency work; each terrain call receives at most
500 microseconds. A model callback uses at most 64 KiB and 256 native work items.
Polling stops when DMA is pending. The frame's preparation profiler includes
this work rather than excluding it from the measured frame.

An individual native allocation or work item cannot be interrupted. The manager
records total advance/admission CPU time, maximum indivisible advance/admission
time, work steps, logical package bytes copied/restored, demotions/restores,
cancellations, retry failures, loading frames and doorway wait ticks. Cold/warm
debug log markers report species identity, ARAM backing and the observed maximum
advance duration; native budget charges are not claims about total physical
MEM1. ARAM transfer statistics remain necessary to measure actual bus traffic.

Portable source-linked tests exercise asynchronous ownership and controlled DMA
completion, both cancellation boundaries, admission/fragmentation/slot limits,
restore retries, snapshot release, foreground priority, zero-allocation warm
polling, teardown during pending snapshot/restore DMA, and preserved
model/animation identity. Domain tests cover shared player
ownership, doorway commit/save semantics, visible-actor coverage, hidden prefetch
without scene stalls, exact landing focus and late camera/doorway view changes.

Run `tools/test-changes.ps1` for routed checks. The focused managed suites are:

```sh
dotnet run --project tests/AnimalTown.Checks -c Release -- --suites buildings,terrain,saves
```

Full game emulator checks must additionally confirm rendering, ordinary doorway
latency, matched frame intervals, audio and cancellation under DMA contention.
Hardware qualification is required before claiming unchanged stock-console
performance.

## Actual asset allocation verification

The [native cohort report](measurements/aram/current-model-cohort.json) uses the
six current species GLBs cooked with Assets 328, the normal preparation/winding
steps and two mip levels. It runs production SDK source `d53a6589` with six shared
models and seven instances, keeping a fox player and its fox NPC warm while the
other five species demote and restore. The default quality remains 256 pixels;
128 pixels is only a comparison cohort.

| Texture edge | Five cold packages in ARAM | Warm native requests | Cold native requests | Native request bytes freed |
| --- | ---: | ---: | ---: | ---: |
| 256 (default) | 3,233,472 B | 6,803,872 B | 1,071,488 B | **5,732,384 B** |
| 128 (comparison) | 1,513,152 B | 2,871,712 B | 579,968 B | **2,291,744 B** |

These figures intercept actual production `calloc`, `aligned_alloc` and `free`
calls rather than summing per-model budget charges. Twenty underlying native
allocations are freed in each cold cohort. Peak native allocation requests stay
only 256 B above the fully warm set during these sequential transitions. The
test includes the game's 2 MiB audio reservation and 8 MiB terrain ARAM
allocation; the five snapshots fit the 4 MiB model backing budget.

Every restored package, material table, runtime texture copy, skin output,
retained pose/global matrix and animation state matches its warm expectation.
Every native test-owned model allocation is released at final teardown. Each
step stays within 4,096 copied logical bytes and 32 native work items.

This is a **host native allocation measurement**, not a whole-game MEM1 or
stock-console performance trace. Input/expectation buffers, simulated external
ARAM, native static storage and the managed heap are excluded. The report also
records host allocator usable sizes separately; those vary by allocator and
must not be presented as GameCube memory totals. These are conditional maximum
savings for the stated idle species set, not a guarantee that ordinary play
reaches that set. CPU parity does not verify GX cache behavior or rendering.

[Reproduction instructions and the verifier](measurements/aram/README.md)
preserve converter/package hashes and the compiled native source snapshot.
