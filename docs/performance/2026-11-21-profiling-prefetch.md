# Bounded profiling and optional warm terrain prefetch

The implementation extends the existing immutable terrain archive. The saved
**Warm terrain prefetch** debug setting defaults **Off**, including on cards
written by dev-config versions 1–5. Toggle it at the title or in the town's
Debug / testing menu. Its v6 dev-config record is 44 bytes; town saves do not
change. In-game changes apply immediately and report a card-write failure.

## Terrain comparison

Off keeps the existing demand-binding path. On plans the actual camera pages
first, then one neighboring page of lookahead, favoring movement direction.
Whole sections are admitted inside the existing **256 mesh handles / 1 MiB
MEM1 cache**. No second geometry cache, mesh reconstruction, UV edits or
shadow regeneration is introduced. Only actual camera pages enter draw lists;
prefetched pages are excluded from triangles and ground-material demand.

Warm transfers use `BindStep` only from the existing shared residency pump:
at most eight native calls per terrain opportunity, up to 500 microseconds
of the shared one-millisecond presentation credit. These are soft CPU bounds:
an individual native call can overrun its deadline. Current demand takes
priority over speculation and speculation is evicted first under pressure.
The water inspector uses its effective camera position in both phases.

Cold construction, cache restoration and large teleports retain explicit
readiness transitions. An incomplete alias is never submitted as geometry.
The pending native request owns its destination until cancellation completes;
toggle-off, saving, suspension and disposal drain it before reuse. Turning the
setting off also drops the small lookahead plan and speculative aliases.

Use the same saved town, season, video mode, route and profiler mode for both
cases. Prefer **Counters** or **Capture** for timing comparisons; the pixel
overlay draws extra rectangles. Normal walking/running acceptance requires
zero missing-terrain and loading presentations. A fault-injected or cold
transition must be reported separately, not silently excluded.

## Profiling controls

**Profiling** cycles Off → Counters → Overlay → Capture. Counters and Capture
perform no profiling UI drawing. The original Performance display control and
L+R+Z still select the overlay. The new mode selector is session-only; it never
silently restores a large capture allocation after restart.

Capture admits a **20 KiB reservation** only when at least a further 256 KiB
of physical MEM1 and 32 KiB of managed headroom remain. It contains 256 packed
64-byte frame records, an all-frame interval histogram, phase maxima and 64
checkpoint events. Recording allocates no per-frame objects or strings. Ring
overwrite and event-drop counts are explicit. A refused capture falls back to
Counters with a visible status. Disabling profiling releases the capture and
collects its unused managed storage.

Select **Export profiling capture to debug log** to freeze the ring and write
it through the native debug log (Dolphin log / USB Gecko), returning to Counters.
This explicit export can stall; it performs no memory-card save writes. Decode:

```sh
python3 tools/read-profile-dump.py console.log --platform hardware \
  --storage DVD --video-mode 480p --metadata qualification.json
```

Exact interval p50/p95/p99/max cover the retained ring. The all-frame histogram
retains overwritten-frame hitch counts in explicitly labelled log2-microsecond
buckets. Checkpoints bracket town load/construction. Loading presentations are
recorded too. Frame completion remains after both residency opportunities and
the task delay; nested phases must not be added as a CPU total.

## Video protocol and provenance

Protocol v6 preserves the old three bands and adds identity and memory bands
at canvas Y400 and Y384. A CRC-16 covers all five bands from one presentation.
Session and frame sequence are 32-bit; detail/submission tables use both, so
the old 65,536-frame wrap cannot overwrite previous frame details. Memory
snapshots are latched across eight parts, each held for two presentations.

```sh
python3 tools/read-frame-profile.py capture.avi --platform hardware \
  --storage DVD --video-mode 480p --metadata qualification.json
```

Both decoders accept explicit platform/storage/video provenance. Supply
`commit`, `sdk`, `engine`, `artifact_sha256`, `fixture_sha256` and
`debug_settings` in the metadata JSON. The video decoder reports missing
qualification fields. It keeps transitions, reports missing/repeated/invalid
samples and still reads v1–v5 with their inferred legacy identity limitation.
CPU submission time and blocking GX/VI waits are **not GPU execution time**.
The implementation does not take ownership of raw GX hardware metrics.

## Memory accounting scope

System queries run at most four times a second during steady play, with
explicit load/construction checkpoints; query cost and sample age are recorded.
Physical MEM1 and draw-resource peaks are sampled. Managed high-water tracking
and native model allocator request tracking run from boot. Model accounting
uses existing allocation headers and atomic counters, with no pointer registry;
it counts shared texture allocations once and excludes duplicated model budget
charges. It includes the allocator's existing header/alignment request bytes,
not a claim about malloc's usable size.

Snapshots expose physical total/used, arena/malloc free, managed live/peak/
capacity/collections, native draw and model allocation ownership, ARAM usage/
largest block/slots, terrain cache live/peak/largest free run and synchronous
read count/bytes/wait time. Terrain ownership is a **subset** of draw ownership;
managed live bytes are already inside managed capacity. Never add these twice.
The residual estimate includes executable/static data, stacks, audio, frame
surfaces, allocator overhead and other untracked allocations. MEM1's largest
allocatable block remains unknown; EFB is separate. This is scoped accounting,
not a complete per-subsystem audio/UI allocator census.

## Validation

The production managed archive runs against delayed fake DMA over a serpentine
route spanning the full town, with no missing selections or synchronous binds,
and checks the fixed handle budget, drawable-only material/triangle counts,
allocation-free steady work and cancellation back to the original path.
Native fixtures cover cache ownership, failed transfers and readback identity.
Profiling fixtures exercise capture bounds/overwrite/events, real native export,
checksums, cross-band corruption, multiple sessions, 16-bit sequence wrap,
32-bit sequence identities and legacy decoding. Original GameCube hardware
qualification remains required; emulator results cannot substitute for it.
