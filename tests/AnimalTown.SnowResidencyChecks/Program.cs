using AnimalTown;
using Dolphin.Timing;

int checks=0;
void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
Town Flat()
{
    var town=new Town(3);Array.Fill(town.Tiles,(byte)Terrain.Grass);Array.Clear(town.Elevation);
    town.Traffic.Snow.SetWinter(true);return town;
}
var town=Flat();var snow=town.Traffic.Snow;
var terrain=new TerrainRenderer(town);var memory=terrain.Memory!;
NativeShadows.Reset();memory.Observe(128*1024,0,0,0);
Check(terrain.Prepare(town)&&terrain.Ready&&NativeShadows.Allocations==0,"undented winter needs no native mask or memory reservation");
town.Traffic.Enter(town,20,30);int calls=NativeShadows.Calls;
Check(!terrain.Prepare(town)&&!terrain.Ready&&NativeShadows.Calls==calls,"denied mask admission blocks publication before creating geometry holes");
Check(memory.UnderPressure&&memory.WaitingPeakBytes>128*1024,"essential snow demand asks the global policy to reclaim enough headroom");
memory.Observe(8*1024*1024,0,0,1);
Check(terrain.Prepare(town)&&terrain.Ready&&NativeShadows.Bytes==128*1024,"reclaimed headroom admits and publishes the essential dent mask");
Check(memory.ReservedBytes>0,"successful snow allocation retains its reservation until the next physical sample");
memory.Observe(8*1024*1024,0,0,2);
Check(memory.ReservedBytes==0,"a physical memory sample retires completed snow reservations");
memory.Observe(128*1024,0,0,3);town.Traffic.Enter(town,20,30);
Check(terrain.Prepare(town)&&NativeShadows.Allocations==1,"updates to an existing mask remain allocation-free under pressure");
var packed=(byte[])snow.Packed.Clone();
Check(terrain.Suspend()&&NativeShadows.Bytes==0&&snow.Winter&&snow.Packed.SequenceEqual(packed),"indoor retirement releases only native snow coverage and preserves the authoritative winter and dents");
memory.Observe(8*1024*1024,0,0,4);
Check(terrain.Prepare(town)&&NativeShadows.Allocations==2,"an exit reconstructs native coverage from preserved authoritative dents");
memory.Observe(8*1024*1024,0,0,5);
Check(terrain.Suspend(),"a second indoor transition releases the reconstructed mask");
for(int i=0;i<SnowWear.RecoverySeconds*30;i++)snow.TickSnowfall(true);
Check(terrain.Prepare(town)&&NativeShadows.Bytes==128*1024&&snow.VisitCount(20,30)==1,"main's recovered shallow dent remains authoritative and visible after indoor snowfall");
Check(terrain.Suspend(),"settled snow coverage still releases on indoor retirement");
snow.ResetForTownEntry();int settledAllocations=NativeShadows.Allocations;
Check(terrain.Prepare(town)&&NativeShadows.Bytes==0&&NativeShadows.Allocations==settledAllocations,"a cleared town-entry checkpoint recreates no unnecessary snow mask");

