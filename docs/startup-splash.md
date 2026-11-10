# Startup splash sequence

Boot fades in the user-supplied DolphinDotNet logo, holds it, fades through black
to the approved disintegrating-block Hypercube Engine logo, then fades to the
existing loading/title screen. Each logo gets a 650 ms fade in, 3 second hold
at full visibility and 650 ms fade out: identical 4.3 second real-time deadlines. Between-frame
loading time counts toward these deadlines rather than extending one logo's
hold. Like any frame-based presentation, a blocking loader step can delay the
next displayed frame; the timeline catches up instead of lengthening the sequence.
Input during the sequence is
consumed; the player must press A again on the title screen.

DolphinDotNet uses a solid white background across the entire display, including
the margins outside the fitted logo canvas. Its black logo fades in from white
and back out to white. The display then switches to black for the Hypercube
presentation, whose logo fades against black. Both keep the same timings.

Both PNG files in `assets/textures/splash` are exactly 384 x 256, matching the
supplied DolphinDotNet image. The Hypercube design has been downsampled without
stretching, with its navy gradient and floor glow replaced by flat RGB(0,0,0)
black as requested. `splash-import-engine ROOT PATH` normalizes an edited image
to these exact source dimensions; `splash-check` rejects mismatched source sizes
or non-black engine background margins. `SplashAssets`
converts them offline into opaque, big-endian RGB565 4 x 4 GX tiles in DNTX
packages. Power-of-two storage adds unused right padding (512 x 256); runtime
UVs exclude it. Each texture costs 262,144 bytes, with no mipmaps or runtime PNG
decoder. Previews in `out/brand/splash-*.png` show the actual 384 x 256 content.
The centered 576 x 384 display rectangle and fitted 640 x 480 canvas keep the
logos inside overscan margins and preserve proportions on display modes.

Only one splash texture is resident. It bypasses the shared texture cache and
resource registry, is disposed after its final black frame (after `EndFrame`'s
GX completion fence), and is released before loading the next splash. The private
12-vertex drawing batch is also disposed, including on exceptions. Package arrays
do not escape the loader helper; the existing native collection bridge reclaims
them after upload and reclaims all remaining managed splash arrays after the
entire splash call returns. No splash resource is pinned for gameplay.

Synchronous town/save/font/terrain initialization stays on the app thread before
either timed logo begins, with the display black. Each splash frame calls the existing `AdvanceLoading`
between render passes: async model reads overlap presentation and the existing
loader publishes models/building parts on the app thread. No extra worker, parallel
DVD stream, `Task.Run`, or off-thread GX/resource construction is introduced.
If loading outlasts both logos, the existing loading screen continues it; asset
failures retain the existing error display. Simulation timing begins after the
sequence, so startup time is not treated as gameplay elapsed time.

`dotnet run --project tools/AnimalTown.Tools -c Release -- splash-check .`
checks package dimensions, format, byte budget and every decoded pixel against
the composed originals within RGB565 quantization bounds. ISO art generation
also runs this check. The managed `ui` suite checks fades, holds, equal deadlines
under different frame/loading costs, completion and independent second-logo timing. Physical GameCube validation
is still required for final hardware appearance and timing.
