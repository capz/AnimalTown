# Texture residency, visibility and profiling

See [tree-render-investigation.md](tree-render-investigation.md) for the exact
retired-asset audit and released-ISO comparison: tree submission averages
510.409 ms before and 0.385 ms after generated indexed trees. These are Dolphin
guest-clock measurements; individual polygon/lighting/texture costs are not
isolated, and physical GameCube performance remains unmeasured.

The camera eye is now `(player.x, 5.4, player.z + 7.2)`, nine metres from the
player: another ten percent closer than the previous ten-metre view. The aim
offset is 2.4 metres ahead. Eye and aim offsets scale together, preserving
the pitch and 53.54-degree vertical field of view. Culling derives its direction
from these same offsets and retains partially visible bounds. The current town
is 8 by 8 square 20-metre tiles; old saves preserve retained coordinates during
cropping (see town-size.md). Earlier measurements below used the former larger
town. The terrain cylinder extends beyond the view. Its radius is now
360/(2*pi), ten percent smaller, for about eleven percent stronger curvature.
The forward ground horizon moves from approximately 19.53 to 18.23 metres.
Far clipping decreases from 70 to 60 metres, and the terrain candidate window
from 48 rows ahead / 20 behind to 32 ahead / 20 behind. Camera lens and offsets
are unchanged. At the same fractional camera positions, host geometry audits
compare retained floor cells and strips; these are work counts, not FPS claims.

For a uniform floor at sixteen identical fractional camera positions, the
candidate envelope decreases from 4,290 to 2,658 cells per rebuild (38%). Mean
retained cells decrease from 650.625 to 595.9375 (8.4%), and merged strips from
25.6875 to 24.3125. The alternating-material stress sweep remains within the
unchanged 1,365-strip budget, reaching 617 strips. Coverage auditing spans 64
metres ahead and 40 behind, beyond the generated window, to catch missing rows.

Terrain culling adds one full logical cell around the viewport: its enclosing
sphere grows from 0.8 to 1.8 metres, and each row envelope gains two columns on
each side to support the wider band during fractional camera motion. GPU
clipping handles retained edge-crossing triangles. Offscreen rejection beyond
this band, horizon/backface culling and existing strip budgets remain active.
The padding applies to terrain; actor/object visibility tests keep their own
bounds. Regression sweeps cover all four planes, sub-cell camera positions,
partially visible tile samples and alternating-material strip capacity.
Use `iso-smoke.py ISO CAPTURE_DIR --terrain-edges` with a copied test card to
capture horizontal, vertical and diagonal camera movement in the released ISO.

## Texture policy

The default maximum model/world texture edge is 256 pixels. Explicit per-asset
128/256/512/1024 variants are imported from original sources with two mip levels.
Only the selected variant enters RAM. The ISO audit records package and texture
fingerprints; UI/IPL atlases remain lossless 128-pixel pages. SDK model resources
share identical native payloads; their residency is outside the application cache.

Ordinary DNTX textures are box filtered while loading, preserving aspect ratio
and weighting RGB by alpha to avoid fringes. The decoder supports I4, I8, IA4,
IA8, RGB565, RGB5A3, RGBA8 and CMPR base levels. Temporary source bytes, decoded
pixels and upload buffers leave scope before an explicit native managed-heap
collection. Only native resident pixels, dimensions and identity survive.

World/model textures use two mip levels total: base plus half-size, provided
the shorter dimension can remain at least four pixels. Native samplers clamp
to the available chain and use trilinear minification for linear materials.
UI atlases remain base-only and are split into 128-pixel pages so glyph/icon resolution is preserved.
Gorditas glyphs that cross page boundaries draw as adjoining source slices;
rotated headings retain a shared pivot. The IPL font is extracted into six
128-square IA8 glyph pages. Its temporary 512-square-wide extraction atlas and
libogc decoded font storage are freed after extraction; no oversized font
texture is uploaded and no firmware font is distributed.

The application cache holds at most 256 source entries and 4 MiB of native pixels.
Its immutable-disc key is the build identity plus original disc URI, processing
version and atlas layout. A source FNV fingerprint and original dimensions remain
available without retaining original pixels. A hit avoids disc I/O and decoding.
Leases prevent eviction of active textures; zero-lease entries are evicted by
least recent use. Repeating floors use the selected resolution policy, CMPR/RGB565 mipmaps and
explicit addressing. The `floor-mip2-auto-v5` key distinguishes repeat, clamp and
repeat-U/clamp-V soil. Neutral ground UVs use logical world XZ; seasonal tint,
cliff material bindings and cloud motion are independent of mesh generation.
See [nuget-rendering-services.md](nuget-rendering-services.md).
This is a read-only disc cache, not a hot-reloading SD asset
cache. The SDK's separate model texture cache is outside that 4 MiB budget.
See [mipmaps-streaming-async.md](mipmaps-streaming-async.md) for mip tradeoffs,
streaming applicability and the distinction between completion checks and waits.

## Visibility

