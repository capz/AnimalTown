# Codex handoff: static lighting and shadow-map optimizations (1–4)

**Repository:** `capz/AnimalTown` (GameCube / Hypercube / DolphinDotNet)
**Prepared:** 2026-11-17
**Task:** Implement the four optimizations below on a feature branch/PR, with independent commits, deterministic tests, and before/after performance evidence.
**Priority:** Preserve existing visual output and GC memory constraints over speculative GPU savings. Never introduce a second ground-light texture unless a measured and reviewed need emerges.

## Read first / current implementation

Read `AGENTS.md`, `docs/object-lighting.md`, `docs/cloud-shadows.md`, `docs/villager-lighting.md`, `docs/change-validation.md`, `docs/render-performance.md`.

Relevant code:
- `tools/native-shadow-union.h`: `SuState`, `su_lights`, `su_bake_lights`, `su_cloud_prepare`, `su_page_clouds`, `su_pack`, `su_terrain_offset`, dirty-page management and storage accounting.
- `tools/native-shadow-union.c`: bridge functions `animaltown_shadows_lights`, `animaltown_shadows_prepare`, `animaltown_shadows_page`, GPU flush/publication and lifetime.
- `tools/native-shadow-union-gx.inc`: `town_shadow_apply`, `town_environment_apply`, IA4/GX combiner and receiver-alpha logic; injected by `tools/patch-native.ps1`.
- `game/AnimalTown.GameCube/TownLights.cs`: static lamp/doorway list and moving model point lights; `NativeShadows.Lights(..., strength)` sends source data and time-of-day intensity.
- `tools/native-point-lights-gx.inc`, `docs/object-lighting.md`: two nearest GX point lights for outdoor actors/buildings, *not* for terrain, trees, fences or dropped items.
- `game/AnimalTown.GameCube/LowPolyTrees.cs` and `shared/AnimalTown.Domain/LowPolyTree.cs`: three *shared* indexed/procedural low-poly tree meshes, seasonal RGB vertex palettes, original detail texture. Inspect other static-model renderers before extending baked shading.
- `tests/native-shadow-union.c`, `tests/native-shadow-combiner.inc`, `tests/native-shadow-lifecycle.c`, `tests/AnimalTown.Checks/WorldLightingChecks.cs`.

**Do not misunderstand the existing architecture:** This is not a new real-time shadow map. One GX `GX_TF_IA4` texture packs **low nibble = shadow** and **high nibble = static light coverage**, sampled on ground/caps/opaque-water via `GX_TEXMAP2`. The native system stores a 256×128 static terrain field plus a 256×128 moving/cloud composite (64 KiB total), and 160×32×32 static+combined shadow pages (320 KiB). One page covers 8×8 world metres; the published page data is IA4 despite older I8 terminology in docs. The shadow cache allocation has been reported as about 522,016 bytes (under 512 KiB = 524,288 bytes), so do not add meaningful permanent allocations or a full second lightmap. Existing clouds, cliffs, scene casters, dynamic actors, filtering gutters, water receivers, and indoor exceptions must survive.

Lighting is already *partly implemented*: `su_bake_lights` produces a static coverage field using max overlap and `r*r` quadratic radial attenuation. GPU lighting strength is a separate `animaltown_light_strength()` value used to scale warm RGB (220,158,74) in TEV; day/night intensity updates do **not** need to change field coverage. Do not reimplement item 2 from scratch.

## Implementation 1: bounded, light-centric baking

### Baseline
Currently `su_bake_lights` loops every texel in the 256×128 field and then every light (up to `SU_LIGHTS = 24`), even though light radii are small (typically 2.7–2.8 m). It runs only when `light_dirty`; don't claim this cost occurs every frame.

### Change
1. Keep the current packed IA4 representation and `su_bake_lights(SuState*)` interface. On a dirty bake, first clear **only the high nibble** of all texels in `s->terrain` (retain low-nibble terrain/cliff shadow data byte-for-byte).
2. For each source `[worldX, worldZ, radius, strength]`, determine a conservative integer texel bounding box intersecting the field; clamp indices to valid texture dimensions (including relevant gutters). Use the **existing sample centres** `px=(x-.5f)/SU_TERRAIN_TEXELS_PER_METRE`, `pz=(z-.5f)/...`. For texels inside the radius compute exactly the existing `r = 1 - distanceSquared/radiusSquared`; if `r>0` then `value = r*r*strength`. Quantize exactly as `(int)((value+8)/17)` clamped to 0–15, and store `max(previousLightNibble, newLightNibble)`. No additive accumulation or new color model.
3. Preserve overlap semantics, off-world sources, near-border sampling, shadow/cliff preservation, 32-byte GX layout, and visible filtering at page boundaries. Do not accidentally include any old higher light value after removing/moving a source.
4. Keep existing dirty propagation through `su_lights`, `su_terrain_publish`, `su_cloud_prepare` and page composites. No per-frame allocations, no extra GX texture bindings, draw calls or global field buffers. A full-field high-nibble clear is acceptable; a more elaborate partial update/dirty-rectangle mechanism is **optional only if profiling justifies its complexity**.
5. Add a *test-only*, retained baseline/reference bake implementation or an independently computed expected map (not used in production) for byte-for-byte comparison, with seeded/randomized lighting cases (0/1/24 sources, overlaps, different strengths/radii, edge/off-map positions, source changes/removals). Compare the **full packed terrain and composite**, not only representative pixels. The output should match legacy quantization except where a documented floating-point rounding discrepancy is unavoidable.

