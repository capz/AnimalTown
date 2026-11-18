// Native coverage cache + two TEV stages on existing ground draws. All storage
// is charged to the SDK's fixed native budget; no framebuffer copies or overlays.
internal static class NativeShadows
{
    internal static int Init(byte[] atlas,byte[] circle,byte[] clouds)=>throw new System.PlatformNotSupportedException("Native shadow union bridge required");
    internal static int Cloud(float[] offset)=>throw new System.PlatformNotSupportedException("Native cloud shadow bridge required");
    internal static int Lights(float[] sources,int count,int strength)=>throw new System.PlatformNotSupportedException("Native light map bridge required");
    internal static int Points(float[] sources,int count)=>throw new System.PlatformNotSupportedException("Native model light bridge required");
    internal static int Objects(int scope)=>throw new System.PlatformNotSupportedException("Native model light scope required");
    internal static int Environment(int rgb,int shadowStrength)=>throw new System.PlatformNotSupportedException("Native environment tint bridge required");
    internal static int Shutdown()=>throw new System.PlatformNotSupportedException("Native shadow union bridge required");
    internal static int Terrain(byte[] tile,int offset,int mode)=>throw new System.PlatformNotSupportedException("Native cliff shadow bridge required");
    internal static int Begin(int space,int enabled,int size)=>throw new System.PlatformNotSupportedException("Native shadow union bridge required");
    internal static int Mesh(int slot,int count,int profile,float[] uv,float[] bounds)=>throw new System.PlatformNotSupportedException("Native shadow union bridge required");
    internal static int Active(int slot,int active,float[] center)=>throw new System.PlatformNotSupportedException("Native shadow union bridge required");
    internal static int Prepare(int microseconds)=>throw new System.PlatformNotSupportedException("Native shadow union bridge required");
    internal static int Scope(int mode)=>throw new System.PlatformNotSupportedException("Native shadow union bridge required");
    internal static int Stats(int kind)=>throw new System.PlatformNotSupportedException("Native shadow union bridge required");
}
