# Row visibility follow-up

Validated game: `c5c388ea7fad5e3470dc7af7ccaca0e845ee8373`.
[Release 0.7.12.c5c388ea7fad](https://github.com/capz/AnimalTown/releases/tag/v0.7.12.c5c388ea7fad), with full bootable ISO, direct NKit ISO and the same-commit backend.
[Machine-readable measurements](2026-11-09-row-culling.json).
Comparison build: `a53fa6d2e020cd34581ae221df5de5ebf62cc72f`.

## Change

Each terrain row shares its vertical frustum bounds and cylinder horizon across
all east/west columns. Selection previously repeated generic sphere and box
checks for each section. It now tests a vertical slice once per row and computes
horizontal acceptance intervals; individual columns use those intervals.
Current-view, directional-prefetch and horizon rules are retained. A 1 mm
rounding guard keeps edge geometry conservatively visible. No vertex buffers,
textures or additional persistent caches are allocated by this change.

An independent sweep compares 238,464 bounds with the existing generic camera
frustum: no previously accepted bounds are dropped, and two extra borderline
bounds are retained. A separate 2,500-position full-cache sweep produces exactly
the same section addresses and visibility. The host selection microbenchmark
uses 30.4% of the previous time; this is not a console timing claim.

## Native measurements

Both images boot directly in Dolphin with software OpenGL, DSP-HLE and 1 GiB
shared memory. Each starts from a separate copy of the same regression card,
with spring RTC, shadows and material animation enabled. Six walking/running
directions each run for eight NTSC guest seconds, followed by a neutral interval.
Warmup and saving are excluded by the route markers. These are single controlled
captures per mode/build, with capture overhead; physical GameCube is untested.

| Instrumented route, milliseconds | Before | Row visibility |
| --- | ---: | ---: |
| Selection, 95th percentile | 5.3 | 2.5 |
| Selection, maximum | 5.7 | 2.8 |
| Terrain preparation, 95th percentile | 6.0 | 3.1 |
| Terrain preparation, maximum | 7.4 | 7.4 |
| Frame interval, 95th percentile | 33.3 | 16.6 |
| Frame interval, maximum | 33.3 | 33.3 |

Both instrumented routes have zero loading, missing-terrain and recovery frames.
One garbage collection occurs in each. UV/material work stays below the 0.1 ms
counter resolution, texture image operations remain zero, mesh publication peaks
at 0.4 ms and native masks at 1.5 ms. The maximum preparation/frame interval has
not been eliminated; improving typical selection leaves occasional longer work.

| Profiler display off | Before | Row visibility |
| --- | ---: | ---: |
| Presentations in the marked route | 3,223 | 3,276 |
| Mean interval, ms | 17.0561 | 16.7801 |
| 95th-percentile interval, ms | 16.6833 | 16.6833 |
| Maximum interval, ms | 33.3667 | 33.3667 |
| Intervals spanning two refreshes | 2.2346% | 0.5802% |

The profiler-off windows both span 54.9716 guest seconds, including neutral and
screenshot overhead. Median intervals remain 16.6833 ms. External cadence uses
AVI video chunks, including empty repeats for VI time; its resolution is one
NTSC VI. Finalized AVI frame/packet metadata matches the incremental parser.
These results do not establish locked 60 FPS.

## Save and build checks

Native post-travel saving returns with Saved status and writes a complete
checksummed generation 2 after loading generation 1. The instrumented run's first
immediate file check raced Dolphin's asynchronous raw-card flush; Saved was
visible and generation 2 was verified after clean emulator shutdown. The scenario
now waits up to 30 seconds for the file flush. Its profiler-off rerun verifies
the complete new generation while the emulator is still running.

CI passes 2,488,625 gameplay/render/texture and 323,098 rendering-service checks,
backend integration and native fixtures. Existing stress tests retain all visible
terrain across 16,779 normal/stalled frames and 720 fully starved diagonal frames.
The 4 MiB managed heap, 16 MiB native resource budget and 256-section cache remain.
The published full ISO is 1,459,978,240 bytes; NKit is 88,389,632 bytes. Checksums
and clean same-commit console/backend identities are verified. Physical hardware
validation remains outstanding.

## Reproduce the marked-window summaries

Run [guest_travel.py](guest_travel.py) using the Docker command in the
[original report](README.md#reproduction). Add `--no-overlay` for the normal-play
comparison and `--save-after-travel` to verify explicit saving after the route.
Generate JSON summaries without manually selecting warmup/save frames:

```sh
python3 docs/performance/summarize_travel.py OUTPUT_DIRECTORY
python3 docs/performance/summarize_travel.py OUTPUT_DIRECTORY --capture ARCHIVED.mkv
```

Instrumented runs decode the checksummed bars. Profiler-off runs use the preserved
AVI presentation index in travel-scenario.json. The summarizer reproduces the
preceding published results exactly. Native frame-counter wrap is rejected
rather than silently combining disjoint windows. Keep AVI indices before
archiving video; lossless pictures alone do not preserve empty repeat chunks.
