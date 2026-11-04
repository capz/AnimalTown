# Shared neutral outdoor ground

The supplied `assets/textures/environment/ground/outdoor-floor-neutral.png`
is retained unchanged. All outdoor ground surfaces now share a single repeating
texture, including grass, water/ponds, bridges, sand, rock and paths. Their colors
come from GX material tinting, not separate colored texture copies. Indoor floors
retain their existing separate color treatment. Terrain collision, town generation
and saved terrain IDs are unchanged.

Edit `assets/terrain/terrain.json`:

```json
"neutralFloor": {
  "source": "assets/textures/environment/ground/outdoor-floor-neutral.png",
  "brightnessFloor": 0.86,
  "tints": { "grass": "#72B955", "water": "#4B9ABB" },
  "seasons": {
    "fall": { "grass": "#AD9694" },
    "winter": { "grass": "#FFFFFF" }
  }
}
```

This excerpt illustrates the controls; the repository config includes all outdoor
surface names. `tints` defines default opaque `#RRGGBB` colors. A season's entry
overrides only that surface. Grass is green in spring/summer, reddish gray in
fall, and bright white in winter. `building` is the ground fallback for building
cells and follows grass. The top-level `season` can remain `auto` (console RTC),
or be forced to `spring`, `summer`, `fall` or `winter` for development.

GX tinting multiplies a texture's brightness. To permit bright snow from a gray
source, the build creates one common grayscale master: observed luminance is
mapped to `brightnessFloor..1`. The default 0.86 keeps the pattern while allowing
white highlights and a bright winter surface. Lowering this value increases
contrast/darkness for every variant. It does not modify the original PNG or make
season-specific image copies. Changes take effect on rebuilding.

The disc contains one `floor-outdoor-neutral.dndtex`; all four seasonal metadata
files reference it with different material colors. Identical tints are merged
into one material. UVs repeat every 2 metres in world coordinates, continuing
through strip/acre joins and raised-section tops. The shared texture uses true
repeat wrapping rather than mirrored repetition. Exposed vertical terrain walls
retain their earth-colored material.

The existing loader downsamples to the configured texture resolution (default
256x256), generates one half-size mip, and chooses CMPR when quality permits.
The current source occupies 40,960 native bytes at the default resolution,
including its 128x128 mip. Runtime cache identity includes source path, build,
resolution and wrapping policy. All tints/seasons reuse that same cached texture;
there is no per-frame recoloring, per-tile texture upload or colored texture cache.
Source fingerprints and material colors are recorded in texture-audit.json.

The old transition sheets remain reference assets. While `neutralFloor` is
enabled, their outdoor source settings are inactive. No 9-slice images are used
as regular outdoor ground or blended automatically at boundaries.
