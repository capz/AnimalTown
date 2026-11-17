# Water removal test screenshots

Unedited Dolphin screenshots for [AnimalTown PR #105](https://github.com/capz/AnimalTown/pull/105).
This evidence branch stores test captures for the PR note.

- Tested game source: `a9e4c3c33e3225df38b9e979ea8f632a6f73f492`.
- Build: `0.44.0+a9e4c3c33e32`, native GameCube NKit image.
- Runtime profile: 0; full-resolution AA off; component cable; spring RTC fixture.
- Environment: Dolphin OpenGL with DSP HLE. These are emulator results, not physical-console measurements.
- Validation: `tools/test-changes.ps1 -BaseRef origin/main -NativeInProcess`, clean native console/backend builds, NKit round-trip verification and Dolphin water smoke passed.
- Ocean animation check: 70,688 water pixels; 56,267 changed between samples eight seconds apart.
- Additional debug-control tour: rain and rough ocean enabled, then river, bank corner, bridge, coast and estuary; menu return checked.

| File | What it shows |
| --- | --- |
| `calm-ocean.png` | The remaining ocean material at the western town boundary. |
| `rainy-bridge.png` | Lowered river water continuing beneath a bridge during rain. |
| `rough-estuary.png` | Rough ocean and the river mouth during rain. |
| `debug-menu.png` | The testing page without the retired water-style selector. |

[Water renderer CI](https://github.com/capz/AnimalTown/actions/runs/44080797080)
and [build/release CI](https://github.com/capz/AnimalTown/actions/runs/44080797112)
passed for the tested source.