### Target
On a full rebake, complexity roughly `O(fieldPixels + sum(sourceBoundingBoxPixels))` instead of `O(fieldPixels * lightCount)`; record measured wall-clock cost, not just theoretical operation counts.

## Implementation 2: preserve time-of-day intensity without rebaking

**This feature already exists.** Formalize and regression-test it; change production code only when a verified deficiency exists.

1. Confirm `TownLights.Prepare` rebuilds/re-sends the light source *layout* only when town/building/terrain/light-layout revisions change; rendering may still send the strength or model-light positions each frame.
2. Verify an unchanged light source list with changing `strength` (night/dawn/day/dusk) does **not** dirty the static light coverage or shadow masks, enqueue page rebuilds, trigger a new texture upload or cause actor shadow trails. If `su_lights` uses source equality, preserve it.
3. Preserve the existing single warm-light RGB tint controlled by `animaltown_light_strength()` via `GX_SetTevKColor`. Full darkening at daylight, fading during transition, warm lighting at night, no new per-frame texture updates.
4. Preserve static ground illumination and separately moving GX point lights for characters/buildings. Camera curvature may update world-space point positions without invalidating ground coverage.
5. Add native host and world-clock tests: no-bake counter/map hashes across full-day strength sweep; one changed source causes exactly a needed rebake, none for unchanged sources; no invalidation when entering/leaving a scene except established layout/room cases. Include hardware/recording fixture checks that TEV RGB changes while alpha and cached texels remain identical.

## Implementation 3: bake static-model ambient occlusion / lighting into existing vertex colors

**Important distinction:** Bake *object-local, permanent* ambient occlusion/directional material shading, not world-position-specific lamp pools into shared instanced geometry. A shared tree mesh is reused at many world positions; do not duplicate it per tree, break batching, or bake a particular lamp's contribution into all trees.

1. Start with the existing generated low-poly trees. Inspect `LowPolyTree.Build` and `LowPolyTrees.Prepare`: four face/part seasonal palettes and a small shared mipmapped detail texture are already applied through `VertexPositionColorTexture`. Integrate a deterministic AO factor based on static geometry/underside/branch/trunk contact into the **RGB vertex-color generation** as part of the existing rare prepare/rebuild. Preserve existing season palettes, tree types, tint, mip detail, transparency, part ordering and triangle/mesh count. Default AO should be subtle and stylistically consistent, not visibly soot-black at night. Factor/pattern must be repeatable across revisions; unit-test with fixed data and retain 255 vertex alpha.
2. Inspect one additional eligible static imported model/prop/fence path for existing author-authored vertex color support, and extend the same baked-vertex-color approach only if this can be done without a new material stage, extra unique instance mesh or expanded vertex layout. If that path cannot safely support AO, explain the blocker in the PR and leave it unchanged rather than introducing a costly general rendering refactor.
3. Do **not** alter ground receiver alpha (ground/cap/water uses it as a shadow receiver flag), turn static ambient occlusion into an additional dynamic point light, change skinned villager vertex streams or change runtime collision/placements.
4. Do not recompute AO per frame. Keep shared native indexed geometry and no extra texture. Verify visuals in spring/summer/autumn/winter, day/night, normal/mipmapped distance and several camera angles, including tree repetition.
5. Add deterministic managed/rendering tests for vertex RGB range, alpha, buffer counts, seasonal changes and mesh sharing. Measure MEM1, draw submissions and update time.

## Implementation 4: reduce redundant TEV/GX terrain work, with an explicit safety gate

### Baseline
`town_shadow_apply` currently requests `effect->count + 4` TEV stages for ground (and sets three texgens). These stages handle environment tint, shadow receiver mask/strength and blend, then warm static lighting. `town_environment_apply` handles some unshadowed draws separately. `GX_TF_IA4` reduces storage and texture bindings but the code references the shadow/light texture in more than one stage; do not equate one binding with one fetch.