Objects use conservative sphere tests against all six camera frustum planes.
Outdoors, a second test rejects spheres completely hidden by the cylindrical
ground. In the cylinder's YZ cross-section, the complete projected sphere must
lie behind the camera's tangent chord and inside both tangent shadow planes.
Height and radius are included: roofs, heads and foliage that still peek over
the horizon stay visible. The test uses precomputed camera constants and dot
products, with no per-object trigonometry or allocations. It also runs before
tree selection and villager animation updates. Indoor rooms bypass this test.
Invisible villagers also skip animation/skinning. Terrain rejects backside rows
and off-screen cells before writing vertices. Adjacent equal-material cells
merge into repeating strips with reusable per-material dynamic buffers.
Unused capacity is degenerate white geometry, preserving indexed GX submission.
Ground and solid placeholder meshes enable GX backface culling. Model materials
retain the SDK's determinant-aware backface handling and authored two-sided
foliage. GX clips triangles crossing the viewport and rejects backfaces;
partially visible meshes still pay CPU vertex/submission costs. This is cylinder
occlusion only; buildings and trees do not act as additional CPU occluders.

## Reproducible measurements

Settings → Performance display enables checksummed pixel telemetry in the last
32 display rows. It uses the native guest clock, reports CPU/submission phases,
and includes terrain, triangle and cache counters. Rendering the telemetry has
its own measured cost. Results are displayed one frame after collection.
The developer shortcut L + R + Z toggles the same display from any screen.
Use at least 514 MiB of Docker shared memory for the Linux Dolphin container;
the default 64 MiB can make Mesa/Dolphin exit with SIGBUS before boot.

Run the Linux Dolphin image with `tools/iso-smoke.py ISO CAPTURE_DIR --resume
--profile` and a copied test memory card in that directory (or use `--play
--profile` for a new character). This captures world idle/movement and the
backpack. Decode the resulting `user/Dump/Frames/*.avi` with
`python3 tools/read-frame-profile.py FILE.avi` to obtain p50/p95/max milliseconds
from unique valid frames. Keep the ISO build identity with the JSON results.
The script discards the first five samples per scenario. Protocol v2 encodes
0.1 ms units up to 6.55 seconds per phase, with individual overflow flags.
Older v1 captures are accepted at 0.01 ms precision; clipped values are reported
as lower bounds, never treated as complete measurements or silently discarded.
Protocol v3 retains those units but separates tree selection from tree mesh
submission, and ground-item scanning/drawing from structures/fences/placed props.
Frame setup is included with terrain submission, and the final UI flush is
included with UI construction/submission. The decoder rejects mixed v3/older
phase definitions, reports means and draw/binding/culling counters, and separates
cached-terrain world frames from frames that actually rebuild terrain.
Use `--profile-seconds 60` (5–300 allowed) for longer stationary-world and
backpack captures without changing the game or its simulation.

Protocol v4 adds complete imported-model primitive/texture counts and replaces
cache hit/miss fields with drawing API entries/material setups. It also labels
in-ISO batch comparisons. See [batched-drawing.md](batched-drawing.md).

Protocol v5 retains those fields and records a terrain mesh revision flag in
bit 15 of the scenario word. The decoder reports actual rebuilt/cached frame
counts instead of inferring them from elapsed preparation time; animated water
and cloud materials still need CPU work on cached frames. Older captures remain
readable. `--terrain-scroll --profile-seconds 5 --dsp-hle` captures slow axial
motion up/down a generated ramp, lateral motion and stationary frames.

Outdoor ground, water, foam and banks share a rigid rotation about the cylinder
axis. Geometry is baked in fixed cylinder coordinates and retained in a bounded
cache of 256 sections, each 8x2m. Moving the player only changes the shared
transform and the visible section selection. Four metres of prefetch surround
the view. Existing sections never rebuild because a scroll window changed.

The renderer pumps at most two incoming sections (32 cells) per frame and stops
starting work after a 2ms guest-clock deadline. A single section is the atomic
unit and can exceed that deadline; native profiling must check the worst pump,
not merely its average or the fraction of cached frames. Frustum selection is
cached in half-metre buckets and missing-section priority is updated without
repeating those tests. Horizon culling uses the YZ bound, independently of width.

Sections keep their mesh allocations and recycle inactive material buffers.
All sections share ground, water and soil materials/textures, and draw through
native indexed mesh batches grouped by material. No per-section texture or
water effect is allocated. World UVs, curved row chords and cliff ownership
remain stable across section boundaries.

Cold entry, teleports, terrain edits and town/season changes prepare geometry
behind a loading transition instead of drawing holes or constructing the whole
view in one frame. Input/simulation pause during that cold transition. Normal
movement prefetches ahead of the view. `--terrain-scroll --stream-stress
--profile-seconds 5 --dsp-hle` profiles a generated ramp and river/corner/bridge/
sea transitions. Protocol v5's revision flag now denotes small section updates.

`present_total` includes `gpu_blocking_wait` and `retrace_wait`; do not add them
together. Blocking GX completion is outstanding-work wait, not total GPU time.
`frame_interval` includes presentation and deliberate delay. Simulation excludes
the separately recorded skinning work. World samples include movement and idle;
terrain geometry remains cached while movement streams incoming sections.
In older v1/v2 captures UI construction and the final batch flush were separate
timings. The final flush also includes the telemetry rectangles.

Dolphin guest-clock results are useful for finding expensive code paths but do
not establish GameCube hardware frame times. The same pixel protocol can be
decoded from a native-resolution physical console video capture. Physical
hardware, GC Loader, Swiss and burned-disc compatibility require device tests
in addition to structural ISO validation and direct ISO boot in Dolphin.
