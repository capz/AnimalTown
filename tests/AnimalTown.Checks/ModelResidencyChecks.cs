using AnimalTown;
using Dolphin.Graphics;
using Dolphin.Memory;
using Dolphin.Timing;

internal static class NativeModels
{
    internal static int Retain(Model model)=>model.RetainPermanently();
}

namespace Dolphin.Memory
{
    public sealed class AramStatistics
    {
        public int FreeBytes=>Aram.FreeBytes;
        public int LargestFreeBlockBytes=>Aram.LargestFreeBlockBytes;
        public int AllocationCount=>Aram.AllocationCount;
    }
    public static class Aram
    {
        public static bool IsAvailable=true;
        public static int FreeBytes,LargestFreeBlockBytes,AllocationCount;
        public static AramStatistics Statistics=>new AramStatistics();
        public static int WakeCalls;
        public static void WakeTransfers(){WakeCalls++;}
        public static void Reset(){IsAvailable=true;FreeBytes=LargestFreeBlockBytes=6*1024*1024;AllocationCount=1;}
    }
}
namespace Dolphin.Graphics
{
    public sealed class ModelInstance : IDisposable
    {
        public readonly Model Model;
        public readonly int Budget;
        public int NativeBytes=>Model.InstanceBytes;
        public ModelInstance(Model model,int budget){Model=model;Budget=budget;}
        public bool Disposed;
        public void Dispose(){Disposed=true;}
    }
    // Tasks deliberately complete only through explicit advancement, matching
    // native ownership/polling instead of making every async call succeed inline.
    public sealed class Model : IDisposable
    {
        public readonly List<int> Animations=new List<int>{0,1};
        public bool AutomaticResidencyProgress=true;
        public int ResidencyBytesPerStep,ResidencyWorkItemsPerStep;
        public int PackageBytes {get;set;}=65536;
        public bool IsResident {get;private set;}=true;
        public bool SupportsAramResidency {get;private set;}=true;
        public bool IsTransitioning=>completion!=null;
        public int AramBytes {get;private set;}
        public int NativeBytes=>IsResident?PackageBytes*2:128;
        public bool FailAdmission,FailWork,CancelWork,CommitCancellation,FailRetention;
        public int InstanceCreations,RetentionCalls;
        public int Starts,Advances,AnimationPosition=37,PendingDmaPolls=1,WorkUs=7,ProgressSteps;
        public bool Disposed,FailInstance;
        public int FailInstanceAt;
        public ModelInstance? LastInstance;
        public int InstanceBytes=65536;
        public void Dispose(){Disposed=true;}
        public Task? LastTransition {get;private set;}
        private TaskCompletionSource<int>? completion;
        private CancellationToken cancellation;
        private bool restore;
        private int step;
        public ModelInstance CreateInstance(int budget)
        {
            if(FailInstance||FailInstanceAt>0&&InstanceCreations+1==FailInstanceAt)throw new OutOfMemoryException("Injected instance allocation failure");
            if(!IsResident||IsTransitioning)throw new InvalidOperationException("A cold or transitioning model cannot create an instance");
            InstanceCreations++;LastInstance=new ModelInstance(this,budget);return LastInstance;
        }
        public int RetainPermanently()
        {
            if(!IsResident||IsTransitioning||InstanceCreations!=0)throw new InvalidOperationException("Retain before creating either instance");
            RetentionCalls++;
            if(FailRetention)return -2;
            SupportsAramResidency=false;return 1;
        }
        public Task PrepareAsync(CancellationToken token)=>Start(true,token);
        public Task UnloadToAramAsync(CancellationToken token)=>Start(false,token);
        private Task Start(bool warming,CancellationToken token)
        {
            if(!SupportsAramResidency)throw new InvalidOperationException("Compacted models cannot enter ARAM residency");
            if(FailAdmission){FailAdmission=false;throw new OutOfMemoryException("Injected admission failure");}
            if(IsTransitioning)throw new InvalidOperationException("Overlapping model owners");
            Starts++;restore=warming;cancellation=token;step=0;completion=new TaskCompletionSource<int>();LastTransition=completion.Task;return completion.Task;
        }
        public bool AdvanceResidency()
        {
            if(completion==null)return false;
            Clock.Timestamp+=WorkUs;Advances++;
            if(++step<=PendingDmaPolls)return false; // Native DMA is pending; ordinary frame work must not spin.
            if(step<=PendingDmaPolls+ProgressSteps)return true;
            var done=completion;completion=null;
            if(CancelWork||cancellation.IsCancellationRequested&&!CommitCancellation){CancelWork=false;done.SetCanceled();return true;}
            if(FailWork){FailWork=false;done.SetException(new IOException("Injected restore/copy failure"));return true;}
            if(!restore&&AramBytes==0){AramBytes=(PackageBytes+31)&~31;Aram.FreeBytes-=AramBytes;Aram.AllocationCount++;}
            IsResident=restore;done.SetResult(0);return true;
        }
        public bool ReleaseAramBacking()
        {
            if(IsTransitioning||!IsResident)throw new InvalidOperationException("Cold backing is still owned");
            if(AramBytes==0)return false;Aram.FreeBytes+=AramBytes;Aram.AllocationCount--;AramBytes=0;return true;
        }
    }
}
internal static class ModelResidencyChecks
{
    private static GameState State()
    {
        var state=new GameState(204){HousePlaced=true,Species=0,Space=1,X=2,Z=2};
        for(int i=0;i<6;i++){state.Town.Residents[i].Space=16+i;state.Town.Residents[i].PosX=2;state.Town.Residents[i].PosZ=2;}
        return state;
    }
    private static ModelResidency Setup(out Model[] models,out GameState state,out PortalSystem doors)
    {
        Clock.Timestamp=0;Aram.Reset();models=new Model[6];state=State();doors=new PortalSystem();var manager=new ModelResidency();
        for(int i=0;i<6;i++){models[i]=new Model();manager.Register(i,models[i],0);}
        manager.Require(state,doors,0);Clock.Timestamp=6000000;return manager;
    }
    private static void Pump(ModelResidency manager,bool demote=false)=>manager.Pump(Clock.Timestamp,demote,Clock.Timestamp+1000);
    private static void Drain(ModelResidency manager)
    {for(int i=0;i<20&&manager.PendingSpecies>=0;i++)Pump(manager);if(manager.PendingSpecies>=0)throw new Exception("Residency failed to drain");}
    private static bool Startup(ModelResidency manager,GameState state,PortalSystem doors,int checkpoint,bool pinAll=false)
        =>manager.PrepareStartup(state,doors,checkpoint,Clock.Timestamp,Clock.Timestamp+1000,pinAll);
    private static void FinishStartup(ModelResidency manager,GameState state,PortalSystem doors,int checkpoint,bool pinAll=false)
    {for(int i=0;i<100;i++)if(Startup(manager,state,doors,checkpoint,pinAll))return;throw new Exception("Startup residency checkpoint did not finish");}
    internal static void Run(Action<bool,string> check)
    {
        var manager=Setup(out var models,out var state,out var doors);
        check(models.All(x=>!x.AutomaticResidencyProgress&&x.ResidencyBytesPerStep==8192&&x.ResidencyWorkItemsPerStep==32),"application grants eight KiB and thirty-two work items per manual model step");
        Pump(manager,true);check(manager.PendingSpecies==1&&manager.Ready(0),"one idle shared model starts without touching the player");
        int before=models[1].Advances;Pump(manager);
        check(models[1].Advances==before+1&&manager.PendingSpecies==1,"a pending DMA stops work rather than spinning through frame credit");
        Drain(manager);check(!models[1].IsResident&&manager.Demotions==1&&manager.ColdBytes==65536,"successful offload frees native residency and accounts its ARAM backing");
        check(manager.SnapshotBytes==65536&&manager.MaxAdvanceUs==7&&manager.WorkSteps==2,"paging records copy volume and measured indivisible step cost");
        state.Town.Residents[1].Space=1;manager.Require(state,doors,Clock.Timestamp);Pump(manager);
        check(!manager.ReadyForSpace(state,doors,1)&&manager.PendingSpecies==1,"a cold destination cannot authorize native drawing or portal commit");
        Drain(manager);check(manager.ReadyForSpace(state,doors,1)&&manager.Restores==1&&models[1].AnimationPosition==37,"restore retains wrapper/playback identity and makes the complete space drawable");
        check(manager.ColdBytes==65536,"restored snapshots still count against the cold budget");
        manager.Enabled=false;manager.Require(state,doors,Clock.Timestamp);Pump(manager);
        check(manager.ColdBytes==0&&manager.BackingReleases==1,"disable releases backing only after every required resource is resident");
        manager.Require(state,doors,Clock.Timestamp);Pump(manager);long allocated=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<10000;i++){manager.Require(state,doors,Clock.Timestamp);Pump(manager);}
        check(GC.GetAllocatedBytesForCurrentThread()==allocated,"steady demand and completed residency polling allocate nothing");

