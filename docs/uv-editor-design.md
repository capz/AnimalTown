# Human-driven UV repacking

Status: proposed UI/workflow, not an implemented editor. The CLI repacker and
quarter-turn chart matching are implemented separately. UI host choice is pending;
the recommended host is Blender, with an AnimalTown add-on for the game-specific
rules. This avoids rebuilding modelling selection, camera controls and undo.

## Workspace

```text
 AnimalTown UV Studio      shop.glb          Original | Packed | Difference
 +----------------------------+----------------------------+------------------+
 |                            |                            | Selected islands |
 |      Textured 3D model     |      UV atlas + pixel grid | Rotate [ 90 deg] |
 |                            |                            | Scale  [ 100% ]  |
 |      Select faces here    <---->   Select islands here   | Uniform [x]      |
 |      Orbit / zoom          |      Move / rotate / scale | Pin / Protect    |
 |                            |                            |                  |
 +----------------------------+----------------------------+------------------+
 | Matches: exact / quarter-turn matches   Preview | Stack | Unstack / Undo   |
 +--------------------------------------------------------------------------+
 | Output [Original / 128 / 256 / 512 / 1024]   Bake preview | Validate | Export |
 +--------------------------------------------------------------------------+
```

The artist stays in control: select matching islands, preview their correspondence
on the model, accept a stack, rotate or scale the target layout, and pin important
artwork before asking the packer to fill remaining space. Scaling should show
effective texel density immediately, especially for eyes, clothing patterns and
the shop sign. Protecting an island sets a minimum density rather than merely
pinning its location. Similar colours may be suggested separately, but exact
matching remains the only automatic stacking policy.

Rotation controls: clockwise/counterclockwise 90-degree buttons, numeric angle
and Blender's normal interactive rotate tool. Automatic matching tests 0, 90,
180 and 270 degrees. Arbitrary manual angles require rebaking and can blur art;
quarter turns can remain texel-exact when chart dimensions are preserved.
Mirroring is a separate future option, not silently treated as rotation.

## Preserve artwork while editing

Moving UVs by itself changes which artwork a surface samples. Keep an immutable
source mesh/material/UV set and a separately editable target UV set. The 3D preview
continues to sample the source until a preview bake transfers that source albedo
into the edited target layout. Do not bake lighting into albedo.

Stacking must compare the sampled RGBA content, including its filtering footprint,
not just the shapes of islands. Rotated duplicates require both a UV transform
and corresponding texture rotation. Different alpha values never merge. An
artist-approved approximate match must remain distinct from an exact match.

Bake padding is expressed in output pixels and accounts for AnimalTown's base plus
one half-size mip. Overlaps are legal only for accepted matching stacks. Other
overlaps, off-atlas faces, missing images, unsupported material maps and density
violations block final export with specific offending islands highlighted.

## Preview and validation

The primary preview is the model under unlit albedo, so lighting cannot hide
changed artwork. Provide a second approximate GameCube material view and native
pixel-resolution display with linear/nearest and mip preview. Dolphin and actual
hardware remain the final visual checks; the editor cannot promise identical GX
lighting by itself.

Validate before export: per-triangle source/target samples and a visible difference
heatmap; inspect thin charts and important faces at gameplay distance. Show atlas
dimensions, native format, base/mip bytes and maximum/worst-island errors. Export
a new GLB and audit, preserving rig, animation and source files. The current CLI
is the independent GLB validation/conversion component, not a substitute for
rebaking arbitrary manual edits. A future editor export bridge must preserve the
source/target UV correspondence so it can validate edits against the source GLB.

Save editable work as a separate .blend project, with source SHA-256 identities,
stable chart identifiers, stack membership, constraints and output policy. Keep
undo history in Blender; a small versioned JSON sidecar can carry export settings
and reproducible accepted transformations for headless builds.

## Tool choices

* Blender UV Editor + a Python add-on: recommended first UI. Existing island
  selection, transforms, pinning, packing, undo and GLB import/export provide the
  foundation. Custom code supplies source/target management, matching review,
  GameCube budgets, preview baking and export checks.
* Blender Cycles bake: source-albedo transfer into the target atlas, using a
  controlled emission bake to exclude lighting. Handle alpha separately and
  preserve authored colour-space interpretation. Secondary maps require their
  own coordinated bake path before enabling those materials.
* Existing AnimalTown C# tool: exact RGBA/rotated-chart matching, GLB checks,
  provenance, quality gates and repeatable command-line jobs on Linux.
* xatlas: optional future packing/unwrapping helper for a standalone editor;
  it does not supply the editor UI or rebake source artwork automatically.
* Dolphin and hardware: validate exported textures, mipmaps, rigging and final
  in-game appearance. Desktop editing dependencies never ship on the console.

Official references:
https://docs.blender.org/manual/en/latest/modeling/meshes/uv/editing.html
https://docs.blender.org/manual/en/latest/render/cycles/baking.html
https://github.com/jpcy/xatlas

## Delivery sequence

1. Confirm the UI host. Build the source/target project and reversible island
   transforms with synchronized model/UV selection.
2. Add exact-match review (including rotations), stacking, pinning and density
   constraints. Keep approximate matches opt-in and visibly labelled.
3. Implement source-albedo preview baking and difference inspection. Validate
   opaque base-colour materials first, then alpha and additional maps.
4. Wire GLB export/audits to Linux checks and validate the shop and fox through
   the native importer and GameCube renderer before replacing game assets.
