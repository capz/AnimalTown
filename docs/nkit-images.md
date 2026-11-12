# Compact SD-card images

Releases include `AnimalTown-VERSION.nkit.iso` for GC Loader SD-card storage.
Copy the `.nkit.iso` onto the SD card and select it in GC Loader firmware. Keep that
extension; do not convert it to GCZ or RVZ for the GameCube loader. Only one
image is needed on the card. Physical direct GC Loader testing remains separate
from Dolphin testing.

The title's Update option supports the installed image's chosen filename and
folder. For direct firmware boot, use the updater repository's provisioning tool
with the host-side installed path, for example
`--image 'games/AnimalTown/My Town.nkit.iso'`. It records the canonical target in
the SD-root `animaltown-updater/image-path.txt`; the game checks its identity and
current version before passing the exact path to the updater. Missing/conflicting
path information stops Update. See [updater setup](updater-build.md). After a
verified replacement, follow **Power off/on** and reopen the image in firmware.

NKit ISO removes unused disc padding and reorganizes file placement. The game
files are preserved; this is not a ZIP that the console must decompress. A
production sample shrank from 1,459,978,240 to 88,160,256 bytes (93.96% less SD
space). Sizes vary with build contents. The NKit ISO is the sole console image
published, directly without a ZIP wrapper, so no extraction is needed.

Full-size source and round-trip ISOs exist only in temporary conversion
directories. Both are deleted on success or failure; the full-size source is
not retained as a build output or uploaded to releases. Smaller files do not
remove filesystem fragmentation; copy to a card with sufficient contiguous free space.

Console builds use [NKit 3.2.0](https://github.com/Nanook/NKit/releases/tag/v3.2.0),
with platform-specific archive SHA-256 pins in `build/nkit.lock.json`. The Linux
builder preloads the legacy-glibc x64/ARM64 CLI; desktop packaging can cache the
matching Windows/macOS CLI. The executable is verified against its trusted ZIP
entry on each use. NKit's MIT notice accompanies cached tools and the builder
image. No local NKit configuration is loaded and source deletion is
never enabled.

Every console build must pass:

1. The usual full-size ISO, title, artwork, DOL and FST checks.
2. NKit conversion with verification, followed by expansion to a temporary ISO.
3. Exact restored/source SHA-256 and size comparison, plus a recheck that the
   temporary source did not change.
4. A physical compact-layout check: the custom apploader and fixed-address DOL
   remain intact, identity/BI2 match, file ranges are valid, and every payload
   including banners and save art has the same size and SHA-256.

NKit normally packs files at four-byte boundaries. The SDK's disc filesystem
reads aligned 2048-byte sectors and copies each requested prefix, so it supports
these placements. The GameCube apploader's DOL stays at 0x10000 and its loadable
sections keep 32-byte DMA alignment. This is checked rather than inferred from
a successful round trip: a reversible container alone does not prove bootability.

`nkit-validation.json` records converter provenance, both sizes/hashes, the
round-trip result and per-file hashes. Packaging verifies that the complete NKit
image and every file still match that report, without rebuilding or expanding it.
`SHA256SUMS.txt` covers only the NKit image, the other release archives and the
reports. Publication proceeds only after verification. No game/save schema
changes are needed.

For a local console build:

```powershell
./tools/build-game.ps1
```

The build script creates the verified NKit output and removes its raw source.
The release script publishes that output without a second conversion. The tool cache defaults to
`out/tool-cache/nkit`; `ANIMALTOWN_NKIT_CACHE` selects a preinstalled cache.
`nkit-check` exercises compact offsets and deliberately corrupted image fixtures.
The real conversion additionally checks every release's exact full-image round
trip. Direct `.nkit.iso` Dolphin boot must test actual asset loading as well.
