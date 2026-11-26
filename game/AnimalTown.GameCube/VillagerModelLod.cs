using System;
using System.Threading;
using System.Threading.Tasks;
using AnimalTown;
using Dolphin.Diagnostics;
using Dolphin.Graphics;
using Dolphin.Timing;

// One shared species variant at a time. The selected player's species stays
// full detail. A replacement is published only between frames, after all of
// its native data and every NPC instance in the shared species cohort exist;
// only then are the old model and all of its instances retired.
internal sealed class VillagerModelLod : IDisposable
{
    internal const int InstanceBudget=ModelMemoryPlan.MaximumInstanceBytes;
    internal const int LoadGuardBytes=64*1024;
    private readonly Model[] models;
    private readonly ModelInstance?[] actors;
    private readonly ModelResidency residency;
    private readonly int[] current=new int[6],wanted=new int[6];
    private readonly bool[] essential=new bool[6];
    private readonly bool[] noBenefit=new bool[6];
    private readonly int[] fullSharedPeak=new int[6],fullInstancePeak=new int[6];
    private readonly ModelInstance?[] candidates,retired;
    private readonly long[] retryAt=new long[6];
    private Task<Model>? pending;private CancellationTokenSource? cancellation;
    private int active=-1,loadingLod,ticket,changed,reservedPeak,plannedInstances;
    private Model? loadingSource;
    private bool stopped;
    internal bool Paused;
    internal int OnlineSpeciesMask=0;
    internal VisualMemoryPressure? Memory;
    internal int Changes,Failures;
    internal int PendingSpecies=>active;
    internal bool FullReady {get{if(pending!=null)return false;for(int i=0;i<current.Length;i++)if(current[i]!=0)return false;return true;}}
    internal bool FullForSpace(GameState state,PortalSystem doors,int space)
    {if(space==0)return true;for(int i=0;i<current.Length;i++)if(current[i]!=0&&ModelResidencyDemand.UsesSpace(state,doors,i,space))return false;return true;}
    internal VillagerModelLod(Model[] models,ModelInstance?[] actors,ModelResidency residency)
    {this.models=models;this.actors=actors;this.residency=residency;candidates=new ModelInstance?[actors.Length];retired=new ModelInstance?[actors.Length];}
    internal int Level(int species)=>current[species];
    internal int ReplacementPeak(int species)
    {
        // The coarse cooker preserves every non-geometry section byte for
        // byte and only removes vertices/indices. Its allocation ceiling is
        // therefore the observed full package/instance, plus one package for
        // transient parsing and a fixed aligned-loader/job guard.
        var model=models[species];if(model==null||!residency.Ready(species))return 0;
        int count=0,instancePeak=0;
        for(int actor=1;actor<actors.Length;actor++)if(actors[actor] is ModelInstance instance&&instance.Model==model){
            count++;instancePeak=Math.Max(instancePeak,instance.NativeBytes);
        }
        if(count==0)return 0;
        if(current[species]==0){
            int shared=ModelResidency.AddBytes(model.NativeBytes,model.PackageBytes);
            fullSharedPeak[species]=ModelResidency.AddBytes(shared,LoadGuardBytes);
            fullInstancePeak[species]=Math.Max(fullInstancePeak[species],instancePeak);
        }
        int peak=fullSharedPeak[species];if(peak==0)return 0;
        for(int i=0;i<count;i++)peak=ModelResidency.AddBytes(peak,fullInstancePeak[species]);
        return peak;
    }
    internal int ConsumeChangedActors(){int result=changed;changed=0;return result;}
    internal void Require(GameState state,ActorFrameVisibility visibility,bool pinFull=false,PortalSystem? doors=null)
    {
        for(int species=0;species<6;species++){
            bool indoorRequired=NeedsFullIndoor(state,doors,species);
            essential[species]=pinFull||indoorRequired||(OnlineSpeciesMask&(1<<species))!=0;
            bool playerModel=actors[0]!=null&&actors[0]!.Model==models[species];
            if(species==state.Species||playerModel||essential[species])wanted[species]=0;
            // An invisible species keeps its existing variant and follows the
            // ordinary ARAM eviction owner; no speculative dual-cohort load.
            else{
                bool visible=false;int lod=1;
                for(int npc=0;npc<Town.NpcActorCount&&npc+1<visibility.Visible.Length;npc++){
                    if(!state.NpcPresent(npc)||state.NpcSpecies(npc)!=species||!visibility.Visible[npc+1])continue;
                    visible=true;if(visibility.GeometryLod[npc+1]==0)lod=0;
                }
                wanted[species]=visible?(noBenefit[species]||!VillagerLod.CoarseAvailable(species)?0:lod):current[species];
            }
        }
        if(pending!=null&&(Paused||Memory?.UnderPressure==true&&!essential[active]||wanted[active]!=loadingLod))cancellation!.Cancel();
    }
    private static bool NeedsFullIndoor(GameState state,PortalSystem? doors,int species)
    {
        if(doors==null){
            if(state.Space==0)return false;
            for(int npc=0;npc<Town.NpcActorCount;npc++)if(state.NpcPresent(npc)&&state.NpcSpecies(npc)==species&&state.Town.NpcAt(npc).Space==state.Space)return true;
            return false;
        }
        if(state.Space!=0&&ModelResidencyDemand.UsesSpace(state,doors,species,state.Space))return true;
        if(!doors.Busy(0))return false;
        var crossing=doors.Actors[0];
        return crossing.Source.Space.Value!=0&&ModelResidencyDemand.UsesSpace(state,doors,species,crossing.Source.Space.Value)||
            crossing.Destination.Space.Value!=0&&ModelResidencyDemand.UsesSpace(state,doors,species,crossing.Destination.Space.Value);
    }
    internal void Pump(long now,long deadline)
    {
        if(stopped||Clock.Timestamp>=deadline)return;
        if(Paused&&pending==null)return;
        if(pending!=null){
            if(!pending.IsCompleted)return;
            Model? replacement=null;
            try{
                replacement=pending.GetAwaiter().GetResult();
                if(Paused||Memory?.UnderPressure==true&&!essential[active]||wanted[active]!=loadingLod){replacement.Dispose();replacement=null;return;}
                var oldModel=models[active];int count=0;
                for(int actor=1;actor<actors.Length;actor++)if(actors[actor]!=null&&actors[actor]!.Model==oldModel)count++;
                if(oldModel!=loadingSource||count!=plannedInstances){replacement.Dispose();replacement=null;return;}
                int oldInstanceBytes=0,newInstanceBytes=0,changedNow=0;
                for(int actor=1;actor<actors.Length;actor++)if(actors[actor] is ModelInstance oldInstance&&oldInstance.Model==oldModel){
                    candidates[actor]=replacement.CreateInstance(InstanceBudget);retired[actor]=oldInstance;
                    oldInstanceBytes=ModelResidency.AddBytes(oldInstanceBytes,oldInstance.NativeBytes);
                    newInstanceBytes=ModelResidency.AddBytes(newInstanceBytes,candidates[actor]!.NativeBytes);changedNow|=1<<actor;
                }
                // Count immutable geometry as well as skin outputs. Unskinned
                // villagers have equally small full/coarse instances while
                // their model geometry still saves useful submission work.
                int oldBytes=ModelResidency.AddBytes(oldModel.NativeBytes,oldInstanceBytes),newBytes=ModelResidency.AddBytes(replacement.NativeBytes,newInstanceBytes);
                if(loadingLod==1&&oldBytes-newBytes<1024){
                    noBenefit[active]=true;wanted[active]=0;ReleaseCandidates();replacement.Dispose();replacement=null;return;
                }
                residency.Replace(active,replacement,now);
                residency.RecordWarmPeak(active,newInstanceBytes);
                models[active]=replacement;replacement=null;
                for(int actor=1;actor<actors.Length;actor++)if(candidates[actor]!=null){actors[actor]=candidates[actor];candidates[actor]=null;}
                current[active]=loadingLod;changed|=changedNow;Changes++;
                if(ticket!=0){Memory!.Complete(ticket);ticket=0;}
                for(int actor=1;actor<retired.Length;actor++){retired[actor]?.Dispose();retired[actor]=null;}
                oldModel.Dispose();
            }catch(OperationCanceledException){ReleaseCandidates();replacement?.Dispose();retryAt[active]=now+1000000;}
            catch(Exception error){
                ReleaseCandidates();replacement?.Dispose();Failures++;retryAt[active]=now+1000000;Memory?.AllocationFailed(reservedPeak,now);
                DebugOverlay.WriteLine("ANIMALTOWN LOD RETRY "+active+": "+error.Message);
            }finally{
                residency.UnlockReplacement(active);
                if(ticket!=0){Memory!.Cancel(ticket);ticket=0;}
                Array.Clear(retired,0,retired.Length);pending=null;cancellation!.Dispose();cancellation=null;active=-1;loadingSource=null;
            }
            return;
        }
        for(int species=0;species<6;species++)if(models[species]!=null&&current[species]!=wanted[species]&&now>=retryAt[species]){
            reservedPeak=ReplacementPeak(species);if(reservedPeak==0)continue;
            if(Memory!=null&&!Memory.TryReserve(reservedPeak,now,essential[species],out ticket))continue;
            if(!residency.TryLockReplacement(species)){if(ticket!=0){Memory!.Cancel(ticket);ticket=0;}continue;}
            loadingSource=models[species];plannedInstances=0;
            for(int actor=1;actor<actors.Length;actor++)if(actors[actor]!=null&&actors[actor]!.Model==loadingSource)plannedInstances++;
            active=species;loadingLod=wanted[species];cancellation=new CancellationTokenSource();
            try{pending=ModelResources.LoadLodAsync(Name(species),loadingLod,cancellation.Token);}
            catch(Exception error){
                Failures++;retryAt[species]=now+1000000;Memory?.AllocationFailed(reservedPeak,now);residency.UnlockReplacement(species);cancellation.Dispose();cancellation=null;active=-1;loadingSource=null;
                if(ticket!=0){Memory!.Cancel(ticket);ticket=0;}
                DebugOverlay.WriteLine("ANIMALTOWN LOD ADMISSION "+species+": "+error.Message);
            }
            return;
        }
    }
    private void ReleaseCandidates()
    {for(int actor=1;actor<candidates.Length;actor++){candidates[actor]?.Dispose();candidates[actor]=null;}}
    public void Dispose()
    {
        stopped=true;if(pending==null)return;
        // Session teardown is a blocking boundary. Do not drop a reservation,
        // its cancellation source, or an unadopted native model before drain.
        cancellation!.Cancel();
        try{pending.GetAwaiter().GetResult().Dispose();}catch(OperationCanceledException){}catch(Exception error){DebugOverlay.WriteLine("ANIMALTOWN LOD SHUTDOWN: "+error.Message);}
        finally{
            residency.UnlockReplacement(active);if(ticket!=0){Memory!.Cancel(ticket);ticket=0;}
            pending=null;cancellation.Dispose();cancellation=null;active=-1;loadingSource=null;
        }
    }
    private static string Name(int species)=>species switch{0=>"fox",1=>"bunny",2=>"giraffe",3=>"cat",4=>"hamster",_=>"spaniel"};
}
