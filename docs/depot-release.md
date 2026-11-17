# Dolphin Depot app releases

Every console build emits both `AnimalTown-VERSION.nkit.iso` and
`AnimalTown-VERSION-depot.zip`. They use the same source snapshot, release
version, runtime profile and cooked assets as the separately built backend.
The existing merged-PR minor counter is unchanged. NKit remains a raw release
asset; it is never wrapped in a ZIP. Full-size conversion ISOs remain temporary.
The display name and approved banner artwork follow main's Smalltown Forest
branding. The AnimalTown repository, package filenames, install slug and save
identity remain unchanged.

The additional ZIP installs directly as `apps/animaltown/`, with the real game
at `default.dol`, `opening.bnr`, save artwork, cooked content, notices,
`BUILD.txt`, `version.md`, `version.json`, `content.id` and
`depot-package.json`. There is no extra wrapper directory. The latter records
the complete file list and SHA-256 hashes. A clean extraction is checked before
publication. GitHub's asset SHA-256 digest covers the app ZIP. Backend binaries,
host tools, authoring assets, retired Sunshine textures and updater/credentials
are excluded from the installed app by an explicit whitelist.

The release uploads five files: the NKit ISO, this Depot ZIP, the server
ZIP, the compiled-tools ZIP and the compact `update.json` feed. All supplementary reports, checksums, banners,
updater DOL/notices and captures are stored under `release/` inside this ZIP.
`release/manifest.json` binds each supplementary file's SHA-256 to the app's
source commit and version; these files are never moved into `apps/animaltown/`.
`release/SHA256SUMS.txt` covers the ISO, server/tools ZIPs and primary reports.
It excludes itself and the containing Depot ZIP to avoid circular hashes.
Use GitHub's asset digest for the containing ZIP. `tools/release-bundle.py`
verifies and extracts reports; optional evidence workflows append their captures
under `release/scenarios/` while retaining the original app and reports.

Source/authoring kits and Python source tools are not release assets. GitHub
always displays automatic source ZIP/tar.gz links; `.gitattributes` makes those
archives README-only. Obtain complete source through Git instead.

Release notes are capped at 1 KiB and the full GitHub release response is checked
against the updater's 32 KiB metadata limit before publication. The updater reads
the stable GitHub release API directly. Older updaters can continue using the
standalone `update.json`, which retains its original compact format and 2 KiB
limit: `tag_name`, `draft` and one ISO asset's `name`, `browser_download_url`,
`size` and `digest`. Its exact bytes are also archived as `release/update.json`.
Saves are unaffected.

## Startup and storage

The game is compiled twice. The disc bootstrap requires its exact identified
disc. The SD app bootstrap never mounts a disc, including when no launch
arguments exist. Keeping this decision in the executable avoids mistaking an
unrelated or previously mounted AnimalTown ISO for a standalone installation.
Both executables embed the expected commit/version/source-digest/profile
identity. `content.id` must match before configuration, splash or other assets
are read. This is a build-mixing check, not a cryptographic trust mechanism.

The app uses the SDK-owned `Dolphin.IO.LaunchContext` API. A recognized,
validated executable path determines the installation directory. SDK mount
names `cardb:/`, `sd:/` and `gcloader:/` are used internally; physical launcher
aliases `sdb:/`, `sd2:/`, `sd2sp2:/` and `gcldr:/` normalize to those mounts.
A launcher's generic `sd:/` does not identify an adapter and uses the fixed
probe policy instead, so a Slot B install is not mistaken for SD2SP2. Paths
containing traversal, controls, repeated separators or non-DOL filenames fail.
A recognized path with missing/wrong assets fails without trying another copy.
Unrecognized or absent launcher paths only probe `apps/animaltown/` in this
fixed order: SD2SP2, Slot B SD Gecko, GC Loader. The first complete matching
build wins. A matching but incomplete package fails instead of borrowing files.
No working-directory assumptions, folder scans or per-load discovery occur.

