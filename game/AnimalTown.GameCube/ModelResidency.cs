using System;
using System.Threading;
using System.Threading.Tasks;
using AnimalTown;
using Dolphin.Diagnostics;
using Dolphin.Graphics;
using Dolphin.Memory;
using Dolphin.Timing;

internal static class ModelStartup
{
    internal static int SpeciesAt(int selected,int progress)
    {
        if(selected<0||selected>=ModelResidencyDemand.SpeciesCount||progress<0||progress>=ModelResidencyDemand.SpeciesCount)
            throw new ArgumentOutOfRangeException("species");
        if(progress==0)return selected;
        int species=progress-1;return species>=selected?species+1:species;
    }
    internal static ModelInstance? ReservePlayer(int selected,int species,Model model)
        =>species==selected?model.CreateInstance(512*1024):null;
    internal static void RetainSelected(int selected,int species,Model model)
    {
        if(species!=selected)return;
        // The selected shared model stays pinned until this town is disposed.
        // Retain before either instance exists; pageable species keep their
        // complete immutable package for the SDK's ARAM snapshot/restore path.
        int result=NativeModels.Retain(model);
        if(result!=1)throw new InvalidOperationException("Selected model retention failed: "+result);
    }
}

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
    private readonly bool[] startupAttempted=new bool[ModelResidencyDemand.SpeciesCount];
    private int startupCheckpoint=-1;
    private Task? pending;
    private CancellationTokenSource? cancellation;
    private int active=-1;
    private bool restoring,cancelRequested,hadBacking,stopped;
    internal bool Enabled=true;
    internal int Demotions,Restores,Cancellations,Failures,BackingReleases;
    internal int WorkSteps,MaxAdvanceUs,MaxAdmissionUs,ReadinessWaits,PortalWaits;
    internal long AdvanceUs,AdmissionUs,SnapshotBytes,RestoredBytes;
    internal int PendingSpecies=>active;
    internal int RequiredSpeciesCount {get{int count=0;for(int i=0;i<demand.Required.Length;i++)if(demand.Required[i])count++;return count;}}
    internal void WaitForScene()=>ReadinessWaits++;
    internal void WaitForPortal()=>PortalWaits++;
    internal bool Required(int species)=>demand.Required[species];
    internal int ColdBytes {get{int bytes=0;for(int i=0;i<models.Length;i++)if(models[i]!=null)bytes+=Round(models[i]!.AramBytes);return bytes;}}
    internal bool AllReady {get{for(int i=0;i<models.Length;i++)if(!Ready(i))return false;return true;}}
    internal void Register(int species,Model model,long now)
    {
        if(stopped||species<0||species>=models.Length||models[species]!=null)throw new InvalidOperationException("Invalid species residency registration");
        models[species]=model;lastRequired[species]=now;warmUntil[species]=now+WarmGraceUs;
        model.AutomaticResidencyProgress=false;
        model.ResidencyBytesPerStep=8192;model.ResidencyWorkItemsPerStep=32;
    }
    internal void Require(GameState state,PortalSystem doors,long now,bool pinAll=false,int targetSpace=-1)
    {
        if(stopped)return;
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
        if(stopped||Clock.Timestamp>=deadline)return;
        if(!CompletePending(now,deadline)||RestoreRequired(now))return;
        if(!Enabled){ReleaseWarmBacking();return;}
        if(allowDemotion)TryDemote(Candidate(now,false),now);
    }
    // Loading remains visible, but work still yields between bounded grants.
    // A checkpoint owns no disc load; it must settle its DMA before another
    // large package is admitted. Only startup bypasses the idle/grace timers.
    internal bool PrepareStartup(GameState state,PortalSystem doors,int checkpoint,long now,long deadline,bool pinAll=false)
    {
        if(stopped)return false;
        if(startupCheckpoint!=checkpoint){Array.Clear(startupAttempted,0,startupAttempted.Length);startupCheckpoint=checkpoint;}
        Require(state,doors,now,pinAll);
        if(Clock.Timestamp>=deadline||!CompletePending(now,deadline)||RestoreRequired(now))return false;
        if(!Enabled){ReleaseWarmBacking();return true;}
        int candidate;
        while((candidate=Candidate(now,true))>=0){
            // Optional failure, cancellation or exhausted ARAM must not trap
            // this checkpoint in a retry loop. A later load may try again.
            startupAttempted[candidate]=true;
            if(TryDemote(candidate,now))return false;
            if(Clock.Timestamp>=deadline)return false;
        }
        return true;
    }
    private bool CompletePending(long now,long deadline)
    {
        // A false step is waiting on native DMA/resources; never busy-poll it.
        if(pending!=null){
            var model=models[active]!;
            for(int steps=0;steps<64&&!pending.IsCompleted&&Clock.Timestamp<deadline;steps++){
                // Always permit the first grant so an expensive allocation or
                // urgent restore cannot starve. Further grants use the observed
                // indivisible cost instead of knowingly overrunning the credit.
                if(steps>0&&MaxAdvanceUs>deadline-Clock.Timestamp)break;
                long at=Clock.Timestamp;bool progressed=model.AdvanceResidency();
                int elapsed=(int)(Clock.Timestamp-at);AdvanceUs+=elapsed;WorkSteps++;if(elapsed>MaxAdvanceUs)MaxAdvanceUs=elapsed;
                if(!progressed)break;
            }
            if(!pending.IsCompleted)return false;
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
        return true;
    }
    private bool RestoreRequired(long now)
    {
        // Demand always precedes optional demotion, including a disable request.
        bool missing=false;
        for(int priority=0;priority<2;priority++)for(int i=0;i<models.Length;i++)if(models[i]!=null&&demand.Required[i]&&!Ready(i)&&urgent[i]==(priority==0)){
            missing=true;
            if(now>=retryAt[i]&&!models[i]!.IsTransitioning){Start(i,true,now);return true;}
        }
        return missing;
    }
    private void ReleaseWarmBacking()
    {
        for(int i=0;i<models.Length;i++)if(Ready(i)&&models[i]!.AramBytes>0){models[i]!.ReleaseAramBacking();BackingReleases++;return;}
    }
    private int Candidate(long now,bool startup)
    {
        int candidate=-1;
        for(int i=0;i<models.Length;i++)if(models[i]!=null&&!demand.Required[i]&&models[i]!.SupportsAramResidency&&Ready(i)&&now>=retryAt[i]&&
            (startup?!startupAttempted[i]:now-lastRequired[i]>=IdleDelayUs&&now>=warmUntil[i])&&
            (candidate<0||lastRequired[i]<lastRequired[candidate]))candidate=i;
        return candidate;
    }
    private bool TryDemote(int candidate,long now)
    {
        if(candidate<0)return false;
        var source=models[candidate]!;int bytes=Round(source.PackageBytes);
        if(bytes<1||bytes>ColdBudget){retryAt[candidate]=now+IdleDelayUs;return false;}
        if(source.AramBytes==0){
            int retained=ColdBytes;
            if(bytes>ColdBudget-retained){
                // Drop one hot recoverable snapshot, never a cold model's only copy.
                for(int i=0;i<models.Length;i++)if(i!=candidate&&Ready(i)&&models[i]!.AramBytes>0){models[i]!.ReleaseAramBacking();BackingReleases++;return false;}
                retryAt[candidate]=now+RetryDelayUs;return false;
            }
            if(!Aram.IsAvailable){retryAt[candidate]=now+IdleDelayUs;return false;}
            var stats=Aram.Statistics;
            if(stats.FreeBytes<bytes+AramReserve||stats.LargestFreeBlockBytes<bytes||stats.AllocationCount>=248){retryAt[candidate]=now+RetryDelayUs;return false;}
        }
        Start(candidate,false,now);
        return pending!=null;
    }
    // Session teardown is an explicit blocking boundary, after EndFrame. A
    // manually driven model must reach its terminal state before its instances
    // and shared package are disposed. Poll only our owner; normal Pump could
    // admit a replacement restore or demotion while the session is unwinding.
    internal void Stop()
    {
        stopped=true;
        if(pending==null)return;
        if(!pending.IsCompleted&&!cancelRequested){cancelRequested=true;Cancellations++;cancellation!.Cancel();}
        var model=models[active]!;
        while(!pending.IsCompleted)model.AdvanceResidency();
        try{pending.GetAwaiter().GetResult();}
        catch(OperationCanceledException){ }
        catch(Exception error){Failures++;DebugOverlay.WriteLine("ANIMALTOWN MODEL RESIDENCY SHUTDOWN: "+error.Message);}
        finally{
            cancellation!.Dispose();cancellation=null;pending=null;active=-1;cancelRequested=false;
        }
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
