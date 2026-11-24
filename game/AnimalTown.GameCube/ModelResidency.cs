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
        =>species==selected?model.CreateInstance(ModelMemoryPlan.MaximumInstanceBytes):null;
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
    internal const long IdleDelayUs=5000000,WarmGraceUs=3000000,RetryDelayUs=1000000,PressureIdleDelayUs=1000000;
    private readonly Model?[] models=new Model?[ModelResidencyDemand.SpeciesCount];
    private readonly long[] lastRequired=new long[ModelResidencyDemand.SpeciesCount],warmUntil=new long[ModelResidencyDemand.SpeciesCount],retryAt=new long[ModelResidencyDemand.SpeciesCount];
    private readonly ModelResidencyDemand demand=new ModelResidencyDemand();
    // Visible / imminent / outer prefetch. Keep the earliest arrival ahead of
    // speculative work when several species need the single ARAM transfer owner.
    private readonly byte[] priority=new byte[ModelResidencyDemand.SpeciesCount];
    private readonly bool[] startupAttempted=new bool[ModelResidencyDemand.SpeciesCount];
    private readonly bool[] replacementLocked=new bool[ModelResidencyDemand.SpeciesCount];
    private readonly int[] warmPeak=new int[ModelResidencyDemand.SpeciesCount];
    internal VisualMemoryPressure? Memory;
    private int memoryTicket;
    private int startupCheckpoint=-1;
    private Task? pending;
    private CancellationTokenSource? cancellation;
    private int active=-1;
    private bool restoring,cancelRequested,hadBacking,stopped;
    private bool outdoors;
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
        int initialPeak=AddBytes(model.NativeBytes,ModelMemoryPlan.MaximumInstanceBytes);
        warmPeak[species]=AddBytes(initialPeak,ModelMemoryPlan.LoadGuardBytes);
    }
    internal void RecordWarmPeak(int species,int instanceBytes)
    {
        if(instanceBytes<0||!Ready(species))throw new InvalidOperationException("Record complete warm model ownership");
        int nativePeak=AddBytes(models[species]!.NativeBytes,instanceBytes);
        warmPeak[species]=AddBytes(nativePeak,ModelMemoryPlan.LoadGuardBytes);
    }
    internal int RestorePeak(int species)=>warmPeak[species];
    internal bool TryLockReplacement(int species)
    {
        if(stopped||replacementLocked[species]||!Ready(species))return false;
        replacementLocked[species]=true;return true;
    }
    internal void UnlockReplacement(int species)=>replacementLocked[species]=false;
    internal void Replace(int species,Model replacement,long now)
    {
        if(stopped||!replacementLocked[species]||!Ready(species)||!replacement.IsResident||replacement.IsTransitioning)throw new InvalidOperationException("Invalid LOD replacement boundary");
        replacement.AutomaticResidencyProgress=false;replacement.ResidencyBytesPerStep=8192;replacement.ResidencyWorkItemsPerStep=32;
        models[species]=replacement;lastRequired[species]=now;warmUntil[species]=now+WarmGraceUs;
        int initialPeak=AddBytes(replacement.NativeBytes,ModelMemoryPlan.MaximumInstanceBytes);
        warmPeak[species]=AddBytes(initialPeak,ModelMemoryPlan.LoadGuardBytes);
    }
    internal void RequireForView(GameState state,PortalSystem doors,long now,int waterInspection,bool pinAll=false,int targetSpace=-1)
    {
        if(waterInspection==0){Require(state,doors,now,pinAll,targetSpace);return;}
        // Draw temporarily moves the camera, then restores the player. Demand
        // between draws must use that same focus without moving the player.
        float x=state.X,z=state.Z;
        try{WaterInspection.Focus(state,waterInspection);Require(state,doors,now,pinAll,targetSpace);}
        finally{state.X=x;state.Z=z;}
    }
    internal void Require(GameState state,PortalSystem doors,long now,bool pinAll=false,int targetSpace=-1)
    {
        if(stopped)return;
        outdoors=state.Space==0;
        bool urgent=Memory?.WaitingPeakBytes>0;
        demand.Prepare(state,doors,pinAll||!Enabled,targetSpace,Memory?.UnderPressure==true,urgent);
        byte otherPriority=3;
        for(int i=0;i<models.Length;i++){
            if(demand.Required[i])lastRequired[i]=now;
            priority[i]=3;
            if(demand.Required[i]&&!Ready(i)){
                bool visible=ModelResidencyDemand.DrawableUsesSpace(state,doors,i,state.Space)||
                    demand.DestinationSpace>=0&&ModelResidencyDemand.DrawableUsesSpace(state,doors,i,demand.DestinationSpace);
                bool imminent=visible||ModelResidencyDemand.ImminentUsesSpace(state,doors,i,state.Space)||
                    demand.DestinationSpace>=0&&ModelResidencyDemand.ImminentUsesSpace(state,doors,i,demand.DestinationSpace);
                priority[i]=(byte)(visible?0:imminent?1:2);
                if(i!=active&&priority[i]<otherPriority)otherPriority=priority[i];
            }
        }
        if(active>=0&&!cancelRequested&&(!restoring&&demand.Required[active]||restoring&&(otherPriority<priority[active]||urgent&&!demand.Required[active]))){
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
        if(!CompletePending(now,deadline))return;
        if(RestoreRequired(now)){
            // A foreground restore denied by headroom must not prevent the
            // optional resident models that consume that headroom retiring.
            if(pending==null&&Memory?.UnderPressure==true)TryDemote(Candidate(now,false),now);
            return;
        }
        if(!Enabled){ReleaseWarmBacking();return;}
        if(allowDemotion||Memory?.UnderPressure==true)TryDemote(Candidate(now,false),now);
    }
    // Loading remains visible, but work still yields between bounded grants.
    // A checkpoint owns no disc load; it must settle its DMA before another
    // large package is admitted. Only startup bypasses the idle/grace timers.
    internal bool PrepareStartup(GameState state,PortalSystem doors,int checkpoint,long now,long deadline,bool pinAll=false)
    {
        if(stopped)return false;
        if(startupCheckpoint!=checkpoint){Array.Clear(startupAttempted,0,startupAttempted.Length);startupCheckpoint=checkpoint;}
        Require(state,doors,now,pinAll);
        if(Clock.Timestamp>=deadline||!CompletePending(now,deadline))return false;
        if(RestoreRequired(now)){
            if(pending==null&&Memory?.UnderPressure==true){int release=Candidate(now,true);if(release>=0){startupAttempted[release]=true;TryDemote(release,now);}}
            return false;
        }
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
            catch(Exception error){failed=true;Failures++;retryAt[species]=now+RetryDelayUs;Memory?.AllocationFailed(warmPeak[species],now);DebugOverlay.WriteLine("ANIMALTOWN MODEL RESIDENCY RETRY "+species+": "+error.Message);}
            cancellation!.Dispose();cancellation=null;pending=null;active=-1;cancelRequested=false;
            if(memoryTicket!=0){if(wasRestore&&model.IsResident)Memory!.Complete(memoryTicket);else Memory!.Cancel(memoryTicket);memoryTicket=0;}
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
        for(int rank=0;rank<3;rank++)for(int i=0;i<models.Length;i++)if(models[i]!=null&&demand.Required[i]&&!Ready(i)&&priority[i]==rank){
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
        bool pressure=Memory?.UnderPressure==true,urgent=Memory?.WaitingPeakBytes>0;
        long idleDelay=pressure?PressureIdleDelayUs:outdoors?0:IdleDelayUs;
        // A low watermark alone must not churn models as the camera reverses
        // at its smaller guard. Only an actual waiting essential reservation
        // may reclaim a freshly restored or recently required optional model.
        for(int i=0;i<models.Length;i++)if(models[i]!=null&&!replacementLocked[i]&&!demand.Required[i]&&models[i]!.SupportsAramResidency&&Ready(i)&&now>=retryAt[i]&&
            (startup?!startupAttempted[i]:urgent||now-lastRequired[i]>=idleDelay&&now>=warmUntil[i])&&
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
            if(memoryTicket!=0){if(restoring&&model.IsResident)Memory!.Complete(memoryTicket);else Memory!.Cancel(memoryTicket);memoryTicket=0;}
        }
    }
    private void Start(int species,bool restore,long now)
    {
        long at=Clock.Timestamp;
        // The package plus decoded native texture storage and all instance
        // skin outputs can overlap during a restore. Reserve the whole bound
        // before the asynchronous owner starts; optional lookahead cannot
        // bypass global pressure. Visible and imminent arrivals may use real
        // headroom before their first drawable frame; outer prefetch cannot.
        if(restore&&Memory!=null){
            int estimate=warmPeak[species];
            if(!Memory.TryReserve(estimate,now,priority[species]<=1,out memoryTicket))return;
        }
        var token=new CancellationTokenSource();
        try{
            hadBacking=models[species]!.AramBytes>0;
            var task=restore?models[species]!.PrepareAsync(token.Token):models[species]!.UnloadToAramAsync(token.Token);
            active=species;restoring=restore;cancelRequested=false;cancellation=token;pending=task;
        }catch(Exception error){token.Dispose();if(memoryTicket!=0){Memory!.Cancel(memoryTicket);memoryTicket=0;}Failures++;retryAt[species]=now+RetryDelayUs;Memory?.AllocationFailed(warmPeak[species],now);DebugOverlay.WriteLine("ANIMALTOWN MODEL RESIDENCY ADMISSION "+species+": "+error.Message);}
        int elapsed=(int)(Clock.Timestamp-at);AdmissionUs+=elapsed;if(elapsed>MaxAdmissionUs)MaxAdmissionUs=elapsed;
    }
    private static int Round(int bytes)=>bytes==0?0:(bytes+31)&~31;
    internal static int AddBytes(int first,int second)
    {
        if(first<0||second<0||first>int.MaxValue-second)throw new InvalidOperationException("Native allocation estimate exceeds its bounded range");
        return first+second;
    }
}
