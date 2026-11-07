# Compact SD-card images

Releases include `AnimalTown-VERSION.nkit.iso.zip` for Swiss/GC Loader use.
Extract the `.nkit.iso` onto the SD card and launch it through Swiss. Keep that
extension; do not convert it to GCZ or RVZ for the GameCube loader. Only one
image is needed on the card. Physical Swiss/GC Loader testing remains separate
from Dolphin testing.

NKit ISO removes unused disc padding and reorganizes file placement. The game
files are preserved; this is not a ZIP that the console must decompress. A
production sample shrank from 1,459,978,240 to 88,160,256 bytes (93.96% less SD
space). Sizes vary with build contents. The ZIP download savings are smaller
because the existing full-ISO ZIP already compresses blank padding well.

The canonical full-size bootable ISO and its ZIP remain available for archival,
fallback and disc burning. NKit is an additional delivery artifact, not a
replacement for the required canonical build. Smaller files do not remove
filesystem fragmentation; copy to a card with sufficient contiguous free space.

Packaging uses [NKit 3.2.0](https://github.com/Nanook/NKit/releases/tag/v3.2.0),
with platform-specific archive SHA-256 pins in `build/nkit.lock.json`. The Linux
builder preloads the legacy-glibc x64/ARM64 CLI; desktop packaging can cache the
matching Windows/macOS CLI. The executable is verified against its trusted ZIP
entry on each use. NKit's MIT notice accompanies cached tools and the builder
image. No local NKit configuration is loaded and source deletion is
never enabled.

Every package must pass:

1. The usual full-size ISO, title, artwork, DOL and FST checks.
2. NKit conversion with verification, followed by expansion to a temporary ISO.
3. Exact restored/source SHA-256 and size comparison, plus a recheck that the
   canonical source did not change.
4. A physical compact-layout check: the custom apploader and fixed-address DOL
   remain intact, identity/BI2 match, file ranges are valid, and every payload
   including banners and save art has the same size and SHA-256.

NKit normally packs files at four-byte boundaries. The SDK's disc filesystem
reads aligned 2048-byte sectors and copies each requested prefix, so it supports
these placements. The GameCube apploader's DOL stays at 0x10000 and its loadable
sections keep 32-byte DMA alignment. This is checked rather than inferred from
a successful round trip: a reversible container alone does not prove bootability.

`nkit-validation.json` records converter provenance, both sizes/hashes, the
round-trip result and per-file hashes. `SHA256SUMS.txt` covers both image ZIPs,
both extracted images and the reports. Temporary expanded images are removed;
publication only proceeds after verification. No game/save schema changes are
needed.

For a local full build:

```powershell
dotnet build tools/AnimalTown.Tools -c Release
./tools/compress-nkit.ps1 -Iso out/AnimalTown-VERSION.iso `
  -Destination out/AnimalTown-VERSION.nkit.iso `
  -Report out/nkit-validation.json
```

The release script runs this automatically. The tool cache defaults to
`out/tool-cache/nkit`; `ANIMALTOWN_NKIT_CACHE` selects a preinstalled cache.
`nkit-check` exercises compact offsets and deliberately corrupted image fixtures.
The real conversion additionally checks every release's exact full-image round
trip. Direct `.nkit.iso` Dolphin boot must test actual asset loading as well.
