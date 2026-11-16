# Dropped-item billboards

Ordinary outdoor drops (`GroundItem.Kind == 0`) use six possible appearance
textures. They share the existing **0.65m** square camera-facing billboard
geometry, with a 3cm terrain-relative bottom offset. The actual outdoor camera
position determines yaw and pitch (including off-centre and raised views); the
bottom edge remains level as the player walks past.

| Appearance | Example |
| --- | --- |
| Generic fallback | Fruit, bugs, fish, furniture |
| Acorn money bag | 1,000 / 10,000 / 50,000 acorns (IDs 22–24) |
| Shovel | Fishing rod, bug net, shovel, watering can, axe |
| Shirt | Any wearable item |
| Scroll | Quest letter and lost umbrella |
| Present | Parcel, or any ground item with `Wrapped == true` |

`ItemFlags.Gift` means *can be gifted* and never implies gift wrapping. The
present appearance uses the parcel item or the explicit wrapped state, encoded
in the high bit of the persisted `GroundItem.Kind` field. No new ground save
fields, item IDs, or format-version changes are needed.

Each sprite is [64×64 GX RGB5A3 with seven native mip levels](../assets/textures/items/README.md).
All source PNGs live next to the generic `dropped-item.png`; the existing asset
cooker includes the native category packages in `out/disc` without PNG decoding
in the game. Soft shadows are baked into the transparent images.

`DroppedItemPlan` retains per-visible-drop appearance and invalidates geometry
when the item identity or wrapped state changes. It culls using the frustum
and cylinder horizon, stably sorts far-to-near using preallocated scratch,
and avoids rebuilding when a camera/ground snapshot is unchanged.

`DroppedItemRenderer` keeps the same bounded six-triangle billboard write
path and native `DynamicMeshBuffer` system. Geometry is batched by texture,
with depth ordering retained within each appearance batch. A category allocates
its material, native texture, and retained mesh only when first visible.
Native packages are held until the town renderer is disposed. Alpha blending,
depth testing, alpha cutoff, and disabled depth writes preserve antialiased
cutouts against scenery and other opaque objects. Different-category overlapping
transparent drops are drawn in category order rather than one global alpha
order; drops cannot occupy the same tile in the current game.

Pickup, burials, inventory IDs, collision, and item interaction rules remain
unchanged. A wrapped ground item retains its original item ID on pickup; the
appearance flag is visual while on the ground.

Validation covers source/native alpha and RGB round trips, all six package
dimensions and mip layouts, per-item category selection, reclassification after
ground changes, flat/raised terrain, camera alignment, sprite winding, culling,
pickup/bury invalidation, 512-drop capacity, and allocation-free plan updates.
