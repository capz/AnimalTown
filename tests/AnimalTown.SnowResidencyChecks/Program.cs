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
Check(memory.UnderPressure&&memory.WaitingPeakBytes>256*1024,"essential snow demand asks the global policy to reclaim enough headroom");
memory.Observe(8*1024*1024,0,0,1);
Check(terrain.Prepare(town)&&terrain.Ready&&NativeShadows.Bytes==256*1024,"reclaimed headroom admits and publishes the essential dent mask");
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
Check(terrain.Prepare(town)&&NativeShadows.Bytes==0&&NativeShadows.Allocations==2,"dents recovered indoors do not recreate an unnecessary mask on exit");

town.Traffic.Enter(town,20,30);NativeShadows.FailNext=true;memory.Observe(8*1024*1024,0,0,6);
Check(!terrain.Prepare(town)&&!terrain.Ready&&memory.ReservedBytes==0,"native rejection cancels its reservation and withholds the incomplete scene");
Check(memory.UnderPressure&&memory.FailedRequestBytes>0,"native snow allocation failure reaches global pressure and retry policy");
calls=NativeShadows.Calls;
Check(!terrain.Prepare(town)&&NativeShadows.Calls==calls,"failed mask creation backs off instead of retrying every frame");
Clock.Timestamp=VisualMemoryPressure.FailureBackoffUs+1;memory.Observe(8*1024*1024,0,0,Clock.Timestamp);
Check(terrain.Prepare(town)&&terrain.Ready&&NativeShadows.Bytes==256*1024,"snow publication safely retries after backoff and headroom recovery");
snow.SetWinter(false);
Check(terrain.Prepare(town)&&NativeShadows.Bytes==0,"leaving winter retires native coverage through the same admitted lifecycle");
Console.WriteLine(checks+" snow residency checks passed using production admission/retirement code");

internal sealed partial class TerrainRenderer
{
    internal VisualMemoryPressure? Memory=new VisualMemoryPressure();
    private Town? cachedTown;
    internal TerrainRenderer(Town town){cachedTown=town;}
    internal bool Ready=>snowMaskReady;
    internal bool Prepare(Town town)=>PrepareSnowMask(town);
    internal bool Suspend()=>SuspendSnowMask();
}
internal static class NativeShadows
{
    internal static int Bytes,Allocations,Calls;internal static bool FailNext;
    internal static void Reset(){Bytes=Allocations=Calls=0;FailNext=false;Clock.Timestamp=0;}
    internal static int Stats(int kind){if(kind!=14)throw new ArgumentOutOfRangeException();return Bytes;}
    internal static int Snow(byte[] packed,int revision,int winter)
    {
        Calls++;
        if(winter==0||packed.All(value=>value==0)){Bytes=0;return 1;}
        if(Bytes==0){if(FailNext){FailNext=false;return 0;}Bytes=256*1024;Allocations++;}
        return 1;
    }
}
namespace Dolphin.Timing {internal static class Clock {internal static long Timestamp;}}
namespace Dolphin.Diagnostics {internal static class DebugOverlay {internal static void WriteLine(string value){}}}
