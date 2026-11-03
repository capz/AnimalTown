# Texture residency, visibility and profiling

The camera eye is now `(player.x, 6, player.z + 8)`, ten metres from the
player rather than fifteen. Eye and aim offsets both scale by 2/3, preserving
the pitch and 53.54-degree vertical field of view. World coordinates, saves and
the 20-by-20 layout of square 20-metre tiles remain stable; the closer camera
shows less of each tile. The terrain cylinder still extends beyond the view.

## Texture policy

Every native texture allocated by the game is at most 128 by 128 pixels.
Model import already applies `--texture-limit 128`; the ISO builder now rejects
oversized embedded model textures and writes `out/texture-audit.json` with
package and texture fingerprints. High-resolution GLB images stay on the build
machine. The SDK shares identical converted model texture payloads using a
hash, length and full byte comparison. Its retained DNMD package still contains
the converted texture section for model restore/export; that section is already
bounded to 128 pixels, rather than being the original high-resolution image.

Ordinary DNTX textures are box filtered while loading, preserving aspect ratio
and weighting RGB by alpha to avoid fringes. The decoder supports I4, I8, IA4,
IA8, RGB565, RGB5A3, RGBA8 and CMPR base levels. Temporary source bytes, decoded
pixels and upload buffers leave scope before an explicit native managed-heap
collection. Only native resident pixels, dimensions and identity survive.

UI atlases are split into 128-pixel pages so glyph/icon resolution is preserved.
Gorditas glyphs that cross page boundaries draw as adjoining source slices;
rotated headings retain a shared pivot. The IPL font is extracted into six
128-square IA8 glyph pages. Its temporary 512-square-wide extraction atlas and
libogc decoded font storage are freed after extraction; no oversized font
texture is uploaded and no firmware font is distributed.

The application cache holds at most 32 source entries and 2 MiB of native pixels.
Its immutable-disc key is the build identity plus original disc URI, processing
version and atlas layout. A source FNV fingerprint and original dimensions remain
available without retaining original pixels. A hit avoids disc I/O and decoding.
Leases prevent eviction of active textures; zero-lease entries are evicted by
least recent use. This is a read-only disc cache, not a hot-reloading SD asset
cache. The SDK's separate model texture cache is outside that 2 MiB budget.

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
and off-screen cells before writing vertices; fixed 4095/8190-vertex mesh buckets
bound uploads and GX submissions. Unused vertices are degenerate triangles.
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
Use at least 512 MiB of Docker shared memory for the Linux Dolphin container;
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

`present_total` includes `gpu_blocking_wait` and `retrace_wait`; do not add them
together. Blocking GX completion is outstanding-work wait, not total GPU time.
`frame_interval` includes presentation and deliberate delay. Simulation excludes
the separately recorded skinning work. World samples include movement and idle;
terrain preparation is cached while the camera is stationary.
In older v1/v2 captures UI construction and the final batch flush were separate
timings. The final flush also includes the telemetry rectangles.

Dolphin guest-clock results are useful for finding expensive code paths but do
not establish GameCube hardware frame times. The same pixel protocol can be
decoded from a native-resolution physical console video capture. Physical
hardware, GC Loader, Swiss and burned-disc compatibility require device tests
in addition to structural ISO validation and direct ISO boot in Dolphin.
