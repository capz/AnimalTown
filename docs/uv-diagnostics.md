# UV diagnostics

Settings now includes two independent, session-only switches, both off on boot:

* **UV island colors** replaces imported-model textures with stable flat colors,
  one color per UV-connected triangle component.
* **UV island outlines** draws thin, depth-tested dark boundary lines over normal
  textured rendering. Enable both for outlines over the island colors.

These switches affect the player, computer villagers and imported structures
(the shop/house model and tent). Generated trees, ground, cliffs, furniture boxes
and UI use a different mesh path and retain their normal appearance. Settings
shows all nine rows on one page. A/B and stick/D-pad navigation stay unchanged.

An island joins triangles only when an edge's 3D endpoints and UV endpoints
match. Split vertices caused only by normals remain connected. UV seams and open
mesh boundaries become outlines; ordinary triangulation diagonals do not. Separate
disconnected pieces remain separate islands even when their UV coordinates overlap.
Islands are identified within each imported mesh; material/mesh breaks may also
appear as boundaries. This is a surface diagnostic, not an editable 2D UV atlas.

The game-owned native adapter wraps only the imported-model draw path of the
pinned SDK; no engine/SDK checkout changes are required. Both views reuse the
instance's evaluated skin positions, node transforms and cylindrical world
transform. Colors intentionally ignore texture opacity/lighting so the complete
UV geometry is visible. Outlines keep textures and use a small normal offset,
depth testing and no depth writes; they are not an X-ray view.

Topology is cached outside the render frame when the first switch is enabled.
Result arrays are capped at 1 MiB in total, with a per-mesh 25,000-triangle
preparation limit (under 4 MiB edge-sort scratch). Preparation failure leaves the
current mode intact and reports it. Turning both switches off frees the cache.
No source GLB, compiled package, texture or save data is modified.

Normal mode takes the existing indexed/batched path without topology processing
or extra passes. Colored diagnostics use a direct triangle pass; outlines add a
line pass and invalidate model state-cache assumptions. These are inspection
modes and may reduce performance; their frame times are not representative of
normal gameplay. Native tests cover seam detection, duplicated normals, hidden
triangulation edges and malformed/bounded input. The ISO smoke test's `--uv-debug`
mode captures normal, colored, combined, textured-outline and restored views.
