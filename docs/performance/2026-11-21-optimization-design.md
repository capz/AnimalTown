# LOD, streaming, culling and hardware profiling: audit and implementation design

Audit baseline: AnimalTown main **f5cc386df683827899bfab62479488603a0ecc92**,
published as **0.82.0**. The consumed SDK is **0.1.0-preview.376.1** and
Hypercube is **0.1.0-preview.52.1**. This document is a design; it does not
change runtime behavior. The subsequent
[profiling and optional terrain prefetch implementation](2026-11-21-profiling-prefetch.md)
records the implemented subset and its remaining qualification work.

The accompanying [audit data](2026-11-21-optimization-audit.json) records
fresh measurements from the published Depot package and its SHA-256.
These are package/payload measurements, not measurements of live console RAM.
Older Dolphin and host-allocation results are useful regression evidence, but
are not a performance baseline for this revision on original hardware.

## Current coverage

| Area | Present in the game | Relevant next work |
| --- | --- | --- |
| Asset reduction and LOD | Villagers default to 128px; 256px variants are allowed; 512/1024px villager variants are pruned. Four generated tree kinds share meshes of at most 96 triangles and one 1,440-byte detail texture. Ground has mip controls and a coarse-first load. | Make uploaded mip policy agree with intended ground detail; publish a reachable-asset manifest; add animated mesh LOD only after measured justification. |
| Bounded full-town streaming | Full immutable terrain archive in ARAM; fixed 1 MiB MEM1 cache and 256 mesh-view handles. Shared species models page to ARAM with demand/pinning, cancellation, priorities and a 4 MiB backing limit. Paths load on demand. | Budget ordinary warm terrain cache misses; coordinate admission and transient memory across resource classes; test a route covering the whole town. |
| Animated-instance culling | One render-time actor visibility decision covers room, camera frustum, curved horizon, door interpolation, fade and cold resources. Hidden actors skip native animation controls, pose updates and body submission. NPC simulation continues. | Extend evidence to newer outfits/effects; measure false-positive bounds and particle work; consider far-pose throttling before adding another model tier. |
| Telemetry and hardware profiling | FrameProfile v5, managed-allocation/graphics-operation counters, checksummed video telemetry, SDK FPS/MAIN/ARAM overlay, model-transfer counters and native GX counter APIs. | Counter-only collection, long-session-safe identities, owner/high-water memory accounting, safe GX metric ownership, reproducible physical-console qualification. |

## Evidence and concrete gaps

### Assets: already reduced, with a small ground-mip inconsistency

The six published default villager packages contain **12,425 vertices and
9,895 triangles** in total, with **8 textures**. Their texture payloads total
**655,360 bytes** at 128px versus **2,621,440 bytes** at 256px. The reduction is
**1,966,080 bytes per complete texture copy**. The default model packages total
**1,846,048 bytes**. Runtime model packages, shared GX textures and instance
buffers have different ownership, so these numbers cannot simply be added to
one another or to historical ARAM savings.

