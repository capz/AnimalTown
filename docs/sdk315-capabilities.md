# SDK 318 and current Hypercube capabilities

AnimalTown pins DolphinDotNet SDK/Core/GameCube/Assets 0.1.0-preview.318.1
(source 4c8cd26a1b5f8003598463116960367ffa17418f), and Hypercube Core/GameCube
0.1.0-preview.28.1 (source b60ccf78aa1716debf88046f0cbf6d556671edd5).
The game and rendering-check runtime locks and offline asset-tool manifest
advance together. Hypercube 28 was compiled against SDK 315; direct runtime 318
references select the additive SDK upgrade, and package resolution accepts only
this reviewed version combination. See [water integration](sunshine-water-comparison.md).
NuGet.org remains the configured package source; local source references and
validation-only packages are excluded from the delivered build.

The SDK emits and validates the complete apploader image as part of the native
build. The game packs that exact output and includes its SDK license; the
separate boot-release downloader and lock are removed. The release manifest
records the actual image hash and the shared compiler/runtime/boot revision.

Hypercube's package now defaults to SDK MeshPasses for ordered single-texture,
unlit color/UV variants, retaining the portable comparison switch. It also
includes main's HCPK v2 texture-family importer, independent level CRC/UV metadata,
shared visible demand, projected footprint selection, sparse ladders, hysteresis
and dwell policies. Native and published-SDK validation use the same revision.

AnimalTown retains its existing direct cooked ground preview/mip uploads,
material-only water animation, indexed immutable terrain archives, shared model
textures, shadow projection and between-frame resource publication. Current
water/cloud effects use multiple texture bindings and keep their suitable draw
path. Progressive family metadata/selection is available for future large assets;
full variable-resolution runtime streaming still requires the SDK's planned
chunked detached upload primitive and additional engine residency work.

Validation covers both engine ordered-pass backends, allocation-free draw,
external package AOT/native consumption, texture-family format/demand tests,
AnimalTown's routed checks and targeted Dolphin startup, water and save paths.
Comparative emulator checks do not establish physical console throughput.
