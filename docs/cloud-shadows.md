# Cloud shadows

Ground, beach, bridge decks and world-aligned cliff tops/lips share a soft cloud
mask in their existing material pass. One original 64 x 64 GX I8 texture uses
4 KiB, uploaded once. The shader uses two texture units and four physical TEV
stages (three RGB operations plus the SDK opacity stage):

1. Base texture multiplied by vertex/material tint.
2. Interpolate white to CloudShade using the shared cloud mask.
3. Multiply the base result by that shade.

There are no added draw calls, triangles, shadow maps, mesh invalidations or
per-frame texture uploads. One effect is shared by all ground material slots.
Animation changes only the secondary UV transform; the color uniform changes
only when cloud cover changes. No managed objects are allocated by animation.
This bounds rendering cost but does not establish a measured hardware frame-time
improvement; physical GameCube timing remains unverified.

The mask repeats every 128 logical metres and drifts at 0.18 / 0.09 m/s. Ground
UV0 is world XZ multiplied by 0.5, so secondary scale 2/128 maps the same mask onto
all material variants and cliff tops. Camera motion, cylinder bending and terrain
height never enter its coordinates. Texture translations wrap modulo one using
double-precision elapsed time. Bilinear filtering and a periodic smooth mask
avoid hard edges and texture seams.

Cover ranges from zero to one, with a partly cloudy default of 0.5. Clear skies
have no tint or patch contrast. Overcast skies use a uniform ground tint and no
patch contrast; both endpoints bypass the cloud shader and second texture.
Partly cloudy weather has up to about 17% patch darkening plus a restrained
ambient tint. The renderer has no weather simulation yet: this is the shading
control for a future weather system, not new persisted weather/progression data.
L+R+D-pad Right cycles partial -> clear -> overcast -> partial for inspection.

Indoor floors reset the effect, secondary texture/transform and base tint. Menus
use their existing materials. Soil faces, water, foam, trees, characters and
buildings retain their existing shading. Water already uses the SDK's two
available texture slots for its independently scrolling layers; adding a third
texture would require a separate SDK extension, so its effect is preserved.

## Verification

`tools/test-changes.ps1` covers mask packing, periodicity, soft edges, cloud-cover
endpoints and bounded/continuous scrolling over long and negative times. The
Dolphin scenario uses a fresh card, captures stationary partial clouds twenty
seconds apart, clear and overcast cover, restored partial cover, the menu, indoor floors and return outside:

```sh
docker run --rm --init --shm-size=512m --mount "type=bind,src=$PWD,dst=/work" -w /work animaltown-dolphin python3 tools/iso-smoke.py out/AnimalTown-VERSION.iso out/cloud-smoke --cloud-shadows
```

Inspect cloud-partial-a/b.png, cloud-clear.png, cloud-overcast.png,
cloud-restored.png and cloud-menu.png. The same-commit prerelease workflow is
explicitly enabled for feat/cloud-shadows and delivers ISO/server/tools/cliff kit,
revision/version/validation manifests and SHA-256 checksums.
