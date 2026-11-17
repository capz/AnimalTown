internal static class NativeWaterProbe
{
    // Inspection-only bridge. Native capture observes the GX commands used by
    // actual draws, including archived/indexed terrain, rather than Animate.
    internal static int Enable(int scene)=>throw new System.PlatformNotSupportedException("Native water probe requires AOT");
}
