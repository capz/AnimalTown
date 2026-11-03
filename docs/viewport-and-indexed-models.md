# Viewport retention and imported-model rendering

The viewport bug has a game-side visibility cause: a fixed 22-unit rectangle
was applied before the camera/horizon sphere test for buildings and villagers.
That rectangle is not a conservative projection test, especially for tall or
distant objects whose visible portion reaches into the camera frustum. Fences
and dropped objects also had independent distance cutoffs. Those cutoffs are
removed; full bounds decide visibility and GX clips triangles crossing a plane.
Sphere tests retain a small 0.025-unit boundary guard. Box bounds now use the
actual rigid cylinder rotation, rather than sampling a different surface point
at half the box depth.

Regression checks cover partially visible spheres with off-screen centers at
all four viewport edges, multiple depths and a building outside the former
22-unit range that still intersects the visible frustum/horizon. Existing
terrain-coverage and conservative horizon-oracle tests remain active. This does
not disable culling: wholly outside/hidden bounds are still rejected.

The remaining performance target is the SDK imported-model path, previously
118.170 ms mean for structures in the recorded c861ec4 world capture. The new
SDK revision binds eligible model geometry as GX indexed arrays and moves
the default diffuse headlight to GX. Source textures, geometry, material alpha,
culling and UVs stay authored. There is no expanded geometry allocation. Skins
reuse their existing evaluated position/normal buffers. Animated material
channels, nonuniform vertex colors and unsupported lighting/transforms
retain the direct path. See the pinned SDK's `docs/indexed-rigid-models.md`.

Settings displays the performance toggle's On/Off state and a developer model
drawing comparison mode. L+R+Z toggles profiling, L+R+Y switches indexed/direct
model submission outside the frame. This native diagnostic switch is bridged
through `NativeRenderProbe` during AOT, alongside the existing game bridges.
Both modes use identical geometry and corrected visibility, allowing a more
controlled comparison than replacing assets between builds.

Run `tools/iso-smoke.py ISO CAPTURE_DIR --resume --profile --compare-renderers`
in the Linux Dolphin image with a copied regression card and 512 MiB shared
memory. The script records stationary indexed and direct scenarios before its
movement/backpack captures. The decoder labels direct scenarios separately,
including cached/rebuilt terrain subsets. Discard five transition frames in
each primary scenario. Timings remain guest-clock CPU/submission/wait values,
not physical GameCube measurements. Publish the exact measured ISO identity,
raw capture, JSON and checksums with the GitHub release.

Rounded UI corners and side bulges now reuse precomputed curve coordinates.
The same segments, rounding and panel bulges remain; the backpack no longer
re-evaluates thousands of sine/cosine calls for unchanged curve geometry each
frame. Only animated heading/popover motion still evaluates its changing angle.
