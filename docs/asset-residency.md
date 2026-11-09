# Logo retirement and ARAM model residency

The title-only logo releases its cache lease and immediately evicts its native
pages after leaving the title screen, including new-game creation. Its 512 KiB
pixel payload is not retained as a zero-reference cache entry or copied to ARAM.
The current flow never returns to the title screen. Other fonts, icons and banners
keep their existing ownership and manual-save behavior.

Exterior and door models stay resident. Interior floor/wall models are shared
by building definition, and become cold after the last current room, transition
or approach-prefetch demand ends. After exit, source-room parts remain pinned
until the door animation finishes. An approach within 20 metres while facing a
door prefetches its parts and occupants. Exiting facing away does not retain them
merely because the player is still beside the door.

The six species share their model package between player/NPC instances. The
player's species always stays resident. Current-room villagers remain resident
for immediate camera rotation. Outside, a 32-metre guard around the conservative
animated view/horizon envelope requests models before they can become visible;
unused species wait 120 rendered frames before eviction. Hidden animation time
continues to be discarded, and native animation/submission calls require a ready
model. Shadow masks and journeys use logical positions independently of model
residency. Offscreen visits continue without loading their interiors or models.

Creation keeps all species ready for immediate selection. Explicit developer
inspection jumps first warm all registered models while rendering the existing
view, then commit the jump. This avoids presenting cold actors at an abrupt
camera destination. Water-inspection comparisons also retain their assets.

## Transfer ownership and publication

AnimalTown owns one transfer scheduler rather than mixing SDK asynchronous
model transitions with terrain binds. Both use the SDK's single ARAM transfer
channel. Every scheduler call completes at most one 32 KiB aligned DMA quantum
between frames, leaving the channel idle before terrain can bind its own pages.
One fixed 32 KiB native staging buffer is retained. Package checks, texture copies and shared-texture comparisons each process at
most 32 KiB per call. One retained pose is prepared per call; that existing SDK
pose evaluator remains indivisible and must be measured on target.

Startup registers each sequential, completed model load explicitly. Native
registration requires exactly one unclaimed model and matching native byte
accounting; ambiguous loads fail rather than guessing private managed layouts.
The manager retains the native model until unregistering it. It stores no managed
pointer across a call. Exterior/door records have no ARAM backing allocation.

Cacheable immutable packages are copied to separately held ARAM blocks, then read
back and checked against the original package's hash before eviction is allowed.
Allocation/write/verification failure preserves the resident source and disables
its eviction. Initial caching completes under the existing startup loading UI.
The 2 MiB audio reservation, 8 MiB terrain ARAM reservation, 1 MiB terrain MEM1
cache and existing model/draw budgets are unchanged. Default species packages
plus the current five interior parts fit within the remaining ARAM capacity;
allocation failure gracefully retains that asset in main RAM.

Eviction frees the model package, unreferenced shared texture payloads and instance
vertex buffers using the SDK. Reusable metadata, instances and animation intent
remain. The original SDK load validates the package structure once. Native metadata
retains checked offsets into that immutable package. Restore verifies each
32 KiB block with CRC32 and the whole package with its original FNV hash, then
rebases those validated offsets without reparsing all vertices. Texture payloads
are copied/compared incrementally through the same shared registry and byte
accounting as the SDK; retained poses are prepared one per call before publishing.
A restore owns each partial texture copy; urgent work may preempt between
complete textures or transport blocks. Partially filled textures remain
unpublished. Uninitialized staging is fully filled and verified before use.
GX texture and model-state caches are invalidated when resources change. No draw
or animator call is allowed on a cold model. A failed ARAM read/hash falls back to
the exact original read-only disc path, in bounded read chunks, and disables
future eviction for that asset. Disc fallback is an error path and may stall on
I/O. An unrecoverable disc error stops gameplay with the existing asset-error UI;
it never writes the memory card.

Player doorway commits wait under the existing opaque fade until the destination
parts and required villagers are ready. This runtime-only gate never alters saved
locations before commit or serializes transfer state. Distant NPC doors remain
independent of graphics readiness. Normal walking never uses a loading panel for
asset residency; prefetching and eviction hysteresis provide the working set.

## Checks and diagnostics

`tools/test-changes.ps1` routes the native `assets` fixture. It uses the pinned
SDK loader, animation evaluator and ARAM implementation with actual cooked
species/interior packages. It checks repeated byte-exact unload/restore cycles,
shared-model lifetime, memory reclamation, readback verification, interleaved
terrain-channel use, active-frame guards, DMA/checksum failures, disc fallback
and ambiguous registration. Address/undefined-behavior sanitizer runs cover the
same fixture. Domain checks cover visible/prefetch envelopes, player-species pins,
room/approach demand, asset-gated doorway commits, independent offscreen journeys,
allocation-free demand updates and unchanged saves.

The existing **Performance display** adds a cached assets label: cold-model count,
budgeted model KiB, restore count and worst restore-quantum microseconds.
Its counters are diagnostics, not a physical-console frame-time guarantee. The
SDK performance panel remains available for actual main/ARAM memory totals.