The published neutral ground source contains seven CMPR levels and 43,712 pixel
bytes. Its 64px preview contains five levels and 2,752 bytes. However,
[TextureCache.DecodeFloor](../../game/AnimalTown.GameCube/TextureCache.cs#L132)
calls [FloorTexture.Prepare](../../shared/AnimalTown.Domain/FloorTexture.cs#L8)
without requesting a full chain. The published texture audit consequently
reports only **two uploaded levels**: 40,960 bytes for the full ground and 2,560
bytes for the preview. The audit is consistent with the loader; the older
full-chain documentation is not.

Recommendation: explicitly request full mip chains for ordinary ground,
including its caps/lips, and keep soil/model/path policies explicit and
independent. This restores the intended distance controls at a cost of
**2,752 additional native pixel bytes** for the full neutral ground
(**2,944 bytes** while its preview also exists). Include mip policy in the
cache key and make the cooker, loader, diagnostic bands and audit agree.
Sampling a lower mip does not release its higher-resolution storage.

The package also contains generic building/tent variants and non-128px
cottage/shop variants totaling **24,196,544 uncompressed bytes**. These are
manifest-pruning candidates, not established live RAM savings. Prove their
reachability against all shipped modes, building definitions and supported
quality options before excluding them. Preserve artist sources. Update the
hard-coded variant expectations in TextureAudit along with the build manifest.

### Streaming: memory bounds exist; a normal-travel stall path remains

The logical town is 128 by 120 cells: 7 by 6 map acres, with 20-cell acres and a
partial eastern column. The terrain archive also covers the camera's ocean
envelope. It is already a full-town system.

[TerrainRenderer.PrepareOutdoorResidency](../../game/AnimalTown.GameCube/TerrainRenderer.cs#L55)
short-circuits when the current archive is presentable. A later movement-driven
selection can therefore reach the synchronous branch of
[TerrainArchive.PrepareSelection](../../game/AnimalTown.GameCube/TerrainArchive.cs#L140).
[NativeTerrain.Bind](../../tools/native-terrain-archive.inc#L209) calls a DMA
helper that polls until completion on a cache miss. This path is outside the
one-millisecond shared model/cold-terrain residency credit.

The asynchronous BindStep machinery already exists, including resource holds,
cancellation and publication after completion. Cold resume uses it. This is a
specific opportunity to improve normal travel, not evidence that it causes
every reported hitch. Measure hit/miss counts and durations before attributing
current frame spikes to it.

### Culling: preserve the shared decision and its limitations

[ActorFrameVisibility](../../shared/AnimalTown.Domain/ActorFrameVisibility.cs)
is recomputed using the final camera before rendering.
[UpdateAnimationFrame](../../game/AnimalTown.GameCube/Program.cs#L822) gates
Play/Stop/speed and Update on that decision. AnimationFrameClock discards hidden
elapsed work, avoiding a large reappearance catch-up. New job outfits also use
actorVisibility. This feature should be maintained, not reimplemented.

The bounds are deliberately conservative. They do not prove occlusion by a
house. GX backface/depth rejection also does not eliminate CPU skinning for a
partly visible submitted model. Shadow demand must remain separate from body
visibility: an off-screen caster may still affect a visible receiver.

Rain/snow uses a bounded 64-drop stream, but still calculates every drop and
uploads degenerate vertices for hidden drops. Leaf/petal pools are bounded to
24 particles; visible-tree emission is gated, while existing live particles
still generate geometry. These are measured-work candidates rather than an
unbounded particle problem. Cloud shadows use a shared procedural mask and
global drift, with an existing night-time sleep path; there is no separate
animated model for each off-screen cloud.

### Profiling: good foundations, incomplete long-run evidence

[FrameProfile](../../game/AnimalTown.GameCube/FrameProfile.cs) records sixteen
CPU/submission/wait phases at 0.1 ms resolution, draw/material/texture counts,
animation updates and native detail counters. Its three video bands occupy the
bottom 64 canvas rows. The SDK overlay separately samples live MAIN/ARAM totals.
GPU wait measures blocking at completion, not the GPU's total execution time.

The frame sequence in the main, detail and submission packets is 16-bit. It
wraps after about **18.2 minutes at 60 presentations/s**.
[The decoder](../../tools/read-frame-profile.py#L62) keys detail and submission
tables by that sequence alone. Later data can overwrite earlier frames'
details; global tuple deduplication also cannot represent a session identity
reliably. Fix this before a long full-town soak.

The decoder currently labels results as guest-clock/emulator data even though
its pixel format can be captured from hardware. Add explicit platform and video
capture metadata instead of inferring the source from an AVI file.

RecordFrame also requires resourcesReady. Establishing a steady-play trace
therefore does not by itself capture cold-start allocation peaks. Start the
memory recorder before title/town construction, retain allocation high-water
counters across loading, and feed loading/transition checkpoints into the same
profiling session.

Existing host allocation reports and historical Dolphin captures document
specific builds and configurations. Several ARAM documents still describe
256px as the default; today's code and 0.82.0 package use 128px. Preserve the old
results as historical evidence and update the surrounding descriptions.
Original-hardware qualification for this baseline remains to be collected.

## Design 1: bounded telemetry and a hardware baseline

Introduce a profiling session with four modes: Off, Counters, Overlay and
Capture. Counters/Capture collection must not require rendering either overlay.
Use the existing phase hooks and native counters. Sample expensive system
memory queries at checkpoints or a low fixed rate; record their cost and age.
Keep phase nesting and the complete frame boundary explicit: the current loop
records after both residency opportunities and its task delay. Preserve that
coverage; overlapping phase durations must not be blindly added into a claimed
CPU total. An initial counter-only overhead target is below 0.2 ms at p95,
subject to hardware verification and a measured profiler-on/off comparison.

Use a versioned v6 schema shared by the recorder and decoder:

- Session identifier, 32-bit frame sequence, raw presentation interval in
  microseconds, explicit video refresh and sample/phase overflow flags.
- Existing phase timings, submissions and pose work, plus residency
  queue/admission/publication/cancellation events and synchronous-wait duration.
- Typed memory snapshots containing exact byte units, validity/ownership scope
  and timestamps. Associate every auxiliary packet with session plus sequence.
- CRC validation, explicit missing/dropped/repeated samples, and warmup/transition
  labels. Preserve v1-v5 decoding and qualify old captures' wrap limitations.
- Source commit, SDK/engine/converter versions, ISO/Depot hash, fixture hash,
  platform, storage device, video mode and enabled debug features in the report.

A proposed capture allocation ceiling is **24 KiB MEM1**, admitted only when
the required safety reserve remains available:

| Component | Initial design bound |
| --- | ---: |
| 256 packed frame records, at most 64 bytes each | 16 KiB |
| 64 event records, at most 16 bytes each | 1 KiB |
| Frame and phase histograms | At most 3.1 KiB |
| Snapshot/owner counters, session metadata and alignment | At most 1.9 KiB |
| Optional pixel packet texture and upload scratch | At most 2 KiB |

The rolling ring is not an unlimited trace. Record overwrite counts; use video
or an explicit checkpoint export for longer traces. Histograms must report
their quantization. Keep all frames for tail/transition reporting; disclose
exclusions rather than silently trimming hitches. Allocate no per-frame
objects or formatted log strings. Release capture buffers after cancellation
and outstanding users drain when capture/debug profiling is disabled. Existing
low-memory overlay/counters remain usable if capture admission fails.

### Memory ownership

Add a non-allocating SDK memory snapshot and fixed owner counters at existing
allocation/free sites. Reuse known allocation metadata instead of allocating a
second per-resource dictionary. Distinguish:

1. Physical MEM1, uncommitted arena and allocator free space.
2. Managed heap capacity, live bytes, peak and collection count.
3. Native graphics/model/package/instance/texture/buffer live and peak requests.
4. Terrain cache, staging, model backing, UI, audio and profiler ownership.
5. ARAM capacity, reserved/allocated/free bytes, largest free block, slots and
   actual DMA traffic/waits.

Do not count a shared texture once per model instance, or add managed live
bytes to an already counted committed managed arena. Distinguish allocator
request bytes, reserved/usable bytes and physical totals. Report unaccounted
usage explicitly. EFB storage is separate from MEM1; actual XFB/frame surfaces,
binary/static data and stacks need their own accounting.

ARAM already exposes free/largest-block/slot information. For MEM1,
mallinfo free bytes do not establish its largest allocatable block. Expose
largest-block information only where the actual allocator can supply it;
otherwise mark it unknown and report controlled pool headroom separately.

### GPU counters

The SDK already has raw GX metric entry points. Add an owner-scoped, typed
profiling wrapper if needed, rather than issuing competing raw counter changes
from game code. Collect after the normal completed frame; multiplex counter
groups across explicitly labelled samples. Restore counter ownership/state.
Do not insert a GX completion wait around every draw pass. Keep GPU busy/stall
counters, CPU submission time, completion wait and VI wait distinct.

## Design 2: full-town bounded residency without rebuilding geometry

Keep the immutable ARAM archive and common cylinder transform. Keep logical
terrain/collision, NPC journeys and saves in their current gameplay storage.
Extend the working residency system rather than substituting the generic
Hypercube WorldStreamer: its available implementation has a 24-cell catalog and
base-only double-buffered texture pool, and is not connected to this game.

Create a small ResidencyCoordinator around the existing managers. Its fixed
request records identify resource, generation, owner, priority, destination
bytes, staging bytes, work credit and outstanding consumers. Account for both
the old visible resource and a replacement while admission is in progress.

    demand and predict
        -> reserve byte/slot/transient headroom
        -> enqueue bounded native work
        -> poll without waiting
        -> publish between frames after completion
        -> retire old owners after their last GPU/transfer use

Prioritize current visible terrain and doorway/player requirements, then
imminent arrivals, then speculative work and optional demotion. Preserve the
audio broker's reservation/latency contract. Start with the existing shared
one-millisecond residency CPU credit, 8 KiB/32-work-item model grants and
bounded terrain BindStep calls. Add global byte/work accounting so two
opportunities in one presentation cannot each spend a fresh full budget.

These remain soft CPU budgets until indivisible native operations are bounded.
Record maximum single admission/advance cost separately. Split or move large
allocation/conversion/zeroing work out of ordinary travel when measurements
show that it overruns; asynchronous Task syntax alone is not a time bound.

### Warm terrain changes

1. Separate the currently drawable view set from the next demand/prefetch plan.
2. Predict incoming pages from velocity and the same conservative camera,
   horizon, elevation and doorway bounds. Begin with the established guards;
   tune them from measured queue and restore latency.
3. Use BindStep for warm misses under the shared credit. Retain current
   drawable aliases while a next selection is incomplete.
4. Admit current, incoming and speculative pages inside the existing 1 MiB
   cache and 256-view capacity. Evict speculation first. Measure byte and
   contiguous-block high-water marks before considering a larger pool.
5. Publish only complete geometry aliases. Preserve world UVs, receiver alpha,
   material identity and matching shadow-page selection.
6. Keep explicit cold-start/teleport readiness transitions distinct from
   ordinary walking. Ordinary travel acceptance requires zero missing-terrain
   and loading-panel frames; do not hide an admission problem by rebuilding
   geometry, blocking on DMA, or silently omitting visible terrain.

Maintain one clear owner for a pending transfer; cancellation must drain that
owner before buffers or handles are reused. Failed optional work keeps a valid
existing resource or small fallback and backs off. Never resize queues to the
number of objects encountered while walking.

### Other resources

Preserve the player-shared species pin, whole-species ARAM retirement and
current cancellation/grace rules. A distant NPC sharing the player's species
does not justify unloading the player's model. Consider instance-only skin
buffer residency only if owner measurements justify a separate SDK facility.

Retain the small current building kit, bounded to 768 KiB, until a larger
catalog justifies per-definition interior residency. Future room demand must
pin the current room and both sides of an active doorway, then retire the
previous kit after its users drain. Invisible NPC room visits must continue
without requiring that room's graphics.

Keep the tiny repeating ground resident. Preserve coarse-first ground loading
and the demand-loaded path page. Future outfit/interior texture streaming needs
real mip/format/layout-aware accounting and a shared asset catalog. A streaming
texture with two full buffers can increase MEM1; prefer a small valid fallback
plus one admitted replacement. Changing sampler LOD alone does not free memory.

## Design 3: visual work selection and conditional LOD

Extend the existing actor snapshot with a VisualWorkPlan, consumed by body
submission, animation, outfits and attention cues. Keep separate shadow-demand
flags. Preserve full-rate gameplay, navigation, doors and job logic while
graphics are hidden or cold.

Keep camera visibility, resource readiness, pose-due, body-draw and shadow-needed
as distinct decisions. Today a cold model clears the actor's visible flag;
profiling should also retain the original camera-visible result so it can count
late visible assets rather than reporting every unavailable model as a cull.

Add tests/counters for every native animation control, not just explicit
Update calls. Keep the conservative current bounds as fallback. Tighter
cooked clip envelopes must include interpolation, limbs, ears and attachments;
validate them against animation sweeps and partial viewport silhouettes.

Apply effect changes in proportion to measured cost:

- For rain/snow, reject conservative cells before expensive position/geometry
  work, compact visible drops and use a counted-prefix mesh draw if supported.
  A shorter upload alone does not reduce the draw's fixed index range.
- For leaves/petals, age particles cheaply from time and generate vertices only
  for visible live quads. Retain fixed pools and expiry; do not accumulate
  hidden update catch-up.
- Keep the existing shared cloud-shadow phase and night sleep. Cloud coverage
  must not disappear merely because its caster is outside the camera.
- Add an acre/object index only if the measured full-town object-selection
  scan becomes significant; current ground/placement arrays are already bounded.
  Static scenery is also a candidate for cached absolute cylinder poses under
  the shared scrolling parent. Invalidate those poses on placement/growth
  changes, rather than recalculating each visible object's bend during travel.
  Prove equivalence to the existing matrices, world UVs and shadow projection;
  already-relative poses must not receive the cylinder rotation twice.

### LOD ladder, gated by the hardware results

1. Keep today's 128px near-villager quality and shared low-poly trees.
2. If distant pose work is material, trial a 15 Hz far-pose tier against the
   current 30 Hz simulation-driven poses. Select by projected size with
   hysteresis; accumulate only short visible intervals and keep hidden work zero.
   Initial tuning candidates are far-entry below 48 pixels of projected actor
   height and near-entry above 64 pixels, expressed in the 480-row presentation
   canvas and scaled for other modes. These are calibration values, not measured
   quality thresholds. Keep the player and doorway-critical actors on the near
   tier. Use a conservative union bound across all tiers and attachments.
3. If that is insufficient, cook a compact far mesh, initially targeting
   roughly half the transformed vertices/triangles. Preserve skeleton, material
   mapping, silhouettes and clip phase. Author/cook it offline.
4. Introduce an SDK mesh-tier binding only if necessary. Share clips, rig and
   material/texture ownership across tiers; skin only the active compact vertex
   set. Swapping full independent DNMD models or keeping both complete tiers
   warm can erase the intended RAM saving.
5. Keep the current tier while a replacement is unavailable. Switch between
   frames, outside active GPU use. Use measured hysteresis and a bounded
   transition working set; preserve the canonical shadow shape/projection.

An index-only LOD still skinning every original vertex does not solve CPU pose
cost. Tree billboards are a later experiment: current trees are already tiny
shared meshes, while view/season impostor textures and alpha overdraw can cost
more memory and GPU time. Whole-instance house occlusion is also conditional:
use only provably opaque coverage with conservative depth bounds, excluding
door openings and cutaway walls. GPU depth readback/per-triangle CPU occlusion
is not the starting point for this seven-actor scene.

## Hardware qualification and acceptance

Use a disposable, known save and exact immutable ISO/Depot build identity.
Physical DVD is the storage baseline from the session; repeat on GC Loader
with firmware/Swiss/card details recorded. Capture actual video refresh and
mode, including supported 50 Hz and 60 Hz configurations, rather than assuming
that every mode has a 16.7 ms frame budget.

Share a deterministic route description between Dolphin automation and a
debug-only hardware benchmark driver. On console it can drive ordinary
collision-checked movement through waypoints, with a visible cancel action;
it must not teleport between the timing samples or write town saves.

Test:

- Cold startup, normal steady travel and a repeated warm pass across every
  reachable acre, shoreline, ramp, diagonal cliff and bridge; assert route
  coverage instead of calling a few movement directions a full-town sweep.
- Fast reversals and horizon/viewport approaches with several cold species.
- All six selected-player species, all drawable actors, and current-room versus
  off-screen NPC journeys. Include current jobs, shops and large save content.
- Dense supported trees/placements, weather, winter snow, day/night transitions,
  indoor camera turns, doors, overlays and repeated debug feature toggles.
- At least a 30-minute soak and repeated title/town/room transitions, so sequence
  wrapping, cancellation and retained-memory growth cannot hide in a short run.

Record p50/p95/p99/max frame and phase times, one/two/multiple-refresh counts,
maximum single native operation, memory live/peak/minimum headroom, largest
known blocks, DMA bytes/waits, cache misses, late assets and native pose calls.
Report startup, intentional save I/O and transitions separately from ordinary
walking, including their actual stalls. Treat profiler-on/off A/B results as
different observations; do not subtract overlay cost and claim a measured
uninstrumented frame time.

Acceptance gates:

1. No movement-driven terrain builds, missing geometry or loading panels on
   the full ordinary-travel route; async wait/cancellation tests must pass.
2. Zero native animation controls/pose updates for fully culled/cold actors;
   gameplay journeys remain equivalent and visible edge silhouettes survive.
3. No new steady-travel allocations/texture operations from LOD policy
   evaluation; all capture/variant/transition memory stays inside explicit caps.
4. No monotonic retained-memory growth after repeated cycles. Define the safety
   reserve from measured maximum transitions, save scratch and stack needs;
   live byte totals alone cannot admit a large contiguous allocation.
5. Zero late-visible-model frames on the qualification route, and no regression
   in correctly projected/merged shadows or ground/cap/lip sampling. New tiers
   need visual as well as timing approval.
6. Establish physical-console frame targets from the baseline, aiming for one
   actual refresh per ordinary frame. Do not claim locked 60 FPS from a Dolphin
   route. The 1 ms streaming credit and 24 KiB recorder cap are initial design
   budgets, not measured hardware performance.

Run the repository's change router for implementation work. Extend existing
ModelResidency, TerrainResidency, ActorVisibility, frame-profile decoder and
native ownership fixtures with delayed DMA, stale generations, allocation/slot
exhaustion, rapid reversal and sequence-wrap cases. Preserve schema compatibility
and save behavior. Use focused visual scenarios plus normal full-town travel.

## Implementation order

| Slice | Concrete deliverable | Dependency / release decision |
| --- | --- | --- |
| 1. Profiling foundation | Counter-only mode, v6 session/sequence handling, bounded capture, memory owner/high-water snapshots, decoder provenance and optional safe GX metrics | Establish a physical baseline before judging LOD wins. |
| 2. Asset/mip policy | Explicit ground full-chain upload/audit, reachable-asset manifest and proven unused-variant pruning | Independent small change; preserve existing quality options. |
| 3. Warm residency | Async prefetch/bind for normal terrain movement, retained drawable selection, shared admission/work accounting | Requires queue/cache/latency measurements from slice 1. |
| 4. Visual work | New-effect/outfit culling coverage, measured particle work reduction, optional far-pose cadence | Reuse the actor decision; maintain independent shadow demand. |
| 5. Mesh/texture tiers if justified | Shared-resource compact mesh tier and/or future distinct-room/outfit streaming | Proceed only if measurements justify CPU/RAM benefit and transition costs fit. |
| 6. Qualification | Full-town hardware A/B, long soak, memory-cycle and visual reports | Qualify each relevant slice; promote defaults after results are recorded. |

Platform allocation/GX/mesh-tier primitives belong in DolphinDotNet; generic
admission/catalog helpers belong in Hypercube; town visibility, priorities,
rooms, assets and scenario rules belong in AnimalTown. Verify new APIs against
the locked package versions before changing consumers. A generic 24-cell
streamer expansion is needed only if that engine path is actually adopted.

Every console implementation build must retain the same-source ISO, Depot app
and backend delivery rules. Put profiling reports/captures under release/ inside
the Depot ZIP, rather than adding loose release assets. Update stale explanatory
docs without rewriting the historical measurement files.
