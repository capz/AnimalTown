# NuGet rendering services

AnimalTown builds without engine or SDK source submodules. It consumes
`Capz.Hypercube.GameCube` and `Capz.Hypercube.Core` **0.1.0-preview.4.1**, paired
with `DolphinDotNet.Sdk`, GameCube/Core and the Assets tool
**0.1.0-preview.245.1**. global.json pins the SDK; exact PackageReference versions,
committed package locks and the local tool manifest pin the runtime/tool graph.
NuGet.Config uses NuGet.org only. Package licenses are GPL-2.0-only.

`tools/resolve-packages.ps1` resolves the installed SDK through MSBuild and copies
native test support into out/. The packaged compiler generates a private native
bundle; `patch-native.ps1` applies the game's RTC, card, font-page and debug
bridges only there. It checks expected signatures and preserves the 4 MiB managed
heap. No global package files are patched. Offline imaging references the Imaging
assembly shipped inside the pinned Assets package via PackageDownload, with its
matching SkiaSharp dependencies; it does not compile SDK sources. BUILD.txt
records package versions, source commits and the game package-lock SHA256.

## Adopted services

| Engine/SDK capability | AnimalTown use |
| --- | --- |
| CloudShadowLayer | Owns the 4 KiB mask and TEV effect; game supplies cover and world motion |
| ScrollingTextureLayer | Two independent water UV layers; opaque surface, depth tint and foam retained |
| DynamicMeshBuffer / BufferGrowth | Bounded floor, water, foam and soil/ground cliff uploads; explicit content revisions |
| MeshBatch | Groups opaque box colors within the existing pass boundaries; fixed capacity 128 |
| SharedGraphicsResources / ResourceLease | Sole owner of resident application texture pages; cache retains identity and LRU policy |
| CameraFrustum | Shared sphere culling with the existing .025 m guard band |
| RenderCounters / RenderTelemetry | Shared counters and completed-frame snapshot; existing 16 phases and v4 pixel protocol retained |
| Model.LoadAsync | One model task in flight; loading UI between frames, instance publication outside passes |
| Runtime scheduling / frame-clear controls | Independent opt-in experiments; existing 30 Hz simulation retained |

Box positions and rotations are already projected into a common world space.
The parent worldRoot is an identity; MeshBatch receives those world TRS values
without parenting them again. Indexed immutable trees and SDK model queues stay
in use. Transparent foam and painter-ordered UI remain outside opaque grouping.

Geometry revision changes follow camera/room/terrain/material changes; animated
UVs never dirty vertices. Inactive floor/cliff material slots release their CPU
and native capacity. Water/soil buffers retain bounded capacity. GeometryStaging
preserves the currently written prefix during growth: package preview.4.1's
Reserve only preserves the previous committed Count. Capacity recreation can
briefly hold old and new native buffers; budgets bound this peak.

Texture cache keys still include build, disc URI, processing/addressing policy,
resolution and source fingerprint. An entry holds registry leases until eviction;
application references prevent eviction. Loading/eviction occurs between frames,
after synchronous EndFrame has completed GPU work. Cloud and water own their
private effects/textures separately, so resources never have two owners. The
application cache is 256 source entries / 4 MiB; default textures are 256 pixels,
with 128/256/512/1024 choices and lossless 128-pixel UI/IPL pages.

Frame state caches player elevation and the camera/horizon constants. Wallet,
debt, creation rows and keyboard glyphs rebuild only when their values change.
The const build-title concatenation already folds at compile time. Existing
wrapped dialogue, batched fonts and map caching remain in use.

## Runtime experiments

`build-game.ps1 -RuntimeProfile N` writes one byte to runtime.cfg (0..7).
L+R+B cycles the same modes at runtime. Bit 0 removes the extra 1 ms Task.Delay;
bit 1 enables productive waits; bit 2 enables frame-clear reuse. Defaults remain
mode 0 pending equivalent-scene input/audio/save/timer regression measurements.
Runtime mode occupies scenario bits 6..8 in the existing v4 telemetry word; the decoder labels these modes separately. Each change logs `ANIMALTOWN RUNTIME N`; do not mistake logical API calls for GX
draws or blocking GPU wait for GPU execution time. Scheduler timers, completion
checks, native reads and poll stride already have optimized package defaults.

The seven model loads no longer block construction. Loading shows per-model
progress, keeps one request in flight and creates instances only after task
completion between frames. Failure displays an error and preserves the save;
startup never regenerates an existing town. Town state and save formats are
unchanged by package adoption.

Spatial buckets remain conditional: the existing nearest-32 tree selection is
bounded and previously about 0.6 ms, so adding invalidation/index complexity lacks
performance evidence. Full-town WorldStreamer adoption also needs an adapter
beyond its fixed 24-cell fixture and mip-aware texture support. Neither is
required to use the shared rendering services; compare measured scenes first.

Validation: 590,481 gameplay/render/texture checks and 8,050 package rendering checks pass. The full tooling, native card/UV/font/audio, validation-policy/versioning and disposable PostgreSQL integration suites pass. The first full-ISO Dolphin run displayed loading progress, reached the title and verified river, closed-bank corners, bridge, beach/coast and estuary views. The river image check found 51,212 water pixels and 48,412 animated pixels. Final clean-build runtime and cloud checks follow in the PR.
Physical GameCube validation remains outstanding.
