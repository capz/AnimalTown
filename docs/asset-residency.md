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
One fixed 32 KiB native staging buffer is retained. Package checks, texture copies
and shared-texture comparisons each process at most 32 KiB per call. One retained
pose is prepared per call; that existing SDK
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

## Published native validation

Validated runtime commit `4287df620af5881433b5b48b7275ade00c6e7542`, release
[0.12.9.4287df620af5](https://github.com/capz/AnimalTown/releases/tag/v0.12.9.4287df620af5).
The clean same-commit game/backend build passed 2,702,859 gameplay/render/texture
checks, 324,925 rendering-service checks and the selected native/tooling checks.
The native residency fixture also passed address/undefined-behavior sanitizers.
The earlier lifecycle build restored correctly but its monolithic validation and
publication caused a 57,825 us step. This release replaces that normal path with
the bounded transport, texture and pose work described above.

The focused Dolphin run used software OpenGL and DSP HLE, with a copied committed
save placing six villagers in one house. The first outdoor position is more than
30 metres from every door. It exercised title retirement, cold species/interiors,
developer prewarming, camera rotation, two entry/exit cycles and an ordinary
approach followed by a readiness-gated entry. It completed twenty model restores.

| Measurement | Observed result |
| --- | --- |
| Title texture-cache payload | 1,524 KiB before / 1,012 KiB after: **512 KiB freed** |
| Main memory with all species and room parts ready | **23.35 / 24.00 MiB** |
| Main memory after leaving and facing away | **17.64 / 24.00 MiB**, **5.71 MiB reclaimed** |
| ARAM including audio and terrain reservations | **13.78 / 16.00 MiB** |
| Cold render models | **10**: five unused species and five shared interior parts |
| Worst complete native restore step | **4,070 us** across both cycles |
| Repeated room region | Byte-exact RGB match, including the six villagers |
| Memory-card contents | Whole-card SHA-256 unchanged |

The ordinary approach's 151 fully decoded frames had a 16.6 ms maximum frame
interval and 2.8 ms maximum preparation time at the counter's 0.1 ms resolution.
There were no terrain stages, mesh creates/updates/releases, collections, missing
terrain, recovery or loading frames in that interval. It is a short approach,
not a sustained travel benchmark. ARAM/package/texture copies still occur;
the draw-API texture counter does not count those copies.

The wider entry intervals include completion of every required restore and the
scene transition. Door fades cover the encoded telemetry during 23 and 64 frames
respectively; the analyzer reports those gaps explicitly and cannot establish
their complete frame timings. The native worst-step counter remains cumulative
through the fades. An immediate re-entry with all six species cold can wait under
the opaque door fade. This keeps unready models out of the destination view.

[Screenshots, release hashes and native counters](performance/asset-residency-4287df620af5/validation.json)
and [decoded timing/image checks](performance/asset-residency-4287df620af5/analysis.json)
record the evidence. The full ISO is **1,459,978,240 bytes**; the direct NKit ISO
is **88,684,544 bytes**. Published checksums, clean console/backend manifests and
the byte-exact NKit expansion audit were verified. Physical GameCube validation
remains outstanding; emulator timings are not a hardware performance guarantee.

### Reproduce the focused scenario

Use the repository's `animaltown-dolphin:latest` image from
`build/Dockerfile.dolphin`. Prepare a **copy** of a valid committed town card;
`card.py write` creates the output card in the fixture folder and refuses to
overwrite its source:

```sh
python3 docs/performance/world-debug-fixture/card.py extract INPUT.raw out/residency
dotnet run --project docs/performance/world-debug-fixture/Fixture.csproj -- out/residency --residency
python3 docs/performance/world-debug-fixture/card.py write INPUT.raw out/residency
docker run --rm --init --shm-size=1g -e LIBGL_ALWAYS_SOFTWARE=1 \
  --mount type=bind,src="$PWD",dst=/work -w /work animaltown-dolphin:latest \
  python3 docs/performance/asset_residency.py RELEASE.nkit.iso out/residency \
  --stream-travel --season spring --dsp-hle
python3 docs/performance/analyze_asset_residency.py out/residency \
  --capture out/residency/user/Dump/Frames/*.avi
```
