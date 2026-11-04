# Performance panel and next investigations

Settings → Performance display or **L + R + Z** toggles the panel. The shortcut
works on every screen, including water inspection. It starts hidden. Turning it
back on clears the timing window, so time spent with profiling off is excluded.
The existing v4 capture pixels in rows 448–479 remain available to
`tools/read-frame-profile.py` without changing its protocol.

The panel displays the previous completed frame. Text refreshes at most four
times per second, with no string formatting or memory sampling while hidden.
FPS is frames divided by elapsed time across the last 120 completed intervals;
min/average/max use that same window. The graph updates every completed frame,
with a 33.3 ms reference line and a 66.7 ms ceiling. Slow bars saturate in red;
the numerical maximum retains the actual duration. The first interval is omitted.

## What the counters mean

- **Frame:** guest-clock end-to-end interval, including presentation and delay.
- **Polygons (tris):** submitted triangles, including UI and diagnostic geometry;
  not unique model polygons or visible pixels. Draws are GX primitive submissions.
- **Main RAM:** physical MEM1 minus arena space and allocator free blocks, using
  the same calculation as the pinned SDK's performance overlay. Includes code,
  stacks, reserved managed heap, native assets and graphics allocations.
- **Textures in RAM:** all SDK drawing-resource texture payloads (including
  fonts, cached floors, generated map textures and retained pending resources),
  plus the SDK's global shared model-texture allocation counter, read once.
  Shared model textures are not multiplied by model or instance count. Model
  allocator padding is included; other drawing textures report payload bytes.
  This excludes duplicate encoded model-package sections and the SDK's static
  console-glyph atlas, framebuffers and FIFO.
- **Draw resources:** SDK drawing-resource bytes, including meshes, canvases,
  buffers and textures. Excludes imported model packages/instance workspaces,
  framebuffers and FIFO. Overlaps the texture figure; do not add the two.
- **GX wait / VSync:** blocking durations after submission, not GPU execution
  time. Presentation includes these waits; do not add them again to frame time.

GameCube has no separate texture VRAM allocation pool: GX reads textures and
indexed arrays from main RAM. These are resource residency measurements, not
hardware texture-cache occupancy or the host emulator's GPU memory usage.
Panel rendering affects the measured workload. The telemetry phase includes
panel construction; final UI flush remains in the UI phase. Memory queries and
text refresh happen between frames and contribute to the following interval.

## Source investigation, ordered by likely value

These are hypotheses to profile, not newly measured speedups. Existing evidence
in `tree-render-investigation.md` already shows the generated indexed tree change
removed the former dominant tree-submission cost; retain that implementation.

1. **Terrain preparation during movement.** `TerrainRenderer.Prepare` invalidates
   on exact player X/Z changes, rebuilding strips, floor vertices, water and
   foam. It uploads entire capacity-sized buffers and pads unused vertices with
   degenerates. Compare moving-world `terrain_prepare` and submission to cached
   stationary frames using v4 captures. If dominant, investigate separating
   visibility/topology from camera-relative cylinder transforms and supporting
   active indexed draw ranges in the SDK. Preserve fractional-camera edge
   coverage, terrain revisions, elevations, water tint and foam continuity.
2. **Idle player skinning.** `TownGame.Step` calls the player's model update every
   simulation tick whenever the model has animations, including stopped/menu
   states. Villagers already avoid updates when stationary or invisible. Measure
   `animation_skinning` with stopped versus moving players before adding dirty
   pose evaluation in the engine. Stopping, species changes and the first visible
   pose must still publish correctly. Do not simply skip the first stopped update.
3. **UI allocation and submission.** Settings labels construct an entire string
   array per `SettingLabel` call; HUD values also concatenate every frame.
   Cache unchanged labels/values, then compare backpack/settings `ui_submit`,
   API calls, material changes and draw counts. Keep the existing batched glyph
   and shape paths, and preserve long-dialogue visible-character behavior.
4. **Frame pacing.** The loop adds a one-millisecond task delay after a blocking
   present. Compare `task_delay`, `retrace_wait` and interval distributions before
   changing it. Removing the delay without checking executor/network fairness
   and audio can trade throughput for stalls elsewhere.
5. **Memory headroom.** Seven imported models and their actor/prop instances stay
   resident. Profile startup and species switches. `GraphicsDevice.NativeBytes`
   alone omits these allocations, while summing model NativeBytes overstates
   shared texture storage. Use MEM1 headroom first; consider measured lazy
   residency only if needed, since synchronous restores can hurt frame times.

## Validation and follow-up measurements

The native accounting fixture is integrated into `tools/test-changes.ps1` as
the `performance` suite, including full validation. It checks empty caches,
nontexture/freed slots, and global model-texture accounting across multiple
models. The AOT hook fails if its expected signature/resource table changes.

Run `pwsh -File tools/test-changes.ps1` in the configured builder. Native patch
and validation-map changes require the repository's full selected coverage.
Then build the same-commit ISO/backend using the repository's build scripts.
In Dolphin, verify Settings and L+R+Z toggles on title/world/menu/water inspection,
wait for samples, hide for several seconds, and re-enable. Check text fit, bars,
and unchanged v4 decoding. Capture at least 60 seconds of idle world, movement,
backpack and dialogue with the same card/build; compare p50/p95/max phase times.
Keep raw captures, decoded results and build identity together. Physical-console
frame times and compatibility still require hardware testing.

In this editing environment the isolated GCC fixture passes. PowerShell, .NET,
Docker and the console toolchain are unavailable, so the repository runner,
AOT build, emulator behavior and hardware behavior have not been validated.
No new runtime FPS improvement is claimed by this change.
