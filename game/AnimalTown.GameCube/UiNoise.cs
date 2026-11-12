using System;
using AnimalTown;

// One app-lifetime UI texture, also needed after title/setup views are retired.
// Native scopes flush painter order and modulate fills only; text keeps its atlas.
internal static class UiNoise
{
    private static int prepared=-1,depth;
    internal static void Prepare()
    {
        int percent=UiNoiseSettings.Percent;if(prepared==percent)return;
        if(NativePrepare(percent==0?null:LowPolyTree.DetailTexture(percent))!=0)throw new InvalidOperationException("UI noise texture preparation failed");
        prepared=percent;
    }
    internal static NoiseScope Scope()=>new NoiseScope(true);
    internal readonly struct NoiseScope : IDisposable
    {
        internal NoiseScope(bool begin){if(depth++==0&&UiNoiseSettings.Percent>0&&NativeScope(1)!=0)throw new InvalidOperationException("UI noise scope failed");}
        public void Dispose(){if(--depth==0&&UiNoiseSettings.Percent>0&&NativeScope(0)!=0)throw new InvalidOperationException("UI noise scope restoration failed");}
    }
    internal static void Shutdown(){if(NativePrepare(null)!=0)throw new InvalidOperationException("UI noise shutdown failed");prepared=-1;}
    internal static int NativePrepare(byte[]? package)=>throw new PlatformNotSupportedException("UI noise native bridge required");
    internal static int NativeScope(int enabled)=>throw new PlatformNotSupportedException("UI noise native bridge required");
}
