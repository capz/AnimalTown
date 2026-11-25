using System.Threading.Tasks;
namespace Dolphin.Timing { internal static class Clock { internal static long Timestamp; } }
namespace Dolphin.Diagnostics { internal static class DebugOverlay { internal static readonly List<string> Lines=new();internal static void WriteLine(string text){Lines.Add(text);} } }
namespace Dolphin.Graphics
{
    internal readonly struct Color32
    {
        internal readonly byte R,G,B,A;
        internal Color32(byte r,byte g,byte b,byte a=255){R=r;G=g;B=b;A=a;}
    }
    internal readonly struct Vector3 { public static Vector3 Zero=>default;public Vector3(float x,float y,float z){} }
    internal readonly struct Quaternion { public Quaternion(float x,float y,float z,float w){} }
    internal sealed class Transform { public Transform? Parent;public Vector3 LocalPosition,LocalScale;public Quaternion LocalRotation; }
    internal sealed class Renderer { public void DrawModel(ModelInstance instance,Transform pose){if(instance.Disposed)throw new Exception("Disposed instance drawn");} }
    internal sealed class GraphicsDevice { internal static readonly GraphicsDevice Default=new GraphicsDevice();internal bool IsFrameActive=false; }
    internal sealed class Model : IDisposable
    {
        internal static int Live,Loads,Bytes,InstanceCostUs;
        internal static bool FailNext,FailNativeNext,DeferNext,FailInstanceNext;
        internal static TaskCompletionSource<Model>? Deferred;
        private bool disposed;
        internal int NativeBytes=>8192;
        internal int PackageBytes=>160;
        private Model(){Live++;Bytes+=NativeBytes;}
        internal static Task<Model> LoadAsync(string path,CancellationToken cancellation=default,int budget=0)
        {
            Loads++;
            if(FailNext){FailNext=false;return Task.FromException<Model>(new OutOfMemoryException());}
            if(FailNativeNext){FailNativeNext=false;NativeMemory.Failures++;return Task.FromException<Model>(new IOException("Invalid model package or insufficient memory"));}
            if(DeferNext){DeferNext=false;Deferred=new TaskCompletionSource<Model>();return Deferred.Task;}
            return Task.FromResult(new Model());
        }
        internal static void CompleteDeferred(){Deferred!.SetResult(new Model());Deferred=null;}
        internal ModelInstance CreateInstance(int budget){Dolphin.Timing.Clock.Timestamp+=InstanceCostUs;if(FailInstanceNext){FailInstanceNext=false;throw new OutOfMemoryException();}return new ModelInstance();}
        public void Dispose(){if(disposed)throw new Exception("Model double disposal");disposed=true;Live--;Bytes-=NativeBytes;}
    }
    internal sealed class ModelInstance : IDisposable
    {
        internal static int Live;internal bool Disposed;
        internal int NativeBytes=>1024;
        internal ModelInstance(){Live++;Model.Bytes+=NativeBytes;}
        public void Dispose(){if(Disposed)throw new Exception("Instance double disposal");Disposed=true;Live--;Model.Bytes-=NativeBytes;}
    }
}
internal static class NativeBuildings { internal static int Retain(Dolphin.Graphics.Model model)=>1; }
internal static class NativeShadows
{
    internal static void Scope(int scope){}
    internal static int LightCalls,LayoutUploads,PointCalls,PointCount,SourceCount,Gain;
    internal static bool FailLightsNext,FailPointsNext;
    internal static readonly float[] Sources=new float[24*4],PointsData=new float[24*5];
    internal static void ResetLighting()
    {
        LightCalls=LayoutUploads=PointCalls=PointCount=SourceCount=Gain=0;FailLightsNext=FailPointsNext=false;
        Array.Clear(Sources);Array.Clear(PointsData);
    }
    internal static int Lights(float[]? sources,int count,int strength)
    {
        LightCalls++;if(FailLightsNext){FailLightsNext=false;return 0;}
        Gain=strength;
        if(sources!=null){LayoutUploads++;SourceCount=count;Array.Copy(sources,Sources,count*4);}
        return 1;
    }
    internal static int Points(float[] sources,int count)
    {
        PointCalls++;if(FailPointsNext){FailPointsNext=false;return 0;}
        PointCount=count;Array.Copy(sources,PointsData,count*5);return 1;
    }
}
internal static class NativeMemory
{
    internal static int FreeBytes=10*1024*1024,Failures,ReadCostUs;
    internal static int Read(int[] snapshot){Dolphin.Timing.Clock.Timestamp+=ReadCostUs;snapshot[0]=FreeBytes-Dolphin.Graphics.Model.Bytes;snapshot[3]=Failures;return 1;}
}
internal static class TextureCache { internal static int Unused,TrimCostUs;internal static bool TrimOneUnused(){Dolphin.Timing.Clock.Timestamp+=TrimCostUs;if(Unused<1)return false;Unused--;return true;} }
internal sealed class LowPolyTrees { internal bool Eligible;internal bool ReleaseUnused(bool outdoors,long now,bool pressure){if(!Eligible||outdoors)return false;Eligible=false;return true;} }
internal sealed class DroppedItemRenderer { internal bool Eligible;internal bool ReleaseUnused(bool outdoors,long now,bool pressure){if(!Eligible||outdoors)return false;Eligible=false;return true;} }
