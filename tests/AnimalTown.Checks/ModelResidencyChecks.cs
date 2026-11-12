using AnimalTown;
using Dolphin.Graphics;
using Dolphin.Memory;
using Dolphin.Timing;

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
    // Tasks deliberately complete only through explicit advancement, matching
    // native ownership/polling instead of making every async call succeed inline.
    public sealed class Model
    {
        public bool AutomaticResidencyProgress=true;
        public int ResidencyBytesPerStep,ResidencyWorkItemsPerStep;
        public int PackageBytes {get;set;}=65536;
        public bool IsResident {get;private set;}=true;
        public bool IsTransitioning=>completion!=null;
        public int AramBytes {get;private set;}
        public int NativeBytes=>IsResident?PackageBytes*2:128;
        public bool FailAdmission,FailWork,CommitCancellation;
        public int Starts,Advances,AnimationPosition=37;
        private TaskCompletionSource<int>? completion;
        private CancellationToken cancellation;
        private bool restore;
        private int step;
        public Task PrepareAsync(CancellationToken token)=>Start(true,token);
        public Task UnloadToAramAsync(CancellationToken token)=>Start(false,token);
        private Task Start(bool warming,CancellationToken token)
        {
            if(FailAdmission){FailAdmission=false;throw new OutOfMemoryException("Injected admission failure");}
            if(IsTransitioning)throw new InvalidOperationException("Overlapping model owners");
            Starts++;restore=warming;cancellation=token;step=0;completion=new TaskCompletionSource<int>();return completion.Task;
        }
        public bool AdvanceResidency()
        {
            if(completion==null)return false;
            Clock.Timestamp+=7;Advances++;
            if(++step==1)return false; // Native DMA is pending; owner must not spin.
            var done=completion;completion=null;
            if(cancellation.IsCancellationRequested&&!CommitCancellation){done.SetCanceled();return true;}
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
    internal static void Run(Action<bool,string> check)
    {
        var manager=Setup(out var models,out var state,out var doors);
        check(models.All(x=>!x.AutomaticResidencyProgress&&x.ResidencyBytesPerStep==65536),"application grants bounded manual model work");
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
    }
}
