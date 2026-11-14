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

The updater scans the GC Loader volume recursively for stable AnimalTown ISO
filenames, compares their major, minor and patch counters numerically, and uses
the directory of the highest version. Hash-suffixed development images are
ignored. The image header must identify AnimalTown and match the filename's
version. Equal highest versions use the smallest canonical path under
ASCII-case-insensitive ordinal comparison,
so directory enumeration order cannot change the selected installation.
This is an installation-selection policy: GC Loader does not expose
which physical image booted the game. The selected highest stable image is the
old installation retired after the new download is verified and selected in
GC Loader's hardware disc mapping.

Keep a supported official normal Swiss DOL at the GC Loader drive root, under
its existing `.dol` filename. Provision HTTPS inputs on the computer that manages
the card:

```sh
python3 tools/provision.py /path/to/SD --roots /path/to/ca-bundle.pem --utc-offset-minutes 60
```

This is the updater repository's provisioning tool. Settings remain in
`animaltown-updater/`, even when game images are in subfolders. Swiss is discovered
among root DOLs by verified file fingerprints, regardless of filename; generic
DOL headers have no Swiss identifier. The newest recognized official revision
wins. The old private helper path remains a validated compatibility fallback.
The catalog includes 12 official normal releases, including r2119; custom/new
builds need a catalog update. Optional `--swiss /path/to/swiss_r2119.dol` verifies
and copies a supported DOL to the root as `swiss.dol`. No `image-path.txt` mapping
is required.

The game stages its embedded updater DOL directly from `dvd:/`; only older
discs missing that file need the root SD fallback. The SDK passes three separate
arguments: updater DOL path, `--animaltown-update=2`, and the running version.
The updater is entirely resident before it releases the source disc and mounts
the writable GC Loader volume. Its SDK remains pinned to
`2f8e81650a6d23f2053da0118495b93866efb14e` for verified physical image selection
and the exFAT traversal fix.

The new image keeps GitHub's published filename in the selected directory.
Streaming, contiguous allocation and on-card verification complete first. The
SDK then switches GC Loader's hardware disc mapping to the new image and reads
its complete boot header back through the physical DVD interface. Only after
that comparison succeeds does the updater delete the selected old image.
Other installations are retained. Swiss is staged and validated before this
sequence, and its SDK autoload handoff boots the new image after the updater
returns successfully. A missing helper, invalid download or failed mapping
check preserves the old image.

Failure screens retain the stopped phase, last operation and available
exception/path/native-result details, including empty-message exceptions.
The updater reports the current scan directory or candidate file. Its SDK
rebuilds the matching FAT drivers with a 64-level exFAT path limit; the original
10-level limit incorrectly reported deep paths as out-of-memory failures.
Native exception messages now survive without allocating memory, and the updater
keeps its 1 MiB managed heap. Real native AOT tests exercise the full updater and
UI over wide, candidate-heavy and 30-level directory trees before native builds.
Physical GC Loader writes, BBA operation and Swiss relaunch still require
hardware tests.

The targeted Dolphin scenarios in `tools/iso-smoke.py` are `--title-menu`
(animated background, debug edit/save, Start), `--title-load` (reuse the previous
memory card to inspect restored config and Start), and `--title-update` (embedded
updater handoff without an image mapping). Dolphin cannot establish physical
GC Loader/media behavior.

The release tools archive includes the matching PC setup script as
`provision-updater.py` and its guide as `updater-setup.md`. The script comes from
the exact updater commit pinned into that game build. Run it against the mounted
SD card, for example `python3 provision-updater.py /media/GCLOADER`.
