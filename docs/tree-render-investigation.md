# Why the original trees were slow

The dominant issue is high geometry volume through the imported-model CPU
submission path. Texture size is already bounded; tree selection is cheap.
This conclusion combines measured frame phases with an audit of the exact
retired model packages. Individual costs of lighting, texture sampling and
vertex submission have not been isolated experimentally.

## Measurements from released ISOs

Both captures use Dolphin 2503, single-thread JIT CPU, LLE JIT DSP, OGL/llvmpipe,
1x resolution, the same copied regression town and protocol-v3 telemetry.
The new capture boots the actual full-size released ISO, not a standalone DOL.
Times are **Dolphin guest-clock CPU/submission/wait milliseconds**, not measured
physical GameCube times or total GPU duration. Five warm-up frames per primary
scenario are discarded and repeated telemetry records are deduplicated.

| World phase | Original trees, 017c491bb811 | Generated trees, c861ec48330b |
|---|---:|---:|
| Tree geometry submission, mean | 510.409 ms | 0.385 ms |
| Tree geometry submission, p95 | 513.2 ms | 0.4 ms |
| Tree scan/visibility/selection, mean | 0.500 ms | 0.602 ms |
| Structures/fences/placed props, mean | 114.640 ms | 118.170 ms |
| Completed frame interval, mean | 669.901 ms | 160.811 ms |
| Completed frame interval, p95 | 684.0 ms | 233.5 ms |
| Unique world frames | 77 | 268 |

The current tree submission pass is about 1,326 times cheaper in these captures.
Geometry, shading, textures, visible-tree capacity and the floor renderer all
changed between builds. This is an end-to-end comparison, **not a one-variable
benchmark assigning that speedup to polygon count or textures alone**. Different
movement outcomes and sample mixes also affect whole-world averages. The
stationary/cached-terrain subset averages 667.300 ms before and 150.099 ms after;
its tree submission means are 513.004 ms and 0.401 ms respectively.

The scene still misses the 33.3 ms target. Structures now dominate at 118.170 ms
mean (114.7 ms median, 172.1 ms p95). Animation/skinning averages 18.081 ms,
villager submission 11.143 ms, terrain preparation 1.715 ms, ordinary world UI
2.731 ms, and presentation 4.587 ms. Presentation includes the retrace/GX waits;
do not add those counters again. GX blocking wait is below the 0.1 ms reporting
resolution, which is consistent with expensive CPU submission but does not
measure the GPU's total work or prove it is idle.

Backpack UI in this new capture averages 24.446 ms versus 2.731 ms for world UI;
the earlier release measured 13.137 ms versus 1.891 ms. This is the entire UI
construction/submission/flush counter, not an inventory-only timer or a
controlled explanation of the difference between captures.

## Exact asset audit

`tree-audit` reads DNMDs directly from the old ISO without loading, modifying or
staging them in the current game. Its output records ISO/package/texture SHA-256,
vertex/index counts, referenced material modes and texture mip residency.

| Original tree | Triangles | Stored vertices | Processed triangle corners per draw | Direct GX vertex payload per draw |
|---|---:|---:|---:|---:|
| Deciduous | 10,929 | 12,541 | 32,787 | 786,888 bytes |
| Palm | 12,252 | 10,784 | 36,756 | 882,144 bytes |
| Pine | 10,528 | 10,622 | 31,584 | 758,016 bytes |

The old capacity is eight trees: up to 98,016 triangles, 294,048 triangle corners
and 6.73 MiB of direct vertex attribute payload per frame at the palm worst case.
This payload counts XYZ floats, RGBA8 and UV floats (24 bytes/corner), excluding
GX commands, state changes, GPU vertex fetches and texture traffic. It is a
submission bound, not a measurement of total bus bandwidth or visible triangles.

All three trees are static: no skinned meshes. Every referenced material is lit,
opaque, not alpha-tested, and **two-sided**. The importer also creates an unused
default material; the audit distinguishes it using referenced corner counts.
The authored two-sided material makes `model_graphics.c` select `GX_CULL_NONE`,
so the old trees do not receive backface rejection. Enabling backface culling
would help rasterization only; this renderer already paid the CPU submission
cost, and single-sided conversion must first check authored winding/thin leaves.

## What the CPU repeats

The game calls `Begin3D(camera)` without custom scene lighting. In the pinned SDK
`source/model_graphics.c`, `draw_model_selected` then loops over **every index**:

1. Read the index and fetch position and normal from the 80-byte imported vertex.
2. Transform the normal, compute a square root for normalization, and evaluate
   the fallback diffuse headlight. A palm repeats this 36,756 times per draw,
   even when several triangle corners refer to the same stored vertex.
3. Read four floating-point color channels, multiply by material/lighting,
   clamp and pack them to bytes.
4. Send direct position/color/UV attributes to GX. Off-screen/back-facing
   triangles within a retained mesh still pass through this CPU loop.

Model attributes and indices are read from the retained package during this
loop; original high-resolution GLB images are not decoded per frame. On the
big-endian target, describing these reads as mandatory byte swaps would be
misleading. The relevant inefficiency is repeated attribute/shading work and
immediate submission rather than reuse of indexed GPU-side input.

