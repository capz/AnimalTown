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

The coordinator retires one unused owner per grant. Candidates include released
texture leases, unused dropped-item buffers, outdoor tree resources and unused
room floors/walls. Outdoor terrain cache and floor leases suspend indoors;
optional UI geometry caching and full ground-texture upgrades yield to pressure.
Resource owners clear borrowed bindings before freeing their native storage.
Reservations outlive asynchronous work and are cancelled only after cleanup.

Building startup loads exterior/door/closed parts and only the saved current
interior. Other interiors restore near an entrance or during a doorway crossing.
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
Undented pages use the simpler snow shader. Complete recovery releases the
256 KiB dent texture. Snow recovery scans only lazily allocated active chunks.

## Threshold selection

Tests choose projected screen size rather than a universal player-distance
cutoff. Useful villager mesh variants enter at 24 pixels high and return to full
detail at 30 pixels. Separate 48/60-pixel hysteresis reduces distant pose updates
from 30 to 15 Hz. The selected player's shared species and indoor scenes retain
full geometry. Variants without useful reduction avoid replacement I/O.

Terrain prefetch uses a six-metre guard in two-metre buckets. A four-metre guard
failed the injected slow-DMA route, so it was increased. Tests cover 3,600
full-speed movement/reversal frames at 2, 8 and 16 polls per transfer, plus
teleports, cancellation and indoor restoration, with no missing terrain.
Prefetch is bounded inside the existing view/cache pools and is never drawn.

Host geometry and native allocation tests establish policy bounds, ownership
and failure behavior. They do not establish physical GameCube frame-rate gains.
Native cooked-asset memory/error reports and emulator acceptance supplement
these tests; hardware measurements remain a separate qualification.
