# Terrain mip and water test captures

Unedited Dolphin framebuffer captures from
`AnimalTown-0.51.0+388d8677dede.nkit.iso`, built at commit
[`388d8677dedea2df261c5a35224154150ab1f204`](https://github.com/capz/AnimalTown/commit/388d8677dedea2df261c5a35224154150ab1f204).
The [capture run](https://github.com/capz/AnimalTown/actions/runs/44136311295)
booted that ISO directly with DSP HLE, OpenGL/software Mesa, a spring noon RTC
and a disposable memory card. These are emulator results; physical GameCube
hardware was not tested.

ISO SHA-256:
`46313dde475f75a2b19f24462b630263d110d5a95e438820fe012b1dc3468aa8`.

The six PNGs below are byte-for-byte copies of the actual X11 captures, with
shortened filenames only. They have no cropping, resizing, retouching or
generated content. [build.json](build.json) preserves the capture manifest;
[validation.json](validation.json) preserves the complete passing report with
its original 37 capture filenames. This directory contains the six selected
illustrations listed here.

| View | Image | Original capture filename |
| --- | --- | --- |
| Ordinary grass artwork | [Normal](normal.png) | `terrain-materials-normal.png` |
| Actual GX mip levels and blended transitions | [Mip bands](mips-auto.png) | `terrain-materials-mips-auto.png` |
| Debug option and help text | [Menu](menu-on.png) | `terrain-materials-menu-on.png` |
| Legacy river, first frame | [Water A](water-legacy-a.png) | `terrain-materials-water-river-legacy-a.png` |
| Legacy river, 120 rendered frames later | [Water B](water-legacy-b.png) | `terrain-materials-water-river-legacy-b.png` |
| Animated coastal water | [Coast](water-coast.png) | `terrain-materials-water-coast-b.png` |

The scenario checks the real menu, Auto/1/10 mip distances, reset to Auto,
disabled mipmaps and restoration of the original artwork. It compares actual
sampled colors through the native shadow blend and verifies the applied native
settings. Disabling mipmaps yields 99.997% level-zero samples; Auto and reset
mean levels differ by 0.000942.

Water checks cover archived and legacy rendering, animated/frozen/resumed
river pairs and the coast. Native draw observations verify both UV matrices,
texture and indirect bindings, and preservation of both foam color constants.
Both frozen river controls have zero measured color changes, crest-edge
reversals and UV movement; animation resumes in both paths. The test restores
archived rendering and confirms the saved card and town are unchanged.

These renderer captures precede the packaging-only integration of
[#112](https://github.com/capz/AnimalTown/pull/112); rendering files are unchanged
by that integration. The final integration candidate reruns the full scenario.
Its new captures, build manifest and report ship under `release/` inside the
Depot ZIP, outside the installed `apps/animaltown/` payload. The images and JSON
in this directory retain their explicit `388d8677dede` provenance.