town.Traffic.Enter(town,20,30);NativeShadows.FailNext=true;memory.Observe(8*1024*1024,0,0,6);
Check(!terrain.Prepare(town)&&!terrain.Ready&&memory.ReservedBytes==0,"native rejection cancels its reservation and withholds the incomplete scene");
Check(memory.UnderPressure&&memory.FailedRequestBytes>0,"native snow allocation failure reaches global pressure and retry policy");
calls=NativeShadows.Calls;
Check(!terrain.Prepare(town)&&NativeShadows.Calls==calls,"failed mask creation backs off instead of retrying every frame");
Clock.Timestamp=VisualMemoryPressure.FailureBackoffUs+1;memory.Observe(8*1024*1024,0,0,Clock.Timestamp);
Check(terrain.Prepare(town)&&terrain.Ready&&NativeShadows.Bytes==128*1024,"snow publication safely retries after backoff and headroom recovery");
snow.SetWinter(false);
Check(terrain.Prepare(town)&&NativeShadows.Bytes==0,"leaving winter retires native coverage through the same admitted lifecycle");
snow.SetWinter(true);town.Traffic.Enter(town,20,30);
terrain.Memory=memory=new VisualMemoryPressure();memory.Observe(8*1024*1024,0,0,Clock.Timestamp);
Check(terrain.Prepare(town)&&NativeShadows.Bytes==128*1024,"visible rows reconstruct main's smaller streamed snow strip");
memory.Observe(128*1024,0,0,Clock.Timestamp);
Check(!terrain.PrepareFull(town)&&NativeShadows.Bytes==128*1024,"a growing row interval cannot bypass admission just because a smaller mask already exists");
memory.Observe(8*1024*1024,0,0,Clock.Timestamp);
Check(terrain.PrepareFull(town)&&NativeShadows.Bytes==256*1024,"wide visible rows retain main's full-height fallback under an admitted growth budget");
town.Traffic.Enter(town,20,30);terrain.Memory=memory=new VisualMemoryPressure();memory.Observe(8*1024*1024,0,0,Clock.Timestamp);
Check(terrain.Prepare(town)&&NativeShadows.PreviewBytes>0&&NativeShadows.LastRecovery!=null,"smooth snow retains the automatic native preview and authoritative recovery stamps");
var borrowed=snow.RecoveryStamps;
memory.Observe(128*1024,0,0,Clock.Timestamp);
Check(terrain.Prepare(town)&&NativeShadows.PreviewBytes==0&&!ReferenceEquals(borrowed,snow.RecoveryStamps),"memory pressure temporarily releases smoothing workspace and the borrowed stamp view");
memory.Observe(8*1024*1024,0,0,Clock.Timestamp);
Clock.Timestamp+=VisualMemoryPressure.RecoveryGraceUs;
memory.Observe(8*1024*1024,0,0,Clock.Timestamp);
Check(terrain.Prepare(town)&&NativeShadows.PreviewBytes>0&&NativeShadows.LastRecovery!=null,"smoothing resumes automatically after the headroom recovery grace period");
Check(terrain.Suspend()&&NativeShadows.Bytes==0&&NativeShadows.PreviewBytes==0,"suspension releases both streamed mask and optional smoothing workspace");
terrain.SetExitReady(false);
Check(!terrain.PrepareSnowForExit(town,20,30),"door snow readiness waits for a complete destination terrain selection");
terrain.SetExitReady(true);memory.Observe(8*1024*1024,0,0,Clock.Timestamp);
Check(terrain.PrepareSnowForExit(town,20,30),"door preparation uses actual destination ground bounds through the new twelve-argument bridge");
// Admission follows drawable logical rows, rather than the spare rows retained
// in the native strip or a distant dent elsewhere on the curved town.
NativeShadows.Reset();var regionalTown=Flat();regionalTown.Traffic.Enter(regionalTown,20,50);
var regional=new TerrainRenderer(regionalTown);var regionalMemory=regional.Memory!;
regionalMemory.Observe(128*1024,0,0,0);
Check(regional.Prepare(regionalTown)&&regional.Ready&&NativeShadows.Allocations==0&&regionalMemory.ReservedBytes==0,"a far dent in native spare strip rows cannot block a fresh visible region behind denied mask admission");
calls=NativeShadows.Calls;
Check(!regional.PrepareRows(regionalTown,45,60)&&NativeShadows.Calls==calls,"moving the same snow revision into the dented rows recomputes essential admission before native allocation");
regionalMemory.Observe(8*1024*1024,0,0,1);
Check(regional.PrepareRows(regionalTown,45,60)&&NativeShadows.Bytes==128*1024,"visible dent rows reconstruct native coverage when headroom permits");
regionalMemory.Observe(128*1024,0,0,2);
Check(regional.Prepare(regionalTown)&&NativeShadows.Bytes==0&&regionalTown.Traffic.Snow.VisitCount(20,50)==1,"leaving all visible dents releases the mask without erasing authoritative far wear");
regionalTown.Traffic.Enter(regionalTown,20,41);
Check(!regional.Prepare(regionalTown),"bilinear and relief guard rows retain a dent just beyond the visible interval");

NativeShadows.Reset();var edgeTown=Flat();edgeTown.Traffic.Enter(edgeTown,20,Town.Height-1);
var edges=new TerrainRenderer(edgeTown);edges.Memory!.Observe(128*1024,0,0,0);
Check(edges.PrepareRows(edgeTown,-8,8)&&NativeShadows.Allocations==0,"northern rows clamp rather than wrapping a southern dent through the curved-world projection");
edgeTown.Traffic.Enter(edgeTown,20,0);
Check(!edges.PrepareRows(edgeTown,-8,8),"the first logical row still participates in guarded mask admission");
NativeShadows.Reset();edgeTown=Flat();edgeTown.Traffic.Enter(edgeTown,20,0);edges=new TerrainRenderer(edgeTown);edges.Memory!.Observe(128*1024,0,0,0);
Check(edges.PrepareRows(edgeTown,Town.Height-8,Town.Height+8)&&NativeShadows.Allocations==0,"southern padded rows do not wrap northern snow wear");
edgeTown.Traffic.Enter(edgeTown,Town.Width-1,Town.Height-1);
Check(!edges.PrepareRows(edgeTown,Town.Height-8,Town.Height+8),"the final packed high-nibble cell remains a required southern dent");

