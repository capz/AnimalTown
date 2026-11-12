using AnimalTown;
using Dolphin.Timing;

// Execute the production archive lifecycle with deterministic DMA completion.
// Geometry selection still uses the real domain camera/section bounds.
namespace Dolphin.Timing
{
    public static class Clock {public static long Timestamp {get;set;}}
}
namespace Dolphin.Diagnostics
{
    public static class DebugOverlay
    {
        public static int TerrainSuspensions,TerrainRestorations;
        public static string LastTerrainSuspension="",LastTerrainRestoration="";
        public static void WriteLine(string message)
        {
            if(message.StartsWith("ANIMALTOWN TERRAIN CACHE SUSPENDED",StringComparison.Ordinal)){TerrainSuspensions++;LastTerrainSuspension=message;}
            else if(message.StartsWith("ANIMALTOWN TERRAIN CACHE READY",StringComparison.Ordinal)){TerrainRestorations++;LastTerrainRestoration=message;}
        }
    }
}
namespace Dolphin.Graphics
{
    public class Mesh
    {
        private readonly int token;
        protected Mesh(int token){this.token=token;}
        public void Dispose()=>NativeTerrain.ReleaseView(token);
    }
}
namespace AnimalTown.GameCube
{
    internal static class NativeTexturePages
    {
        internal static int Collections;
        internal static void Collect(){Collections++;}
    }
}
internal static class FrameProfile {internal static int RecycledSections;}
internal sealed class TerrainRenderer
{
    internal readonly CylinderRows Rows=new CylinderRows();
    internal TerrainSection CreateSection()=>new TerrainSection();
}
internal static class WaterRenderer {internal const int BedKind=108,PartKinds=109;}
internal sealed class TerrainSection
{
    internal void Begin(GameState state,int x,int z,CylinderRows rows){}
    internal bool Advance(GameState state,TerrainRenderer renderer,FloorStrips staging,byte[] data)=>true;
    internal void Archive(int section,bool final){}
    internal void Dispose(){}
}
internal static class NativeTerrain
{
    internal static int CacheBytes,AramBytes,CreatedViews,ReleasedViews,BindCalls,StepCalls,ResumeCalls;
    internal static bool FailResume,FailStep,BedOnly;
    private static readonly int[] entries=new int[1024];
    private static int pending,entry,remaining,cancelRemaining;
    internal static int Reset(int sections)
    {
        if(pending!=0)throw new InvalidOperationException("Reset lost an active DMA hold.");
        CacheBytes=sections==0?0:1048576;AramBytes=sections*96;
        return 1;
    }
    internal static int View(){int token=++CreatedViews;entries[token]=-1;return token;}
    internal static void ReleaseView(int token)
    {
        if(pending==token)throw new InvalidOperationException("Disposed a view before DMA drained.");
        entries[token]=-1;ReleasedViews++;
    }
    internal static int Unbind(int token){if(pending==token)return -1;entries[token]=-1;return 1;}
    internal static int Bind(int token,int value){if(CacheBytes==0)throw new InvalidOperationException("Bound a cold cache.");BindCalls++;entries[token]=value;return 1;}
    internal static int BindStep(int token,int value)
    {
        StepCalls++;
        if(FailStep){FailStep=false;pending=0;return -3;}
        if(pending==0){pending=token;entry=value;remaining=cancelRemaining=2;return 0;}
        if(pending!=token||entry!=value)throw new InvalidOperationException("Changed a pending destination without draining.");
        if(--remaining>0)return 0;pending=0;return Bind(token,value);
    }
    internal static int CancelBind(){if(pending==0)return 1;if(--cancelRemaining>0)return 0;pending=0;return 1;}
    internal static int Suspend()
    {
        if(pending!=0)throw new InvalidOperationException("Freed the cache before DMA drained.");
        for(int i=1;i<=CreatedViews;i++)if(entries[i]>=0)throw new InvalidOperationException("Freed a cache with a published alias.");
        CacheBytes=0;return 1;
    }
    internal static int Resume(){ResumeCalls++;if(FailResume)return -2;CacheBytes=1048576;return 1;}
    internal static int Cache(int enabled)=>enabled==1?Resume():Suspend();
    internal static int Info(int section,int kind)=>kind==0?section*2:kind==1?2:kind==2?4:BedOnly?4:2;
    internal static int Kind(int value)=>BedOnly||value%2==1?WaterRenderer.BedKind:(value/2)%4;
    internal static int Stat(int kind)=>kind==0?CacheBytes+9216:kind==5?AramBytes:kind==8?CacheBytes:0;
}
internal static class TerrainResidencyChecks
{
    private static bool Finish(TerrainArchive archive,float x,float z,CylinderRows rows,bool drawBeds=true)
    {
        for(int i=0;i<4096;i++){
            int calls=NativeTerrain.StepCalls;
            bool complete=archive.PrepareAsync(x,z,rows,Clock.Timestamp+1000,drawBeds);
            if(NativeTerrain.StepCalls-calls>8)throw new InvalidOperationException("Unbounded terrain publication in one step.");
            if(complete)return true;
        }
        return false;
    }
    internal static void Run(Action<bool,string> check)
    {
        Clock.Timestamp=0;var archive=new TerrainArchive();var owner=new TerrainRenderer();
        var state=new GameState(91);var strips=new FloorStrips(64);var data=new byte[0];
        archive.Reset();while(!archive.Ready)archive.Bake(state,owner,strips,data);
        check(archive.Ready&&!archive.Presentable,"baking and drawable selection have distinct readiness");
        archive.Prepare(30,50,owner.Rows);
        check(archive.Presentable&&archive.ViewCount>0,"warm preparation publishes the original complete selection");
        int allViews=archive.ViewCount,allTriangles=archive.Triangles;
        check(archive.First(WaterRenderer.BedKind)>=0,"sunshine selection includes the separately archived water bed");
        archive.Prepare(30,50,owner.Rows,false);
        check(archive.Presentable&&archive.ViewCount*2==allViews&&archive.Triangles*2==allTriangles&&archive.First(WaterRenderer.BedKind)==-1,
            "ordinary water excludes bed aliases and bed triangle accounting");
        archive.Prepare(30,50,owner.Rows,true);
        check(archive.ViewCount==allViews&&archive.Triangles==allTriangles,"switching sunshine back restores the original bed selection");
        var identity=archive.Mesh(0);int views=NativeTerrain.CreatedViews,aram=NativeTerrain.AramBytes,collections=AnimalTown.GameCube.NativeTexturePages.Collections;
        check(views==256&&archive.Suspend()&&archive.CacheSuspended&&!archive.Presentable,"suspension drops only the fixed cache and invalidates drawable state");
        check(NativeTerrain.CacheBytes==0&&NativeTerrain.AramBytes==aram&&archive.Ready&&archive.Suspend(),"suspension preserves the archive and is idempotent");
        check(Dolphin.Diagnostics.DebugOverlay.TerrainSuspensions==1&&Dolphin.Diagnostics.DebugOverlay.LastTerrainSuspension==
            "ANIMALTOWN TERRAIN CACHE SUSPENDED cache bytes 0 reclaimed bytes 1048576 archive bytes "+aram,"suspension logs the actual payload delta only once");
        int resumes=NativeTerrain.ResumeCalls;
        check(!archive.PrepareAsync(30,50,owner.Rows,Clock.Timestamp)&&NativeTerrain.ResumeCalls==resumes,"an expired work budget does not allocate a cache");
        NativeTerrain.FailResume=true;
        check(!archive.PrepareAsync(30,50,owner.Rows,1000)&&archive.ResidencyError==-2&&archive.CacheSuspended,"failed allocation leaves a retryable cold archive");
        resumes=NativeTerrain.ResumeCalls;NativeTerrain.FailResume=false;
        check(!archive.PrepareAsync(30,50,owner.Rows,1000)&&NativeTerrain.ResumeCalls==resumes,"allocation failure backs off without a per-frame retry storm");
        Clock.Timestamp+=250001;
        int wakes=Dolphin.Memory.Aram.WakeCalls;
        check(!archive.PrepareAsync(30,50,owner.Rows,Clock.Timestamp+1000)&&!archive.Presentable,"a submitted DMA does not expose partial terrain");
        check(Dolphin.Memory.Aram.WakeCalls>wakes,"pending terrain DMA wakes the shared background broker");
        check(Finish(archive,30,50,owner.Rows)&&archive.Presentable&&NativeTerrain.AramBytes==aram,"resumption publishes only the completed destination");
        check(Dolphin.Diagnostics.DebugOverlay.TerrainRestorations==1&&Dolphin.Diagnostics.DebugOverlay.LastTerrainRestoration==
            "ANIMALTOWN TERRAIN CACHE READY cache bytes 1048576 archive bytes "+aram,"readiness logs only a completed restored selection");
        check(ReferenceEquals(identity,archive.Mesh(0))&&NativeTerrain.CreatedViews==views,"resumption retains every mesh view identity");
        check(AnimalTown.GameCube.NativeTexturePages.Collections==collections,"suspension and resumption introduce no collections");
        int suspensions=Dolphin.Diagnostics.DebugOverlay.TerrainSuspensions,restorations=Dolphin.Diagnostics.DebugOverlay.TerrainRestorations;
        check(archive.SuspendForSave()&&archive.CacheSuspended&&NativeTerrain.AramBytes==aram,"explicit saving releases a warm cache while preserving backing");
        archive.ResumeAfterSave();archive.Prepare(30,50,owner.Rows,true);
        check(archive.Presentable&&!archive.CacheSuspended&&archive.ViewCount==allViews,"save cleanup restores normal warm preparation and water beds");
        check(Dolphin.Diagnostics.DebugOverlay.TerrainSuspensions==suspensions&&Dolphin.Diagnostics.DebugOverlay.TerrainRestorations==restorations,
            "save-only pressure relief does not masquerade as the debug terrain transition");
        archive.Suspend();archive.PrepareAsync(30,50,owner.Rows,Clock.Timestamp+1000,true);
        check(!archive.PrepareAsync(30,50,owner.Rows,Clock.Timestamp+1000,false),"changing bed style drains its partial prefetch first");
        check(Finish(archive,30,50,owner.Rows,false)&&archive.ViewCount*2==allViews&&archive.First(WaterRenderer.BedKind)==-1,
            "a style change during restoration removes all partially bound bed aliases");
        archive.Suspend();archive.PrepareAsync(30,50,owner.Rows,Clock.Timestamp+1000,true);
        suspensions=Dolphin.Diagnostics.DebugOverlay.TerrainSuspensions;
        check(archive.SuspendForSave()&&archive.CacheSuspended,"explicit saving drains a partially restored cache before card I/O");
        archive.ResumeAfterSave();archive.Prepare(30,50,owner.Rows,true);
        check(archive.Presentable&&archive.ViewCount==allViews&&Dolphin.Diagnostics.DebugOverlay.TerrainSuspensions==suspensions,
            "save interruption restores complete bed geometry without another debug suspension");
        archive.Suspend();
        check(!archive.SuspendForSave(),"saving an already debug-cold cache does not claim ownership of resumption");
        archive.ResumeAfterSave();check(archive.CacheSuspended&&NativeTerrain.CacheBytes==0,"save cleanup does not resurrect a debug-cold cache");
        check(!archive.PrepareAsync(31,51,owner.Rows,Clock.Timestamp+1000),"cold selection starts pending");
        check(!archive.Suspend(),"rapid reversal drains its in-flight DMA before freeing the cache");
        check(archive.Suspend()&&archive.CacheSuspended,"reversal frees the cache after drainage");
        check(!archive.PrepareAsync(31,51,owner.Rows,Clock.Timestamp+1000),"resume can restart after cancellation");
        NativeTerrain.FailStep=true;
        check(!archive.PrepareAsync(31,51,owner.Rows,Clock.Timestamp+1000)&&archive.ResidencyError==-3&&!archive.Presentable,"transfer failure leaves the destination unpublished");
        Clock.Timestamp+=250001;
        check(Finish(archive,31,51,owner.Rows),"transfer failure can recover on a later bounded attempt");
        archive.Prepare(31,51,owner.Rows);long allocated=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<10000;i++)archive.Prepare(31,51,owner.Rows);
        check(GC.GetAllocatedBytesForCurrentThread()==allocated,"warm unchanged terrain preparation remains allocation-free");
        archive.Suspend();archive.PrepareAsync(30,50,owner.Rows,Clock.Timestamp+1000);
        check(!archive.TryDispose()&&NativeTerrain.ReleasedViews==0,"reset/disposal waits for the active cache owner");
        check(archive.TryDispose()&&NativeTerrain.ReleasedViews==views&&NativeTerrain.AramBytes==0,"disposal releases the archive and original view set after drainage");
        NativeTerrain.BedOnly=true;archive.Reset();while(!archive.Ready)archive.Bake(state,owner,strips,data);
        archive.Prepare(30,50,owner.Rows,false);
        check(archive.Presentable&&archive.ViewCount==0&&archive.Triangles==0,"a completely filtered section has no drawable aliases");
        archive.Prepare(30,50,owner.Rows,true);
        check(archive.Presentable&&archive.ViewCount==allViews&&archive.Triangles==allTriangles,
            "water style changes invalidate filtered sections even when they had no aliases");
        archive.Dispose();NativeTerrain.BedOnly=false;
    }
}
