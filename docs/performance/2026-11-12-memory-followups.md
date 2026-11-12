# Game memory follow-ups — 2026-11-12

This implements all four high/medium opportunities from the
[first memory investigation](2026-11-12-memory-reduction.md). Baseline:
`3baec95bfccd47a2e918a1262536f96f76f35041`, including that first pass.
Implementation stays in game/shared gameplay code, with supporting tests and
validation routing. SDK, engine and package pins are unchanged.

## Shadow receiver cache

The 64 logical entries now share a packed UV arena and one flat terrain-snapshot
array. Supplied UVs remain exact floats, including arbitrary six-vertex shapes;
there is no inferred rectangle or quantization. Logical capacity, recency,
terrain invalidation and maximum complex-receiver capacity remain unchanged.

| Workload | Previous retained payload | New retained payload | Saved |
| --- | ---: | ---: | ---: |
| Empty or all 64 flat receivers | 157,824 B | 13,440 B | 144,384 B |
| Generated-town traces, seeds 7319/781/101 | 157,824 B | 34,944 B | 122,880 B |
| All 64 maximum-complexity receivers | 157,824 B | 157,824 B | 0 B |

On a cold miss, reusable spans are overwritten in place. When needed, the cache
compacts live spans in address order with overlapping-safe copies, then grows
geometrically up to its original ceiling. It never evicts an extra entry to fit a
complex shape. After warming its high-water capacity, hits, eviction, insertion
and compaction allocate no memory. The high-water arena stays resident until the
cache owner is released, including across town-source invalidation.

Growth is a tradeoff: a cold transition can temporarily retain both old and new
arenas. The largest step holds 98,304 B of old coordinates, 147,456 B of new
coordinates and 10,368 B of snapshots: **256,128 B of payload until collection**.
This implementation reduces normal retained memory, not that worst-case cold
transition peak.

Evidence:

- Warmed .NET 8 constructor allocation: 168,512 → 20,232 B.
- Generated-town caster traces restore identical UV/count/flat outputs and match
  baseline cache decisions: 192 hits / 1,120 misses for each measured trace.
- An independent 64-slot recency model matches a 7,000-operation fragmented trace
  with 3,375 hits / 3,625 misses. All 64 maximum-complexity entries remain usable.
- A warmed mixed trace of 7,000 operations allocates zero bytes.
- A 200,000-operation host timing sanity check found no regression: flat baseline
  43.1–43.5 ms versus 42.2–42.4 ms; mixed baseline 44.9–48.1 ms versus
  44.0–44.6 ms. This is an isolated host measurement, not a Dolphin/FPS claim.

## Save copies and semantic validation

Split decoding validates the assembled state directly instead of encoding it
into a combined save and decoding a second world. The combined current-format
reader uses the same final validator. Wire readers retain CRC, schema, generation,
range and snapshot checks. The common path retains plot overlap, incremental
placement validation, fixed-point normalization, profile capture, building-location
normalization and terrain revision publication. Old water migration defers its
intermediate notification until final publication, preserving the original import
revision counters.

Combined, split, profile and commit writers allocate their exact final lengths,
with a cursor/length assertion before publishing. Bytes, file names, versions,
checksums and legacy formats remain compatible.

Warm .NET 8 cumulative allocations per operation, in **bytes**:

| Fixture / operation | Before | After |
| --- | ---: | ---: |
| Seed 7319 combined encode | 44,992 | 18,656 |
| Seed 7319 split encode | 34,024 | 14,952 |
| Seed 7319 split decode | 258,552 | 104,504 |
| Four furnished profiles: combined encode | 33,944 | 15,528 |
| Four furnished profiles: split encode | 19,368 | 8,824 |
| Four furnished profiles: split decode | 250,696 | 104,856 |
| Maximum-capacity fixture: combined encode | 66,200 | 31,656 |
| Maximum-capacity fixture: split encode | 51,624 | 24,952 |
| Maximum-capacity fixture: split decode | 282,952 | 104,856 |

Seed 7319 output remains 15,060 B combined / 14,476 B across split records. The
existing full save/readback fixture drops from 238,784 to 116,808 allocated B.
These are allocation totals, not live/peak memory measurements.

Tests pin baseline combined/split hashes and cover maximum text/mail/object
capacities, mixed generations, malformed semantic values with recomputed CRCs,
legacy split schemas 1–3, interrupted writes, bank fallback, onboarding, profile
deletion and snapshot readback. Independent old/new codec probes additionally
matched acceptance and canonical output for 16,932 checksum-valid mutations and
1,269 snapshot cases, including four furnished profiles and visitor normalization.
Snapshot checks left the live world byte-exact. After the legacy migration fix,
27 additional strict cases matched terrain/shadow revisions and snapshot aliasing
across old/current schemas; a separate 6,245-case differential replay also passed
without metadata exemptions.

## Map upload scratch

`MapBackground` no longer owns a 65,536 B RGBA array between refreshes. A dirty
refresh allocates one complete zero-initialized scratch, uploads synchronously,
then releases its source reference. An unchanged town/revision returns before
allocating either scratch or its upload delegate. Identity/version publishes
only after a successful upload, allowing a failed upload to retry.

Each dirty revision still allocates that 64 KiB scratch, plus a short-lived upload
delegate in production. Frequent terrain editing therefore increases allocation
traffic; ordinary unchanged frames allocate zero. The native map texture remains
resident and the silhouette, sampling, palette and padding bytes are unchanged.

Warmed host constructor allocation falls 69,936 → 4,368 B. Tests compare every
uploaded pixel/padding byte against the original equations, then exercise terrain
edits, town replacement, failed-upload retry, disposal/reentry and 1,000 unchanged
production/callback frames with zero allocations.

## Rain staging

A dry session no longer allocates the 384-vertex, 6,144 B managed rain stream.
The first actual preparation creates it; subsequent rainy frames retain/reuse it.
Disposal clears the native mesh and managed staging reference. Reentry creates one
new bounded stream. A dry interval after the first rain keeps its warm stream.

Warmed host constructor allocation falls 6,808 → 640 B. Tests compare all geometry,
colors, culled degenerate slots and painter order against the original equations
across 27 camera/height/time combinations. One thousand warmed staging calls
allocate zero bytes; disposal/reentry preserves the same vertices.

## Validation and limits

Use `./tools/test-changes.ps1` for the affected checks. Map/rain host regressions
are independently selectable:

```sh
dotnet run --project tests/AnimalTown.RenderingChecks -c Release -- --suites map,rain
```

Changes to validation/build gates select full coverage. Final validation includes
the managed, rendering, tooling, generated native-source guards and native host
checks, plus server protocol regression tests for the shared save codecs. No server
implementation is changed.

Array-payload savings depend on cache workload and whether rain has run. The
all-flat, never-rained case releases **216,064 B (211 KiB)** of retained payload
beyond the first pass; save allocation reductions are separate and must not be
added to this residency figure. Console peak memory, first-rain timing and dirty
map refresh cadence still need Dolphin/hardware measurement. No console build or
hardware result is implied by host validation.
