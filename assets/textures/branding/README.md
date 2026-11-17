# Smalltown Forest branding

`smalltown-forest-logo.png` is the approved full-color title artwork, preserved
at its original 1774×887 resolution. `disc-banner.png` is the separately approved
96×32 opaque banner. Their original SHA-256 hashes are recorded in
`provenance.json`; cooking fails if either source changes without updating that
reviewed provenance.

Run the focused cooker and native-format checks with:

```powershell
dotnet run --project tools/AnimalTown.Tools -- brand-check .
```

The normal `art`/disc build also runs these checks. Outputs are reproducible:

| Output | Native layout | Pixel bytes |
| --- | --- | ---: |
| `out/disc/wordmark.dndtex` | 512×256, GX RGB5A3, 4×4 tiled | 262,144 |
| Disc/save banner | 96×32, opaque RGB5A3 (RGB555), 4×4 tiled | 6,144 |
| Save icon | 32×32, opaque RGB5A3 (RGB555), 4×4 tiled | 2,048 |

The title package has an additional 32-byte DNTX v1 header. Its existing disc
filename is retained so title, profile-selection and onboarding screens share
one cache identity. The runtime uploads eight 128×128 RGB5A3 pages, totaling
256 KiB, compared with 512 KiB for the previous monochrome wordmark.

The cooker clears only nearly invisible background noise at alpha values 0–8,
then crops to the visible source bounds `(72,75,1633,759)`. Alpha-weighted area
resampling fits that artwork into a 508×236 rectangle at `(2,10)` inside the
512×256 texture. It preserves the aspect ratio and at least two transparent
pixels on every edge. It does not redraw the lettering, recolor the artwork,
or generate mipmaps for this fixed-size UI logo.

RGB5A3 retains full color and a smooth three-bit alpha silhouette. CI8/TLUT
packages are not supported by the game's current decoder and atlas upload
path; CMPR provides only one-bit alpha. The font atlas remains IA8 and is
unaffected by the color-logo conversion.

The save icon clips the one right-hand acorn directly from the approved full
logo, reduces it without stretching, and composites it onto a warm cream
background. Its source-coordinate path is documented in `BrandTextureAssets`;
no wooden sign, lettering or unrelated icon is substituted.

Review PNGs, including a native RGB5A3 decode of the logo, and a memory report
are emitted in `out/brand`. Checks compare native tile/channel/alpha output
against the resampled artwork, verify the actual runtime atlas round trip and
memory budget, and verify the banner/icon's opaque RGB555 pixels.

Disc BNR1 and alternate BNR2 metadata are generated from the current build
version and commit. Static BNR files are deliberately not used as build inputs.
The release publishes `opening.bnr` (6,496 bytes, the exact US disc payload)
and `opening-pal.bnr` (8,096 bytes, six English fallback text slots) directly,
with their hashes in `BUILD.txt` and `SHA256SUMS.txt`. Both use the same approved
96×32 artwork. The BNR1 image is also the memory-card banner; PAL metadata does
not change the current disc's US region.

The visible title is **Smalltown Forest**, by **Capz**. The legacy
`AnimalTown / <version>` boot-header title, `AMTE01` disc ID, package names and
save identities stay compatible with the pinned updater and existing cards.
Renaming those identifiers requires a coordinated migration beyond artwork.

Release CI runs the focused Dolphin `--title-logo` scenario for branding and
shared-header changes. Its original title, picker, name-entry and return-to-title
PNGs are published alongside a report and a manifest that binds them to the
exact source commit, build version and NKit ISO SHA-256. These are runtime
captures; `out/brand/wordmark-native.png` is only a decoded texture preview.
