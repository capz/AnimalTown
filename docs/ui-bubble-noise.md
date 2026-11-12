# UI bubble grain

The title, resident picker, onboarding panels, gameplay menus, dialogue/name
bubbles and door/date popovers use deterministic, uniformly distributed fine
grain. The optional villager-attention icon uses the same pattern too. Each
texel is sampled independently from a fixed coordinate hash, without the tree
texture's coarse interpolated layer. This removes broad mottling; random nearby
matches can still occur. Tree noise is unchanged.

**UI bubble noise** appears at the bottom of both Debug configuration and
START → Debug / testing. Left/Right or A adjusts intensity in 5% steps from
0–300%; Z restores the restrained 35% default. 0% uses the original flat fills.
The value is session-only, surviving screen transitions but resetting when the
application starts. It is absent from `dev_config` and every town save record;
changing only this title option does not mark debug configuration dirty.

The generated 32×32 I8 texture has six mips and 1,440 resident native bytes.
At the 35% default, texels uniformly span intensities 222–255 (0–13% darkening).
The slider scales the sampled
range, capped at 0–255 before sampling to avoid clipping or byte wrap at high
settings. The existing box-filtered mip chain smooths grain under minification.
A shared app-lifetime UI texture is rebuilt only on an intensity
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
