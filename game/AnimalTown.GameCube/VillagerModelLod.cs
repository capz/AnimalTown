using System;
using System.Threading;
using System.Threading.Tasks;
using AnimalTown;
using Dolphin.Diagnostics;
using Dolphin.Graphics;
using Dolphin.Timing;

// One shared species variant at a time. The selected player's species stays
// full detail. A replacement is published only between frames, after all of
// its native data and its NPC instance exist; the old owner is then retired.
internal sealed class VillagerModelLod : IDisposable
{
    internal const int InstanceBudget=ModelMemoryPlan.MaximumInstanceBytes;
    internal const int LoadGuardBytes=64*1024;
    private readonly Model[] models;
    private readonly ModelInstance[] actors;
    private readonly ModelResidency residency;
    private readonly int[] current=new int[6],wanted=new int[6];
    private readonly bool[] essential=new bool[6];
    private readonly bool[] noBenefit=new bool[6];
    private readonly int[] fullPeak=new int[6];
    private readonly long[] retryAt=new long[6];
    private Task<Model>? pending;private CancellationTokenSource? cancellation;
    private int active=-1,loadingLod,ticket,changed,reservedPeak;
    private bool stopped;
    internal bool Paused;
    internal VisualMemoryPressure? Memory;
    internal int Changes,Failures;
    internal int PendingSpecies=>active;
    internal bool FullReady {get{if(pending!=null)return false;for(int i=0;i<current.Length;i++)if(current[i]!=0)return false;return true;}}
    internal bool FullForSpace(GameState state,PortalSystem doors,int space)
    {if(space==0)return true;for(int i=0;i<current.Length;i++)if(current[i]!=0&&ModelResidencyDemand.UsesSpace(state,doors,i,space))return false;return true;}
    internal VillagerModelLod(Model[] models,ModelInstance[] actors,ModelResidency residency)
    {this.models=models;this.actors=actors;this.residency=residency;}
    internal int Level(int species)=>current[species];
    internal int ReplacementPeak(int species)
    {
        // The coarse cooker preserves every non-geometry section byte for
        // byte and only removes vertices/indices. Its allocation ceiling is
        // therefore the observed full package/instance, plus one package for
        // transient parsing and a fixed aligned-loader/job guard.
        if(fullPeak[species]==0&&current[species]==0&&residency.Ready(species))
            fullPeak[species]=checked(models[species].NativeBytes+models[species].PackageBytes+actors[species+1].NativeBytes+LoadGuardBytes);
        return fullPeak[species];
    }
    internal int ConsumeChangedActors(){int result=changed;changed=0;return result;}
    internal void Require(GameState state,ActorFrameVisibility visibility,bool pinFull=false,PortalSystem? doors=null)
    {
        for(int species=0;species<6;species++){
            int actor=species+1;
            bool indoorRequired=NeedsFullIndoor(state,doors,species);
            essential[species]=pinFull||indoorRequired;
            if(species==state.Species||pinFull||indoorRequired)wanted[species]=0;
            else if(visibility.Visible[actor])wanted[species]=noBenefit[species]||!VillagerLod.CoarseAvailable(species)?0:visibility.GeometryLod[actor];
            // An invisible species keeps its existing variant and follows the
            // ordinary ARAM eviction owner; no speculative dual-cohort load.
            else wanted[species]=current[species];
        }
        if(pending!=null&&(Paused||Memory?.UnderPressure==true&&!essential[active]||wanted[active]!=loadingLod))cancellation!.Cancel();
    }
    private static bool NeedsFullIndoor(GameState state,PortalSystem? doors,int species)
    {
        if(doors==null)return state.Space!=0&&state.Town.Residents[species].Space==state.Space;
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
            Model? replacement=null;ModelInstance? instance=null;
            try{
                replacement=pending.GetAwaiter().GetResult();
                if(Paused||Memory?.UnderPressure==true&&!essential[active]||wanted[active]!=loadingLod){replacement.Dispose();replacement=null;return;}
                instance=replacement.CreateInstance(InstanceBudget);
                var oldModel=models[active];var oldInstance=actors[active+1];
                // Count immutable geometry as well as skin outputs. Unskinned
                // villagers have equally small full/coarse instances while
                // their model geometry still saves useful submission work.
                int oldBytes=oldModel.NativeBytes+oldInstance.NativeBytes,newBytes=replacement.NativeBytes+instance.NativeBytes;
                if(loadingLod==1&&oldBytes-newBytes<Math.Max(1024,oldBytes/20)){
                    noBenefit[active]=true;wanted[active]=0;instance.Dispose();instance=null;replacement.Dispose();replacement=null;return;
                }
                residency.Replace(active,replacement,now);
                residency.RecordWarmPeak(active,instance.NativeBytes);
                models[active]=replacement;actors[active+1]=instance;replacement=null;instance=null;
                current[active]=loadingLod;changed|=1<<(active+1);Changes++;
                if(ticket!=0){Memory!.Complete(ticket);ticket=0;}
                oldInstance.Dispose();oldModel.Dispose();
            }catch(OperationCanceledException){instance?.Dispose();replacement?.Dispose();retryAt[active]=now+1000000;}
            catch(Exception error){
                instance?.Dispose();replacement?.Dispose();Failures++;retryAt[active]=now+1000000;Memory?.AllocationFailed(reservedPeak,now);
                DebugOverlay.WriteLine("ANIMALTOWN LOD RETRY "+active+": "+error.Message);
            }finally{
                residency.UnlockReplacement(active);
                if(ticket!=0){Memory!.Cancel(ticket);ticket=0;}
                pending=null;cancellation!.Dispose();cancellation=null;active=-1;
            }
            return;
        }
        for(int species=0;species<6;species++)if(models[species]!=null&&current[species]!=wanted[species]&&now>=retryAt[species]){
            reservedPeak=ReplacementPeak(species);if(reservedPeak==0)continue;
            if(Memory!=null&&!Memory.TryReserve(reservedPeak,now,essential[species],out ticket))continue;
            if(!residency.TryLockReplacement(species)){if(ticket!=0){Memory!.Cancel(ticket);ticket=0;}continue;}
            active=species;loadingLod=wanted[species];cancellation=new CancellationTokenSource();
            try{pending=ModelResources.LoadLodAsync(Name(species),loadingLod,cancellation.Token);}
            catch(Exception error){
                Failures++;retryAt[species]=now+1000000;Memory?.AllocationFailed(reservedPeak,now);residency.UnlockReplacement(species);cancellation.Dispose();cancellation=null;active=-1;
                if(ticket!=0){Memory!.Cancel(ticket);ticket=0;}
                DebugOverlay.WriteLine("ANIMALTOWN LOD ADMISSION "+species+": "+error.Message);
            }
            return;
        }
    }
    public void Dispose()
    {
        stopped=true;if(pending==null)return;
        // Session teardown is a blocking boundary. Do not drop a reservation,
        // its cancellation source, or an unadopted native model before drain.
        cancellation!.Cancel();
        try{pending.GetAwaiter().GetResult().Dispose();}catch(OperationCanceledException){}catch(Exception error){DebugOverlay.WriteLine("ANIMALTOWN LOD SHUTDOWN: "+error.Message);}
        finally{
            residency.UnlockReplacement(active);if(ticket!=0){Memory!.Cancel(ticket);ticket=0;}
            pending=null;cancellation.Dispose();cancellation=null;active=-1;
        }
    }
    private static string Name(int species)=>species switch{0=>"fox",1=>"bunny",2=>"giraffe",3=>"cat",4=>"hamster",_=>"spaniel"};
}
