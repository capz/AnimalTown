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
        public static int UiCacheDeferrals;
        public static string LastTerrainSuspension="",LastTerrainRestoration="";
        public static void WriteLine(string message)
        {
            if(message.StartsWith("ANIMALTOWN TERRAIN CACHE SUSPENDED",StringComparison.Ordinal)){TerrainSuspensions++;LastTerrainSuspension=message;}
            else if(message.StartsWith("ANIMALTOWN TERRAIN CACHE READY",StringComparison.Ordinal)){TerrainRestorations++;LastTerrainRestoration=message;}
            else if(message.StartsWith("ANIMALTOWN UI CACHE DEFERRED",StringComparison.Ordinal))UiCacheDeferrals++;
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
internal sealed partial class TerrainRenderer
{
    internal bool Ready=true;
    private TerrainArchive? archive;
    internal int BuiltThisFrame {get;private set;}
    internal readonly CylinderRows Rows=new CylinderRows();
    internal TerrainSection CreateSection()=>new TerrainSection();
    internal int SeasonAt(int month,int preview)=>TerrainTiles.Season(month,preview);
    internal void PumpBake(TerrainArchive source,GameState state,FloorStrips staging,byte[] data)
    {archive=source;BuiltThisFrame=0;BakeArchive(state,staging,data);}
}
internal static class WaterRenderer {internal const int PartKinds=110;}
internal sealed class TerrainSection
{
    internal void Begin(GameState state,int x,int z,CylinderRows rows){}
    internal bool Advance(GameState state,TerrainRenderer renderer,FloorStrips staging,byte[] data)=>true;
    internal void Archive(int section,bool final){}
    internal void Dispose(){}
}
internal static class NativeTerrain
{
    internal static int CacheBytes,AramBytes,CreatedViews,ReleasedViews,BindCalls,StepCalls,ResumeCalls,ResetCalls;
    internal static int PollsPerTransfer=2;
    internal static bool FailResume,FailStep,Empty,PauseTransfers,FailReset,ThrowResume;
    private static readonly int[] entries=new int[2048];
    private static int pending,entry,remaining,cancelRemaining;
    internal static int Reset(int sections)
    {
        ResetCalls++;
        if(pending!=0)throw new InvalidOperationException("Reset lost an active DMA hold.");
        if(sections!=0&&FailReset)return -2;
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
        if(PauseTransfers)return 0;
        if(FailStep){FailStep=false;pending=0;return -3;}
        if(pending==0){pending=token;entry=value;remaining=PollsPerTransfer;cancelRemaining=2;return 0;}
        if(pending!=token||entry!=value)throw new InvalidOperationException("Changed a pending destination without draining.");
        if(--remaining>0)return 0;pending=0;entries[token]=value;return 1;
    }
    internal static int CancelBind(){if(pending==0)return 1;if(--cancelRemaining>0)return 0;pending=0;return 1;}
    internal static int Suspend()
    {
        if(pending!=0)throw new InvalidOperationException("Freed the cache before DMA drained.");
        for(int i=1;i<=CreatedViews;i++)if(entries[i]>=0)throw new InvalidOperationException("Freed a cache with a published alias.");
        CacheBytes=0;return 1;
    }
    internal static int Resume(){ResumeCalls++;if(ThrowResume)throw new InvalidOperationException("Injected resume exception");if(FailResume)return -2;CacheBytes=1048576;return 1;}
    internal static int Cache(int enabled)=>enabled==1?Resume():Suspend();
    internal static int Info(int section,int kind)=>kind==0?section*2:kind==1?(Empty?0:2):kind==2?(Empty?0:4):-1;
    internal static int Kind(int value)=>value%2==1?104:(value/2)%4;
    internal static int Stat(int kind)=>kind==0?CacheBytes+9216:kind==5?AramBytes:kind==8?CacheBytes:0;
}
internal static class TerrainResidencyChecks
{
    private static bool Finish(TerrainArchive archive,float x,float z,CylinderRows rows)
    {
        for(int i=0;i<4096;i++){
            int calls=NativeTerrain.StepCalls;
            bool complete=archive.PrepareAsync(x,z,rows,Clock.Timestamp+1000);
            if(NativeTerrain.StepCalls-calls>8)throw new InvalidOperationException("Unbounded terrain publication in one step.");
            if(complete)return true;
        }
        return false;
    }
    internal static void Run(Action<bool,string> check)
    {
        PrefetchEnvelope(check);
        Clock.Timestamp=0;var archive=new TerrainArchive();var owner=new TerrainRenderer();
        var state=new GameState(91);var strips=new FloorStrips(64);var data=new byte[0];
        archive.Reset();while(!archive.Ready)archive.Bake(state,owner,strips,data);
        check(archive.Ready&&!archive.Presentable,"baking and drawable selection have distinct readiness");
        archive.Prepare(30,50,owner.Rows);
        check(!archive.Presentable&&NativeTerrain.BindCalls==0,"initial terrain uses bounded transfers without synchronous binds");
        check(Finish(archive,30,50,owner.Rows)&&archive.ViewCount>0,"bounded preparation publishes the original complete selection");
        int allViews=archive.ViewCount,allTriangles=archive.Triangles;
        check(archive.First(104)>=0,"ordinary water is included in terrain selection");
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
        archive.ResumeAfterSave();Finish(archive,30,50,owner.Rows);
        check(archive.Presentable&&!archive.CacheSuspended&&archive.ViewCount==allViews,"save cleanup restores normal warm preparation and ordinary water");
        check(Dolphin.Diagnostics.DebugOverlay.TerrainSuspensions==suspensions&&Dolphin.Diagnostics.DebugOverlay.TerrainRestorations==restorations,
            "save-only pressure relief does not masquerade as the debug terrain transition");
        archive.Suspend();archive.PrepareAsync(30,50,owner.Rows,Clock.Timestamp+1000);
        check(!archive.PrepareAsync(38,58,owner.Rows,Clock.Timestamp+1000),"changing camera completes its owned transfer before preparing the new selection");
        check(Finish(archive,38,58,owner.Rows)&&archive.ViewCount>0,
            "a camera change during restoration publishes only the new selection");
        archive.Suspend();archive.PrepareAsync(30,50,owner.Rows,Clock.Timestamp+1000);
        suspensions=Dolphin.Diagnostics.DebugOverlay.TerrainSuspensions;
        check(archive.SuspendForSave()&&archive.CacheSuspended,"explicit saving drains a partially restored cache before card I/O");
        archive.ResumeAfterSave();Finish(archive,30,50,owner.Rows);
        check(archive.Presentable&&archive.ViewCount==allViews&&Dolphin.Diagnostics.DebugOverlay.TerrainSuspensions==suspensions,
            "save interruption restores complete geometry without another debug suspension");
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
        Travel(check,archive,owner.Rows);
        archive.Prepare(31,51,owner.Rows);long allocated=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<10000;i++)archive.Prepare(31,51,owner.Rows);
        check(GC.GetAllocatedBytesForCurrentThread()==allocated,"warm unchanged terrain preparation remains allocation-free");
        archive.Suspend();archive.PrepareAsync(30,50,owner.Rows,Clock.Timestamp+1000);
        check(!archive.TryDispose()&&NativeTerrain.ReleasedViews==0,"reset/disposal waits for the active cache owner");
        check(archive.TryDispose()&&NativeTerrain.ReleasedViews==views&&NativeTerrain.AramBytes==0,"disposal releases the archive and original view set after drainage");
        archive.Reset();while(!archive.Ready)archive.Bake(state,owner,strips,data);
        archive.Prepare(8,8,owner.Rows);archive.WarmPrefetch=true;
        for(int i=0;i<256;i++)archive.PrepareAsync(8,8,owner.Rows,Clock.Timestamp+1000);
        int sync=NativeTerrain.BindCalls,handles=NativeTerrain.CreatedViews,late=archive.WarmLateSelections;
        bool routeReady=true,bounded=true;float px=8,pz=8;
        for(int row=0;row<15;row++){
            float targetZ=8+row*7;
            while(pz<targetZ){pz=Math.Min(targetZ,pz+.125f);Tick();}
            float targetX=(row&1)==0?Town.Width-8:8;
            while(px!=targetX){px+=Math.Sign(targetX-px)*Math.Min(.125f,Math.Abs(targetX-px));Tick();}
        }
        void Tick(){
            int calls=NativeTerrain.StepCalls;archive.PrepareAsync(px,pz,owner.Rows,Clock.Timestamp+500);
            archive.Prepare(px,pz,owner.Rows);routeReady&=archive.Presentable;
            archive.PrepareAsync(px,pz,owner.Rows,Clock.Timestamp+500);bounded&=NativeTerrain.StepCalls-calls<=24;
        }
        check(routeReady&&archive.WarmLateSelections==late,"prefetched full-town walking route has no missing/loading selections");
        check(NativeTerrain.BindCalls==sync&&NativeTerrain.CreatedViews==handles&&bounded,"warm movement never enters synchronous binding or grows the view pool and respects the two residency grants and bounded draw preparation");
        var expected=new TerrainArchiveView();expected.Prepare(px,pz,owner.Rows);
        check(archive.ViewCount==expected.Count*2&&archive.Triangles==expected.Count*4,"prefetched geometry is excluded from drawable counts and preserves the camera selection");
        long warmAllocated=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<1000;i++){archive.Prepare(px,pz,owner.Rows);archive.PrepareAsync(px,pz,owner.Rows,Clock.Timestamp+500);}
        check(GC.GetAllocatedBytesForCurrentThread()==warmAllocated,"stable prefetch pumping is allocation-free");
        archive.PrepareAsync(30,30,owner.Rows,Clock.Timestamp+500);archive.WarmPrefetch=false;
        check(archive.WarmPrefetch&&archive.WarmActive&&Finish(archive,px,pz,owner.Rows)&&archive.Presentable,
            "retired prefetch debug values cannot disable the always-asynchronous terrain path");
        archive.Dispose();
        NativeTerrain.Empty=true;archive.Reset();while(!archive.Ready)archive.Bake(state,owner,strips,data);
        Finish(archive,30,50,owner.Rows);
        check(archive.Presentable&&archive.ViewCount==0&&archive.Triangles==0,"empty sections become drawable without aliases");
        archive.Dispose();NativeTerrain.Empty=false;
        MemoryAdmission(check,state,owner,strips,data);
    }
    private static void MemoryAdmission(Action<bool,string> check,GameState state,TerrainRenderer owner,FloorStrips strips,byte[] data)
    {
        var memory=new VisualMemoryPressure();var archive=new TerrainArchive{Memory=memory};
        Clock.Timestamp+=2000000;int free=TerrainArchive.ResetPeakBytes+VisualMemoryPressure.SafetyBytes+512*1024;
        memory.Observe(free,0,0,Clock.Timestamp);
        check(memory.TryReserve(700*1024,Clock.Timestamp,true,out int other),"concurrent model/building owner reserves its future allocation");
        int resets=NativeTerrain.ResetCalls;archive.Reset();
        check(NativeTerrain.ResetCalls==resets&&NativeTerrain.CacheBytes==0&&memory.ReservedBytes==700*1024,
            "terrain startup cannot spend another owner's in-flight reservation");
        check(memory.UnderPressure&&memory.WaitingPeakBytes==TerrainArchive.ResetPeakBytes,
            "denied essential terrain startup requests global reclamation without inventing an allocator failure");
        owner.PumpBake(archive,state,strips,data);
        check(owner.BuiltThisFrame==0&&NativeTerrain.ResetCalls==resets,
            "production bake loop stops on denied admission without counting 256 nonexistent build stages");
        check(owner.WarmTerrainPrefetch==archive.WarmPrefetch&&owner.WarmTerrainPrefetch,
            "renderer diagnostics match the archive's always-asynchronous prefetch policy");
        memory.Cancel(other);owner.PumpBake(archive,state,strips,data);
        check(owner.BuiltThisFrame==256&&!archive.Ready&&NativeTerrain.ResetCalls==resets+1,
            "reclaimed headroom resumes useful baking under the existing 256-stage frame limit");
        check(NativeTerrain.ResetCalls==resets+1&&memory.ReservedBytes==TerrainArchive.ResetPeakBytes,
            "startup completion keeps cache/staging charged until a fresh physical observation");
        check(!memory.TryReserve(900*1024,Clock.Timestamp,true,out _),"a stale pre-allocation snapshot cannot fund another large owner");
        memory.Observe(free-TerrainArchive.ResetPeakBytes,0,0,Clock.Timestamp);
        check(memory.ReservedBytes==0,"physical observation retires the completed startup ticket exactly once");
        archive.Dispose();memory.Observe(4*1024*1024,0,0,Clock.Timestamp);
        NativeTerrain.FailReset=true;archive.Reset();
        check(memory.ReservedBytes==0&&archive.ResidencyError==-2&&memory.UnderPressure,
            "failed native terrain startup cancels its admission charge and requests pressure recovery");
        resets=NativeTerrain.ResetCalls;archive.Bake(state,owner,strips,data);
        check(NativeTerrain.ResetCalls==resets,"failed startup backs off instead of retrying its native allocation each bake stage");
        NativeTerrain.FailReset=false;Clock.Timestamp+=VisualMemoryPressure.FailureBackoffUs+1;
        memory.Observe(4*1024*1024,0,0,Clock.Timestamp);
        while(!archive.Ready)archive.Bake(state,owner,strips,data);
        memory.Observe(3*1024*1024,0,0,Clock.Timestamp);
        // Main's native reset retains the existing contiguous cache and borrows
        // staging from it. A rebuild must not require a second cache's headroom.
        memory.Observe(VisualMemoryPressure.SafetyBytes+TerrainArchive.AllocationGuardBytes+4096,0,0,Clock.Timestamp);
        resets=NativeTerrain.ResetCalls;
        check(archive.TryReset()&&NativeTerrain.ResetCalls==resets+1&&memory.ReservedBytes==TerrainArchive.AllocationGuardBytes,
            "warm archive reset reserves only new guard bytes and reuses the existing cache under pressure");
        while(!archive.Ready)archive.Bake(state,owner,strips,data);
        memory.Observe(3*1024*1024,0,0,Clock.Timestamp);
        check(archive.Suspend()&&archive.CacheSuspended,"pressure admission fixture retains only its ARAM archive while cold");
        free=TerrainArchive.CachePeakBytes+VisualMemoryPressure.SafetyBytes+512*1024;
        memory.Observe(free,0,0,Clock.Timestamp);
        check(memory.TryReserve(700*1024,Clock.Timestamp,true,out other),"another owner may hold headroom while terrain stays cold");
        int resumes=NativeTerrain.ResumeCalls;
        check(!archive.PrepareAsync(30,50,owner.Rows,Clock.Timestamp+1000)&&archive.CacheSuspended&&NativeTerrain.ResumeCalls==resumes,
            "cold terrain restoration requires global headroom before allocating its one-megabyte cache");
        memory.Cancel(other);NativeTerrain.FailResume=true;
        check(!archive.PrepareAsync(30,50,owner.Rows,Clock.Timestamp+1000)&&memory.ReservedBytes==0&&archive.CacheSuspended,
            "failed native cache restoration cancels its ticket without publishing geometry");
        resumes=NativeTerrain.ResumeCalls;
        archive.PrepareAsync(30,50,owner.Rows,Clock.Timestamp+1000);
        check(NativeTerrain.ResumeCalls==resumes,"global failure backoff suppresses repeated one-megabyte restoration attempts");
        NativeTerrain.FailResume=false;Clock.Timestamp+=VisualMemoryPressure.FailureBackoffUs+1;
        memory.Observe(free,0,0,Clock.Timestamp);
        archive.PrepareAsync(30,50,owner.Rows,Clock.Timestamp+1000);
        check(memory.ReservedBytes==TerrainArchive.CachePeakBytes&&!archive.CacheSuspended,
            "successful cache restore remains charged until the allocator sample includes the new cache");
        memory.Observe(free-TerrainArchive.CachePeakBytes,0,0,Clock.Timestamp);
        check(memory.ReservedBytes==0&&Finish(archive,30,50,owner.Rows),"fresh headroom sampling allows bounded geometry restoration to complete");
        check(archive.SuspendForSave(),"save path releases cache under the shared-pressure fixture");
        resumes=NativeTerrain.ResumeCalls;archive.ResumeAfterSave();
        check(archive.CacheSuspended&&NativeTerrain.ResumeCalls==resumes,"save cleanup defers cache allocation to the same ticketed restore path");
        memory.Observe(free,0,0,Clock.Timestamp);memory.TryReserve(700*1024,Clock.Timestamp,true,out other);
        archive.PrepareAsync(30,50,owner.Rows,Clock.Timestamp+1000);
        check(NativeTerrain.ResumeCalls==resumes,"post-save cache restoration respects concurrent model/building tickets");
        memory.Cancel(other);NativeTerrain.ThrowResume=true;bool threw=false;
        try{archive.PrepareAsync(30,50,owner.Rows,Clock.Timestamp+1000);}catch(InvalidOperationException){threw=true;}
        check(threw&&memory.ReservedBytes==0&&archive.CacheSuspended,"exceptional cache allocation also releases its ticket");
        NativeTerrain.ThrowResume=false;archive.PrepareAsync(30,50,owner.Rows,Clock.Timestamp+1000);
        int reserved=memory.ReservedBytes;
        while(!archive.TryDispose()){}
        check(reserved==TerrainArchive.CachePeakBytes&&memory.ReservedBytes==reserved,
            "disposal never prematurely releases a completed charge against an old allocator sample");
        memory.Observe(free,0,0,Clock.Timestamp);
        check(memory.ReservedBytes==0&&NativeTerrain.CacheBytes==0,"post-disposal observation clears all archive charges without a ticket leak");
        Console.WriteLine("Terrain memory admission: concurrent owners, stale snapshots, startup/resume failure, backoff, save recovery and ticket cleanup passed");
    }
    private static void PrefetchEnvelope(Action<bool,string> check)
    {
        var rows=new CylinderRows();var required=new TerrainArchiveView();var prefetched=new TerrainArchiveView();
        int peak=0;
        foreach(bool wide in new[]{false,true}){
            RenderView.SetWidescreen(wide);
            for(int z=2;z<Town.Height;z+=17)for(int x=2;x<Town.Width;x+=19){
                float centerX=(float)Math.Floor(x/TerrainArchive.PrefetchBucket)*TerrainArchive.PrefetchBucket+TerrainArchive.PrefetchBucket*.5f;
                float centerZ=(float)Math.Floor(z/TerrainArchive.PrefetchBucket)*TerrainArchive.PrefetchBucket+TerrainArchive.PrefetchBucket*.5f;
                prefetched.Prepare(centerX,centerZ,rows,TerrainArchive.PrefetchGuard);
                peak=Math.Max(peak,prefetched.Count);
                for(float dz=-TerrainArchive.PrefetchGuard;dz<=TerrainArchive.PrefetchGuard;dz+=.5f)for(float dx=-TerrainArchive.PrefetchGuard;dx<=TerrainArchive.PrefetchGuard;dx+=.5f){
                    required.Prepare(centerX+dx,centerZ+dz,rows);
                    for(int i=0;i<required.Count;i++){
                        int section=required.Sections[i];bool found=false;
                        for(int n=0;n<prefetched.Count;n++)if(prefetched.Sections[n]==section){found=true;break;}
                        check(found,"prefetch guard contains actual page "+TerrainArchivePlan.X(section)+","+TerrainArchivePlan.Z(section)+" at camera "+(centerX+dx)+","+(centerZ+dz)+" around "+centerX+","+centerZ+" wide="+wide);
                    }
                }
            }
        }
        RenderView.SetWidescreen(false);
        Console.WriteLine("Terrain prefetch envelope: all half-metre camera samples within "+TerrainArchive.PrefetchGuard+"m retained; maximum "+peak+" pages");
    }
    private static void Travel(Action<bool,string> check,TerrainArchive archive,CylinderRows rows)
    {
        foreach(int polls in new[]{2,8,16}){
        NativeTerrain.PollsPerTransfer=polls;
        Finish(archive,31,51,rows);
        for(int i=0;i<1024&&!archive.PrefetchComplete;i++)archive.PrepareAsync(31,51,rows,Clock.Timestamp+1000);
        check(archive.PrefetchComplete,"terrain prefetch completes within its view admission budget");
        float x=31,z=51;int missing=0;
        for(int frame=0;frame<1200;frame++){
            // Full running steps at 30 Hz, including abrupt eight-way reversals.
            int direction=(frame/60)%8;
            float dx=direction==0||direction==1||direction==7?1:direction==3||direction==4||direction==5?-1:0;
            float dz=direction==1||direction==2||direction==3?1:direction==5||direction==6||direction==7?-1:0;
            float length=(float)Math.Sqrt(dx*dx+dz*dz);x+=dx/length*PlayerMovement.RunStep;z+=dz/length*PlayerMovement.RunStep;
            int calls=NativeTerrain.StepCalls;
            archive.Prepare(x,z,rows);
            check(NativeTerrain.StepCalls-calls<=8,"ordinary running uses at most eight nonblocking terrain steps per frame");
            if(!archive.Presentable){if(missing<5)Console.WriteLine("Terrain coverage miss frame "+frame+" at "+x+","+z);missing++;}
            Clock.Timestamp+=33333;
        }
        check(missing==0,"guarded async terrain retains complete geometry throughout 1200 running/reversal frames at "+polls+" DMA polls per part");
        int before=NativeTerrain.StepCalls;NativeTerrain.PauseTransfers=true;
        archive.Prepare(x+.1f,z,rows);
        check(archive.Presentable&&NativeTerrain.StepCalls-before<=8,"stalled speculative DMA never hides already resident visible terrain");
        NativeTerrain.PauseTransfers=false;
        check(Finish(archive,90,95,rows)&&archive.Presentable,"teleports recover asynchronously without publishing partial terrain");
        check(NativeTerrain.BindCalls==0,"startup, ordinary travel, reversals, teleports and resume never call synchronous bind");
        // Keep subsequent lifecycle checks on their original fixture view.
        Finish(archive,31,51,rows);
        }
        NativeTerrain.PollsPerTransfer=2;
        Console.WriteLine("Async terrain: "+TerrainArchive.PrefetchGuard+"m guard / 2m buckets, 3600 running/reversal frames at 2/8/16 DMA polls per part without missing terrain, bounded DMA and teleport recovery passed");
    }
}
