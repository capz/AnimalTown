# Texture residency and static-instance plan

Status: accepted design, implemented in the texture-streaming/static-reuse branch.
See [implementation and validation](texture-streaming.md). The measurements below
remain the original baseline and arithmetic projections, not new console results.

## Accepted requirements and scope

The user selected more free main memory at today's eventual visual quality,
temporary softness even on nearby villagers, and physical DVD as the performance
baseline. GC Loader is an additional target, not the source of the latency
assumptions. Keep stock GameCube memory limits, existing generated terrain,
world-aligned materials, projected shadow union, visibility gates and manual-save
behavior. Ground mip distances continue to mean individual ground squares.

Room entry requires geometry and minimum-quality textures; sharpening uses simple
texture swaps. The existing door fade covers required restoration. Finished
optimizations default on after correctness validation, as requested; performance
measurements do not gate the defaults. Smaller texture formats require per-asset
visual review. Unreviewed artwork keeps RGBA8 and the existing safe mip range.

## Measurements and provenance

The read-only host probe loaded all thirteen current models through the pinned
production native loader and evaluated one instance each, plus an extra fox
player instance. Inputs were extracted from the already verified published NKit
image for runtime `4287df620af5881433b5b48b7275ade00c6e7542`, release
[0.12.9.4287df620af5](https://github.com/capz/AnimalTown/releases/tag/v0.12.9.4287df620af5).
Every extracted package SHA-256 matches that release's NKit payload manifest.
Local cooked files were not substituted: some differ in bytes from the release.

[Audit source](performance/texture-streaming-audit.c) and
[per-model measurements, identities and projections](performance/2026-11-10-texture-streaming-audit.json)
record the result. Payload sizes are exact. Host metadata/pointer accounting is
explicitly labelled and must not be treated as a physical GameCube reading.

| Allocation/payload | Measured bytes | Interpretation |
| --- | ---: | --- |
| All thirteen retained model packages | 4,048,448 | Includes geometry, animation, descriptors and embedded images |
| Images embedded in those packages | 2,764,800 | **2.637 MiB** of removable copies in the full working set |
| Unique native texture pixels | 2,641,920 | **2.520 MiB**; already shared by the native texture cache |
| Six species' eight unique texture payloads | 2,621,440 | **2.500 MiB**, using 256 + 128 pixel RGBA8 levels |
| Cottage's one shared texture payload | 20,480 | All seven parts share this GPU image, but retain seven embedded copies |
| Skin output for six species plus extra fox player | 292,752 | Positions/normals; not all of it is shareable |
| Extra fox player's skin output alone | 52,392 | **51.2 KiB**, shareable only when its pose matches another fox |
| Shared outdoor ground texture with seven mips | 43,712 | **42.7 KiB**, 256-pixel CMPR; keep resident |

The skin-output measurement includes only skinned meshes; the bunny in this
release has no skinned output allocation. Independent poses, animation workspaces
and logical NPC state cannot generally be merged. Houses already reuse one
model/instance per part and definition; trees already share twelve meshes across
three types and batch by type/material.

## Recommended design

Separate immutable model geometry/animation from texture payloads. Use a global
texture identity and a few bounded residency tiers, sourced from independently
addressable mip levels. A fully available tier initially retains the same two
mip levels and RGBA8 format as today's model textures. This preserves a precise
quality and memory comparison; deeper chains and lossy formats are later changes.

The base tier for a drawable model remains available. Sharper tiers are optional
render quality, not a prerequisite for model readiness. Maintain independent
geometry/base readiness and desired/available texture detail. Required geometry
and minimum textures have priority over all optional promotions. Distant NPC
journeys continue independently of both, as in the existing residency manager.

### Offline data and explicit native interfaces

- Cook geometry, skeletons and clips once. Replace embedded image payloads with
  typed texture references through a versioned game-owned loader/container and
  native binding adapter; keep legacy embedded packages readable during migration.
  The reference metadata retains texture count, material indices, sampler and
  alpha semantics. Do not fabricate offsets into private managed objects.
- Store unique texture pixels in indexed pack files, with original dimensions,
  GX format, mip dimensions/offsets, lengths, content identity and chunk checksums.
  Preserve one canonical normalized UV layout across every quality tier. Artist
  originals remain untouched; generated banks and models use separate outputs.
- Put small required levels together for efficient initial reads; lay optional
  levels out to favor likely room/asset requests. Use bounded, sequential batches
  and DVD-friendly alignment. Verify the resulting I/O behavior for both full ISO
  and NKit layouts: file-start alignment may change during packing, so relative
  chunk alignment is not proof of aligned device reads. Do not create a separate
  file/open for every mip.
- Downsample and encode GX tiles during the asset build. No PNG decode, texture
  compression or mip generation during walking. Bake the existing repeating-floor
  package offline too, while retaining its wrapping and seasonal tint policy.
- Validate atlas padding, filtering and alpha coverage at every proposed level.
  The current repacker contract protects only the base and one lower mip.
  A 64-pixel fallback is a candidate, not a universal minimum: use 128 pixels or
  an asset-specific limit where 64 causes unacceptable bleeding or lost coverage.
- Start with original RGBA8 for model textures. CMPR/RGB565/RGB5A3 require separate
  appearance and alpha checks plus renderer-format support; their savings are not
  included in this plan's estimates.

### Memory ownership and publication

Each texture has a stable ID, content revision, resident tier/generation and a
shared physical allocation. Consumers hold explicit leases. Demand is the maximum
across its visible/prefetched users; a distant object cannot downgrade a texture
also used nearby. Texture accounting charges shared pixels once.

Select detail by projected screen coverage, with preference for the player,
conversation partner and current/approaching room. Add hysteresis and a minimum
residency period. Preserve conservative cylinder/horizon prefetch and visibility
checks. Pin minimum-quality pixels for currently drawable assets, not every asset
that might ever appear in an expanded town.

Use bounded size classes and reusable staging buffers; allow unused storage to
return to the general allocator rather than permanently reserving maximum detail
for every texture. Admission includes the coarse fallback, incoming allocation,
old allocation awaiting retirement, staging and metadata. On budget exhaustion or
fragmentation, retain the lower tier rather than retrying allocation every frame.

Read and verify incoming data into detached native memory. Assemble/copy/hash in
small steps with a measured CPU deadline; a starting goal is under 1 ms of texture
processing per rendered frame, subject to target measurements. Avoid clearing or
validating an entire large allocation in one call. Fully initialize required
pixels and padding before publication.

Publish between frames after verification and before material submission. Reuse
existing completed-frame ownership/fences; do not add a synchronous GX wait for
every upload. Old resources remain alive until prior GPU references end. The SDK
`StreamingTexture` publication pattern is useful, but its permanent pair of
same-size buffers is not the residency allocation policy: use shared incoming
work capacity instead of doubling every texture allocation.

The native renderer resolves the current texture view (pointer, dimensions,
format, mip range and generation) through the explicit binding adapter. Generation
changes invalidate texture/material binding caches only. They must not increment
terrain topology revisions, rebuild geometry/UV debug topology, reset animation
or regenerate shadow masks. Preserve per-instance transforms, sampler state,
seasonal colors, narrow cliff spillover and projected shadow coordinates.

### DVD, ARAM and scheduler behavior

Pre-open the texture banks during initialization. Use `AssetStream`/native
background I/O into held native buffers. Keep a bounded queue and one admitted
DVD read; start with 32 KiB requests and two 32 KiB buffers, then compare 16/32/64
KiB requests on hardware. Request size limits bytes, not seek/completion latency.
CPU validation and publication are separate budgeted steps.

Coordinate all filesystem users. The pinned storage implementation serializes
other calls behind a detached read; an incidental synchronous open/read/close or
recovery call on the application thread could otherwise block rendering. Preload
small fixed metadata where appropriate, defer other operations until admitted,
and prioritize required base/model reads and explicit saves over optional detail.
Never wait on an unfinished read in Update/Draw. An in-flight DVD operation need
not be instantly cancellable: finish safely, then discard/cache a stale result.

Keep the existing exclusive ARAM channel ownership and completed transfer quanta
for model/terrain operations. DVD and ARAM are distinct paths; do not assume a
background DVD read owns or makes ARAM unavailable. Audit shared locks and buffers
before allowing overlap. A later bounded ARAM cache can retain recently used
optional levels and avoid DVD seeks, but the GPU must still sample from main RAM.
Recompute backing occupancy after texture extraction and avoid duplicate model
and texture backing. Add no mandatory ARAM reservation to the first prototype.

## Memory projection, not a new console result

The previous native Dolphin warm-room measurement was 23.35 / 24.00 MiB, with
all species and room parts ready. Full separation removes 2.637 MiB of embedded
image payloads. Eight permanently available 64 + 32 pixel fallbacks add 160 KiB;
128 + 64 fallbacks add 640 KiB. The house's existing 64-pixel shared texture stays
at its current quality and needs no duplicate fallback.

| Projected full-quality working set | 64-pixel species fallbacks | 128-pixel species fallbacks |
| --- | ---: | ---: |
| Steady state, retaining today's high-quality pixels | 20.87 MiB | 21.34 MiB |
| Plus 64 KiB staging and a conservative 320 KiB incoming allocation | 21.24 MiB | 21.71 MiB |

These are arithmetic projections from the rounded prior counter reading. New
loader metadata, allocator behavior, partially retained tiers and actual fences
must be measured. They exclude extra deep-chain mips and make no promise about
future asset counts. A candidate acceptance target is at least 2 MiB of headroom
in the existing main-memory budget during this fixture, while also keeping the
normal cold/outdoor case from growing. No full-size streaming arena should remain
allocated merely because it was once used.

## Instancing and CPU priorities

Instancing is complementary. Preserve current geometry/texture sharing. Profile
static submission before adding another batch abstraction: `LowPolyTrees` already
uses the SDK's native bulk mesh path. Candidates are reusable indexed GX display
lists, cached static local placement and a common cylinder/world transform, plus
similar opaque house-part batching. Lists retain geometry commands only; each
instance supplies its transform and necessary render state. Culling remains per
instance, and translucent ordering remains correct. Display lists add memory and
do not reduce the triangles processed by the GPU.

Defer a general villager pose cache. With the current mostly different species,
the obvious duplicate is the player and resident fox. Matching their exact pose
could share about 51.2 KiB of skin output; differing clip phases, transitions or
node overrides cannot use the same output. Multiple cached poses could cost more
than they save. Animation quantization is not an accepted requirement.

## Staged implementation and exit criteria

1. **External texture ownership at current quality.** Introduce versioned
   references/bindings, remove embedded pixel duplication and retain today's two
   mip levels. Validate identical rendered regions, existing animation/door
   behavior, ownership, native byte accounting and cold/warm ARAM model cycles.
   This isolates the largest structural memory saving before quality scheduling.
2. **Offline coarse data and bounded quality cache.** Audit fallback artwork,
   share texture identity across instances, add per-tier demands/hysteresis and
   atomic publication. Test budget exhaustion, repeated promotion/demotion,
   GPU retirement, stale requests, partial reads, checksum failure and shutdown.
   Optional detail failure keeps the last valid minimum texture; invalid minimum
   data follows existing required-asset error behavior. Asset reads never write
   the memory card or save transfer state.
3. **Physical-DVD scheduling.** Add bank ordering, prefetch and coordinated I/O.
   Exercise rapid turns, sustained travel, immediate re-entry, six villagers in
   one room, different room types, manual save, season changes and read faults.
   Late optional reads must leave a softer image rather than a loading panel or
   blocked render thread. Measure seek and completion latency separately from CPU
   work; a worker alone is not evidence of nonblocking gameplay.
4. **Profile static reuse separately.** Compare current bulk drawing against
   display-list/placement changes with the same scene and renderer settings.
   Keep only demonstrated CPU wins, with explicit list-memory costs. Consider an
   ARAM quality cache only after measuring repeated DVD reads and available ARAM.

Expose current/fallback/incoming texture bytes, desired/available tier, reads and
bytes by source, queue age/latency, promotions/demotions/cancellations, checksum
failures and worst per-frame texture CPU work. Distinguish these from existing
mesh/terrain counters. Capture frame intervals, animation, shadow work and missing
terrain/loading flags; account explicitly for telemetry obscured by doorway fades.
Do not assert that quality changes are free of all data movement.

Dolphin tests establish behavior, image fidelity, memory accounting and bounded
CPU processing. Physical DVD and stock GameCube tests are required to accept the
storage/frame-time baseline. No new console build or physical performance claim
is part of this proposal.

## Reproduce the inventory

Extract the thirteen named model files from the validated release image into
`MODEL_DIRECTORY`, verify them against `nkit-validation.json`, then run from the
repository root:

```sh
cc -std=c11 -O2 -Wall -Wextra -Werror -ffunction-sections -fdata-sections \
  -Iout/sdk-native/include docs/performance/texture-streaming-audit.c \
  out/sdk-native/source/model.c out/sdk-native/source/model_animation.c \
  -Wl,--gc-sections -lm -o out/texture-streaming-audit
./out/texture-streaming-audit MODEL_DIRECTORY > out/texture-streaming-audit.json
```

This prints the raw loader inventory. The recorded audit adds the verified input
identities and explicitly labelled arithmetic projection. The source uses no card
or graphics service and does not infer GPU timing from host execution.
