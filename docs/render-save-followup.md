# Horizon, debug resources and save I/O

Building exteriors and relocation previews now use the cylinder horizon test
as well as the frustum test. Tree selection now uses a verified .82-times-scale
mesh envelope instead of the generic 1.5-times-scale envelope, so hidden trees
stop occupying draw slots sooner. Villagers, items, fences, attention bubbles,
rain and projected shadows already use the shared horizon predicate. The bound
must be entirely hidden before rejecting it, preserving visible roof/canopy tips.

Switching away from Sunshine frees its textures, effects and EFB capture.
Inactive storm effects/textures, disabled rain geometry/staging and disabled
cloud masks/effects are released outside frames. Cloud material references are
detached before releasing their resources, including dormant floor slots.
UV debug already frees its native cache when disabled; the cliff gallery already
releases empty mesh batches. Testing menu actions now retire cached UI geometry.

Saving previously reread records during comparison, snapshot validation, mirror
repair and final reference verification. A transaction-local cache retains at
most 64 KiB across 24 entries; records over 8 KiB bypass it. Writes invalidate
their entry before physical I/O, including failed writes. The first subsequent
readback always reads the card. The cache is discarded after each menu save and
never survives a mount or a later save. Publication order, mirror repair and
corruption checks remain intact. In the compound-save fixture physical reads
drop from 60 to 23 with identical writes. This is an I/O-count result, not a
physical-console latency measurement. The save diagnostic reports elapsed
transaction milliseconds, physical reads and cache hits. Card mount, terrain
cache suspension/restoration and card hardware latency still need console
profiling; no wall-clock speedup is claimed yet.

CRC32 checks now use two nibble-table lookups per byte instead of eight
bit-by-bit branches, using a 64-byte table. Published IEEE vectors and every
offset/length span of a patterned 257-byte buffer match the independent bitwise
algorithm; existing save checksums and file layouts are unchanged.

The Sunshine body pass now uses translucent dark blue beneath the specular and
close-wave layers. Its source-alpha blending preserves the visible seabed.
