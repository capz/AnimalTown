# Visual work and memory

World simulation and collision do not depend on visual residency. Reclamation
runs between completed GPU frames and keeps current scenes and both ends of
the player's doorway pinned.
Waiting for visual restoration does not consume the doorway's traffic timeout;
crowding and stale-location checks still cancel invalid crossings.

## System memory admission

`NativeMemory` samples free libc storage plus the unclaimed MEM1 arena without
adding overlapping model/texture owner charges. It also records failed native
allocation requests. It does not claim to measure the largest contiguous block.

`VisualMemoryPressure` uses a 256 KiB safety reserve, pressure below 1.5 MiB,
recovery above 2.5 MiB for three seconds, and a one-second failure backoff.
An essential request can trigger reclamation before reaching the low threshold.
Fixed-size admission tickets reserve overlapping asynchronous allocation peaks;
completed allocations stay charged until a fresh physical sample includes them.
Required restoration can proceed under pressure when its reservation fits.
Ordinary pressure keeps an optional species warm for at least one second after
it leaves required demand and preserves the three-second grace after a restore.
An actual waiting essential request can bypass those timers for an optional
species outside demand; current scene, doorway and player pins remain intact.
While an essential allocation is waiting, model lookahead narrows to complete
drawable bounds and transition endpoints. Optional imminent models can retire,
and their pending restores cancel and drain before releasing admission tickets.
This prevents smaller prefetch requests from starving a required terrain cache
or room restore. Ordinary low-watermark pressure retains the 12 m imminent
lookahead and admits its restores before actors become visible.

The coordinator retires one unused owner per grant. Candidates include released
texture leases, unused dropped-item buffers, outdoor tree resources and unused
room floors/walls. Outdoor terrain cache and floor leases suspend indoors;
optional UI geometry caching and full ground-texture upgrades yield to pressure.
Resource owners clear borrowed bindings before freeing their native storage.
Reservations outlive asynchronous work and are cancelled only after cleanup.

Demand/readiness preparation and physical sampling finish before the shared
one-millisecond executable residency grant starts. Required building parts run
before optional reclamation scans. Before/after-draw opportunities spend only
the same unused CPU credit; drawing does not replenish it. Preparation telemetry
still counts the entire demand, sampling and work cost.

Building startup loads exterior/door parts and only the saved current
interior. Other interiors restore near an entrance or during a doorway crossing.
Closed composites remain comparison assets rather than runtime resident parts.
Unseen NPC interiors do not create graphical demand.

Villager startup keeps the selected player first, then prepares species outside
the initial view before the required cohort. Each unused package reaches ARAM
before another load starts. A dense saved view therefore cannot consume the
temporary load headroom needed to prepare an unused final species.

## Visibility and snow

Existing backface, camera-frustum and cylindrical-horizon culling remain.
Conservative opaque building and raised-terrain bounds can reject complete actor
bounds before animation and submission; partial silhouettes remain visible.
Eight-metre object bins share nearby candidates across tree, ground-marker and
placement selection. Final precise visibility tests still decide drawing.

Only fully snow-covered flat grass pages omit their hidden base ground. Buried
small objects require uninterrupted snow across their footprint and a halo;
paths, dents, terraces and edges preserve geometry. Tall item billboards remain.
Undented pages without snow boundaries use the simpler snow shader. Boundary
pages use the same streamed 128/256 KiB mask to clip the cap above a white
25 cm ramp to bare ground, including clear-ground patches. Ramp geometry is
cached per selected page and shares normal ground shadowing. Indoor retirement
releases derived snow masks and geometry. Snowfall preserves main
behavior by settling dents to one shallow visit; settled wear needs no active
recovery timestamps. Recovery scans only lazily allocated active chunks.

All snow ownership is seasonal. Outside winter there is no town snow owner,
wear/recovery storage, derived coverage/visibility cache, ramp mesh, native
snapshot, mask or smoothing workspace. Winter exit also retires the snow
particle stream and cancels outstanding snow memory reservations. Re-entering
winter creates fresh state; ordinary movement skips snow processing entirely
in other seasons.

## Threshold selection

Tests choose projected screen size rather than a universal player-distance
cutoff. Useful villager mesh variants enter at 24 pixels high and return to full
detail at 30 pixels. Qualified fox, bunny and giraffe assets remove about
14–22% of triangles with sampled normalized animation error below .05. The
worst sampled fox error projects to about 1.36 pixels at the 30-pixel upgrade
request threshold; an asynchronous upgrade completes when its resources are
ready. Separate 48/60-pixel hysteresis reduces distant pose updates
from 30 to 15 Hz. The selected player's shared species and indoor scenes retain
full geometry. Variants below 12.5% triangle reduction avoid replacement I/O. Shared NPC
cohorts restore and replace atomically, with all instances included in admission.

Terrain prefetch uses a six-metre guard in two-metre buckets. A four-metre guard
failed the injected slow-DMA route, so it was increased. Tests cover 3,600
full-speed movement/reversal frames at 2, 8 and 16 polls per transfer, plus
teleports, cancellation and indoor restoration, with no missing terrain.
Prefetch is bounded inside the existing view/cache pools and is never drawn.
It is automatic; the obsolete warm-prefetch debug row and saved setting no
longer control ordinary travel.

Host geometry and native allocation tests establish policy bounds, ownership
and failure behavior. They do not establish physical GameCube frame-rate gains.
Native cooked-asset memory/error reports and emulator acceptance supplement
these tests; hardware measurements remain a separate qualification.

## Idle work and optional effects

Model-light points are projected and uploaded only when lighting is active and
their layout, terrain or cylinder focus changes. Gain changes still reach the
native renderer every frame. Exact building origins, existence and definitions
invalidate the cache; failed publication remains eligible for retry.

Cloud coverage reuses the exact repeating samples for the supported 32/64-metre
patterns, then combines each destination with its own terrain and lamp texel.
The fast path needs only a 64-byte stack row. It reduces bilinear samples from
16,640 to 1,024 normally or 4,096 in winter, with the original scalar path for
other scales or motion bounds. An independent scalar oracle checks 12,853
complete masks byte for byte. Host benchmarks show 52–64% lower mask-generation
CPU time; these are not GameCube frame-rate measurements.

Path demand follows requested drawable sections, including pages whose DMA is
pending. Speculative prefetch pages do not request path art or the native mask.
The result is reused until the town, path data, terrain or drawable selection
changes, so unchanged views perform no section scans or allocations.

Optional water optics reserve their complete native allocation peak before
initialization or resize. At 640×480 the two maps and refraction capture own
169,984 bytes, plus a 1 KiB admission guard. Required reservations take priority;
waiting essential demand, allocation backoff or less than 1 MiB of unreserved
headroom retires optics before essential residency pumps, even if the scene is
still loading. The small capture can remain warm during global pressure
hysteresis. A cold capture can recover after three seconds of stable headroom
when its full allocation peak still leaves that 1 MiB margin. This exception
does not clear global pressure or admit unrelated optional owners. Transparent
water remains available when optics are denied.
Short visibility gaps retain storage for three seconds without capture or draw
work; longer absence releases it. Allocation failures drain partial owners and
back off before retry, and completed reservations remain charged until sampled.
