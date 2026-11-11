# Published dependency upgrade and rendering work

The follow-up [SDK 315 capability integration](sdk315-capabilities.md) records
the current compiler/runtime/asset pins and shared SDK apploader build.

| Dependency | Previous | Updated |
| --- | --- | --- |
| DolphinDotNet SDK, Core, GameCube, Assets | preview.266.1 | preview.305.1 |
| Capz.Hypercube Core / GameCube | preview.9.1 | preview.15.1 |
| Npgsql | 10.0.0 | 10.0.3 |
| SkiaSharp and Linux native assets | 4.153.1 | 4.155.0, latest stable |
| .NET build SDK | 10.0.100 | 10.0.404 |
| .NET 8 build image | prior pinned digest | SDK 8.0.428 digest |
| PowerShell build image | prior pinned digest | refreshed immutable latest digest |
| Updater source | e4c19a7e7912 | 16efe5ef76a3 |

All application packages remain exact NuGet references with committed locks and
NuGet.org as the sole configured feed. The matching engine compatibility change
is [Hypercube PR #13](https://github.com/capz/hypercube-engine/pull/13).
SDK 305 identifies source `56d67609927a7787900026e445fe63dea659ff3b`; engine 15
identifies `cd7ac010b1ddefc6be125b7b36a408e31c85b0cc` and pins that SDK exactly.
The resolver now rejects compiler, runtime and asset-tool version mismatches.
The asset checksum manifest derives its version from the tool manifest instead
of a separately hard-coded package path. The SDK-supported native compiler
image remains pinned; the newer rolling native-toolchain image is not adopted
without a supported ABI revision. NKit 3.2.0 and GitHub CLI 2.103.3 are already
current and retain their verified pins.

## Applied improvements

The public SDK water-warp preset replaces the temporary DNEF v3 serialization
bridge and failed-startup probe. It makes the planned GPU-only UV distortion
available in the distributable build. Storm-only textures/effects are lazy;
material ownership and failure cleanup are explicit. This avoids retaining
8 KiB of unused native textures compared with a naive upgrade that leaves the
old fallback allocated. It is a capability/overhead improvement, not a measured
frame-rate claim. See [water rendering](water-rendering.md).

The optional rough-sea patch now caches wave phases. Its four-wave height field
and edge fade are unchanged within the tested floating-point tolerance. Steady
frames use eight trigonometric calls instead of 1,156, with zero warm-frame
allocations. Ordinary play no longer allocates that patch's vertex/index arrays
or cylinder-row table. Archived terrain remains immutable.

## Reviewed capabilities

SDK 305 adds DNLT descriptors and independently streamable texture levels. The
current ground pipeline already ships a 64px preview and a fully authored 256px
CMPR mip chain, uploads cooked bytes directly, and publishes upgrades between
frames. Rewrapping these into another container would add metadata/CRC/range
work for only a small read saving, so this update retains the proven path.
DNLT remains useful for future larger texture families, not a reason to rebuild
terrain or recompress an already cooked texture.

Storage mount snapshots, retained mount recipes and removal generations are
available for filesystem-backed content. Current saves use the raw memory-card
transaction manager, so filesystem mount polling is not added to the frame loop.
The experimental image-defragmentation APIs are not used by game/save paths.
The latest stable database/image packages and current .NET build images are
included without changing the game's world, collision or save schemas.

## Validation

Run `tools/test-changes.ps1` for full dependency-upgrade coverage. Regression
checks include water-program bindings, cached-wave/reference parity, allocations,
terrain, source-image preservation, renderer/resource lifetimes, native allocator
and archive behavior, save migrations, profiles and real PostgreSQL integration.
The review PR records clean console/backend artifacts and targeted Dolphin water,
style-switching, menu and save checks. Physical GameCube testing is outstanding.
