# External textures and quality residency

Implementation branch for the accepted texture/instancing plan. External model
ownership and bounded texture reads are the first stage; the full rollout is
still under development. Physical DVD and GameCube qualification is outstanding.

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
The debug controls and comparison captures will expand with the remaining stages.

The `banks` tool checks cooking identity/source safety. The native `stream`
fixture compiles the same copied-loader patch used by the console build and
exercises actual cooked models, texture sharing, bounded reads, five byte-exact
cold/warm cycles, geometry ARAM backing, CRC failures and frame-active guards.
