# SDK-owned apploader

AnimalTown no longer builds a local apploader C file or linker script. It uses
DolphinDotNet's complete `apploader.img`, including the IPL header, native code
and initialized loader BSS. The game packer writes those bytes at disc offset
`0x2440` and keeps its DOL at `0x10000`.

The SDK source revision, immutable boot-component release, image/license lengths
and SHA-256 hashes are pinned in `build/apploader.lock.json`. The resolver caches
only verified files, rejects a mismatched download before replacing output and
includes the SDK's MIT license in the disc filesystem. Build revisions record
both the runtime SDK and boot-component source/hash. Normal release requirements
still include full-size ISO, direct NKit image, title/build identity, disc/save
artwork, same-commit backend and integrity manifests.

The boot component is deliberately independent of AnimalTown's existing runtime
and compiler NuGet pins. It has no managed runtime dependency. This migration
therefore does not require an unrelated rendering, storage or compiler upgrade.
The SDK also emits the same component from new NuGet/standalone GameCube builds;
see [SDK boot documentation](https://github.com/capz/DolphinDotNet/blob/feat/gamecube-apploader/docs/gamecube-apploader.md).

`tools/resolve-apploader.ps1` runs before packaging. It never fetches `latest`,
compiles loader C or edits the SDK. `SdkApploader.Validate` separately checks the
complete header, entry, DMA size and exact payload length. The `nkit` test group
includes these layout checks and the isolated resolver cache/failure tests.

Physical GameCube IPL/DVD/Swiss/GC Loader testing must still be reported
separately from source checks and Dolphin boot validation.
