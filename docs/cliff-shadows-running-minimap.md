# Cliff shadows and running minimap

Raised terrain casts a restrained shadow toward the south-east, matching the
existing fixed north-west scenery light. Heights come from the saved terrain,
including diagonal cliff corners, natural ramps and the land/water halves of
coastal cliffs. Upper caps do not shadow themselves. Opaque water receives the
terrain projection while retaining its two scrolling TEV layers; foam and
vertical soil faces retain their existing rendering.

A world-aligned 256×128 GX I8 mask uses one texel per metre and a one-pixel
gutter. Baking runs in bounded tile jobs on terrain revision changes, including
during asset loading. Each upload is one 32-byte tile; the complete cliff source is published
into the shared moving composite. Old coverage is hidden immediately on edits.
The static field occupies 32 KiB; a second 32 KiB field merges moving clouds
by maximum coverage. Together they retain the previous 64 KiB field budget inside the native shadow allocation, which remains
below 512 KiB including the local object/actor masks and support data. Each texel
retains four area samples scaled to its one-metre footprint, and GPU bilinear
filtering softens the field over the same town coordinates. No additional caster
slots, mask pages or per-cliff draw calls are
created. Local scenery/character pages merge the field by maximum coverage, so
overlapping shadows do not repeatedly darken the same ground. Steady movement
does not rebake it.

A north-up minimap appears after **more than six seconds of uninterrupted actual
running** outdoors. Walking, standing or being blocked breaks the streak. After
a visible map's running stops, it remains for **four seconds**; restarting
running within that grace keeps it visible. Menus and interiors hide/reset it.
The minimap shares the full map's terrain texture and shows the player, home
and shops with a fixed number of markers. Terrain texture updates remain
revision-based.
The terrain fills the running map's rounded, bulging silhouette edge to edge,
including its convex sides, with only the offset shadow behind it. Markers use
the same enlarged map coordinates and stay inside the curved edge. The full
town-map screen retains its existing layout.

The persistent build-number, acorns and loan panels are removed, including the
title-screen version caption. Build identity remains available through Settings
and release/disc manifests. Money, loans, possessions and save formats are
unchanged.
