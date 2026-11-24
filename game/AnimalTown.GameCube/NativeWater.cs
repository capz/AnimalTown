// Game-owned extension of the pinned GX backend. Prepare/Initialize/Shutdown
// are between-frame lifetime calls; Begin/Scope/Capture run inside the 3D pass.
// The native patch replaces these AOT stubs and charges every texture to the
// existing graphics budget. No resource is allocated from a draw hook.
internal static class NativeWater
{
    internal static int Initialize(byte[] slopes)=>throw new System.PlatformNotSupportedException("Native water optics bridge required");
    internal static int Prepare(float[] settings,int active,int sunlight,int environmentRgb)=>throw new System.PlatformNotSupportedException("Native water optics bridge required");
    internal static int Begin()=>throw new System.PlatformNotSupportedException("Native water optics bridge required");
    internal static int Scope(int mode)=>throw new System.PlatformNotSupportedException("Native water optics bridge required");
    internal static int Capture()=>throw new System.PlatformNotSupportedException("Native water optics bridge required");
    internal static int Shutdown()=>throw new System.PlatformNotSupportedException("Native water optics bridge required");
    internal static int Stats(int kind)=>throw new System.PlatformNotSupportedException("Native water optics bridge required");
}
