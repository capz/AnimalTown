# Title menu and dev config

Startup mounts the disc, reads its runtime defaults and the independent Slot A
`dev_config` record, then runs the SDK/engine splash screens. `TitleScreen` owns
only the original cream panel, tree emblem, wordmark, tagline, IPL menu font
and a procedural 64×64 mint dot texture. The
staggered dots repeat and drift diagonally at four pixels per second. It creates
no town, terrain, model, building or audio resources. Exiting the title trims
released splash/title texture pages immediately, then collects their managed
staging data before constructing the game; native model/terrain allocations do
not share the texture cache's budget.

Start immediately removes the panel, logo and menu. Only the moving dot
background and a small hopping, blinking sprout in the bottom-right remain while
assets and the landscape load. No world, loading panel or debug overlays are
drawn during this transition. The animation uses simple UI geometry and retires
its native resources before the first gameplay scene needs them.

Start saves changed dev settings, then constructs `TownGame`, loads the last
complete town commit set, and begins staged game loading. Without a town save it
opens character creation. A held confirmation button cannot accept the next game
screen because the controller edge was consumed by the title.

The title has exactly three options: Start, Update and Debug configuration.
Up/Down selects and A activates; START also activates the focused Start option.
Debug configuration opens a modal above the dimmed title. Inside it, Up/Down
selects a paged setting; A or Left/Right changes it; Z resets the rim or
mip-distance slider. B (or START) saves changed settings and closes the modal. The record's memory-card title is `dev config`. Its versioned,
checksummed payload is separate from the town commit set and never increments a
town sequence. Missing/corrupt/unavailable settings use the disc defaults and
report a status; closing the modal retries writing changed settings. Starting/updating also
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
