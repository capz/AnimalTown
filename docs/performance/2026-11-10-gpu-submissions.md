# Fewer GX submissions

The implementation starts at main `9a4aad4ff05c307bcc7daf4e553269ac93027c03`.
It keeps DolphinDotNet `0.1.0-preview.266.1` and Hypercube `0.1.0-preview.9.1`.
`DrawMeshes` saves managed/native call overhead but still submits each mesh to GX.
These changes merge retained geometry instead of expanding indexed vertices in
`DrawMeshBatch`'s 768-vertex direct staging buffer.

| Pass | Change | Submission bound |
| --- | --- | --- |
| Trees | One colored mesh per species/season with white material tint | Four to one per visible tree; up to 128 to 32 |
| Garden fence | Four cached cylinder-baked sides, retaining post visibility tests | Up to 53 posts to four sides; remove two duplicate corner posts |
| Terrain | Two 8×4 m scratch sections feed one immutable 8×8 m shadow page; native cold coalescing rebases indices | Compatible parts within a page become one; different pass, format and uniform color remain separate |
| Cottage | Derived closed exterior includes the translated door | Two to one per closed building; moving doors remain separate |
| Rain | Fixed 64-drop dynamic mesh, updated before BeginFrame | Up to 64 to one; hidden slots are degenerate |
| UI panels | Cached shadow then face in one multicolor geometry | Two to one per warm panel |
| IPL text | Crop to ink plus filter guard; group disjoint glyphs by page | Fewer atlas flushes when safe; retain order of overlaps |

Terrain keeps the 8 MiB ARAM reservation, 1 MiB MEM1 cache, exact source vertex
attributes and shadow-page/material boundaries. Coalescing uses a temporary
128 KiB cold staging buffer (previously 64 KiB), released when baking finishes.
Pages over 64 KiB or the bounded coalescer limits retain their original parts.
The conservative visibility oracle still evaluates the original 8×4 m cells,
then deduplicates their parent pages. Drawing a selected page can submit an extra
half-page of offscreen triangles. No geometry is built while travelling.

Fences rebuild only for town identity, house placement/relocation, garden tier
or terrain revision. Visibility is accepted if any original post in a side is
visible. Extra offscreen posts trade a bounded amount of GPU geometry for fewer
submissions. Collision and placement stay unchanged.

The cooker accepts the current kit's one unindexed static primitive, identity
node and shared atlas/material. Unsupported layouts fail the build explicitly.
The derived GLB lives under `out/derived-buildings`; originals and the seven-name
building catalog/save schema are unchanged. Eight native parts share the existing
768 KiB residency ceiling. Door animation and interior wall cutaways keep their
original paths.

IPL ordering is confined to one text draw. A glyph moves past another only when
both filtered rectangles are disjoint. Headings keep their original order.
Layout ordering and geometry lookup use bounded caches; partial typewriter text
uses a separate count key. Blank ink retains its advance. Rain retains drop and
triangle order, alpha blending and depth testing. Inactive weather has no upload.

## Validation and measurement

`tools/test-changes.ps1 -BaseRef HEAD -NativeInProcess` selects full coverage.
Local managed, rendering, importer and native checks pass; backend integration
requires the clean CI database because the managed cloud's Docker disk is full.
Native archive regression tests compare every merged vertex byte and rebased
index, separate uniform color/water passes, exercise 10,000 rebinds and verify
atomic cache exhaustion. Managed tests cover page-oracle coverage, zero travel
allocations and safe glyph ordering.

The branch release workflow builds and publishes a clean full ISO, direct NKit
ISO, matching server/tools/kits, manifests and checksums. The GPU submission
workflow compares main and candidate against the same disposable 32-tree,
maximum-garden card. It verifies full ISO SHA-256 and clean source identities,
then captures profiler-off guest-VI cadence, walking/running, backpack, rain,
closed/opening cottage and interior screenshots. Reports and evidence are uploaded
to the matching prerelease. Timings use guest NTSC VI chunks, including repeats;
wall time determines only watchdog deadlines.

The optional `0x4153` checksummed telemetry strip at y=416 records GX draw deltas
for terrain/interior, trees, buildings, fences, rain, UI before profiler and the
final UI flush. That last flush includes pending profiler geometry. Existing
frame/detail strips and decoders remain compatible. Profiling is off by default;
no snapshots or strip draws execute during normal play. `read-frame-profile.py`
reports per-pass statistics only when matching complete samples are present.

Submission bounds above describe geometry, not measured frame-rate gains.
Record clean CI and Dolphin results here after the matching release completes.
Physical GameCube hardware remains untested.