NativeShadows.Reset();var changedTerrain=Flat();changedTerrain.Traffic.Enter(changedTerrain,20,20);Array.Fill(changedTerrain.Tiles,(byte)Terrain.Water);changedTerrain.NotifyTerrainChanged();
var eligible=new TerrainRenderer(changedTerrain);eligible.Memory!.Observe(128*1024,0,0,0);
Check(eligible.Prepare(changedTerrain)&&NativeShadows.Allocations==0,"stale wear surrounded by terrain that cannot receive snow needs no texture");
changedTerrain.Tiles[21*Town.Width+20]=(byte)Terrain.Grass;changedTerrain.NotifyTerrainChanged();
Check(!eligible.Prepare(changedTerrain),"a terrain change exposing eligible grass next to a stale stamp reintroduces required storage for native spill");

NativeShadows.Reset();var failedTown=Flat();failedTown.Traffic.Enter(failedTown,20,100);var failedRegion=new TerrainRenderer(failedTown);
failedRegion.Memory!.Observe(8*1024*1024,0,0,0);NativeShadows.FailNext=true;
Check(!failedRegion.PrepareRows(failedTown,90,110),"regional native allocation failure begins ordinary retry backoff");
Check(failedRegion.Prepare(failedTown)&&failedRegion.Ready&&NativeShadows.Bytes==0,"a fresh region can draw during an unrelated dent-mask retry backoff");
Console.WriteLine(checks+" snow residency checks passed using production admission/retirement code");

internal sealed partial class TerrainRenderer
{
    internal VisualMemoryPressure? Memory=new VisualMemoryPressure();
    private Town? cachedTown;
    private bool legacy=false;
    private TerrainArchive? archive;
    private int season=3;
    private ulong snowGroundMask=1;
    internal TerrainRenderer(Town town){cachedTown=town;}
    internal bool Ready=>snowMaskReady;
    internal bool Prepare(Town town)=>PrepareSnowMask(town,20,30,0,40);
    internal bool PrepareFull(Town town)=>PrepareSnowMask(town,20,30,0,Town.Height);
    internal bool PrepareRows(Town town,int minZ,int maxZ)=>PrepareSnowMask(town,20,30,minZ,maxZ);
    internal bool Suspend()=>SuspendSnowMask();
    internal void SetExitReady(bool ready)=>archive=new TerrainArchive{Presentable=ready};
}
internal sealed class TerrainArchive
{
    internal bool Ready=true,Presentable=true;
    internal void GroundBounds(ulong mask,out int minZ,out int maxZ){minZ=0;maxZ=40;}
}
internal static class NativeShadows
{
    internal static int Bytes,PreviewBytes,Allocations,Calls;internal static bool FailNext;
    internal static ushort[]? LastRecovery;
    internal static void Reset(){Bytes=PreviewBytes=Allocations=Calls=0;FailNext=false;LastRecovery=null;Clock.Timestamp=0;}
    internal static int Stats(int kind)=>kind==14?Bytes:kind==15?PreviewBytes:kind==16?0:throw new ArgumentOutOfRangeException();
    internal static int Snow(byte[] packed,byte[] tiles,byte[] elevation,ushort[]? recovery,int revision,int terrainRevision,int winter,int focus,int elapsedMs,int snowfallTicks,int minZ,int maxZ)
    {
        Calls++;LastRecovery=recovery;
        if(winter==0||packed.All(value=>value==0)){Bytes=PreviewBytes=0;return 1;}
        int near=Math.Max(0,minZ-1),far=maxZ+1,desired=(far-near<=63?128:256)*1024;
        if(Bytes!=desired){if(FailNext){FailNext=false;return 0;}Bytes=desired;Allocations++;}
        PreviewBytes=focus>=0?5400:0;
        return 1;
    }
}
namespace Dolphin.Timing {internal static class Clock {internal static long Timestamp;}}
namespace Dolphin.Diagnostics {internal static class DebugOverlay {internal static void WriteLine(string value){}}}