The selected content root is immutable. Synchronous reads, asynchronous floor
and model loads, texture/model cache keys, variant selection, cleanup probes
and save artwork all use it. Payloads are read-only. Existing memory-card
formats and settings remain unchanged: AMTE/01 and the consolidated AnimalTown
save item in Slot A. The separate-card gameplay UI remains outside this change.
Slot A SD Gecko
is rejected because title options and debug preferences require memory card A.
SD2SP2 plus memory card A and Slot B SD Gecko plus memory card A are the initial
target configurations. GC Loader support is implemented but not physically
qualified. Emulator results do not establish SD throughput or launcher behavior
on hardware.

The available Dolphin 2503 build does not emulate SD Gecko or SD2SP2. It can
validate the native disc regression path, but cannot qualify standalone SD app
I/O. Startup/device policy is exercised with host mount fixtures and the production
bootstrap through the published SDK's native AOT/host filesystem runtime; real SD app
boot, save/reload and launcher handoff remain hardware qualification items.
An ISO-mounted run must never be presented as proof of app-directory startup.

The existing title Update action reports "Update this installation through
Dolphin Depot." for the app, using the existing status area. It cannot launch
the disc updater or modify ISO installations. The disc build keeps its updater.

## Build and validation

`tools/build-game.ps1` builds separate native bundles in `out/native` and
`out/native-depot`. The second uses `DepotProfile=true` and the SDK launch
capture. `depot-receipt` binds its native DOL hash to the same content identity.
The existing game native patches accept an explicit bundle directory; no
game-owned incoming-argv shim is added. Banner/build metadata generation is a
separate tool command and does not require successful ISO construction.

`tools/test-changes.ps1` selects the `content` managed suite and `depot` tooling
suite for their respective components; build/policy changes retain full checks.
Startup fixtures cover disc/app isolation, missing argv, device priority,
relocated paths, malformed paths, stale/incomplete installations and bounded
identity reads. Fourteen production-bootstrap AOT cases also exercise the real
SDK argument capture and synchronous/asynchronous filesystem reads, updater
rejection and closed handles, with and without garbage-collection stress.
Package fixtures cover clean extraction, exact paths/case,
duplicate executables, symlinks, corruption and stale native build receipts.

The release publisher re-verifies the app ZIP against the exact build before
uploading it alongside the NKit image and same-commit backend. No independent
version, SDK runtime override or unverified replacement executable is accepted.

## Registry submission remains separate

The schema-valid draft is [depot-registry/animaltown/manifest.json](depot-registry/animaltown/manifest.json).
It targets only `AnimalTown-*-depot.zip`, then extracts and moves the complete
app directory. Hash-suffixed development tags, legacy build tags and old
four-component versions are ignored. The source resolver must also exclude
draft/prerelease releases; verify its behavior when submitting.

Validate against DolphinDepot/registry commit
`fb40d8afc9355d7cc273dcc0a9ce8c6adf090db1` (or re-review the current schema):

```sh
cp -R docs/depot-registry/animaltown /path/to/registry/registry/
cd /path/to/registry
npm ci
npm run lint:json
npm run validate -- --all
# Then run the registry's indexer test-manifest command against the entry.
```

Before opening a hosted catalog submission, establish rights-holder permission
for original code and all bundled artwork/sound, account for third-party terms,
and complete a download/install/boot test through the GC client on a physical
GameCube. `NOASSERTION` is metadata, not permission. The license proposal is
unapplied: GPL-2.0-only SDK/engine dependencies generally prevent adding resale
or redistribution restrictions to a linked game without alternative licensing
from the relevant rights holders. Original art can have separate terms. No
registry PR or hardware certification is produced by this build feature.

Optional catalog PNGs must be `banner.png` at 96x32, `screenshot0.png` through
`screenshot2.png` at 640x480, and `hero.png` at 640x160. Use the actual submitted
build for screenshots and do not treat gallery or ISO-only captures as proof
of standalone app boot.
