using Dolphin.Graphics;

// Run the production optional-cache lifecycle with deterministic native
// allocation outcomes. Tessellation uses the real UiShapes.Append path.
internal static class NativeFrame
{
    internal static int MeshAllocationFailure,FailureReads;
    internal static int TakeMeshAllocationFailure()
    {FailureReads++;int result=MeshAllocationFailure;MeshAllocationFailure=0;return result;}
}
internal static class UiNoise
{
    internal static int Depth;
    internal static void Begin(){Depth++;}
    internal static void End(){if(--Depth<0)throw new InvalidOperationException("Unbalanced UI noise scope");}
}
namespace Dolphin.Graphics
{
    internal readonly struct Vector2
    {
        internal readonly float X,Y;
        internal Vector2(float x,float y){X=x;Y=y;}
    }
    internal readonly struct RectF
    {
        internal readonly float X,Y,Width,Height;
        internal RectF(float x,float y,float width,float height){X=x;Y=y;Width=width;Height=height;}
    }
    internal sealed class Draw2D
    {
        internal int Draws;
        internal void DrawGeometry(Geometry2D shape,Vector2 offset,Color32 color){shape.Check();Draws++;}
    }
    internal sealed class Geometry2D : IDisposable
    {
        internal static int LiveBytes,Created,Released;
        private readonly int bytes;
        private bool disposed;
        internal Geometry2D(int bytes){this.bytes=bytes;LiveBytes+=bytes;Created++;}
        internal int NativeBytes{get{Check();return bytes;}}
        internal void Check(){if(disposed)throw new ObjectDisposedException("Geometry2D");}
        public void Dispose(){if(disposed)return;disposed=true;LiveBytes-=bytes;Released++;}
    }
    internal sealed class Geometry2DBuilder
    {
        internal static int Builds,MaximumNativeBytes=int.MaxValue;
        internal static bool ActiveFrame;
        internal static Exception? NextBuildError;
        private readonly int capacity;
        private int count;
        internal Geometry2DBuilder(int capacity){this.capacity=capacity;}
        private static void Point(Vector2 p)
        {if(!float.IsFinite(p.X)||!float.IsFinite(p.Y))throw new ArgumentException("Non-finite geometry");}
        private void Reserve(int added)
        {if(added>capacity-count)throw new InvalidOperationException("Geometry capacity exceeded");count+=added;}
        internal void AddTriangle(Vector2 a,Vector2 b,Vector2 c,Color32 color)
        {Point(a);Point(b);Point(c);Reserve(3);}
        internal void AddRectangle(RectF r,Color32 color)
        {
            Point(new Vector2(r.X,r.Y));Point(new Vector2(r.Width,r.Height));Point(new Vector2(r.X+r.Width,r.Y+r.Height));
            if(r.Width<0||r.Height<0)throw new ArgumentException("Negative rectangle size");Reserve(6);
        }
        internal Geometry2D Build()
        {
            Builds++;NativeFrame.MeshAllocationFailure=0;
            if(ActiveFrame)throw new ArgumentException("Active frame");
            if(NextBuildError!=null){var error=NextBuildError;NextBuildError=null;throw error;}
            int bytes=count*36;
            if(bytes>MaximumNativeBytes){NativeFrame.MeshAllocationFailure=1;throw new ArgumentException("Native mesh allocation unavailable");}
            return new Geometry2D(bytes);
        }
    }
}
internal static class UiShapeCacheChecks
{
    private static void Reset()
    {
        if(Geometry2D.LiveBytes!=0)throw new InvalidOperationException("Previous UI fixture leaked native geometry");
        Geometry2D.Created=Geometry2D.Released=Geometry2DBuilder.Builds=NativeFrame.FailureReads=NativeFrame.MeshAllocationFailure=0;
        Geometry2DBuilder.MaximumNativeBytes=int.MaxValue;Geometry2DBuilder.ActiveFrame=false;Geometry2DBuilder.NextBuildError=null;
        Dolphin.Diagnostics.DebugOverlay.UiCacheDeferrals=UiNoise.Depth=0;
    }
    private static bool Key(UiShapes cache,Draw2D ui,float width=51)
        =>cache.Draw(ui,43,223,width,33,11,false,Color32.White);
    internal static void Run(Action<bool,string> check)
    {
        Reset();
        using(var cache=new UiShapes()){
            var ui=new Draw2D();cache.Prepare("chat",0,true);
            check(!Key(cache,ui),"a cold shape uses the original immediate rendering path");cache.Prepare("chat",0,true);
            int first=cache.ResidentBytes;
            check(first>0&&Key(cache,ui),"a successful shape becomes resident before a later allocation failure");
            Geometry2DBuilder.MaximumNativeBytes=5000;
            Key(cache,ui,52);cache.DrawPanel(ui,20,16,600,448,30);cache.Prepare("chat",0,true);
            check(cache.ResidentBytes==first*2&&Geometry2D.LiveBytes==cache.ResidentBytes&&Key(cache,ui)&&Key(cache,ui,52),"a failed large panel preserves the existing key and still admits another small shape");
            check(!cache.DrawPanel(ui,20,16,600,448,30)&&Dolphin.Diagnostics.DebugOverlay.UiCacheDeferrals==1,"only the rejected panel falls back and emits one recovery event");
            int builds=Geometry2DBuilder.Builds,reads=NativeFrame.FailureReads;
            Geometry2DBuilder.MaximumNativeBytes=int.MaxValue;
            for(int i=0;i<256;i++){
                cache.Prepare("chat",0,true);
                check(!cache.DrawPanel(ui,20,16,600,448,30)&&Key(cache,ui),"repeated rejected draws preserve the immediate path and warm shared geometry");
            }
            check(Geometry2DBuilder.Builds==builds&&NativeFrame.FailureReads==reads&&Dolphin.Diagnostics.DebugOverlay.UiCacheDeferrals==1,"stable-view fallback performs no repeated builds, failure queries or logs");
            cache.Prepare("chat",1,true);cache.DrawPanel(ui,20,16,600,448,30);cache.Prepare("chat",1,true);
            check(cache.DrawPanel(ui,20,16,600,448,30)&&Geometry2DBuilder.Builds==builds+1,"tab invalidation permits a single successful retry when capacity is available");
            Geometry2DBuilder.MaximumNativeBytes=0;Key(cache,ui,53);cache.Prepare("chat",1,true);
            cache.Trim();check(cache.ResidentBytes==0&&Geometry2D.LiveBytes==0,"trim retires both rejected work and all resident native meshes");
            Geometry2DBuilder.MaximumNativeBytes=int.MaxValue;cache.Prepare("chat",1,true);Key(cache,ui,53);cache.Prepare("chat",1,true);
            check(Key(cache,ui,53),"trim makes a previously rejected key eligible for rebuilding");
        }
        check(Geometry2D.LiveBytes==0&&Geometry2D.Created==Geometry2D.Released&&UiNoise.Depth==0,"cache disposal releases every successful mesh and keeps UI scopes balanced");

        Reset();
        using(var cache=new UiShapes()){
            var ui=new Draw2D();cache.Prepare("chat",0,true);
            Geometry2DBuilder.MaximumNativeBytes=0;Key(cache,ui);cache.Prepare("chat",0,true);
            cache.Prepare("chat",0,false);Geometry2DBuilder.MaximumNativeBytes=int.MaxValue;
            cache.Prepare("chat",0,true);Key(cache,ui);cache.Prepare("chat",0,true);
            check(Key(cache,ui)&&Dolphin.Diagnostics.DebugOverlay.UiCacheDeferrals==1,"disable/re-enable invalidates rejected keys without a duplicate failure event");
        }

        Reset();
        using(var cache=new UiShapes()){
            var ui=new Draw2D();cache.Prepare("chat",0,true);cache.Draw(ui,0,0,float.NaN,28,14,false,Color32.White);
            NativeFrame.MeshAllocationFailure=1;bool threw=false;
            try{cache.Prepare("chat",0,true);}catch(ArgumentException){threw=true;}
            check(threw&&Geometry2DBuilder.Builds==0&&Dolphin.Diagnostics.DebugOverlay.UiCacheDeferrals==0&&NativeFrame.MeshAllocationFailure==0,"stale native failure state cannot hide invalid geometry rejected before mesh creation");
        }

        foreach(Exception failure in new Exception[]{new ArgumentException("Invalid native geometry"),new InvalidOperationException("Builder fault"),new OutOfMemoryException("Managed heap exhausted")}){
            Reset();
            using var cache=new UiShapes();var ui=new Draw2D();cache.Prepare("chat",0,true);Key(cache,ui);
            Geometry2DBuilder.NextBuildError=failure;Exception? caught=null;
            try{cache.Prepare("chat",0,true);}catch(Exception error){caught=error;}
            check(ReferenceEquals(caught,failure)&&Dolphin.Diagnostics.DebugOverlay.UiCacheDeferrals==0&&cache.ResidentBytes==0,"non-native-allocation failures preserve their original exception and never become cache misses");
        }
        Reset();
        using(var cache=new UiShapes()){
            var ui=new Draw2D();cache.Prepare("chat",0,true);Key(cache,ui);Geometry2DBuilder.ActiveFrame=true;bool threw=false;
            try{cache.Prepare("chat",0,true);}catch(ArgumentException){threw=true;}
            check(threw&&Dolphin.Diagnostics.DebugOverlay.UiCacheDeferrals==0,"active-frame misuse remains a hard failure");
        }
        Reset();
    }
}
