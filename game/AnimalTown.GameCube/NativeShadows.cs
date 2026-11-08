// Native coverage cache + two TEV stages on existing ground draws. All storage
// is charged to the SDK's fixed native budget; no framebuffer copies or overlays.
internal static class NativeShadows
{
    internal static int Init(byte[] atlas,byte[] circle)=>throw new System.PlatformNotSupportedException("Native shadow union bridge required");
    internal static int Begin(int space,int enabled,int size)=>throw new System.PlatformNotSupportedException("Native shadow union bridge required");
    internal static int Mesh(int slot,int count,int profile,float[] uv,float[] bounds)=>throw new System.PlatformNotSupportedException("Native shadow union bridge required");
    internal static int Active(int slot,int active,float[] center)=>throw new System.PlatformNotSupportedException("Native shadow union bridge required");
    internal static int Prepare(int microseconds)=>throw new System.PlatformNotSupportedException("Native shadow union bridge required");
    internal static int Scope(int mode)=>throw new System.PlatformNotSupportedException("Native shadow union bridge required");
    internal static int Stats(int kind)=>throw new System.PlatformNotSupportedException("Native shadow union bridge required");
}
