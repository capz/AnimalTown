// One game-owned ARAM scheduler. Transfers complete between frames, before
// terrain's own DMA binds. No SDK async transfer may overlap this path.
internal static class NativeAssetResidency
{
    internal static int Capture(int slot,int expectedBytes,int holdFrames,string path)=>throw new System.PlatformNotSupportedException("Native residency bridge required");
    internal static int Advance(int[] priorities,int startup)=>throw new System.PlatformNotSupportedException("Native residency bridge required");
    internal static int State(int slot)=>throw new System.PlatformNotSupportedException("Native residency bridge required");
    internal static int Stats(int kind)=>throw new System.PlatformNotSupportedException("Native residency bridge required");
    internal static int Forget(int slot)=>throw new System.PlatformNotSupportedException("Native residency bridge required");
}
