# White player visibility through occluders

The player draws in two passes after the scene's other models and objects:

1. Draw the same player pose with constant white RGB, `GX_GREATER` depth testing
   and depth writes disabled. Existing scene depth selects hidden fragments.
2. Restore ordinary render state and draw the player normally. Visible fragments
   cover any white from farther parts of the player, preventing self-occlusion
   from exposing internal body surfaces in otherwise visible areas.

HUD/menus draw afterward. The effect is always active for the player, indoors
and outdoors; it has no object-category filter or CPU obstruction query. Water
inspection already suppresses the player, so it suppresses both passes.
Anything in the existing depth buffer can obstruct the player, including terrain,
other villagers, fences and placed objects. Transparent scene surfaces that do
not write depth do not independently trigger it.

## Rendering and cost

The managed path recognizes the player instance after ordinary visibility culling
and uses the same transform for both passes. The white draw is immediate, before
the normal draw enters the actor queue. Its native override is reset in a
`finally` block. It does not apply to NPC or structure submissions.

The native hook borrows indexed model data and already evaluated skin buffers.
It performs no additional animation/skinning, heap allocation, framebuffer copy,
texture readback, mask generation or render-target allocation. Cost is one extra
player submission, including its triangles and material/texture setups. Imported
meshes that cannot use the indexed path retain the SDK's existing direct path.
White shading disables lighting, so the indexed pass does not fetch normals.

The existing TEV alpha calculations and texture coordinates remain intact; only
RGB becomes white. Authored alpha cutoffs remain unchanged. Blended player
surfaces are solid white where their final alpha is greater than zero; fully
transparent fragments are discarded. Depth writes remain off for every material.
The native material is copied locally rather than modifying the model. Pipeline
caches are invalidated on entry/exit so the next model or ordinary mesh restores
its own state. The hidden pass bypasses UV visualization; the normal draw retains
the existing UV controls.

`tools/player-occlusion-patches.json` contains exact, guarded replacements for
the pinned SDK model renderer. `tools/patch-native.ps1` applies them to generated
build sources and installs the AOT bridge. The vendor checkout stays unchanged.
A changed SDK signature or render hook fails the build rather than silently
disabling the effect.

## Validation

`python3 tests/player-occlusion.py` compiles the patched production model renderer
with the pinned SDK's recording GX backend and allocation instrumentation. It runs
the existing model acceptance assertions plus checks for reversed read-only depth,
white RGB, preserved alpha/UVs, mirrored/two-sided culling, static and skinned
geometry, no indexed normal fetches, no added allocations, direct/indexed modes,
multi-stage materials and restoration of normal rendering. It uses temporary
files and the same patch table as the build. The `occlusion` suite is wired into
local validation and release builds.

The native recording test passes in this environment. PowerShell/.NET are absent
and the Docker socket is inaccessible, so the complete build, AOT bridge execution
and Dolphin/hardware behavior remain unvalidated. No hardware frame-time claim
is made.

On a built ISO, walk each species behind a tree, building, fence, terrain edge
and NPC; check partial/full obstruction, turning and running, alpha cutouts,
unobstructed limbs, interiors and menu overlays. Check both indexed/direct and
batched/baseline controls. Profile identical paths before/after this commit,
comparing actor submission, triangle/draw counts, GX waits and frame p50/p95/max.
The existing actor timing and submission counters include the additional pass.
