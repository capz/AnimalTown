# Snow indents from the shadow map

![Raised white snow and its textured openings rendered in Dolphin](raised-layer.png)

Regular winter grass gets a solid white layer
about 20 cm above the original tile. That includes level grass on upper
terraces. Roads, water, white building pads, ramps, cliff lips and interiors
use their usual surfaces. Select **Winter** in **Season preview** to see the
effect year-round in a console build.

The renderer draws the textured floor and then reuses the indexed grass
mesh for the white layer. Tile entries use the same movement path as road
traffic. The first visit makes a small dent and the second widens it while
keeping the surface white. The third visit exposes a small patch of ground
at the center, the fourth widens the opening, and the fifth clears the tile
up to its shaded rim. Standing and movement within a tile add no visits.
The wear lasts for the town session and resets when the season leaves winter.
While snow falls outdoors, each dent refills over ten minutes of active
snowfall after its latest visit. The ground closes over before the white
surface finishes smoothing out. Turning off snowfall or previewing rain
pauses recovery. Outdoor snow continues refilling while the player is
inside. Path coverage and visit history follow their existing rules.

The depth field is a demand-allocated I8 texture with four texels per
metre, independent of the shadow page cache. A tile entry updates its GX
blocks and those touched by the rim. Snow receives the same IA4 shadow and
lamp textures as ordinary ground. The raised winter pass adds one visible
grass submission and one depth binding on worn snow. Ground collision
continues to use the original surface.

Once both edge-sharing tiles expose ground, the mask extends the shallower
cutout's cross-section between their centers. The existing rounded ends
remain at the ends of the track. Both axes can join at bends, and a fully
connected 2×2 patch fills its inner corner. Joins require level grass on
the same terrace. As snowfall refills the shallower tile, the strip
narrows and closes with it. The original mask, local dirty blocks and
raised snow draw also handle joined cutouts.

The image is from the earlier standalone raised-layer experiment. Its
fixed impressions demonstrate the same cutout and shading used by the
interactive layer.

The original [flat relief comparison](comparison.png) predates the raised
layer and shows both page resolutions. Its CPU preview can be regenerated
with `python3 docs/concepts/snow-indents/preview.py` from the repository root
with Pillow installed.

## What the renderer supports today

[The shadow cache](../../../tools/native-shadow-union.h) packs 4-bit cast
shadow coverage into intensity and 4-bit baked lamp strength into alpha.
[The GX hook](../../../tools/native-shadow-union-gx.inc) reads both. The
independent model point-light path can still light objects when a receiver
forgoes the baked lamp pool. Detailed pages have 30 interior texels across
8 m, or one texel every 27 cm, with a one-texel gutter at each edge. The
outdoor fallback field runs at 1 texel per metre. The detailed cache is
allocated around shadow casters, so snow away from those casters needs
explicit residency within the existing page budget.

## A. Bake directional relief into IA4 alpha

Rasterize a world-space depression profile wherever snow is compressed.
On the CPU, sample its slope in the chosen light direction and pack the
signed brightness change into the existing alpha nibble, with code 8
representing flat snow. Leave the shadow intensity nibble unchanged. In
the winter snow receiver, decode alpha around that exact neutral value
and apply the signed brightness before the cast-shadow tint. Use a
neutral-alpha texel for an empty snow page.

This keeps the existing one-byte texel and uses a single relief sample.
A snow receiver using this encoding gives up the baked lamp pool. Because
snow and adjacent ground can share a page, the cheap design uses the
snow interpretation for the entire eligible winter page. Draw snow
relief only on snowy surfaces, and suppress baked lamp reads on all draws
bound to those pages unless a separate lamp source is added. Keep the
existing encoding for interiors and other ordinary pages. Rebuild the
snow channel when marks or the baked light direction change. Fast light
direction changes would create page update work, so use a fixed direction
for the first test.

## B. Store depth in RGB565 and derive relief in GX

Pack cast shadow in red (5 bits), snow depth in green (6 bits, zero is
flat), and the baked lamp pool in blue (5 bits). Read red and blue via
TEV texture swaps. Sample green on either side of the pixel along the
world-space light direction, subtract the samples and use the signed
difference to brighten one wall and shade the opposite wall before
compositing cast shadows and lamps.

Two offset texgens can share the same texture binding. Keep each offset
component within half a texel so bilinear filtering at page edges stays
within the existing gutter. Generate gutter texels from the same
world-space marks as the interior. A future version can move the light direction with the time of
day. Replacing the published IA4 copies would reduce the additional storage to
192 KiB, with corresponding changes to the other shadow receivers.

## What to verify in game

- Gate the effect to snow-covered outdoor ground using the same season
  selection as the floor. Check roads, water, cliff faces, interiors and
  non-winter seasons. Verify that an opening exposes the original height.
- Keep the snow field independent of cloud and actor composition so
  moving shadows do not erase marks. Test a mark spanning a page edge,
  eviction and reload, and a transition between detailed and fallback
  pages. Account for displaced shadow pages if snow requests compete
  for slots.
- Test broad indentations first. The preview shows softened, stylized
  footprints at 27 cm per texel. A crisp tread would require finer
  coverage. Check overlap under cast shadows and near a streetlamp,
  then measure upload and TEV costs in Dolphin and on hardware.

## Whole-town memory test

The [native lifecycle fixture](../../../tests/native-shadow-lifecycle.c)
checks the individual wear levels, sparse uploads and every cell in a
fully travelled 128×120 town. The 512×512 I8 field accounts for 256 KiB
when active. The ordinary shadow cache uses 522,016 bytes. Together they
charge 784,160 bytes to the native allocator. The field is released on a
season change or session teardown. A static 7,680-byte snapshot and a
managed 7,680-byte wear array store the packed depth steps. A 30 KiB managed
array tracks recovery after the first dent and is released after the last
one fills or winter ends. Full-game frame timings have not been measured.
