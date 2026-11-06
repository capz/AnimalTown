# Reusable cylinder ground prototype

The common flat grass floor can use one persistent indexed mesh instead of many
cached patch meshes. The existing cylinder transform still moves the entire
landscape. A 64-column × 48-row toroidal allocation covers the visible flat
surface and a guard margin, rather than drawing a complete 360-degree cylinder.
Each logical cell retains its vertex slot while it remains in that window.

Fractional movement changes only the shared transform. Crossing a logical tile
boundary reassigns the entering row or column: positions and world-aligned UVs
are written into the existing vertex allocation, and fixed indices are reused.
A stationary warm window uploads nothing. A teleport, edited terrain, loaded town
or seasonal/material change refills the window. The per-cell geometry retains
the existing shared row chords; no vertex-wave simulation is introduced.

The prototype handles the grass material's zero-height cells. Other material
assignments, beaches, water, raised terrain, ramps, cliff corners and coastal
halves retain their original cached geometry. Ineligible ring cells are
zero-area triangles, with no duplicate visible surface. The cliff gallery uses
the complete patch renderer. Unsupported legacy floor metadata also falls back.
Collision, terrain IDs, save data and authored interior cameras are unchanged.

The ring borrows the original grass material, neutral repeat texture, seasonal
tint and cloud-shadow effect. Logical UVs preserve both texture detail and cloud
alignment. No texture atlas or reduced-detail ground texture is generated. The
pinned Hypercube/DolphinDotNet NuGet packages remain the engine/runtime source.

## Why start with one material

The SDK's indexed mesh path requires uniform vertex colour. Combining every
floor tint into one mesh would switch to the direct staging path. Keeping the
common material in one mesh retains indexed submission while providing a useful
comparison. Ground texture/material changes are represented by membership in
the ring or the original material's cached mesh.

DynamicMesh.SetVertices also rescans colours and flushes the native allocation
for every update. A single entering row uses one range update. Scattered column
changes, multiple entering rows and resets are coalesced into one upload, avoiding
one full native flush for every changed cell. This API limitation makes measured
boundary-crossing costs essential; fewer draw calls alone do not prove a gain.

## Bounds and comparison

The allocation holds 12,288 vertices and 18,432 fixed indices (6,144 submitted
triangles including empty cells). Native geometry is 479,232 bytes; CPU vertices,
row staging and plan storage bring accounted residency to about 0.75 MiB. It
shares the existing 3 MiB terrain target with patches. Prefetch/eviction includes
these bytes. No per-frame vertex or index allocations are made after creation.

The prototype starts disabled. Outdoors, hold L+R, push C-stick up and press Z to
toggle it. Profiling displays `Ground ring` or `Ground patches`. L+R+Z with the
C-stick centred retains its existing profiling toggle. Telemetry v5 word 23 adds
bit 14 for ring-active and bit 15 for a ring upload; lower fields are unchanged.
The decoder reports those flags separately.

Use copies of the same memory card and the same clean ISO for both runs:

```sh
python3 tools/iso-smoke.py ISO OUT_PATCHES --movement-profile --profile-seconds 8
python3 tools/iso-smoke.py ISO OUT_RING --movement-profile --profile-seconds 8 --ground-ring
python3 tools/read-frame-profile.py OUT_PATCHES/user/Dump/Frames/*.avi --check-movement
python3 tools/read-frame-profile.py OUT_RING/user/Dump/Frames/*.avi --check-movement
```

Run the emulator scenarios separately to avoid host contention. Compare terrain
preparation, terrain submission, GPU waits, frame intervals and draw counts,
including tile-boundary uploads. Keep the established renderer as the default
unless the complete movement results justify changing it. Review-PR measurements
identify the exact build and runtime path; physical hardware testing must be
reported separately.