### Change / experiment
1. Instrument or record the actual TEV stage count, texgen count, texture-map bindings, CPU GX submissions, EFB/GPU work where available and rendering output. Include day/night, clouds, cliffs/ramps, water, actor movement, and paths.
2. Attempt to reduce per-draw **redundant GX state changes** and/or combine stages only if the exact result is preserved: same shadow intensity (0–66/255), tint RGB (35,49,66) with environment modulation, warm light gain, alpha, stage-register flow, and indexed/material scope. Check the finite 16-stage hardware limit and existing effect-dependent stages.
3. Respect the three-term shadow gating (texture shadow coverage × receiver flag × global opacity); removing its intermediate stage without accounting for all three factors is incorrect. Preserve clamping after wide TEV inputs, alpha passthrough, and separately colored daytime/nighttime paths.
4. Do not introduce a second texture, extra draw pass, framebuffer copy, duplicate geometry, runtime shader fallback or per-material texture duplication. Keep direct/indexed path consistency, safe cache invalidation and `tools/patch-native.ps1` signature checks.
5. Build/refine `tests/native-shadow-combiner.inc` recording GX fixture so stages/registers/orders/alpha/channels are asserted. Compare before/after screenshots (including overlapping shadows and light pools) and measure actual speed/submit changes. **If a safe three-stage variant cannot be established, retain the existing four-stage combiner and implement only measured redundant-state cleanup**; report the evidence rather than trading visual correctness for theoretical speed.

## Sequence and commits

1. Baseline: inspect all listed code and current CI state; note reference commit and baseline memory/render counters. Establish deterministic reference lightmaps and representative night/day captures *before* changes.
2. Commit 1: bounded light-centric baker and native equivalence tests.
3. Commit 2: time-of-day non-invalidation guards/tests, with only justified source code changes (an all-test commit is valid).
4. Commit 3: low-poly-tree AO vertex color work (plus a second eligible static asset only if safe), managed/rendering tests and seasonal captures.
5. Commit 4: measured GX/TEV-state optimization or well-tested explanation for retaining four stages, including fixture additions.
6. Finish: update lighting docs and relevant profiling notes; summarize byte, allocation, draw-submit/TEV-stage, CPU/GPU performance deltas and screenshot provenance. Avoid unrelated UI/asset changes. If a new UI is truly needed, stop and request a mockup review first.

## Required verification and acceptance

- Inspect `AGENTS.md` and use `pwsh ./tools/test-changes.ps1 -PlanOnly` to derive affected suites; then `pwsh ./tools/test-changes.ps1` (or equivalent available PowerShell invocation). At minimum run `tests/native-shadow-union.c` and `tests/native-shadow-lifecycle.c`, the native TEV combiner fixture, relevant managed terrain/world-lighting and rendering checks, patched-native compile, and console build if the environment supports it. Record exact commands and results, including unavailable tests.
- Compare old/new full IA4 map bytes for a deterministic sample set. Preserve low-nibble cliff coverage; same max overlap and 4-bit lighting quantization; stable cloud composition and support/gutter behavior.
- Changing day/night gain alone neither rerasterizes static terrain nor uploads dirty ground masks; actors/buildings still receive moving GX point-light strength as intended.
- Trees share the original three meshes and one texture, retain seasonal styling, original geometry count, and add no new GX stage or per-frame allocations.
- No new permanently resident lightmap, extra terrain draw pass or regression in SDK accounted native buffer. Report actual `su_storage_bytes()`, memory/resource counts and frame/GPU counters.
- Test at least noon, dusk, night, sunrise; outdoor/indoor; moving cloud/character; trees/lamp overlaps; cliffs/diagonal ramps; coastline/water; different seasons; editing/moving/removing a light source; changing terrain.
- For performance claims, collect before/after numbers for *the same executable build environment and route*, separate emulator measurements from physical GameCube hardware; provide native screenshots from smoke tests if possible. No screenshots fabricated or claimed without a run.
- Document any irreducible visual or perf tradeoffs; prefer shipping the previous correct behavior over a risky stage removal.

## Delivery / Codex instructions

Work in this PR branch unless the environment requires a separate implementation branch; in that case open a linked implementation PR. Make independent commits for phases. Provide a clear PR summary, validation evidence table, perf results and screenshots/test artifact links if available. Make CI green and keep the branch mergeable, but **do not merge or publish** this task without a subsequent explicit request. Do not silently widen scope into engine-wide lighting redesign.

**Start immediately:** read the repository instructions, establish baseline, then implement phases 1–4. Do not stop after writing another plan. When a safe optimization cannot be proven, document the constraint with tests and continue the remaining phases.
