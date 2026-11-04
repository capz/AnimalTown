# Approved river and coast changes

The user approved implementation after reviewing top-down/oblique and cross-section mockups. This brief records the decisions for continuation in Codex Cloud.

- Add distinct river and sea handling, rather than assigning every water cell the same geometry behavior.
- Lower both the river ground and its visible opaque water surface by 0.50 m relative to the local land level. The user chose visible-surface lowering, not a hidden-bed-only change.
- Flank river-to-land edges with continuous 0.50 m banks/walls using the existing soil/cliff art language. Close corners and bridge approaches; do not put walls between adjacent water cells.
- Keep sea water at its existing height. Do not apply river lowering or retaining banks to the sea.
- Where sea meets beach, provide a visible sand/wet-sand/surf transition.
- A sea cell's dry neighbors must be beach or a land cliff facing the water.
- Coastal cliff tiles split their footprint in half: the landward half is regular grass-topped land/cliff, the seaward half is sea. The user explicitly confirmed footprint, not the lower half of the vertical cliff face. Fit the existing 2 m cliff/section language and world-aligned material bindings.
- Keep the existing opaque GX material, two independently scrolling layers, tint/depth variation and restrained optional foam. No vertex wave simulation.
- Preserve possessions, structures, progress and edited terrain when migrating saves. Do not regenerate saved towns.

## Continuation state

The handoff branch integrates the pending water-material and versioning branches with the current main cliff kit. Merge conflicts were resolved to retain both cliff-preview/material binding and water animation. This integration checkpoint has not yet been built or tested. The new river/sea terrain behavior above is not implemented yet.

Important implementation considerations:

- The current persisted `Terrain` enum has values 0..7 and one `Water` value. Both `SaveCodec` (v5) and `SplitSaveCodec` (v1) currently validate terrain against 7. Explicit new kinds need backward-compatible versioned decoding and regression tests.
- New-town generation uses narrow rivers connected to a broad southern sea. A naive boundary flood fill would classify connected rivers as sea; preserve a deliberate mouth boundary.
- Visible river lowering and unchanged sea height leave a height difference at the mouth (currently 0.44 m with the existing 6 cm sea offset). Keep the approved heights and implement/document an explicit watertight estuary/transition rather than silently flattening the river.
- Reuse `TerrainWalls`, `TerrainCliffGeometry` and `TerrainCliffBinding` for cliff art, pooled meshes and world-aligned ground/soil material bindings. Preserve the developer cliff gallery.
- `FloorStrips` selects visible cells, `TerrainRenderer` owns the floor/water caches, and `WaterRenderer`/`WaterMaterial` provide the current opaque TEV effect.
- Verify collision, map colors, terrain/elevation caches, save round trips, coast adjacency, half-footprint orientation, exact bank heights and corner/mouth closure. Then perform targeted Dolphin captures, clean console/server builds and matching release packaging as required by AGENTS.md.

## Versioning and delivery

Use the branch's `xx.yy.zz.bb` workflow. `xx` remains 0: the user's earlier “use 1” referred to the water-height option, not a major-version request. Features use `feat:` and fixes use `fix:`; counters are independent. Read AGENTS.md and docs/versioning.md.

Existing open PRs: #1 is the original scrolling-water material; #2 is versioning. This branch contains their changes for implementation convenience. Do not merge PRs or push main without user authorization. Complete the approved changes on the handoff branch, run the relevant checks, commit, push, open/update a review PR and publish matching development build artifacts. Report physical hardware validation honestly.
