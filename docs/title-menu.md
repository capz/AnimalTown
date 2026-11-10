# Title menu and dev config

Startup mounts the disc, reads its runtime defaults and the independent Slot A
`dev_config` record, then runs the SDK/engine splash screens. `TitleScreen` owns
only the wordmark, IPL menu font and a procedural 64×64 mint dot texture. The
staggered dots repeat and drift diagonally at four pixels per second. It creates
no town, terrain, model, building or audio resources. Exiting the title trims
released splash/title texture pages immediately, then collects their managed
staging data before constructing the game; native model/terrain allocations do
not share the texture cache's budget.

Start saves changed dev settings, then constructs `TownGame`, loads the last
complete town commit set, and begins staged game loading. Without a town save it
opens character creation. A held confirmation button cannot accept the next game
screen because the controller edge was consumed by the title.

The title exposes Start, Update and Save dev config, followed by paged debug
options. Up/Down selects; A or Left/Right changes a setting; Z resets the rim or
mip-distance slider. The record's memory-card title is `dev config`. Its versioned,
checksummed payload is separate from the town commit set and never increments a
town sequence. Missing/corrupt/unavailable settings use the disc defaults and
report a status; explicit Save dev config retries writing. Starting/updating also
attempts to save edited settings. In-game debug controls remain session controls;
the title's configuration is the saved startup configuration.

Persisted startup options cover frame/profile overlays, model/UV/batch/text
paths, rim/shadows/cloud cover, cliff/water previews, runtime wait/clear flags,
rain/rough ocean/season, villager attention, rim strength, ground mipmaps and
distance, frozen landscape motion, mesh recreation and the legacy ground ring.
Teleport/debug-location actions remain actions rather than stored settings.

Update chainloads `dvd:/animal-town-updater.dol` when packaged, or falls back to
`gcloader:/animal-town-updater.dol`. The standalone
[updater repository](https://github.com/capz/AnimalTown.Updater) documents SD setup,
Swiss, BBA networking and HTTPS provisioning. The updater owns release checking,
verification, backup/replacement and boot handoff. Missing updater media leaves
the title usable with an error message. Memory-card town data is not modified by
an update.
