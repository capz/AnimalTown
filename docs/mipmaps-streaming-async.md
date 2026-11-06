# Mipmaps, streaming and asynchronous work

## Applied texture policy

World textures already had source-derived mipmaps. This change caps the chain
at two levels total, not two additional levels: 128/64 for maximum-size model
textures and 64/32 for maximum-size floors. Smaller images stop before either
dimension falls below four pixels. Model conversion generates levels offline;
the floor loader box-filters the packaged source and generates its levels at
load time before releasing temporary pixels. UI, icon and font atlas pages
remain base-only to preserve their small glyphs and avoid atlas bleeding.

The SDK importer option `--mip-levels 2` also bounds generated emissive/AO and
baked material maps. Existing native texture creation reads the recorded count,
uses trilinear minification for linear materials and clamps maximum LOD to
the final available level. No per-frame texture regeneration is introduced.

This primarily controls storage/residency. A 128-square RGBA8 texture falls
from 87,360 to 81,920 pixel bytes (about 6.2%); most mip memory is in the first
two levels already. All seven unique floors in a season total 12,864 bytes,
down from 13,824. Two levels can still shimmer at the distant horizon because
the smaller levels are absent. This is a quality/budget tradeoff, not a claim
of a measured frame-time improvement.

Texture audit checks every model's count and records actual floor dimensions,
formats, levels and payload sizes. Original source hashes and crop provenance
remain in the audit. The versioned floor cache key changes with this policy;
disc URI and build identity still recognize and share subsequent loads.

## What streaming would provide

AnimalTown currently uses its resident `TextureCache`, not Hypercube's
`WorldStreamer`, `TexturePool` or `AramCache`. Models are loaded at startup and
share textures through the SDK's separate immutable model cache. Floor images
repeat across the world: they are seven shared sources per season, not a
separate texture for every tile. Retaining those small active textures already
avoids repeated disc reads, decoding and uploading. Streaming them would not
remove the draw/submission cost that dominates rendering.

Streaming becomes useful when many distinct interiors, outfits or seasonal
assets exceed MEM1 budgets, or to prefetch during transitions. It can reduce
peak residency and overlap I/O with frames. ARAM is a backing store: GX must
sample a texture in MEM1, requiring a transfer back before drawing. The
GameCube has one CPU; background work does not supply another core.

The existing engine streamer is a bounded 24-cell validation catalog, not a
ready-made adapter for AnimalTown's world. Its staged publication, GPU fences,
fallback pages and I/O budgets are useful foundations. Integration needs a
game asset catalog and residency/pinning policy. Also, the current SDK
`StreamingTexture` allocates two **base-only** native textures. The engine's
pool keys width/format and accounts for two base buffers. Streaming mipmapped
assets correctly would need mip-aware allocations, pool keys, byte accounting
and sampler LOD, rather than passing a multi-level package into that pool.
This change does not enable that integration.

## Why async methods sometimes appear synchronous

| Location | Actual behavior | Consequence |
| --- | --- | --- |
| `TownGame` constructor, `Model.LoadAsync(...).GetAwaiter().GetResult()` | Starts and waits for each of seven models sequentially before the first frame | Blocks startup; no loading screen or overlapping model requests |
| Main loop, `Task.Delay(1).GetAwaiter().GetResult()` | Deliberate cooperative wait after presentation, every frame | Blocks the loop; the earlier released-ISO world capture measured about 4.3 ms in this path, despite a 1 ms request |
| `WorldStreamer` read/upload/ARAM completion handling | Checks `IsCompleted` before `GetResult()` | Retrieves an already completed result and propagates errors; does not wait for pending work |
| `StreamingTexture.TryPublish` and model residency completion | Tests completion before consuming the task | Publishes completed work safely and propagates failures |

The SDK supplies a custom task/runtime implementation. `Task.Run` uses a
persistent service worker; waiting services async progress and parks via the
native runtime, releasing the managed execution gate. Completed task reads
return immediately. This avoids treating it as desktop .NET synchronization,
but a pending wait in the application loop still prevents that loop from
rendering its next frame. Only one managed executor owns the gate at a time;
native I/O and GPU activity can overlap application work.

The startup waits are an initialization shortcut, not streaming. A better
loading flow starts bounded jobs, draws a loading screen while polling their
completion, and creates/publishes rendering resources on the application
thread between frames. The per-frame delay is a separate optimization
candidate, especially with retrace already waiting; changing it requires
checking service progress, input/audio and pacing. Neither async flow is
rewritten by the mip-policy change. Moving GX calls into `Task.Run` would not
be an appropriate fix.

Relevant sources are `game/AnimalTown.GameCube/Program.cs`,
[WorldStreamer.cs](https://github.com/capz/hypercube-engine/blob/a9482f8ec6226a8a87bd60d7235c497457a8d978/src/Hypercube.GameCube/WorldStreamer.cs) and
`TexturePool.cs`, the SDK's `StreamingTexture.cs`, `Tasks.cs`,
`source/async_runtime.c` and `docs/async-runtime.md`. The historical capture
behind the delay observation is linked from `docs/viewport-and-indexed-models.md` and the
render investigation release artifacts; it is Dolphin guest time, not a
physical-console timing claim.