        manager=Setup(out models,out state,out doors);Pump(manager,true);Drain(manager);
        state.Space=0;state.X=80;state.Z=80;var prefetched=state.Town.Residents[1];prefetched.Space=0;
        bool found=false;
        for(int z=24;z<137&&!found;z+=2)for(int x=24;x<137&&!found;x+=2){
            prefetched.PosX=x;prefetched.PosZ=z;
            found=ModelResidencyDemand.UsesSpace(state,doors,1,0)&&!ModelResidencyDemand.DrawableUsesSpace(state,doors,1,0);
        }
        check(found,"fixture includes a hidden model inside the expanded prefetch envelope");
        manager.Require(state,doors,Clock.Timestamp);Pump(manager);
        check(manager.Required(1)&&!manager.Ready(1)&&manager.ReadyForSpace(state,doors,0),"background lookahead restores hidden actors without freezing the current scene");Drain(manager);

        manager=Setup(out models,out state,out doors);Pump(manager,true);Drain(manager);Pump(manager,true);Drain(manager);
        check(!models[1].IsResident&&!models[2].IsResident,"priority fixture contains two cold shared resources");
        state.Space=0;state.X=80;state.Z=80;prefetched=state.Town.Residents[1];prefetched.Space=0;
        found=false;
        for(int z=24;z<137&&!found;z+=2)for(int x=24;x<137&&!found;x+=2){
            prefetched.PosX=x;prefetched.PosZ=z;
            found=ModelResidencyDemand.UsesSpace(state,doors,1,0)&&!ModelResidencyDemand.DrawableUsesSpace(state,doors,1,0);
        }
        manager.Require(state,doors,Clock.Timestamp);Pump(manager);check(manager.PendingSpecies==1,"idle prefetch may prepare an invisible actor");
        var visible=state.Town.Residents[2];visible.Space=0;visible.PosX=80;visible.PosZ=80;
        manager.Require(state,doors,Clock.Timestamp);Pump(manager);Pump(manager);
        check(manager.PendingSpecies==2&&manager.Cancellations==1&&manager.Demotions==2,"visible demand preempts hidden prefetch and does not miscount a canceled restore as demotion");
        Drain(manager);check(manager.ReadyForSpace(state,doors,0),"foreground restoration completes before optional prefetch resumes");

