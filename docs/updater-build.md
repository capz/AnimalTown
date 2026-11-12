# Packaging the updater

AnimalTown pins the standalone updater source in `build/updater.lock.json`.
`tools/build-updater.ps1` fetches that exact commit, verifies a clean checkout,
runs the updater's DolphinDotNet build, and copies `animal-town-updater.dol` into
the disc filesystem. It builds outside the game project so its network/TLS code
and assets are not initialized by the title. The updater's own SDK PR revision
is pinned and built into a separate local package feed.

The normal game build packages this DOL. A separately installed SD copy remains
a fallback for manually assembled older discs. For SD layout and HTTPS setup,
see [the updater documentation](https://github.com/capz/AnimalTown.Updater).

Update receives an explicit installation path; there is no required image
filename or SD-root image location. Direct GC Loader boot does not expose the
active image filename through the current documented API. On the computer that
manages the card, provision its target mapping and HTTPS inputs:

```sh
python3 tools/provision.py /path/to/SD --image 'games/AnimalTown/My Town.nkit.iso' --roots /path/to/ca-bundle.pem --utc-offset-minutes 60
```

This is the updater repository's provisioning tool. It writes the canonical
target URI to `gcloader:/animaltown-updater/image-path.txt`; `--image` is a host
path relative to the SD root, or an absolute host path inside it. Settings stay
at the SD root even when the selected image is in a subfolder. Maintain the
mapping if the image moves. Native launch arguments or image-mount metadata can
also supply a candidate; conflicting candidates/mapping stop the update.

Before handoff, the game mounts GC Loader storage and validates the selected
image's GameCube header, `AMTE01` identity and exact running `BuildInfo.Version`.
The mapping selects the installation to replace; matching headers do not prove
which of several identical physical files the firmware opened. Missing or
invalid path information leaves the title usable with an error and never
falls back to a guessed filename.

The project-local native bridge passes four separate arguments: updater DOL
path, `--animaltown-update=1`, the full validated `gcloader:/...` image path and
the running version. The standalone updater revalidates that request. Its SDK
remains pinned to `81d74a8a80e67ac7b4a0be3c269ea5506b8a9647`; the bridge is a
project build hook, rather than an SDK revision change. The source-disc unmount
fix from [updater PR #2](https://github.com/capz/AnimalTown.Updater/pull/2) is
preserved before any image replacement.

The installed path receives a `.bak` backup and `.update` transaction marker;
downloads use a sibling `.animaltown-update/` directory. After verified
installation, **Power off/on** returns control to GC Loader firmware so it can
reopen the replaced image. There is no automatic raw-DOL/disc handoff. Failure
screens retain the stopped phase, last operation and available exception/path/
native-result details, including empty-message exceptions. Physical GC Loader
writes, BBA operation and firmware restart behavior still require hardware tests.

The targeted Dolphin scenarios in `tools/iso-smoke.py` are `--title-menu`
(animated background, debug edit/save, Start), `--title-load` (reuse the previous
memory card to inspect restored config and Start), and `--title-update` (embedded
verified updater handoff or a clear title error when no supported image mapping
is available). Dolphin cannot establish physical GC Loader/media behavior.

The release tools archive includes the matching PC setup script as
`provision-updater.py` and its guide as `updater-setup.md`. The script comes from
the exact updater commit pinned into that game build. Run it against the mounted
SD card, for example `python3 provision-updater.py /media/GCLOADER --image
'games/AnimalTown/My Town.nkit.iso'`; the image name is an explicit selection.
