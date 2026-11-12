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
    public sealed class ModelInstance
    {
        public readonly Model Model;
        public readonly int Budget;
        public ModelInstance(Model model,int budget){Model=model;Budget=budget;}
    }
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
        public bool FailAdmission,FailWork,CancelWork,CommitCancellation;
        public int InstanceCreations;
        public int Starts,Advances,AnimationPosition=37,PendingDmaPolls=1;
        public Task? LastTransition {get;private set;}
        private TaskCompletionSource<int>? completion;
        private CancellationToken cancellation;
        private bool restore;
        private int step;
        public ModelInstance CreateInstance(int budget)
        {
            if(!IsResident||IsTransitioning)throw new InvalidOperationException("A cold or transitioning model cannot create an instance");
            InstanceCreations++;return new ModelInstance(this,budget);
        }
        public Task PrepareAsync(CancellationToken token)=>Start(true,token);
        public Task UnloadToAramAsync(CancellationToken token)=>Start(false,token);
        private Task Start(bool warming,CancellationToken token)
        {
            if(FailAdmission){FailAdmission=false;throw new OutOfMemoryException("Injected admission failure");}
            if(IsTransitioning)throw new InvalidOperationException("Overlapping model owners");
            Starts++;restore=warming;cancellation=token;step=0;completion=new TaskCompletionSource<int>();LastTransition=completion.Task;return completion.Task;
        }
        public bool AdvanceResidency()
        {
            if(completion==null)return false;
            Clock.Timestamp+=7;Advances++;
            if(++step<=PendingDmaPolls)return false; // Native DMA is pending; ordinary frame work must not spin.
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

        StartupChecks(check);
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
                var model=cohort[species]=new Model();manager.Register(species,model,Clock.Timestamp);
                _=model.CreateInstance(512*1024); // NPC adoption precedes paging.
                var reserved=ModelStartup.ReservePlayer(selected,species,model);if(reserved!=null)player=reserved;
                if(progress==0)check(player!=null&&player.Budget==512*1024&&!manager.AllReady,"player reservation does not wait for the unregistered cohort");
            }
            FinishStartup(manager,state,doors,6);
            check(player!=null&&cohort[selected].InstanceCreations==2&&cohort.Where((_,i)=>i!=selected).All(x=>x.InstanceCreations==1),
                "startup creates exactly seven instances and never repeats final player allocation");
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
        for(int i=0;i<6;i++){required[i]=new Model();full.Register(i,required[i],0);}
        check(Startup(full,fullState,fullDoors,6)&&full.RequiredSpeciesCount==6&&required.All(x=>x.IsResident&&x.Starts==0),
            "the actual starting plot reports all six guarded species and never evicts one to hide a full working-set limit");
    }
}
