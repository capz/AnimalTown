# Texture streaming and static reuse validation

Runtime: [`a39b6211f733`](https://github.com/capz/AnimalTown/commit/a39b6211f733d76811e45d95f65073a38bf6621f),
[release 0.13.9.a39b6211f733](https://github.com/capz/AnimalTown/releases/tag/v0.13.9.a39b6211f733).
Dolphin software OpenGL, DSP HLE and the standard 24 MiB configuration.
These are emulator behavior/memory measurements, not physical DVD qualification.

The same copied fixture as the prior ARAM validation places all six NPCs in room
16. The original card is untouched. Two house entry/exit cycles, camera rotation
and reuse switches leave the copied card byte-identical until **Save now**. That
explicit action commits a new complete save generation; closing Settings does not
write another. The title logo releases exactly 512 KiB.

| Rounded SDK physical-memory reading | Prior same-fixture room | Current room |
| --- | ---: | ---: |
| MAIN | 23.35 MiB | 21.47 MiB |
| ARAM | 13.78 MiB | 12.18 MiB |

The current warm view has about 2.53 MiB of MAIN headroom, a 1.88 MiB reduction
from the baseline. These are settled readings, not an assertion about peak memory.
The first outside SDK screenshot occurs before all age-based evictions settle.
Ground's shared seven-level image remains 43,712 bytes; it is not streamed.

[Lifecycle measurements](lifecycle.json) include exact region hashes: fully
sharpened views match across unloading/re-entry, and all six off/on comparisons
for display lists, placement and live poses change **zero RGB channels** in the
fixed room region. The north-wall check catches a previously missing batch flush;
[the fixed room](asset-interior-sharp.png) retains its walls, materials and shadow
receiver. Immediate room entry can still show softer optional images.

Ordinary approach/re-entry has no terrain staging or mesh creation/update/release
in the observed interval. Teleporting the inspector does include one mesh creation
and eight releases, so it is reported separately. First/second door transitions
obscure 23/24 encoded frames behind fades; those are excluded rather than treated
as passing timing samples. Observed entry frame intervals peak at 17.8 ms.

The SDK totals overlay covers telemetry pixels, so the outdoor timing check uses
[a separate unobscured capture](travel.json). It contains 950 consecutive verified
frames, including 297 moving frames, and zero loading/missing/recovery flags.
Frame intervals are 16.6 ms throughout at 0.1 ms resolution. Combined preparation
is 0.2 ms median, 1.8 ms p95 and 3.2 ms maximum. These intervals also include
stationary/collision-blocked frames; they are not a sustained-running benchmark
or a claim that all storage patterns have identical performance.

The texture HUD's cumulative worst work value is 15.273 ms including startup;
the 1 ms scheduling deadline does not establish a hard single-quantum bound.
The captured maximum detached DVD read is 90 ms, completing across up to seven
render frames. The room's working set recorded zero texture-cache hits and zero
currently shared skin bytes. Therefore this fixture establishes correctness,
not a measured ARAM-hit or pose-sharing speed gain. Production-loader host checks
separately verify cache hits, corrupt-cache DVD fallback, exact shared pose hashes,
copy-on-write, disposal and matching updates without repeated allocation splits.

All seven optimization switches default on. Current artwork remains RGBA8 and
uses its existing safe mip range: no smaller-format/deeper-atlas policy has been
approved. Cooking emits per-source contact sheets and error/alpha reports for
review. There is no automatic lossy-format acceptance.

[Validation identities](validation.json) record screenshots, raw captures, checks,
release hashes and the full-ISO download verification. The published ISO is
1,459,978,240 bytes; its SHA-256 matches the NKit reconstructed round trip. The
93,001,728-byte NKit image boots directly. Clean console and backend manifests
identify this same runtime commit. Both image types ship directly without ISO ZIPs.

Physical GameCube, physical DVD seek/throughput, long-play and power-loss storage
qualification remain outstanding. No physical-hardware frame-time claim is made.

## Reproduce

Copy the residency fixture card into each output directory first; never reuse the
source card as a writable emulator card. Run inside `animaltown-dolphin:latest`
with software GL and `--shm-size=1g`:

```sh
python3 docs/performance/texture_streaming.py IMAGE OUTPUT --stream-travel \
  --season spring --dsp-hle --testing-rows 31 --reuse-ab --texture-manual-save
python3 docs/performance/analyze_texture_streaming.py OUTPUT --capture CAPTURE.avi
python3 docs/performance/texture_travel.py IMAGE TRAVEL_OUTPUT --stream-travel \
  --season spring --dsp-hle
python3 docs/performance/analyze_texture_streaming.py TRAVEL_OUTPUT \
  --travel-only --capture TRAVEL_CAPTURE.avi
```

For this lifecycle capture, which also walks with SDK totals visible, the analyzer
used `--skip-walk`; the separate capture supplies the unobscured travel result.
The scenario now hides SDK totals before walking when `--texture-walk` is selected.
The final runtime received the routed managed checks; the full preceding runtime
and local build-gate run additionally passed tooling, backend integration, native
fixtures and ASan/UBSan. The only later runtime change is the wall-batch flush,
which the final native images exercise.
