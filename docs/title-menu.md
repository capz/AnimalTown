# Title menu and dev config

Startup mounts the disc, reads its runtime defaults, discovers the first usable
memory card (A, then B), and reads that card's independent `dev_config` and
`options` records. It applies saved video preferences, then runs the SDK/engine splash screens. `TitleScreen` owns
only the original cream panel, approved Smalltown Forest logo, tagline, IPL menu font
and a procedural 64×64 mint dot texture. The
staggered dots repeat and drift diagonally at four pixels per second. It creates
no town, terrain, model, building or audio resources. Exiting the title trims
released splash/title texture pages immediately, then collects their managed
staging data before constructing the game; native model/terrain allocations do
not share the texture cache's budget.

The full-color logo includes the single right-hand acorn. Its 512×256 RGB5A3
texture replaces the generated monochrome wordmark and separate tree emblem.
It uses eight 128×128 atlas pages (256 KiB, half the previous wordmark's
residency), drawn with a white tint and preserved aspect ratio. The title fits
it into a 360×180 area above the tagline; profile selection and onboarding share
the same texture in a 128×64 header. The existing cream, mint and honey UI
colors remain in place. See the
[approved sources and native-format checks](../assets/textures/branding/README.md).

The title logo and menu buttons share a centered 360-pixel column. The cream
panel fits the visible content: 432×384 with three menu entries, or 432×424
with Debug visible. The group is centered vertically, with 36 pixels of side
padding and 24–25 pixels above and below. The 34-pixel menu pills have eight
pixels between them, and their labels use actual glyph bounds for centering.
Status messages expand the panel to 432×456 and move the group up to reserve
a 360×46 footer. Status text keeps its existing size, wraps into at most two
physical lines and retains 24 pixels of clearance from the bottom of the canvas.

The disc's `opening.bnr` and memory-card banner use the separately approved
96×32 Smalltown Forest artwork; the 32×32 save icon uses its single acorn.
BNR text is generated with the current version and commit. The US disc ships
BNR1, with a six-slot English BNR2 alternate available for PAL packaging.
The boot-header title and disc/save identifiers retain their existing
AnimalTown values for compatibility with the pinned updater and existing saves.

Start opens the approved [memory-card chooser](memory-cards.md), reusing the
paper panel, IPL font and Gorditas heading without a logo. Confirming a usable
slot or temporary play removes the panel and menu. Only the moving dot
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

The title normally has three entries: Start, Update and Options. Press Z three
times consecutively to toggle Debug configuration and the in-game debug entry/tab.
The visibility flag is stored in console preferences, defaults to hidden for new
or v1 records, and survives restart and town deletion. Other inputs interrupt the
sequence; holding Z does not repeat it. Hiding an open debug panel closes it and
preserves its settings, while navigation skips hidden rows and tabs.
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
selects a paged setting; A or Left/Right changes it; X resets the rim or
mip-distance slider. B (or START) saves changed settings and closes the modal. The record's memory-card title is `dev config`. Its versioned,
checksummed payload is separate from the town commit set and never increments a
town sequence. Missing/corrupt/unavailable settings use the disc defaults and
report a status; closing the modal retries writing changed settings. Starting/updating also
attempts to save edited settings. In-game debug controls remain session controls;
the title's configuration is the saved startup configuration.

Persisted startup options cover frame/profile overlays, model/UV/batch/text
paths, rim lighting, cliff/water previews, runtime wait/clear flags,
rain/rough ocean/season, villager attention, rim strength, ground mipmaps,
distance and mip-level bands, frozen landscape motion, mesh recreation and the legacy ground ring.
The mip-band switch sits beside the other ground controls and defaults off.
Dev-config version 5 uses the final reserved byte in the existing 40-byte record;
versions 1–4 still load with the new diagnostic disabled and retain strict checks
on their historical padding and checksum.
Teleport/debug-location actions remain actions rather than stored settings.
UI grain starts at 10% strength; tree grain remains unchanged.

God mode is available only in the in-game **Debug / testing** menu. It starts
off for every town session and is never written to the town save or dev config.
When enabled, **Give 10,000 acorns** credits the wallet up to its 99,999 limit,
and Catalog entries immediately give any item, including unowned/non-store
items, to a free pocket. Shop purchases, backpack upgrades, relocation and loan
repayments waive their charges. House/garden construction can bypass an existing
loan without adding new debt; existing debt is retained. Pocket capacity, plot
validation, upgrade tiers and overnight construction timing still apply.
Turning God mode off restores normal costs and Catalog ownership/delivery rules.
Granted money, items and upgrades remain ordinary game data if manually saved;
returning to the title or restarting always disables the mode itself.

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

For a focused logo check, run `tools/iso-smoke.py DISC OUTPUT --title-logo`.
It captures the title, villager picker and naming screen from a real Dolphin
run, cancels back to the title twice, and checks the native logo dimensions and
resident bytes, visible artwork colors, unchanged committed town state and
absence of gameplay startup or runtime asset errors. It also checks the fitted
panel with Debug hidden and visible, opens and closes the Debug modal, and
captures the expanded panel used for status messages. Release CI runs this
scenario when the branding, shared headers or capture path changes, and keeps
the original screenshots with the release for PR review.
