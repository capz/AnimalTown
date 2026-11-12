using System;
using System.Threading;
using System.Threading.Tasks;
using AnimalTown;
using Dolphin.Diagnostics;
using Dolphin.Graphics;
using Dolphin.Memory;
using Dolphin.Timing;

// One owner drives whole shared models outside render frames. ARAM backing is
// counted even after a restore, and cannot consume terrain/audio reservations.
internal sealed class ModelResidency
{
    internal const int ColdBudget=4*1024*1024,AramReserve=512*1024;
    internal const long IdleDelayUs=5000000,WarmGraceUs=3000000,RetryDelayUs=1000000;
    private readonly Model?[] models=new Model?[ModelResidencyDemand.SpeciesCount];
    private readonly long[] lastRequired=new long[ModelResidencyDemand.SpeciesCount],warmUntil=new long[ModelResidencyDemand.SpeciesCount],retryAt=new long[ModelResidencyDemand.SpeciesCount];
    private readonly ModelResidencyDemand demand=new ModelResidencyDemand();
    private readonly bool[] urgent=new bool[ModelResidencyDemand.SpeciesCount];
    private Task? pending;
    private CancellationTokenSource? cancellation;
    private int active=-1;
    private bool restoring,cancelRequested,hadBacking;
    internal bool Enabled=true;
    internal int Demotions,Restores,Cancellations,Failures,BackingReleases;
    internal int WorkSteps,MaxAdvanceUs,MaxAdmissionUs,ReadinessWaits,PortalWaits;
    internal long AdvanceUs,AdmissionUs,SnapshotBytes,RestoredBytes;
    internal int PendingSpecies=>active;
    internal void WaitForScene()=>ReadinessWaits++;
    internal void WaitForPortal()=>PortalWaits++;
    internal bool Required(int species)=>demand.Required[species];
    internal int ColdBytes {get{int bytes=0;for(int i=0;i<models.Length;i++)if(models[i]!=null)bytes+=Round(models[i]!.AramBytes);return bytes;}}
    internal bool AllReady {get{for(int i=0;i<models.Length;i++)if(!Ready(i))return false;return true;}}
    internal void Register(int species,Model model,long now)
    {
        if(species<0||species>=models.Length||models[species]!=null)throw new InvalidOperationException("Invalid species residency registration");
        models[species]=model;lastRequired[species]=now;warmUntil[species]=now+WarmGraceUs;
        model.AutomaticResidencyProgress=false;
        model.ResidencyBytesPerStep=65536;model.ResidencyWorkItemsPerStep=256;
    }
    internal void Require(GameState state,PortalSystem doors,long now,bool pinAll=false,int targetSpace=-1)
    {
        demand.Prepare(state,doors,pinAll||!Enabled,targetSpace);
        bool otherUrgent=false;
        for(int i=0;i<models.Length;i++){
            if(demand.Required[i])lastRequired[i]=now;
            bool ready=Ready(i);
            urgent[i]=!ready&&(ModelResidencyDemand.DrawableUsesSpace(state,doors,i,state.Space)||
                demand.DestinationSpace>=0&&ModelResidencyDemand.DrawableUsesSpace(state,doors,i,demand.DestinationSpace));
            if(i!=active&&urgent[i])otherUrgent=true;
        }
        if(active>=0&&!cancelRequested&&(!restoring&&demand.Required[active]||restoring&&!urgent[active]&&otherUrgent)){
            cancelRequested=true;Cancellations++;cancellation!.Cancel();
        }
    }
    internal bool Ready(int species)
    {
        var model=models[species];return model!=null&&active!=species&&!model.IsTransitioning&&model.IsResident;
    }
    internal bool ReadyForSpace(GameState state,PortalSystem doors,int space)
    {
        for(int i=0;i<models.Length;i++)if(!Ready(i)&&ModelResidencyDemand.DrawableUsesSpace(state,doors,i,space))return false;
        return true;
    }
    internal void Pump(long now,bool allowDemotion,long deadline)
    {
        if(Clock.Timestamp>=deadline)return;
        // A false step is waiting on native DMA/resources; never busy-poll it.
        if(pending!=null){
            var model=models[active]!;
            for(int steps=0;steps<64&&!pending.IsCompleted&&Clock.Timestamp<deadline;steps++){
                long at=Clock.Timestamp;bool progressed=model.AdvanceResidency();
                int elapsed=(int)(Clock.Timestamp-at);AdvanceUs+=elapsed;WorkSteps++;if(elapsed>MaxAdvanceUs)MaxAdvanceUs=elapsed;
                if(!progressed)break;
            }
            if(!pending.IsCompleted)return;
            int species=active;bool wasRestore=restoring;bool failed=false;
            try{pending.GetAwaiter().GetResult();}
            catch(OperationCanceledException){ }
            catch(Exception error){failed=true;Failures++;retryAt[species]=now+RetryDelayUs;DebugOverlay.WriteLine("ANIMALTOWN MODEL RESIDENCY RETRY "+species+": "+error.Message);}
            cancellation!.Dispose();cancellation=null;pending=null;active=-1;cancelRequested=false;
            if(model.IsResident){
                warmUntil[species]=now+WarmGraceUs;
                if(wasRestore&&!failed){Restores++;RestoredBytes+=model.PackageBytes;DebugOverlay.WriteLine("ANIMALTOWN MODEL WARM "+species+" aram bytes "+model.AramBytes+" max advance us "+MaxAdvanceUs+" readiness waits "+ReadinessWaits+" portal wait ticks "+PortalWaits);}
            }else if(!failed&&!wasRestore){Demotions++;if(!hadBacking)SnapshotBytes+=model.PackageBytes;DebugOverlay.WriteLine("ANIMALTOWN MODEL COLD "+species+" aram bytes "+model.AramBytes+" native budget bytes "+model.NativeBytes+" max advance us "+MaxAdvanceUs);}
        }
        // Demand always precedes optional demotion, including a disable request.
        bool missing=false;
        for(int priority=0;priority<2;priority++)for(int i=0;i<models.Length;i++)if(models[i]!=null&&demand.Required[i]&&!Ready(i)&&urgent[i]==(priority==0)){
            missing=true;
            if(now>=retryAt[i]&&!models[i]!.IsTransitioning){Start(i,true,now);return;}
        }
        if(missing)return;
        if(!Enabled){
            for(int i=0;i<models.Length;i++)if(Ready(i)&&models[i]!.AramBytes>0){models[i]!.ReleaseAramBacking();BackingReleases++;return;}
            return;
        }
        if(!allowDemotion)return;
        int candidate=-1;
        for(int i=0;i<models.Length;i++)if(models[i]!=null&&!demand.Required[i]&&Ready(i)&&now-lastRequired[i]>=IdleDelayUs&&now>=warmUntil[i]&&now>=retryAt[i]&&
            (candidate<0||lastRequired[i]<lastRequired[candidate]))candidate=i;
        if(candidate<0)return;
        var source=models[candidate]!;int bytes=Round(source.PackageBytes);
        if(bytes<1||bytes>ColdBudget){retryAt[candidate]=now+IdleDelayUs;return;}
        if(source.AramBytes==0){
            int retained=ColdBytes;
            if(bytes>ColdBudget-retained){
                // Drop one hot recoverable snapshot, never a cold model's only copy.
                for(int i=0;i<models.Length;i++)if(i!=candidate&&Ready(i)&&models[i]!.AramBytes>0){models[i]!.ReleaseAramBacking();BackingReleases++;return;}
                retryAt[candidate]=now+RetryDelayUs;return;
            }
            if(!Aram.IsAvailable){retryAt[candidate]=now+IdleDelayUs;return;}
            var stats=Aram.Statistics;
            if(stats.FreeBytes<bytes+AramReserve||stats.LargestFreeBlockBytes<bytes||stats.AllocationCount>=248){retryAt[candidate]=now+RetryDelayUs;return;}
        }
        Start(candidate,false,now);
    }
    private void Start(int species,bool restore,long now)
    {
        long at=Clock.Timestamp;
        var token=new CancellationTokenSource();
        try{
            hadBacking=models[species]!.AramBytes>0;
            var task=restore?models[species]!.PrepareAsync(token.Token):models[species]!.UnloadToAramAsync(token.Token);
            active=species;restoring=restore;cancelRequested=false;cancellation=token;pending=task;
        }catch(Exception error){token.Dispose();Failures++;retryAt[species]=now+RetryDelayUs;DebugOverlay.WriteLine("ANIMALTOWN MODEL RESIDENCY ADMISSION "+species+": "+error.Message);}
        int elapsed=(int)(Clock.Timestamp-at);AdmissionUs+=elapsed;if(elapsed>MaxAdmissionUs)MaxAdmissionUs=elapsed;
    }
    private static int Round(int bytes)=>bytes==0?0:(bytes+31)&~31;
}
