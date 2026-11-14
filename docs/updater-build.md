# Packaging the updater

AnimalTown pins the standalone updater source in `build/updater.lock.json`.
`tools/build-updater.ps1` fetches that exact commit, verifies a clean checkout,
runs the updater's DolphinDotNet build, and copies `animal-town-updater.dol` and
its `THIRD-PARTY-NOTICES.txt` into the disc filesystem. The notice is named
`updater-THIRD-PARTY-NOTICES.txt` in the ISO, release tools archive and standalone
release asset. Packaging fails if the pinned updater source is missing that
notice. The updater builds
outside the game project so its network/TLS code and assets are not initialized
by the title. Its own SDK revision is pinned in `sdk.lock.json` and built into a
separate local package feed.

The normal game build packages this DOL. A separately installed SD copy remains
a fallback for manually assembled older discs. For SD layout and HTTPS details,
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
its existing `.dol` filename. Swiss is discovered among root DOLs by verified
file fingerprints, regardless of filename; generic DOL headers have no Swiss
identifier. The newest recognized official revision
wins. The old private helper path remains a validated compatibility fallback.
The catalog includes 12 official normal releases, including r2119; custom/new
builds need a catalog update. No `image-path.txt` mapping is required.

New images embed the updater's public CA trust bundle and obtain fresh
48–64-byte entropy from the SDK on the console, with bounded collection and
health checks. They do not read
`roots.pem`, `entropy.bin` or `utc-offset-minutes.txt`, and need no PC provisioning.
Set the GameCube clock accurately. The updater treats local time as an unknown
UTC offset within ±14 hours and requires certificate validity throughout that
whole interval. Health checks do not prove randomness; physical qualification
of the on-console entropy source remains outstanding for this prerelease.

The game stages its embedded updater DOL directly from `dvd:/`; only older
discs missing that file need the root SD fallback. The SDK passes three separate
arguments: updater DOL path, `--animaltown-update=2`, and the running version.
The updater is entirely resident before it releases the source disc and mounts
the writable GC Loader volume. Its pinned SDK supplies verified physical image
selection, the exFAT traversal fix, on-console entropy and certificate checks
across the clock's UTC uncertainty interval.

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

The release tools archive includes the matching updater guide as
`updater-setup.md` and the optional legacy setup script as `provision-updater.py`,
both from the exact updater commit pinned into that game build. The script is
for older images that still consume external HTTPS inputs; new images do not
require it.
