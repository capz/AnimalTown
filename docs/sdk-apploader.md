# SDK-owned apploader

AnimalTown uses the complete `out/native/apploader.img` emitted by its pinned
DolphinDotNet SDK package. The compiler bundle owns the loader C, linker script,
PowerPC build and IPL header. `make apploader` builds the game and validates its
DOL's DMA alignment, entry point and reserved loader memory before disc packing.
The game writes the complete image at `0x2440` and keeps its DOL at `0x10000`.

The compiler, runtime and boot source now share the SDK version in `global.json`
and the committed package locks. There is no separate boot release resolver or
download pin. The SDK's MIT boot-component license is copied from the generated
bundle into the disc filesystem. Build revisions record the actual compiled
image's SHA-256 and SDK source identity alongside the package hashes.

`SdkApploader.Validate` still checks the complete IPL header, fixed entry, DMA
size and exact payload length before writing the disc. The `nkit` checks include
malformed image rejection. Full-size ISO, direct NKit, title/build identity,
disc/save artwork and same-commit backend requirements remain in effect.

See the [SDK boot documentation](https://github.com/capz/DolphinDotNet/blob/main/docs/gamecube-apploader.md).
Physical GameCube IPL/DVD/Swiss/GC Loader testing is separate from Dolphin boot
validation and must be reported independently.
