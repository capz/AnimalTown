# Villager rim and merged cast shadows

All house, tree and character cast shadows share RGB (35,49,66) and opacity
66/255. Coverage combines with `max`, then the existing ground draw mixes its
shaded colour with that tint once. Overlapping cores retain the same darkness;
soft edges take the strongest coverage. Clouds remain a separate weather layer.
There are no independently blended cast-shadow overlays.

GX has no MAX framebuffer blend equation. The game rasterizes the existing
clipped receivers into a sparse world-space mask cache. 32 scenery entries and
seven actor entries retain at most 96 triangles each. The native rasterizer
uses logical X/Z coordinates and the original soft tree/house/circle profiles.
Indoor floor/mat triangle overlaps also combine by maximum. No skinned silhouette
or EFB copy is needed.

The fixed pool contains 160 pages. Each spans 8x8 metres with a 64x64 I8 mask,
including filtering gutters (62 interior samples). Every caster footprint,
including gutters, fits within four pages; 39 casters need at most 156. Current
rooms fit one page; larger catalog rooms enlarge its world span. Explicit world
and room keys prevent distant-region aliasing. Needed pages cannot be evicted.

Each page retains a static union and a GPU-visible combined mask. Moving actors
dirty old and new footprints, restore those pages from static coverage and merge
current characters. Removing scenery dirties its old footprint. Unchanged,
stationary coverage performs no rasterization or publication. Only modified
4 KiB pages are flushed after the previous frame's normal present fence. No
additional DrawDone is introduced.

All cache storage, profiles and receiver coordinates occupy one SDK-accounted
native buffer below 1.5 MiB; the 16 MiB/512-handle limits remain unchanged. The
previous 39 shadow mesh buffers/materials and native profile textures are
removed. Managed staging is reused; there is no per-frame managed mask array or
visible-ground reconstruction.

The native ground bridge adds two TEV stages to existing floor/cap draws. One
computes mask coverage gated by a receiver flag; the other blends the tint and
preserves alpha. Normal floor UVs identify the page; the first receiving triangle
also handles cliff batches beginning with a lip. Top/lower caps receive shade.
Opaque lip/bank vertices carry a zero receiver flag in alpha, preserving their
ordinary RGB shading. Indoor floor models use position TexGen with their object
matrix before walls/furniture render. Camera rotation needs no mask rebuilding.

Receivers still clip dry floor, ramps, beach toes and exact diagonal half-planes
to connected support. Coverage unions across triangles before a conservative
one-sample support guard prevents bilinear leakage across cliffs. Internal
triangulation stays seamless; shadows may stop slightly short of a cliff edge.
The cottage entry mat has its raised receiver; new flat-floor assets reuse the
logic without that overlay. Doorway visual positions and committed spaces govern
characters. Obsolete receivers are omitted until replacements are ready.

Receiver construction retains terrain's cooperative 2 ms/eight-stage priority.
The player precedes rotating NPC jobs, followed by scenery. Mask updates occur
before BeginFrame and count toward measured terrain preparation, so their cost
also needs emulator/hardware verification. Save layouts are unchanged. Shadows
approximate fixed light and connected support, without self-shadowing or casts
onto disconnected lower ground, water, walls or characters.

Villagers preserve imported albedo, tint, opacity and indexed skin buffers.
Their separate rim uses normal TexGen, one TEV stage and a shared 64x64 I8
lookup (4 KiB). Ordinary model materials use two stages; imported secondary-map
combiners use four. The scoped bridge refreshes the normal matrix per instance
and restores material reuse. This approximates a camera-parallel Fresnel rim.

Both native bridges are applied by tools/patch-native.ps1 to checked signatures
in the pinned package. The SDK checkout is unchanged. Mask quantization retains
the byte-return boundary for the pinned compiler's float-to-byte store bug; the
build audits both real bundled profile generators.

The native-shadow-union host checks cover coincident/mixed-strength overlaps,
moving/removing actors, stationary reuse, clipping guards, triangle/page seams,
room separation and the 156-page worst case. Change routing and console/CI gates
include them. `tools/iso-smoke.py --actor-shadows --dsp-hle` covers walking, NPCs,
entry/exit, interior camera, ramps and cliffs. `--terrain-scroll --stream-stress`
checks streaming peaks. Hold L+R+Z and press Y to cycle both/off/rim/shadows.
Animated outlines remain feasible with silhouette passes or baked pose masks.
Physical GameCube results must be reported separately from emulator results.
