# Title menu and dev config

Startup mounts the disc, reads its runtime defaults and the independent Slot A
`dev_config` and `options` records, applies saved video preferences, then runs the SDK/engine splash screens. `TitleScreen` owns
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

Start saves changed dev settings and loads the last complete town commit set.
A four-card villager picker follows the approved graphical mockup: original-model
portraits, a large selected-villager vignette, rounded name tabs, a honey focus
ring and silhouette/plus cards for empty slots. It appears before `TownGame` is
constructed or town assets load. Left/Right selects a card (Up/Down also work);
A selects a villager; B returns to the title. Empty slots open
[onboarding](onboarding.md) in the shared town: name and portrait screens precede
outdoor loading, then a real home preview and explicit **Save & begin** follow.
See [picker artwork and validation](villager-picker.md).
After identity selection, the dot background and sprout cover staged game
loading. See [shared town profiles](shared-town-profiles.md). A held confirmation button cannot accept the next game
screen because the controller edge was consumed by the title.

The title has four entries: Start, Update, Options and Debug configuration.
Up/Down selects and A activates; START also activates the focused Start option.

Options opens a cream modal with Video, Language and Data sections. Video offers
480p/576p resolution and Yes/No widescreen choices, aligned on the right of each
label. The underline marks the active output; Left/Right moves the candidate
highlight and A applies and immediately saves it to Slot A. B closes the panel.
Language currently shows a disabled Coming soon entry. New console preferences
do not change boot timing until a progressive mode is explicitly selected.

Typography targets TV viewing at 480-line render height: menu/option labels are
about 26 pixels, section headings 28 pixels, and help/status text at least 22 pixels.
Panels and the erase confirmation provide room for the larger text; long status
messages wrap to two cached lines. Debug configuration uses six larger rows per
page. The existing IPL body font and Gorditas title atlas are reused.

Video follows the SDK VideoMode sample: `GraphicsDevice.ConfigureDisplay` runs
between frames, with an explicit progressive scan for resolution changes and
Auto/Auto for aspect-only changes so they retain timing and framebuffers.
Progressive requires a compatible cable and display. Rejected modes/allocation
failures preserve the active output and are not saved. Startup falls back to
boot output if a saved mode is unavailable; hold X during startup to bypass a
saved video mode when changing TVs. There is no extra keep/revert step: A is the
requested commit action. The independent 16-byte options record is versioned,
checksummed and read back after writing. Save failures explicitly report that
the applied settings are session-only. Widescreen updates both drawing cameras
and their outdoor/interior culling frusta. The title centers and fits its fixed
composition in display-correct logical coordinates, retaining readable proportions
in either aspect. The dot backdrop and modal shade cover the full display.

Data contains Delete town data. A opens a Slot A confirmation with Cancel focused;
A on Cancel or B returns without writing. Select Delete and press A to erase all
town records: both banks, all four player villagers, NPC villagers, and legacy
migration copies. Options and dev config remain. A small verified deletion-intent
record is published first and removed last; an interrupted operation blocks town
loading until deletion is retried in Options. Deletion does not construct a town
or load models. The options heading/font is released when the modal closes.

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
UI grain starts at 10% strength; tree grain remains unchanged.

Update chainloads `dvd:/animal-town-updater.dol` when packaged, or falls back to
`gcloader:/animal-town-updater.dol`. The embedded DOL is staged before the game
touches GC Loader storage. The title passes the updater path, version-two
protocol marker and running build version as three arguments. No installation
mapping or physical boot-image path is needed. Missing updater media leaves
the title usable with an error.

The standalone [updater repository](https://github.com/capz/AnimalTown.Updater)
documents direct GC Loader SD setup, BBA networking and embedded HTTPS trust.
New images collect fresh entropy on the console and need no PC provisioning;
the GameCube clock must be set accurately. The updater owns stable-image
discovery, release checking, streaming verification,
and retirement of the selected old image. The new image keeps its published
filename in the selected directory. GC Loader's hardware mapping switches to
the verified image, and its physical boot-header readback must match before
the selected old file is deleted. Swiss is discovered by verified contents among
root-level DOLs, with the newest recognized official revision selected. A staged
Swiss helper autoloads the new image
after the updater exits successfully.
Failures keep the stopped phase and operation visible with available exception,
path and native-result details. See [packaging and setup](updater-build.md).
Memory-card town data is not modified by an update. Physical storage/network
and Swiss relaunch remain subject to hardware validation.

For Dolphin validation, build `build/Dockerfile.dolphin` and provide at least
`--shm-size=512m`. Run `tools/iso-smoke.py DISC OUTPUT --title-menu` to exercise
title/modal navigation, dot motion and distinct sprout poses before
the game appears. `--title-config` checks the modal after reboot using a copied
`OUTPUT/MemoryCardA.USA.raw`; `--title-update` captures the updater handoff.
The checks wait for visible title/modal states around memory-card operations
rather than assuming software emulation advances at wall-clock speed.
`--title-options --component-cable` uses a disposable populated card to exercise
480p/576p, immediate writes, aspect-only timing preservation, both cancellation
paths and whole-town deletion. `--title-options-load --component-cable` checks
the copied card after reboot; `--title-options-unsupported` verifies that rejected
progressive output does not write settings. These checks do not establish
physical TV/cable compatibility or power-loss behavior on a real card.
