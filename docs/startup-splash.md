# Startup splash sequence

Boot fades in the user-supplied DolphinDotNet logo, holds it, fades through black
to the approved disintegrating-block Hypercube Engine logo, then fades to the
existing loading/title screen. Each logo gets a 650 ms fade in, 1.2 second hold
and 650 ms fade out. Presentation deltas are capped at 50 ms so a slow disc or
initialization step cannot skip an entire logo. Input during the sequence is
consumed; the player must press A again on the title screen.

The exact approved PNGs are retained in `assets/textures/splash`. Both are fitted
without cropping or stretching into the DolphinDotNet source's 384 x 256 canvas.
The Hypercube image is downsampled to this same content size. `SplashAssets`
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

The first fade is displayed before synchronous town/save initialization. That
initialization stays on the app thread, with the fully visible DolphinDotNet
frame on screen. Thereafter each splash frame calls the existing `AdvanceLoading`
between render passes: async model reads overlap presentation and the existing
loader publishes models/building parts on the app thread. No extra worker, parallel
DVD stream, `Task.Run`, or off-thread GX/resource construction is introduced.
If loading outlasts both logos, the existing loading screen continues it; asset
failures retain the existing error display. Simulation timing begins after the
sequence, so startup time is not treated as gameplay elapsed time.

`dotnet run --project tools/AnimalTown.Tools -c Release -- splash-check .`
checks package dimensions, format, byte budget and every decoded pixel against
the composed originals within RGB565 quantization bounds. ISO art generation
also runs this check. The managed `ui` suite checks fades, minimum holds, slow-I/O
timing, completion and independent second-logo timing. Physical GameCube validation
is still required for final hardware appearance and timing.
