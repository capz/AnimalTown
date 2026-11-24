internal static class NativeFrame
{
    internal static void Reset(int enabled)=>throw new System.PlatformNotSupportedException("Native frame counters required");
    internal static void Snapshot(int[] data)=>throw new System.PlatformNotSupportedException("Native frame counters required");
    internal static void Service()=>throw new System.PlatformNotSupportedException("Native frame service required");
    internal static void Memory(int[] data)=>throw new System.PlatformNotSupportedException("Native memory snapshot required");
    internal static int TakeMeshAllocationFailure()=>throw new System.PlatformNotSupportedException("Native mesh allocation result required");
    internal static void Dump(uint[] words,int count,int[] memory,uint session,uint total)=>throw new System.PlatformNotSupportedException("Native profiling export required");
    internal static void DumpMetrics(uint[] histogram,uint[] peaks,uint[] events,int count,int dropped,uint session)=>throw new System.PlatformNotSupportedException("Native profiling export required");
    internal static int Create(Dolphin.Graphics.VertexPositionNormalColorTexture[] vertices,int count,ushort[] indices,int indexCount)=>throw new System.PlatformNotSupportedException("Native terrain mesh required");
    internal static int Recycle(int token,Dolphin.Graphics.VertexPositionNormalColorTexture[] vertices,int count,ushort[] indices,int indexCount)=>throw new System.PlatformNotSupportedException("Native terrain mesh required");
}
