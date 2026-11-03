internal static class NativeRenderProbe
{
    // Diagnostic bridge to the pinned SDK's native A/B control. AOT replaces
    // this body; it deliberately fails if that bridge has not been installed.
    internal static int Indexed(int enabled)=>throw new System.PlatformNotSupportedException("Native rendering probe requires AOT");
    internal static int Batch(int enabled)=>throw new System.PlatformNotSupportedException("Native batch probe requires AOT");
}