        manager=Setup(out models,out state,out doors);Pump(manager,true);Drain(manager);Pump(manager,true);Drain(manager);
        state.Space=0;state.X=state.Z=80;
        for(int species=1;species<=2;species++){
            var arriving=state.Town.Residents[species];arriving.Space=0;bool placed=false;
            for(int z=0;z<Town.Height&&!placed;z+=2)for(int x=0;x<Town.Width&&!placed;x+=2){
                arriving.PosX=x;arriving.PosZ=z;
                placed=ModelResidencyDemand.UsesSpace(state,doors,species,0)&&!ModelResidencyDemand.DrawableUsesSpace(state,doors,species,0)&&
                    ModelResidencyDemand.ImminentUsesSpace(state,doors,species,0)==(species==2);
            }
            check(placed,"priority fixture includes distinct invisible outer and imminent prefetch bands");
            if(species==1){manager.Require(state,doors,Clock.Timestamp);Pump(manager);}
        }
        check(manager.PendingSpecies==1,"outer-band work begins before a nearer arrival is known");
        manager.Require(state,doors,Clock.Timestamp);Pump(manager);Pump(manager);
        check(manager.PendingSpecies==2&&manager.Cancellations==1&&manager.ReadyForSpace(state,doors,0),"a nearer offscreen arrival preempts speculative work before either actor becomes visible");
        Drain(manager);manager.Stop();

        manager=Setup(out models,out state,out doors);Pump(manager,true);
        state.Town.Residents[1].Space=1;manager.Require(state,doors,Clock.Timestamp);Drain(manager);
        check(manager.Ready(1)&&manager.Cancellations==1&&manager.Demotions==0,"new demand cancels a precommit demotion while preserving the resident original");
        manager=Setup(out models,out state,out doors);models[1].CommitCancellation=true;Pump(manager,true);
        state.Town.Residents[1].Space=1;manager.Require(state,doors,Clock.Timestamp);Drain(manager);
        check(manager.Ready(1)&&manager.Demotions==1&&manager.Restores==1,"demand after the atomic retirement boundary restores the coherent cold result");

        manager=Setup(out models,out state,out doors);Aram.FreeBytes=ModelResidency.AramReserve+models[1].PackageBytes-1;Pump(manager,true);
        check(manager.PendingSpecies<0&&models.All(x=>x.IsResident),"optional stores preserve global ARAM headroom");
        manager=Setup(out models,out state,out doors);Aram.LargestFreeBlockBytes=65535;Pump(manager,true);
        check(manager.PendingSpecies<0,"fragmentation rejects a snapshot before touching native residency");
        manager=Setup(out models,out state,out doors);Aram.AllocationCount=248;Pump(manager,true);check(manager.PendingSpecies<0,"paging reserves native allocation slots for other owners");
        manager=Setup(out models,out state,out doors);models[1].FailAdmission=true;Pump(manager,true);
        check(manager.Failures==1&&manager.Ready(1),"failed snapshot admission leaves the original drawable");
        manager=Setup(out models,out state,out doors);models[1].FailWork=true;Pump(manager,true);Drain(manager);
        check(manager.Failures==1&&manager.Ready(1)&&manager.ColdBytes==0,"failed upload never exposes an incomplete cold model");
        manager=Setup(out models,out state,out doors);Pump(manager,true);Drain(manager);models[1].FailWork=true;
        state.Town.Residents[1].Space=1;manager.Require(state,doors,Clock.Timestamp);Pump(manager);Drain(manager);
        check(manager.Failures==1&&!manager.Ready(1)&&models[1].AramBytes==65536,"failed restore preserves the only backing and rejects native use");
        int starts=models[1].Starts;Pump(manager);check(models[1].Starts==starts,"failed restore has a bounded retry delay");
        Clock.Timestamp+=ModelResidency.RetryDelayUs+1;Pump(manager);Drain(manager);
        check(manager.Ready(1)&&models[1].AnimationPosition==37,"restore retry recovers the same animation/game object");

        manager=Setup(out models,out state,out doors);models[1].PendingDmaPolls=5;Pump(manager,true);Pump(manager);
        var shutdownDemotion=models[1].LastTransition!;
        check(!shutdownDemotion.IsCompleted&&manager.PendingSpecies==1,"session exit fixture owns an unfinished snapshot DMA");
        manager.Stop();
        check(shutdownDemotion.IsCanceled&&!models[1].IsTransitioning&&models[1].Advances==6&&manager.PendingSpecies<0,
            "session teardown cancels and drains a manually driven snapshot across pending DMA polls");
        check(models[1].IsResident&&manager.ColdBytes==0&&manager.Cancellations==1,"precommit shutdown keeps the original model until normal disposal");
        int advances=models[1].Advances,admissions=models.Sum(x=>x.Starts);
        manager.Stop();manager.Require(state,doors,Clock.Timestamp);Pump(manager,true);
        check(models[1].Advances==advances&&models.Sum(x=>x.Starts)==admissions,"stopped residency is idempotent and admits no replacement work");

        manager=Setup(out models,out state,out doors);Pump(manager,true);Drain(manager);
        state.Town.Residents[1].Space=1;manager.Require(state,doors,Clock.Timestamp);models[1].PendingDmaPolls=5;Pump(manager);Pump(manager);
        var shutdownRestore=models[1].LastTransition!;int restoreAt=models[1].Advances;
        check(!shutdownRestore.IsCompleted&&manager.PendingSpecies==1,"session exit fixture owns an unfinished restoration DMA");
        manager.Stop();
        check(shutdownRestore.IsCanceled&&!models[1].IsTransitioning&&models[1].Advances==restoreAt+5&&manager.PendingSpecies<0,
            "session teardown drains restoration before its instances or model can be disposed");
        check(!models[1].IsResident&&manager.ColdBytes==65536&&models[1].Starts==2,"canceled shutdown preserves the cold model's backing for normal disposal without restarting it");

