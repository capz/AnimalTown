internal static class NativeFrame
{
    internal static void Reset(int enabled)=>throw new System.PlatformNotSupportedException("Native frame counters required");
    internal static void Snapshot(int[] data)=>throw new System.PlatformNotSupportedException("Native frame counters required");
    internal static void Service()=>throw new System.PlatformNotSupportedException("Native frame service required");
    internal static int Create(Dolphin.Graphics.VertexPositionNormalColorTexture[] vertices,int count,ushort[] indices,int indexCount)=>throw new System.PlatformNotSupportedException("Native terrain mesh required");
    internal static int Recycle(int token,Dolphin.Graphics.VertexPositionNormalColorTexture[] vertices,int count,ushort[] indices,int indexCount)=>throw new System.PlatformNotSupportedException("Native terrain mesh required");
}
