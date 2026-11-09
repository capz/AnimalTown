# Debug rain and ocean preview

Ocean, coast-water halves, inland rivers/ponds and water under bridges share
`WaterTerrain.RiverHeight` (-0.50 m relative to the local terrain support).
The former ocean offset (-0.06 m) and artificial 0.44 m river-mouth wall are
removed. Beach toes follow the same water plane; coastal land remains raised.
Player/NPC support interpolates the same two beach triangles as rendering,
including diagonal shore corners, so actors follow the lowered shoreline.
Water footprints and player/villager traversal rules are unchanged. Stored
terrain/elevation layouts are not regenerated or migrated.

Open **START → Settings → Debug / testing**. The last two session options are:

- **Rain preview:** off by default. Enables outdoor depth-tested rain streaks
  and a muted sky background; interiors and fully opaque door fades suppress it.
- **Rough ocean in rain:** off by default. Enables faster stylized whitecaps
  only while Rain preview is on outdoors. Rivers/ponds stay on their calm
  material. Turning rain off immediately restores calm ocean animation.

Neither setting is saved or enabled by the calendar. Existing cloud-cover
controls remain independent. There is no automatic weather simulation, rain
sound, wetness gameplay or changed collision.

## Rendering and performance

The GameCube GX pipeline has no programmable vertex shader. This preview uses
Wind Waker-inspired moving whitecaps and crossing texture flow on the fixed
water plane; it does not simulate physically rising/falling wave geometry.
It adds one shared 64×64 I8 mask (4 KiB) and one two-texture TEV material that
borrows the existing base texture. Normal water and river shading stay intact.
Ocean and inland surfaces receive separate immutable archive kinds; toggling
weather selects a material and never rebuilds/uploads terrain or water meshes.
The existing ARAM/MEM1 budgets and 256-view pool stay fixed.

Rain uses at most 64 world-anchored cells and 128 triangles, one shared mesh and
a fixed transform pool. It evaluates positions analytically from elapsed time,
so hidden/disabled intervals need no simulation or catch-up. Grid crossings
retain each world cell's phase. Viewport/horizon checks reject invisible streaks;
GX backface culling, clipping and depth testing remain active. Rain draws after
opaque world geometry, before the UI, with alpha blending and depth writes off.
Roofs, foliage and actors can occlude the streaks; indoor rooms receive no rain.
There are no per-frame particle/mesh allocations or texture uploads. The shared
rain mesh is created between frames on first enable. No rain selection/drawing
or storm UV animation runs while the corresponding preview is disabled.

Host checks cover shared water levels, bridges/coast orientations, save layout
preservation, opt-in/indoor rules, periodic whitecap packing, long-running rain
coordinates, world anchoring, retained-cell continuity, allocation-free drop
selection, and native binding of the new ocean archive kind. Console and
emulator delivery results will be recorded after native validation.
