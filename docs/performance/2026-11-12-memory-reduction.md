# Runtime memory reduction — 2026-11-12

Baseline: `e4a17651275b42a398f0c968008ff99d5a577180` (tree-noise controls and
camera-facing dropped items). SDK `0.1.0-preview.315.1`, Hypercube
`0.1.0-preview.28.1`; package pins and artwork are unchanged.

Implementation scope is the game runtime: fonts, game terrain/shadows, and
villager navigation. Gameplay classes under `shared/AnimalTown.Domain` are
included; supporting test/native-build checks accompany them. Save-codec
copy reduction was investigation only in this first pass; the follow-up implements it.

The first pass targets retained managed storage in a normal town session. The
implemented changes remove **173,888 bytes (169.8 KiB) of array payload** with the
current IPL body font and Gorditas heading font. This is about 4.1% of the fixed
4 MiB managed heap. Object headers/alignment and native GPU allocations are
separate; this is not a measured reduction in total console peak memory.

## Implemented

| Owner / storage | Previous payload | New payload | Saved |
| --- | ---: | ---: | ---: |
| Two fonts: cached X/Y offsets | 131,072 B | 65,536 B | 65,536 B |
| IPL font: glyph regions and page references | 27,648 B | 3,456 B | 24,192 B |
| Gorditas: glyph regions and page references | 27,648 B | 6,912 B | 20,736 B |
| Gorditas: unused page-order counts | 256 B | 0 B | 256 B |
| Navigation: shared obstacle grid | 25,600 B | 3,200 B | 22,400 B |
| Cliff shadows: candidate grid | 25,600 B | 3,200 B | 22,400 B |
| Terrain renderer: inactive legacy stream arrays | 18,368 B | 0 B | 18,368 B |
| **Total** | **256,192 B** | **82,304 B** | **173,888 B** |

Font reference sizes in this table use the console's 32-bit pointers. Region
storage is `96 × stride × (8 floats + one reference)`. The existing font cooker
produces a maximum Gorditas stride of two regions per glyph (113 occupied slices
across 96 glyphs); IPL needs one. The previous code reserved eight for both.
`PrepareGlyphs` and drawing use the selected stride throughout.

`UiTextLayoutCache` keeps the existing 64 direct-mapped slots and 128-character
limit. Integral font-unit offsets use exact `ushort` values instead of floats.
Oversized, fractional or invalid offsets take the existing uncached float path;
a rejected replacement cannot leave a stale layout/page-order hit. Scaling and
rotation remain outside the cache. Warm hits allocate no memory.

Navigation and cliff-shadow candidate flags use one bit per town cell, replacing
one byte per cell. Their traversal order, refresh cadence and job budgets stay
the same. Clearing a map clears all words, including bits at world boundaries.
The cliff tile upload stream is unchanged.

`TerrainRenderer` owns immutable cylinder rows independently from its optional
`TerrainStream`. Ordinary archive rendering no longer constructs lookup/job
arrays. The stream is created when mesh-recreation or ground-ring comparison is
enabled, reused/reset while needed, and released when returning to archive mode.
Both paths share the same rows. Startup cliff warming remains active.

## Evidence and regression checks

Warmed host measurements used .NET 8 on Linux, with `GC.GetAllocatedBytesForCurrentThread`
around constructors. The original implementations were compiled from the baseline
alongside the replacements. These totals include host headers and alignment;
they are not GameCube heap totals.

| Constructor / ownership | Baseline host allocation | Current host allocation | Difference |
| --- | ---: | ---: | ---: |
| Navigation | 166,280 B | 143,880 B | 22,400 B |
| Cliff-shadow map | 25,736 B | 3,336 B | 22,400 B |
| Default terrain selection ownership | 25,264 B (stream including rows) | 6,472 B (rows only) | 18,792 B |

The legacy stream's array payload is 11,968 B of lookup words, 1,280 B of
boolean flags, and 5,120 B of integer/float selection arrays. When explicitly
requested with existing rows, its measured host allocation is 18,792 B.

Behavior comparisons:

- Navigation: 6,000 ticks × six villagers against the original implementation;
  exact positions, yaw, route indices/lengths/goals and search-work counts.
- Cliff shadows: original/replacement streams for seeds 7319, 781 and 101;
  exact chunk bytes, offsets, coverage and publication timing. Tests additionally
  compare direct shadow rays at bit-word and world edges and clear/reuse the map.
- Fonts: original float layout equations for multiline text, tabs, unsupported
  glyphs, collisions and 128-character boundaries; overflow fallback and glyph
  page bounds; zero allocations across 1,000 warm cache hits.
- Terrain: shared/owned rows select identical section addresses, visible bits,
  scheduling and readiness at four boundary views, including repeated resets.

Run `./tools/test-changes.ps1 -BaseRef origin/fix/eye-facing-pinecones` for this
branch, or use `-BaseRef HEAD` for local edits. `-Full -NativeInProcess` runs all
checks on a prepared Linux host. Validation/build-gate edits select full coverage.
The font regressions are also independently selectable:

```sh
dotnet run --project tests/AnimalTown.RenderingChecks -c Release -- --suites font
```

The native patch gate checks that bitmap writes remain 32-bit integer stores and
font coordinates remain integer `ushort` stores. The latter uses an explicit
integer-return boundary because the pinned compiler has previously emitted float
array stores after narrowing conversions. Full validation also compiles the game,
generates the AOT source bundle and runs native host fixtures. No console ISO or
Dolphin/hardware run is implied by these host checks. Archive/legacy toggles and
native-resolution text remain useful emulator/hardware follow-up scenarios.

## Further opportunities

All four high/medium items below are implemented in the
[game memory follow-up](2026-11-12-memory-followups.md), with measurements and
explicit cold-allocation tradeoffs. This table records the original investigation.

| Priority | Opportunity | Evidence / next step |
| --- | --- | --- |
| High | Compact shadow receiver cache | `ShadowReceiverCache` eagerly reserves 157,824 B of UV/terrain payload. Flat receivers need only six vertices. Design a bounded flat/complex store and measure misses and frame-time allocations before changing the current warm-cache behavior. |
| High | Reduce save decode/encode copies | Seed 7319 split-save decoding allocated 258,552 B cumulatively on the host. `DecodeProfiles` finishes by encoding/decoding another whole world for semantic validation. Factor a shared validator and exact-size writers; retain corruption, migration, interrupted-write and ownership checks. This figure is allocation traffic, not peak live memory. |
| Medium | Retire map upload scratch | `MapBackground` retains a 65,536 B RGBA upload array after preparation. Releasing or sharing it can reduce residency, but needs terrain-edit/refresh tests and a policy for allocation churn. Preserve all map colors and silhouette coordinates. |
| Medium | Allocate rain staging on first use | Dry sessions retain 384 color vertices (6,144 B of managed payload). Lazy allocation can help dry startup; weigh the small saving against the first-rain allocation and preserve a warm stream while rain is active. |

Two apparent opportunities are excluded from the savings above. Actor instance
arguments of 512 KiB are allocation ceilings; the native implementation allocates
only required pose data. `WaterDepthCache` can be compacted substantially but has
no live game owner in this baseline, so changing it would not reduce the running
game's memory. Larger texture/mesh quality changes require separate visual and
performance evidence.