The native renderer also restores GX state and initializes a texture object for
each mesh draw. That is additional overhead, but these old trees each have one
mesh, one used material and one texture: they are not thousands of tiny mesh
draws. Whole-world draw submissions actually increased from 65.0 to 127.795
mean after the replacement while tree submission fell sharply. Generic draw
count alone does not explain the original bottleneck.

## Textures and memory

Each retired tree already uses **one 128x128 RGBA8 texture**, six mip levels down
to 4x4, occupying **87,360 bytes (85.3125 KiB)**. The three distinct payloads total
262,080 bytes (255.9375 KiB); the SDK shares immutable texture payloads across
instances. Eight tree placements do not allocate eight independent copies of
each source texture. Texture binding is not disc I/O or re-decoding.

RGBA8 is less compact than RGB565/CMPR and texture sampling/cache traffic can
contribute GPU cost. However, no oversized texture, per-tree texture duplication,
alpha blending, texture decode loop or tree animation explains the measured
510 ms CPU submission phase. The retained DNMD packages also consume about
3.23 MiB combined, including geometry and converted texture sections, in
addition to native decoded resources. This is a residency issue, not evidence
of a new allocation every frame.

In the measurements above, generated trees contain 84/68/60 triangles for deciduous/palm/pine, no textures,
and four uniform-color unlit parts. They share native geometry and use the SDK
`include/dnd_indexed_mesh.inc` path, which submits position indices instead of
repeating CPU diffuse/color work. Their position-index payload is 504/408/360
bytes per placement, plus command/material overhead. Up to 32 selected trees
are supported. `DrawMeshes` batches CPU submission; this is not hardware
instancing. Original GLBs remain source references and are excluded from the ISO.

The subsequent noise-detail change retains these triangle counts and species
batches, adding one shared 32×32 I8 texture (1,440 native bytes), full mipmaps
and per-vertex UVs. See [the current rendering guide](generated-trees-repeat-floor.md).
The historical texture-free/index payload figures and timings above do not
measure the textured path; its direct vertex payload is 24 bytes per corner.

## Engine/SDK priorities

Keep the current generated trees; tree submission is now below half a
millisecond. The most useful remaining SDK work is the imported-model renderer,
which also draws the remaining expensive structures:

1. Reuse indexed/static geometry or geometry-only GX display lists; avoid
   expanding indexed models into direct triangle corners each frame. Budget
   immutable allocations and GPU lifetimes, and retain authored materials.
2. A/B the existing `Begin3D(camera, RenderSettings)` GX hardware-lighting path
   against fallback CPU diffuse lighting on the **same model, view and texture**.
   The API already exists. It avoids the CPU diffuse calculation but retains
   other imported-model overhead; its visual result must be checked.
3. Prepack invariant colors/UV data and cache coherent material/sampler state.
   Preserve animation, per-instance material overrides and mirrored transforms.
4. To isolate texture cost, compare the same geometry/shading with the existing
   texture versus a white texture of the same format, then compare formats.
   Compare lit/unlit and direct/indexed paths separately; do not infer component
   costs from this replacement comparison.

World/tree selection is approximately 0.6 ms. Replacing its spatial structure or
shrinking the whole map is not the first fix for a 510 ms tree draw pass. Scene
culling and asset budgets remain useful; they cannot make an expensive retained
model cheap to submit. Exact GameCube timings require a physical-console capture
using the existing telemetry.

## Reproduction and release evidence

```text
dotnet run --project tools/AnimalTown.Tools -c Release -- tree-audit . OLD_ISO
python3 tools/iso-smoke.py NEW_ISO CAPTURE_DIR --resume --profile
python3 tools/read-frame-profile.py CAPTURE_DIR/user/Dump/Frames/CAPTURE.avi
```

Use the Linux Dolphin image with at least `--shm-size=512m` and a copied test
memory card. Image ID:
`sha256:4eaecbf6963af024e41054699315a476b1fcf9e3557bbd8ce942a9018107f390`.
No personal save is modified. `out/tree-render-audit.json` is the static audit.

- [Original release and performance evidence](https://github.com/capz/AnimalTown/releases/tag/build-017c491bb811): ISO SHA-256 `22a4c8960befc81c69009a3c51086980458f22b9d32f6ea0d145b134e46e467c`.
- [Generated-tree release and new capture](https://github.com/capz/AnimalTown/releases/tag/build-c861ec48330b): ISO SHA-256 `d8de6c69de0c89d294038d699846449ca7831e05371666fc3be924942325d6a5`.
- The latter release includes this report, decoded JSON, raw lossless AVI, static audit and `tree-performance-SHA256SUMS.txt` alongside the full-size ISO and separate backend.
- Engine: `d0a49c3d0b437250a3eefa2aff3f882621db698e`; SDK: `4327c9fc4e0b6aa3a307802ba5928107f7bd1684`.
- [Pinned imported-model implementation](https://github.com/capz/DolphinDotNet/blob/4327c9fc4e0b6aa3a307802ba5928107f7bd1684/source/model_graphics.c), [indexed mesh implementation](https://github.com/capz/DolphinDotNet/blob/4327c9fc4e0b6aa3a307802ba5928107f7bd1684/include/dnd_indexed_mesh.inc), [texture sharing](https://github.com/capz/DolphinDotNet/blob/4327c9fc4e0b6aa3a307802ba5928107f7bd1684/source/model.c).
