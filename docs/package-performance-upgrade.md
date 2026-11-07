# Published package performance integration

AnimalTown consumes these exact public NuGet versions, with committed locks:

| Packages | Previous | Updated |
| --- | --- | --- |
| DolphinDotNet SDK, Core, GameCube and Assets | 0.1.0-preview.245.1 | 0.1.0-preview.266.1 |
| Capz.Hypercube.Core / GameCube | 0.1.0-preview.4.1 | 0.1.0-preview.9.1 |

SDK 266.1 identifies source `858577c63aaf191fce7c83fd4308075d3775f43a`.
Engine 9.1 identifies `230d0422e5a5a91641dff9486bde7de6b4f87a0e` and pins the
same SDK dependency. The engine's existing NuGet preview workflow verifies a
relocated external consumer and a real native build before trusted publication.
The packaging compatibility change is [Hypercube PR #10](https://github.com/capz/hypercube-engine/pull/10).
No project references point into engine/SDK source checkouts or private feeds.
The offline Assets tool and Imaging payload advance with the compiler/runtime.

## Applied improvements

The SDK now submits eligible unlit vertex-coloured meshes through GX indexed
position/colour/UV arrays when material tint is white. The native allocation is
used directly; the old path expanded indexed geometry through CPU staging.
AnimalTown's opaque water retains its depth colours, two texture layers and TEV
effect and can use this path. Uniform-colour draws keep their existing fast path.
Tinted, lit and generated-UV cases retain exact fallback behaviour. Merely
upgrading does not make every mesh eligible or establish a frame-rate gain.

`TerrainSection` now uses Hypercube's `IndexedMeshCache`. Its reusable staging
shares only completely identical positions, normals, colours and UVs, preserving
wet-sand tint, cliff-height and UV seams. Indices retain the authored triangle
order. Native meshes contain only used prefixes; spare CPU capacity is not drawn
or uploaded. Four common flat row strips need ten vertices and 24 indices:
408 native payload bytes instead of 864 bytes for 24 duplicated native vertices.
The result depends on section topology; no approximate welding is performed.

The existing 8×4-cell streaming, per-frame terrain budget and dirty revisions
remain in place. Water/foam and frequently rebuilt cliff buffers retain their
bounded dynamic path. Materials, textures, soil, river/sea levels, cloud and
object shadows, collision and save formats are preserved. The experimental
ground ring stays opt-in; upgrading the SDK does not remove its whole-allocation
update cost or excess triangle submission.

The native overlay now selects the existing 4 MiB managed heap through the SDK's
`DND_MANAGED_HEAP_BYTES` Makefile setting. Signature checks still fail closed.
The compiler/Assets pins and all transitive package locks advance together.

## Capabilities deliberately kept for suitable consumers

SDK `MeshColorView` shares geometry across colour streams; it is useful only
when two draws actually share positions/topology. Distinct terrain material
footprints cannot be combined by drawing all geometry twice. Ordered native
mesh passes help repeated colour/UV variants of one single-texture mesh; they do
not reduce GPU samples and are unsuitable for the existing two-texture water
and cloud materials. The engine package retains its tested portable ordered-pass
backend. These APIs are available without adding artificial passes to the game.

## Validation

Run `tools/test-changes.ps1` for the selected managed/native/tooling suites.
Terrain indexing checks expand the authored indices and compare winding,
positions, UVs and tint; test seams, staging reuse/growth and fixed section bounds.
The engine separately checks prefix/revision invalidation and failed replacement.

Compare actual moving frames against current-main release
`v0.6.3.28174414ed84`, using copies of the same regression card and runtime mode.
The review PR records source/build identities and Dolphin guest-clock results.
Validate river/coast/cliff and shadow views, menus, memory-card saving and reload
on the matching release artifacts. Emulator results do not establish physical
GameCube performance; hardware testing must be reported separately.
