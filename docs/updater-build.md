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

The targeted Dolphin scenarios in `tools/iso-smoke.py` are `--title-menu`
(animated background, debug edit/save, Start), `--title-load` (reuse the previous
memory card to inspect restored config and Start), and `--title-update` (embedded
updater handoff; GC Loader absence in Dolphin is an expected updater error).