        PressureChecks(check);
        PressureBoundaryChecks(check);
        WaitingHeadroomChecks(check);
        StartupChecks(check);
        OutdoorJourneyChecks(check);
        ApproachChecks(check);
    }
    private static void PressureChecks(Action<bool,string> check)
    {
        var manager=Setup(out var models,out var state,out var doors);
        for(int species=0;species<6;species++)manager.RecordWarmPeak(species,65536);
        var memory=new VisualMemoryPressure();memory.Observe(8*1024*1024,0,0,Clock.Timestamp);manager.Memory=memory;
        Pump(manager,true);Drain(manager);state.Town.Residents[1].Space=state.Space;
        int peak=manager.RestorePeak(1);check(peak==models[1].PackageBytes*2+65536+64*1024,"restore uses captured warm native ownership plus a measured guard rather than a multi-MiB fixed estimate");
        memory.Observe(VisualMemoryPressure.SafetyBytes+peak-1,0,0,Clock.Timestamp);manager.Require(state,doors,Clock.Timestamp);
        check(manager.TryLockReplacement(2),"a warm optional model can be leased by a replacement owner");
        Pump(manager,false);
        check(manager.PendingSpecies==3&&!models[1].IsResident&&models[2].IsResident,"pressure retires an optional model despite the normal portal/idle heuristic while protecting required and replacement-leased species");
        manager.UnlockReplacement(2);memory.Observe(8*1024*1024,0,0,Clock.Timestamp);Drain(manager);
        check(manager.Ready(1)&&manager.Restores==1&&memory.ReservedBytes>=peak,"new headroom restores the required actor and retains completed allocation debt until a fresh sample");
        memory.Observe(8*1024*1024,0,0,Clock.Timestamp);check(memory.ReservedBytes==0,"completed restore reservation retires at the allocator observation boundary");manager.Stop();

        manager=Setup(out models,out state,out doors);for(int species=0;species<6;species++)manager.RecordWarmPeak(species,65536);
        Pump(manager,true);Drain(manager);state.Town.Residents[1].Space=state.Space;
        memory=new VisualMemoryPressure();memory.Observe(VisualMemoryPressure.SafetyBytes+manager.RestorePeak(1)-1,0,0,Clock.Timestamp);manager.Memory=memory;
        check(!Startup(manager,state,doors,19)&&manager.PendingSpecies==2,"startup with denied foreground headroom can retire an optional warm model instead of deadlocking its loading checkpoint");manager.Stop();
    }
    private static void PressureBoundaryChecks(Action<bool,string> check)
    {
        var manager=Setup(out var models,out var state,out var doors);
        for(int species=0;species<6;species++)manager.RecordWarmPeak(species,65536);
        Pump(manager,true);Drain(manager);Pump(manager,true);Drain(manager);
        check(!models[1].IsResident&&!models[2].IsResident,"pressure lookahead fixture begins with two cold species");
        state.Space=0;state.X=state.Z=80;Array.Clear(state.Town.Elevation);Array.Fill(state.Town.Tiles,(byte)Terrain.Grass);RenderView.SetWidescreen(false);
        float inX=FindPressureBand(state,doors,1,true);FindPressureBand(state,doors,2,false);
        var arriving=state.Town.Residents[1];arriving.PosX=inX-.25f;
        check(!ModelResidencyDemand.ImminentUsesSpace(state,doors,1,0)&&ModelResidencyDemand.UsesSpace(state,doors,1,0),"a quarter-metre reversal crosses the imminent guard while staying in the outer prefetch band");
        arriving.PosX=inX;
        var memory=new VisualMemoryPressure();memory.Observe(VisualMemoryPressure.LowBytes-1,0,0,Clock.Timestamp);memory.Observe(2*1024*1024,0,0,Clock.Timestamp);manager.Memory=memory;
        manager.Require(state,doors,Clock.Timestamp);int outerStarts=models[2].Starts;
        Pump(manager,false);
        check(memory.UnderPressure&&memory.WaitingPeakBytes==0&&manager.PendingSpecies==1&&!ModelResidencyDemand.DrawableUsesSpace(state,doors,1,0),"pressure admits an imminent invisible actor using available headroom before it becomes drawable");
        check(!manager.Required(2)&&models[2].Starts==outerStarts,"outer-band speculative restoration stays blocked under pressure despite available native headroom");
        Drain(manager);
        check(manager.Ready(1)&&manager.Restores==1&&!ModelResidencyDemand.DrawableUsesSpace(state,doors,1,0),"the imminent restore completes while the entire actor remains outside the drawable envelope");
        check(models[2].Starts==outerStarts&&!models[2].IsResident,"completing imminent work does not resume optional outer prefetch under pressure");
        memory.Observe(2*1024*1024,0,0,Clock.Timestamp);
        long warmAt=Clock.Timestamp;int starts=models[1].Starts,demotions=manager.Demotions;
        for(int crossing=0;crossing<10;crossing++){
            Clock.Timestamp+=50000;arriving.PosX=inX-.25f;manager.Require(state,doors,Clock.Timestamp);Pump(manager,true);
            Clock.Timestamp+=50000;arriving.PosX=inX;manager.Require(state,doors,Clock.Timestamp);Pump(manager,true);
        }
        check(manager.Ready(1)&&models[1].Starts==starts&&manager.Demotions==demotions,"rapid reversals across the pressure guard never retire and restore the same actor repeatedly");
        arriving.PosX=inX-.25f;manager.Require(state,doors,Clock.Timestamp);
        Clock.Timestamp+=1100000;manager.Require(state,doors,Clock.Timestamp);Pump(manager,true);
        check(manager.Ready(1)&&models[1].Starts==starts&&Clock.Timestamp-warmAt<ModelResidency.WarmGraceUs,"ordinary pressure preserves the full post-restore warm grace even after the outside-demand delay expires");
        Clock.Timestamp=warmAt+ModelResidency.WarmGraceUs+100000;arriving.PosX=inX;manager.Require(state,doors,Clock.Timestamp);Pump(manager,true);
        for(int crossing=0;crossing<5;crossing++){
            Clock.Timestamp+=200000;arriving.PosX=inX-.25f;manager.Require(state,doors,Clock.Timestamp);Pump(manager,true);
            Clock.Timestamp+=200000;arriving.PosX=inX;manager.Require(state,doors,Clock.Timestamp);Pump(manager,true);
        }
        check(manager.Ready(1)&&models[1].Starts==starts&&manager.Demotions==demotions,"the last-demand grace also prevents repeated retirement during boundary reversals after post-restore warmth has expired");
        long lastDemand=Clock.Timestamp;arriving.PosX=inX-.25f;manager.Require(state,doors,Clock.Timestamp);
        Clock.Timestamp=lastDemand+ModelResidency.PressureIdleDelayUs-1;manager.Require(state,doors,Clock.Timestamp);Pump(manager,true);
        check(manager.Ready(1)&&models[1].Starts==starts,"a recently required actor stays warm for one full second outside the imminent guard under non-acute pressure");
        Clock.Timestamp=lastDemand+ModelResidency.PressureIdleDelayUs+1;manager.Require(state,doors,Clock.Timestamp);Pump(manager,true);
        check(manager.PendingSpecies==1,"a stably unneeded actor becomes eligible after both pressure and warm grace periods expire");
        arriving.PosX=inX;manager.Require(state,doors,Clock.Timestamp);Drain(manager);
        check(manager.Ready(1)&&manager.Cancellations==1&&manager.Demotions==demotions,"reversing into demand during optional retirement cancels before publication and keeps the existing warm model");
        // An actual essential reservation denial may reclaim this just-warmed
        // optional actor immediately; general pressure alone may not.
        arriving.PosX=inX-.25f;var foreground=state.Town.Residents[2];foreground.PosX=state.X;foreground.PosZ=state.Z;
        memory.Observe(VisualMemoryPressure.SafetyBytes+manager.RestorePeak(2)-1,0,0,Clock.Timestamp);manager.Require(state,doors,Clock.Timestamp);Pump(manager,false);
        check(memory.WaitingPeakBytes>=manager.RestorePeak(2)&&manager.PendingSpecies==1&&manager.Ready(0)&&!models[2].IsResident,
            "an essential restore waiting for real headroom bypasses optional grace while preserving current and player demand");
        manager.Stop();
    }
    private static void WaitingHeadroomChecks(Action<bool,string> check)
    {
        var manager=Setup(out var models,out var state,out var doors);
        for(int species=0;species<6;species++)manager.RecordWarmPeak(species,65536);
        for(int species=1;species<6;species++){Pump(manager,true);Drain(manager);}
        state.Town.Residents[2].Space=state.Space;manager.Require(state,doors,Clock.Timestamp);Pump(manager);Drain(manager);
        state.Space=0;state.X=state.Z=80;Array.Clear(state.Town.Elevation);Array.Fill(state.Town.Tiles,(byte)Terrain.Grass);
        FindPressureBand(state,doors,1,true);FindPressureBand(state,doors,2,true);
        var memory=new VisualMemoryPressure();memory.Observe(1280*1024,0,0,Clock.Timestamp);manager.Memory=memory;
        manager.Require(state,doors,Clock.Timestamp);Pump(manager);
        int modelPeak=manager.RestorePeak(1);check(manager.PendingSpecies==1&&manager.Required(2)&&memory.ReservedBytes==modelPeak,"ordinary pressure admits imminent prefetch and pins an already warm imminent actor");
        const int cachePeak=1024*1024;
        check(!memory.TryReserve(cachePeak,Clock.Timestamp,true,out _)&&memory.WaitingPeakBytes==cachePeak,"an essential cache restore records its larger unsatisfied allocation peak");
        check(memory.TryReserve(32*1024,Clock.Timestamp,true,out int small)&&memory.WaitingPeakBytes==cachePeak,"a smaller successful essential reservation cannot erase the larger waiting cache request");memory.Cancel(small);
        manager.Require(state,doors,Clock.Timestamp);
        check(!manager.Required(1)&&!manager.Required(2)&&manager.Cancellations==1&&memory.ReservedBytes==modelPeak,"a waiting essential peak removes optional imminent pins and cancels active prefetch without releasing its reservation before drain");
        Pump(manager);check(manager.PendingSpecies==1&&memory.ReservedBytes==modelPeak,"pending native prefetch cancellation keeps its full reservation until DMA work reaches a terminal state");
        Pump(manager);check(manager.PendingSpecies==2&&!models[1].IsResident&&memory.ReservedBytes==0,"drained prefetch releases headroom before the now-optional warm imminent actor is retired");
        Drain(manager);check(!models[2].IsResident&&manager.Ready(0),"urgent reclamation can retire an imminent incumbent while preserving the selected player");
        int starts=models[1].Starts;
        // The allocator observes the released incumbent. The pending cache
        // owner runs before lower-priority model pumping and claims its ticket.
        memory.Observe(1408*1024,0,0,Clock.Timestamp);
        check(memory.TryReserve(cachePeak,Clock.Timestamp,true,out int cache),"reclaimed headroom admits the previously starved essential cache restore");
        manager.Require(state,doors,Clock.Timestamp);Pump(manager);
        check(memory.ReservedBytes==cachePeak&&models[1].Starts==starts&&!models[1].IsResident,"imminent model restoration cannot consume the cache owner's reserved recovery headroom");
        memory.Cancel(cache);manager.Stop();
        var demand=new ModelResidencyDemand();state.Town.Residents[1].PosX=state.X;state.Town.Residents[1].PosZ=state.Z;
        demand.Prepare(state,doors,false,-1,true,true);
        check(demand.Required[0]&&demand.Required[1]&&!demand.Required[2],"urgent reclamation retains a drawable actor while excluding an invisible imminent peer");
        state.Space=1;
        state.Town.Residents[1].Space=1;state.Town.Residents[2].Space=17;state.Town.Residents[3].Space=18;
        var player=doors.Actors[0];player.Phase=DoorPhase.Waiting;player.Source=new ActorLocation(1,2,2,0);player.Destination=new ActorLocation(17,2,2,0);
        var entrant=doors.Actors[4];entrant.Phase=DoorPhase.Waiting;entrant.Source=new ActorLocation(18,2,2,0);entrant.Destination=new ActorLocation(1,2,2,0);
        demand.Prepare(state,doors,false,-1,true,true);
        check(demand.Required[0]&&demand.Required[1]&&demand.Required[2]&&demand.Required[3],"urgent lookahead reduction preserves player, current room, destination room, and an entering NPC's portal endpoint");
        demand.Prepare(state,doors,true,-1,true,true);check(demand.Required.All(x=>x),"explicit all-model inspection pins remain authoritative while essential headroom is waiting");
    }
    private static float FindPressureBand(GameState state,PortalSystem doors,int species,bool imminent)
    {
        var resident=state.Town.Residents[species];resident.Space=0;resident.PosZ=state.Z;
        for(float x=0;x<Town.Width;x+=.25f){
            resident.PosX=x;
            if(ModelResidencyDemand.UsesSpace(state,doors,species,0)&&!ModelResidencyDemand.DrawableUsesSpace(state,doors,species,0)&&
                ModelResidencyDemand.ImminentUsesSpace(state,doors,species,0)==imminent)return x;
        }
        throw new Exception("Missing distinct pressure prefetch bands");
    }
    private static void OutdoorJourneyChecks(Action<bool,string> check)
    {
        Clock.Timestamp=0;Aram.Reset();
        var state=new GameState(7319){HousePlaced=true,Species=0,X=120,Z=70};
        var journey=new VillagerJourney();var manager=new ModelResidency();var models=new Model[6];
        for(int i=0;i<6;i++){models[i]=new Model();manager.Register(i,models[i],0);}
        bool movedCold=false,enteredCold=false,exitedCold=false;var visited=new bool[6];
        for(int tick=0;tick<4000;tick++){
            Clock.Timestamp+=33333;manager.Require(state,journey.Doors,Clock.Timestamp);Pump(manager,true);
            var oldX=new float[6];var oldZ=new float[6];var oldSpace=new int[6];
            for(int i=0;i<6;i++){var v=state.Town.Residents[i];oldX[i]=v.PosX;oldZ[i]=v.PosZ;oldSpace[i]=v.Space;journey.Doors.ResourcesReady[i+1]=!manager.Required(i)||manager.Ready(i);}
            journey.Tick(state);
            for(int i=1;i<6;i++)if(!models[i].IsResident){
                var v=state.Town.Residents[i];movedCold|=oldX[i]!=v.PosX||oldZ[i]!=v.PosZ;
                enteredCold|=oldSpace[i]==0&&v.Space!=0;visited[i]|=v.Space!=0;
                exitedCold|=visited[i]&&oldSpace[i]!=0&&v.Space==0;
                check(models[i].AnimationPosition==37,"cold villager logic never advances a native animation resource");
            }
        }
        check(manager.Demotions>0&&models[0].IsResident,"outdoor paging keeps the player's shared model resident");
        check(movedCold&&enteredCold&&exitedCold,"offloaded villagers keep moving, entering houses and returning outside");
        var approaching=state.Town.Residents[1];approaching.Space=0;approaching.PosX=state.X-19;approaching.PosZ=state.Z;journey.Doors.Actors[2].Phase=DoorPhase.Idle;
        manager.Require(state,journey.Doors,Clock.Timestamp);for(int i=0;i<100&&!manager.Ready(1);i++){Pump(manager);Clock.Timestamp+=33333;}
        check(manager.Ready(1),"returning toward the viewport restores the existing shared model");
        manager.Stop();
    }
    private static void ApproachChecks(Action<bool,string> check)
    {
        // Five cold species share a slow transfer owner. Move at full run speed
        // through both a viewport edge and the cylinder horizon. Each species
        // must finish restoring before its first drawable frame, without waits.
        foreach(bool fromHorizon in new[]{false,true})foreach(bool wide in new[]{false,true}){
            var manager=Setup(out var models,out var state,out var doors);
            for(int i=1;i<6;i++){Pump(manager,true);Drain(manager);}
            check(models.Skip(1).All(m=>!m.IsResident),"approach fixture starts with five cold species");
            RenderView.SetWidescreen(wide);Array.Clear(state.Town.Elevation);state.Space=0;
            state.X=fromHorizon?80:10;state.Z=fromHorizon?110:80;
            for(int i=1;i<6;i++){
                models[i].PendingDmaPolls=8;models[i].ProgressSteps=45;models[i].WorkUs=300;
                var v=state.Town.Residents[i];v.Space=0;v.PosX=fromHorizon?76+i:100+i;v.PosZ=fromHorizon?30:50;
            }
            var seen=new bool[6];int hiddenRestores=0;
            for(int frame=0;frame<600;frame++){
                Clock.Timestamp+=33333;
                if(fromHorizon)state.Z-=PlayerMovement.RunStep;else state.X+=PlayerMovement.RunStep;
                manager.Require(state,doors,Clock.Timestamp);Pump(manager,true);
                for(int i=1;i<6;i++){
                    bool visible=ModelResidencyDemand.DrawableUsesSpace(state,doors,i,0);
                    if(visible){check(manager.Ready(i),"queued ARAM restores complete before the first visible frame at full running speed");seen[i]=true;}
                    if(!visible&&manager.Ready(i)&&models[i].Starts>1)hiddenRestores|=1<<i;
                }
            }
            check(seen.Skip(1).All(x=>x)&&hiddenRestores==62,"all five species preload while invisible and later enter the viewport");
            check(manager.ReadinessWaits==0&&manager.PortalWaits==0,"outdoor prefetch does not freeze gameplay or wait for the transfer");
            manager.Stop();
        }
        RenderView.SetWidescreen(false);
    }
    private static void StartupChecks(Action<bool,string> check)
    {
        // Exercise every profile species, including spaniel which was formerly
        // last in the loader. The same production order/factory reserves one
        // player before any later model is admitted, without a second late copy.
        for(int selected=0;selected<6;selected++){
            Clock.Timestamp=0;Aram.Reset();var state=State();state.Species=selected;var doors=new PortalSystem();var manager=new ModelResidency();
            var cohort=new Model[6];var seen=new bool[6];ModelInstance? player=null;
            for(int progress=0;progress<6;progress++){
                FinishStartup(manager,state,doors,progress);
                int species=ModelStartup.SpeciesAt(selected,progress);
                check(!seen[species]&&(progress!=0||species==selected),"selected species loads first and each shared species loads once");seen[species]=true;
                if(progress>0)check(player!=null&&ReferenceEquals(player.Model,cohort[selected]),"the selected player instance is reserved before every later package");
                var model=cohort[species]=new Model();ModelStartup.RetainSelected(selected,species,model);manager.Register(species,model,Clock.Timestamp);
                _=model.CreateInstance(512*1024); // NPC adoption precedes paging.
                var reserved=ModelStartup.ReservePlayer(selected,species,model);if(reserved!=null)player=reserved;
                if(progress==0)check(player!=null&&player.Budget==512*1024&&!manager.AllReady,"player reservation does not wait for the unregistered cohort");
            }
            FinishStartup(manager,state,doors,6);
            check(player!=null&&cohort[selected].InstanceCreations==2&&cohort.Where((_,i)=>i!=selected).All(x=>x.InstanceCreations==1),
                "startup creates exactly seven instances and never repeats final player allocation");
            check(cohort[selected].RetentionCalls==1&&!cohort[selected].SupportsAramResidency&&cohort.Where((_,i)=>i!=selected).All(x=>x.RetentionCalls==0&&x.SupportsAramResidency),
                "only the permanently pinned selected shared species is compacted before its instances");
            check(cohort[selected].IsResident&&cohort.Where((_,i)=>i!=selected).All(x=>!x.IsResident)&&manager.Demotions==5,
                "startup protects the selected shared model while reclaiming every genuinely unused species");
            check(Clock.Timestamp<ModelResidency.WarmGraceUs&&manager.ReadyForSpace(state,doors,state.Space),"startup bypasses dwell without exposing a cold drawable actor");
            manager.Require(state,doors,Clock.Timestamp);Pump(manager);
            check(cohort.Count(x=>x.IsResident)==1,"normal demand keeps startup-cold unused species cold without a draft-wide pin");
        }

        Clock.Timestamp=0;Aram.Reset();var partialState=State();var partialDoors=new PortalSystem();var partial=new ModelResidency();
        var selectedModel=new Model();partial.Register(0,selectedModel,0);var unused=new Model{PendingDmaPolls=4};partial.Register(1,unused,0);
        check(!Startup(partial,partialState,partialDoors,2)&&unused.Starts==1,"startup admits a store without waiting five seconds");
        for(int i=0;i<4;i++){
            int before=unused.Advances;
            check(!Startup(partial,partialState,partialDoors,2)&&unused.Advances==before+1,"pending startup DMA blocks next package admission and consumes one polling grant");
        }
        check(Startup(partial,partialState,partialDoors,2)&&!unused.IsResident&&partial.PendingSpecies<0&&!partial.AllReady,
            "a checkpoint drains its owned transfer without waiting for unloaded species");
        partialState.Town.Residents[1].Space=partialState.Space;
        check(!Startup(partial,partialState,partialDoors,2)&&!partial.ReadyForSpace(partialState,partialDoors,partialState.Space),
            "new drawable demand keeps startup gated until its cold model restores");
        FinishStartup(partial,partialState,partialDoors,2);
        check(unused.IsResident&&partial.ReadyForSpace(partialState,partialDoors,partialState.Space),"the original shared instance resource is restored before startup can publish");

        foreach(string fault in new[]{"admission","work","cancel","unavailable","reserve","fragment","slots","budget"}){
            Clock.Timestamp=0;Aram.Reset();var state=State();var doors=new PortalSystem();var manager=new ModelResidency();
            manager.Register(0,new Model(),0);var optional=new Model{FailAdmission=fault=="admission",FailWork=fault=="work",CancelWork=fault=="cancel"};manager.Register(1,optional,0);
            if(fault=="unavailable")Aram.IsAvailable=false;
            if(fault=="reserve")Aram.FreeBytes=ModelResidency.AramReserve+optional.PackageBytes-1;
            if(fault=="fragment")Aram.LargestFreeBlockBytes=optional.PackageBytes-1;
            if(fault=="slots")Aram.AllocationCount=248;
            if(fault=="budget")optional.PackageBytes=ModelResidency.ColdBudget+32;
            FinishStartup(manager,state,doors,2);
            check(optional.IsResident&&optional.Starts<=1&&manager.PendingSpecies<0,"an optional startup "+fault+" failure cannot trap loading in a retry loop");
            int starts=optional.Starts;FinishStartup(manager,state,doors,2);check(optional.Starts==starts,"a checkpoint never repeats its failed optional admission");
            if(fault=="work"||fault=="admission"){
                FinishStartup(manager,state,doors,3);check(optional.Starts==starts,"a new checkpoint preserves failure retry backoff");
            }
        }

        Clock.Timestamp=0;Aram.Reset();var pinnedState=State();var pinnedDoors=new PortalSystem();var pinned=new ModelResidency();
        pinned.Register(0,new Model(),0);var protectedModel=new Model();pinned.Register(1,protectedModel,0);
        check(Startup(pinned,pinnedState,pinnedDoors,2,true)&&protectedModel.Starts==0,"UV and water inspection pins prohibit startup paging without waiting for unloaded species");
        pinned.Enabled=false;check(Startup(pinned,pinnedState,pinnedDoors,3)&&protectedModel.Starts==0,"disabled residency keeps the partial cohort resident");
        pinned.Enabled=true;
        var crossing=pinnedDoors.Actors[0];crossing.Phase=DoorPhase.Waiting;crossing.Source=new ActorLocation(1,2,2,0);crossing.Destination=new ActorLocation(17,2,2,0);
        check(Startup(pinned,pinnedState,pinnedDoors,4)&&protectedModel.Starts==0,"startup cannot retire a species needed at the player's doorway destination");
        crossing.Phase=DoorPhase.Idle;pinnedState.Space=0;pinnedState.X=pinnedState.Z=80;var resident=pinnedState.Town.Residents[1];resident.Space=0;bool found=false;
        for(int z=24;z<137&&!found;z+=2)for(int x=24;x<137&&!found;x+=2){resident.PosX=x;resident.PosZ=z;found=ModelResidencyDemand.UsesSpace(pinnedState,pinnedDoors,1,0)&&!ModelResidencyDemand.DrawableUsesSpace(pinnedState,pinnedDoors,1,0);}
        check(found&&Startup(pinned,pinnedState,pinnedDoors,5)&&protectedModel.Starts==0,"startup preserves invisible species inside the twenty-four metre prefetch guard");

        Clock.Timestamp=0;Aram.Reset();var fullState=new GameState(-634240096){HousePlaced=true,Species=0,Space=0,X=18.5f,Z=40.5f};var fullDoors=new PortalSystem();var full=new ModelResidency();var required=new Model[6];
        fullState.Town.Residents[5].PosX=Town.Width-1;fullState.Town.Residents[5].PosZ=Town.Height-1;
        for(int i=0;i<6;i++){required[i]=new Model();full.Register(i,required[i],0);}
        FinishStartup(full,fullState,fullDoors,6);
        var expected=new ModelResidencyDemand();expected.Prepare(fullState,fullDoors);
        check(full.RequiredSpeciesCount==expected.Required.Count(x=>x)&&full.RequiredSpeciesCount<6,
            "the starting plot preloads its expanded visible cohort while pruning distant villagers");
        for(int i=0;i<6;i++)check(required[i].IsResident==expected.Required[i],"startup preserves nearby species and offloads distant packages");

        Clock.Timestamp=0;Aram.Reset();var guardState=State();var guardDoors=new PortalSystem();var guard=new ModelResidency();
        guard.Register(0,new Model(),0);var permanent=new Model();permanent.RetainPermanently();guard.Register(1,permanent,0);
        check(Startup(guard,guardState,guardDoors,2)&&permanent.Starts==0&&guard.Failures==0,"an unsupported compact model is excluded even if a future caller leaves it outside demand");
        Clock.Timestamp=ModelResidency.IdleDelayUs+1;guard.Require(guardState,guardDoors,Clock.Timestamp);Pump(guard,true);
        check(permanent.IsResident&&permanent.Starts==0&&guard.Failures==0,"normal idle eviction also rejects models without complete ARAM packages");
        var failed=new Model{FailRetention=true};bool rejected=false;
        try{ModelStartup.RetainSelected(4,4,failed);}catch(InvalidOperationException){rejected=true;}
        check(rejected&&failed.RetentionCalls==1&&failed.IsResident&&failed.SupportsAramResidency&&failed.InstanceCreations==0,
            "failed selected compaction leaves its original model owned and prevents premature instance creation");

        var budgeted=Setup(out var budgetModels,out var budgetState,out var budgetDoors);
        budgetModels[1].PendingDmaPolls=0;budgetModels[1].ProgressSteps=5;budgetModels[1].WorkUs=400;
        Pump(budgeted,true);long beforeBudget=Clock.Timestamp;
        budgeted.Pump(Clock.Timestamp,false,Clock.Timestamp+1000);
        check(budgetModels[1].Advances==2&&Clock.Timestamp-beforeBudget==800,"observed indivisible work cost prevents a predicted extra grant from exceeding frame credit");
        beforeBudget=Clock.Timestamp;budgeted.Pump(Clock.Timestamp,false,Clock.Timestamp+100);
        check(budgetModels[1].Advances==3&&Clock.Timestamp-beforeBudget==400,"the first bounded grant remains available even when its observed cost exceeds remaining credit");
        budgeted.Stop();
    }
}
