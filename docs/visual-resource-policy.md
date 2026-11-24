# Visual work and memory

World simulation and collision do not depend on visual residency. Reclamation
runs between completed GPU frames and keeps current scenes and both ends of
the player's doorway pinned.

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

Building startup loads exterior/door parts and only the saved current
interior. Other interiors restore near an entrance or during a doorway crossing.
Closed composites remain comparison assets rather than runtime resident parts.
Unseen NPC interiors do not create graphical demand.

## Visibility and snow

Existing backface, camera-frustum and cylindrical-horizon culling remain.
Conservative opaque building and raised-terrain bounds can reject complete actor
bounds before animation and submission; partial silhouettes remain visible.
Eight-metre object bins share nearby candidates across tree, ground-marker and
placement selection. Final precise visibility tests still decide drawing.

Only fully snow-covered flat grass pages omit their hidden base ground. Buried
small objects require uninterrupted snow across their footprint and a halo;
paths, dents, terraces and edges preserve geometry. Tall item billboards remain.
Undented pages use the simpler snow shader. Fresh-snow resets and indoor
retirement release the streamed 128/256 KiB mask. Snowfall preserves main
behavior by settling dents to one shallow visit; settled wear needs no active
recovery timestamps. Recovery scans only lazily allocated active chunks.

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
