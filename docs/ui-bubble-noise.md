# UI bubble grain

The title, resident picker, onboarding panels, gameplay menus, dialogue/name
bubbles and door/date popovers use the generated tree-noise pattern as a subtle
paper grain. The optional villager-attention icon uses the same pattern too.

**UI bubble noise** appears at the bottom of both Debug configuration and
START → Debug / testing. Left/Right or A adjusts intensity in 5% steps from
0–300%; Z restores the restrained 35% default. 0% uses the original flat fills.
The value is session-only, surviving screen transitions but resetting when the
application starts. It is absent from `dev_config` and every town save record;
changing only this title option does not mark debug configuration dirty.

The generated 32×32 I8 texture has six mips and 1,440 resident native bytes.
Intensity uses the existing deterministic tree pattern, independently of the
tree controls. A shared app-lifetime UI texture is rebuilt only on an intensity
change and released at zero/shutdown. The attention renderer lazily owns one
additional 1,440-byte copy while its icon is in use, released with that renderer.
No per-bubble textures, geometry copies, wave animation or overlay pass are used.

A game-owned native bridge shades existing Draw2D fills, including retained
UiShapes and immediate comparison paths. GX generates repeating UVs from logical
viewport positions (one texel per logical pixel), so corners, bulges and tails
join seamlessly. The TEV alpha input remains the original raster alpha. Scoped
painter-order flushes and material-cache invalidation restore normal text/portrait
sampling and prevent grain from leaking into the following world frame.

The native bridge uses SDK resource accounting and forbids replacement during a
frame. Shape scopes nest without additional state transitions; a nesting counter
avoids per-call managed allocations. SDK shutdown releases the final UI texture. Title/setup views retain no extra noise copies.

Regression fixtures cover SDK allocation/replacement/disable, scope ordering,
atlas isolation, retained geometry, frame guards, position UV mapping, original
alpha and save-byte isolation. Dolphin checks cover the title, picker, onboarding
speech, menus, dialogue/tail and attention bubble at default/off/strong settings.
