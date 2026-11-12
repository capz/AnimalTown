# Dropped-item billboards

Ordinary outdoor drops (`GroundItem.Kind == 0`) use the approved pinecone sprite
on a 0.65m square. Each sprite turns in yaw and pitch towards the actual outdoor
camera position, including off-centre views and changes in player/terrain height.
The bottom edge stays level, avoiding rolling as the player walks past. Its bottom
sits 3cm above the curved surface at the tile centre, using local terrain height.
The sprite asset is [64×64 native RGB5A3](../assets/textures/items/README.md).

`DroppedItemPlan` culls by frustum and cylinder horizon and stably sorts visible
sprites from back to front using preallocated merge-sort scratch space. Its
snapshot tracks dropped coordinates, count, town identity, terrain revision and
camera position/height. A stationary unchanged view skips geometry uploads.
The culling sphere and alpha-sort depth use the centre of the rotated quad.
Camera-facing axes are calculated only when geometry changes, with no extra
per-drop native resources or frame allocations.

`DroppedItemRenderer` uses Hypercube's bounded dynamic mesh buffer. All visible
sprites share one texture/material and one draw, with two triangles per sprite;
all 512 saved ground slots fit in the 3,072-vertex ceiling. Buffers grow only as
needed. Native loading and vertex updates happen outside render passes.

Sprites draw after opaque terrain, actors, trees, structures and fences, with
depth testing, alpha blending and depth writes disabled. Zero-alpha fragments
are discarded. This preserves soft edges and occlusion without transparent
rectangles hiding other drops. The native texture and mesh buffer are disposed
with the town renderer. No saved world data or item interaction rules change.

Validation includes source/native alpha and colour round trips, all seven GX
mip layouts and alpha coverage, flat/raised terrain, camera alignment and
clockwise winding, culling, pickup/bury invalidation, 512-item sorting/capacity,
and allocation-free plan updates while moving.
