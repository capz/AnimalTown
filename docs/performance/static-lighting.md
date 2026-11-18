# Static lighting and shadow performance

The reference build is `8aa130eb4248` and the implementation build is
`ac4ba55bc5c0`. Both use main `d9bc5ab10529`, DolphinDotNet SDK
`0.1.0-preview.374.1` and the same pinned Linux GameCube build image. The
published reference ISO matched the SHA-256 recorded in its Depot bundle before
capture. [Reference release](https://github.com/capz/AnimalTown/releases/tag/v0.52.0%2B8aa130eb4248)

## Coverage baking

Each dirty bake clears the light nibble once and evaluates each source in a
clamped rectangle around its radius. Quantization is monotonic, so taking the
maximum of the quantized sources preserves the legacy maximum. The native
fixture compares the full 32 KiB terrain field and 32 KiB cloud composite with
the previous baker. It covers overlaps, gutters, full-capacity source lists,
source removal, terrain republication and off-field inputs.

The timings are host wall-clock microseconds per forced bake. Each value is the
median of three run averages with 1,000 bakes per run on an AMD EPYC 9V74 and
GCC 14.2 at `-O2`, pinned to one virtual CPU. The benchmark holds the source list fixed at each size, uses
2.7–2.8 m radii, and times the bake alone.

| Sources | Legacy | Bounded |
| ---: | ---: | ---: |
| 0 | 38.823 µs | 11.587 µs |
| 1 | 112.251 µs | 10.291 µs |
| 24 | 1,057.654 µs | 17.706 µs |

```sh
cc -std=c11 -O2 -Wall -Wextra -Werror tests/native-shadow-union.c -lm -o /tmp/native-shadow-union
/tmp/native-shadow-union
taskset -c 4 /tmp/native-shadow-union --benchmark
```

The native bridge fixture sweeps all 256 gain values and compares terrain,
composite and actor page bytes, raster counts and texture publication calls.
Changing gain alone leaves the packed fields and page publication idle. Moving
one lamp publishes one updated field. A room transition can refresh its own
actor page while retaining outdoor coverage.

## Shared trees

The three meshes use 84, 68 and 60 triangles and share one 32×32 I8 texture
with mipmaps. Vertex alpha stays at 255. Local ambient factors range from 221 to
255 and multiply the existing seasonal RGB. The same per-kind meshes and
placement transforms serve the original batches.

A .NET 8 host microbenchmark reused the three output streams and measured
mesh generation plus palette application, cycling the four seasons. Across
three runs of 20,000 preparations, the median changed from 12.55 to 17.33 µs
per three-mesh prepare. Managed allocations increased by 960 temporary bytes
per prepare, including object and array overhead. The factors are computed on
initial, season or detail-scale preparation and stored in the existing native
vertex colors.

Fences use uniform white vertices and a separate material tint. Per-vertex AO
on this path would require enabling an indexed color stream or using the SDK's
expanded vertex submission path.

## GX submission

Both versions use four appended TEV stages, three texture coordinate generators,
one IA4 binding and two IA4 sample orders. This gives five total stages for
the regular ground material and eight for the four-stage water effect. The
recording fixture measures outdoor constant-alpha selector calls falling from
three to one. The pinned
libogc2 implementation emits one five-byte BP command per call, so the change
removes two BP commands and 10 FIFO bytes per outdoor receiver submission.
Indoor position projection still selects constant alpha for its always-on gate.

The four stages preserve the independently modulated environment tint, receiver
flag × shadow coverage × opacity, shadow blend and warm light accumulation.
The recording fixture checks both packed channels, the 16-stage limit, stage
register destinations and alpha passthrough across direct, indexed and indoor
scopes. It also checks warm RGB for all 256 gain values. Shadow storage measures
522,016 bytes with 160 pages, measured on the host and confirmed in the
compiled PowerPC allocation calculation.

## Matched Dolphin runs

Dolphin 2503 rendered both NKit images using OpenGL on software Mesa with DSP
HLE. Each run began with a copy of the same disposable seed-3 `village-roads`
card. The terrain inspector placed the player at the tree-lined ramp. The
noon profile used the existing 480p AA study capture, which logged 240 frames.
The table uses the 235 frames after warmup. Dynamic clouds and actors continued
updating during the captures.

| Stationary ramp metric | Reference | Implementation |
| --- | ---: | ---: |
| SDK-accounted native bytes | 2,883,932 | 2,883,932 |
| Resident texture bytes | 1,037,024 | 1,037,024 |
| Draw submissions per frame | 174 | 174 |
| Triangles per frame | 7,009 | 7,009 |
| Median guest CPU per frame | 8.607 ms | 8.610 ms |
| Median guest terrain submission | 0.201 ms | 0.201 ms |
| Median guest frame interval | 16.683 ms | 16.683 ms |
| Median GX completion wait | 0.003 ms | 0.003 ms |
| Managed allocations in the sampled frames | 0 | 0 |

The frame timings have overlapping ranges, and GX completion wait measures
outstanding work at the fence. Physical GameCube GPU duration and SD launcher boot remain hardware
validation items. [Reference samples](../screenshots/static-lighting/profile-before.json)
[Implementation samples](../screenshots/static-lighting/profile-after.json)

| Captured view | Guest RTC at boot | Reference | Implementation |
| --- | --- | --- | --- |
| Spring grove | Apr 15, 2028, 06:30 | [Before](../screenshots/static-lighting/spring-before.png) | [After](../screenshots/static-lighting/spring-after.png) |
| Summer grove | Jul 15, 2027, 12:00 | [Before](../screenshots/static-lighting/summer-before.png) | [After](../screenshots/static-lighting/summer-after.png) |
| Fall grove | Oct 15, 2027, 18:15 | [Before](../screenshots/static-lighting/fall-before.png) | [After](../screenshots/static-lighting/fall-after.png) |
| Winter grove | Jan 15, 2028, 22:00 | [Before](../screenshots/static-lighting/winter-before.png) | [After](../screenshots/static-lighting/winter-after.png) |
| Winter lamp pool and path | Jan 15, 2028, 22:00 | [Before](../screenshots/static-lighting/lamp-before.png) | [After](../screenshots/static-lighting/lamp-after.png) |
| Cottage interior | Jul 15, 2027, 12:00 | [Before](../screenshots/static-lighting/interior-before.png) | [After](../screenshots/static-lighting/interior-after.png) |
| Coast and distant trees | Jul 15, 2027, 12:00 | [Before](../screenshots/static-lighting/coast-before.png) | [After](../screenshots/static-lighting/coast-after.png) |

The summer tour entered and left a cottage, changed the indoor camera angle,
walked onto the ramp and cycled through river, river corner, bridge and coast
views. The PNGs are raw X11 captures. The fixture comes from
`dotnet run --project tests/AnimalTown.Checks -c Release -- --path-fixture OUTPUT`,
with `tools/profile-card-fixture.py` applying the `village-roads` records to a
formatted disposable card. The capture route uses `tools/iso-smoke.py`'s pipe
controller, RTC setup and Xvfb capture. The actual ISO, card and image hashes
identify each pair. [Capture provenance](../screenshots/static-lighting/provenance.json)

## Validation

| Check | Result |
| --- | --- |
| `pwsh ./tools/test-changes.ps1 -BaseRef origin/main -PlanOnly` and `-NativeInProcess` | Selected managed, rendering, tooling, native and PostgreSQL suites passed in the pinned builder. |
| Native shadow union and lifecycle | 71,635 union checks, the GX recording fixture and the bridge lifecycle fixture passed. ASan and UBSan passed with leak detection disabled in the sandbox. |
| Managed and rendering | 2,342,378 gameplay and 329,009 rendering checks passed, including the clock and tree fixtures. |
| `pwsh ./tools/build-linux.ps1 -Target game` | Patched AOT native sources, disc DOL, independently compiled Depot DOL, strict app payload verification, and the 139-file NKit round trip passed. |
| `pwsh ./tools/build-linux.ps1 -Target package` | Local same-commit game, tools and backend packaging passed. |

PR #114 uses the local results. Its GitHub Actions run was skipped at the
maintainer's request because the workflow automatically publishes a prerelease.
