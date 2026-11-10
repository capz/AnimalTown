# External textures and quality residency

Implementation branch for the accepted texture/instancing plan. External model
ownership, bounded texture reads, ARAM quality caching and static/pose reuse
are implemented. Physical DVD and GameCube qualification is outstanding.

The cooker retains legacy DNMD assets for compatibility/developer comparisons and
emits version-3 `*-stream.dndmodel` files for active villagers and building parts.
Their texture section contains metadata, a stable catalog index and the source
pixel hash instead of image bytes. Geometry, skinning, hierarchy and clips retain
the original section bytes. The native loader still reads versions 1 and 2.

`textures.idx` describes unique GX-native image levels and per-read CRC32 values.
`textures-base.bank` contains minimum levels and generated smaller mips;
`textures-detail.bank` contains larger levels. An external reference must match
both the descriptor dimensions and source content identity. Identical house
images have one physical native owner across all seven parts.

`TextureStreaming` runs native reads on the SDK background executor through the
same detached read backend used by `AssetStream`. The two held native staging
buffers and incoming allocations have explicit lifetimes. Native workers perform
I/O only. The application copies/checks at most 4 KiB in each processing step,
with a 1 ms scheduling deadline, and publishes only complete verified textures
between frames. Incoming and retiring bytes count toward the 4 MiB image budget;
optional allocations also observe a 22 MiB main-memory admission target.

Required minimum images load before initial model publication. Geometry/model
ARAM restoration requests missing minimum images independently of optional detail.
A missing sharp level keeps the valid base image. Model eviction releases its
image lease; the player's shared species and exterior images remain protected by
their active model references. Texture swaps invalidate material bindings, not
geometry, topology or shadow masks.

Explicit saves wait to start until the current texture read completes, without
waiting inside the render loop. Season changes defer their filesystem work while
that read is pending. The save latch can only be set by the selected Save now
menu action. Saving itself prevents admission of another texture read.

The active floor/soil packages are prepared offline as `*-native.dndtex`; runtime
loading uploads those exact packages and sets their established addressing and
sampler policy. Ground remains shared, world-aligned and independently tinted.

`assets/texture-policy.json` keys reviewed asset policy by source payload SHA-256.
Unreviewed model images preserve RGBA8 and the existing two safe mip levels.
Asset-specific review is required before enabling smaller formats or deeper sampling for an image.

The `banks` tool checks cooking identity/source safety. The native `stream`
fixture compiles the same copied-loader patch used by the console build and
exercises actual cooked models, texture sharing, bounded reads, five byte-exact
cold/warm cycles, geometry ARAM backing, CRC failures and frame-active guards.

All seven runtime switches are enabled by default in Settings → Testing:
streaming, reviewed deeper mips, reviewed formats, texture ARAM cache, cached
object placement, GX geometry lists and exact live poses. Disabling streaming
requests full quality for resident assets. Disabling the format switch selects
the original RGBA8 fallback. No lossy-format policies are approved in the current
source catalog; that switch therefore changes no current image. Similarly, deeper
mips are generated but sampling stays within each asset's reviewed safe range.
The existing two-level atlas contract remains the default. These are deliberate
art-quality limits, rather than runtime capability or performance gates.

Every bank cook also writes `out/texture-candidates/review.json` and contact sheets.
Columns are RGBA8, RGB565, RGB5A3 and CMPR; rows are successive mip levels, enlarged
without interpolation. Reports identify source pixel SHA-256, byte costs, RGBA
error and changed alpha coverage. These are review evidence, not automatic
approval. A policy can set `reviewed`, `review`, `minimumEdge`, `safeLevels` and
`format`. The review must identify the asset-specific atlas/alpha/native visual
checks; invalid mip ranges and alpha-bearing RGB565 inputs are rejected. A
reviewed alternate is cooked alongside the lossless fallback and can only publish
after its own CRC validation. Failure of optional alternate data retains RGBA8.
Source files and legacy model variants remain available for comparisons.

Visible demand follows projected screen coverage, with player/conversation
priority, a 75% retention threshold and a 120-frame demotion grace. Approaching
rooms use the existing conservative geometry/base prefetch. A missing sharp tier
never changes doorway readiness. Telemetry exposes resident/base/detail/incoming
peak bytes, promotions, demotions, DVD bytes/reads/errors, CRC errors, queue age,
cancellations, ARAM hits/misses/traffic, display-list bytes/hits and actual shared
skin bytes. The on-screen profile includes measured worst texture CPU work and
DVD completion latency; a 1 ms scheduling deadline is a goal between bounded
quanta, not a claim that an individual allocation or DMA can never overrun it.

The optional quality cache lazily borrows 1 MiB of ARAM as 32 verified 32 KiB
blocks. It uses a separate native staging buffer, validates readback before marking
blocks usable, validates cache hits again before publication, and falls back to
DVD after corruption. Model backing can reclaim this entire cache when required.
One completed ARAM transfer quantum follows the existing model/terrain arbiter;
DVD I/O can continue independently. The DVD staging buffer drops its ARAM hold
before the storage API takes its exclusive I/O lease, then reacquires the hold on
completion. The host fixture enforces this same lease rule.

Static trees and house origins cache local cylinder placement. A shared parent
scroll moves these instances around the original common axis; per-instance
frustum/horizon culling and shadow world coordinates remain unchanged. Opaque
house parts use the existing model batch. The GX cache stores only primitive and
index commands, with transforms, textures, tint, shadow projection and render
state bound at submission. It is bounded to 128 KiB, builds incrementally after
frames, and rejects dynamic/skinned sources. Resource generations/disposal remove
stale lists; budget or allocation failure retains normal indexed submission.

The pose cache has eight live entries, rather than historical pose snapshots.
Each villager keeps independent animation clocks, local/global node matrices,
overrides, material state and NPC journeys. Only exact matching native skin output
is shared, with full input comparisons after hashing. Visible updates are grouped
so matching actors advance once; divergence detaches the output before writing.
Actors advancing together keep their shared allocation. Hidden actors do not
enter the evaluation group. Disabling sharing or disposing/unloading an owner
preserves other instances' output. No animation phase quantization is used.

Validation includes the actual production loader/evaluator patches, independent
pose hashes, repeated matching updates without allocation splits, divergence,
blends, owner disposal, all GX index layouts, list generation/lifetime/budget
checks, exact source texture hashes after cold/warm model cycles, DVD/ARAM CRC and
read failures, and ASan/UBSan. The managed cylinder sweep has maximum placement
error below 0.0001 m. Native Dolphin results and release identities are recorded
separately; host checks do not establish physical DVD frame-time performance.

Native release validation and screenshots are recorded in
[texture-streaming-a39b6211f733](performance/texture-streaming-a39b6211f733/README.md).
The warm six-villager room uses 21.47 / 24 MiB MAIN versus 23.35 MiB previously;
repeat and reuse-switch views match exactly. A separate 950-frame travel capture
has no loading/missing flags. The report distinguishes startup work, doorway
telemetry gaps, cache/pose cases without measured savings and hardware work still
outstanding.
