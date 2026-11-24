using System;
using AnimalTown;
using Dolphin.Diagnostics;
using Dolphin.Timing;

internal sealed partial class TerrainRenderer
{
    private const int SnowMaskBytes=256*1024,SnowMaskPeakBytes=SnowMaskBytes+16*1024;
    private bool snowMaskReady=true,snowHasWear;
    private Town? snowDemandTown;
    private int snowDemandRevision=-1;
    private long snowRetryAt;

    private bool SnowNeedsStorage(Town town)
    {
        var snow=town.Traffic.Snow;
        if(snowDemandTown!=town||snowDemandRevision!=snow.Revision){
            snowDemandTown=town;snowDemandRevision=snow.Revision;snowHasWear=false;
            for(int i=0;i<snow.Packed.Length;i++)if(snow.Packed[i]!=0){snowHasWear=true;break;}
        }
        return snow.Winter&&snowHasWear;
    }
    private bool PrepareSnowMask(Town town)
    {
        long now=Clock.Timestamp;
        if(now<snowRetryAt){snowMaskReady=false;return false;}
        bool allocating=NativeShadows.Stats(14)==0&&SnowNeedsStorage(town);
        int ticket=0;
        if(allocating&&Memory!=null&&!Memory.TryReserve(SnowMaskPeakBytes,now,true,out ticket)){
            snowMaskReady=false;return false;
        }
        try{
            var snow=town.Traffic.Snow;
            if(NativeShadows.Snow(snow.Packed,snow.Revision,snow.Winter?1:0)!=1)
                throw new InvalidOperationException("Native snow coverage allocation or publication rejected");
            Memory?.Complete(ticket);ticket=0;
            snowMaskReady=true;return true;
        }catch(Exception error){
            Memory?.Cancel(ticket);
            if(allocating)Memory?.AllocationFailed(SnowMaskPeakBytes,now);
            snowRetryAt=now+VisualMemoryPressure.FailureBackoffUs;snowMaskReady=false;
            DebugOverlay.WriteLine("ANIMALTOWN SNOW RESTORE RETRY "+error.Message);return false;
        }
    }
    private bool SuspendSnowMask()
    {
        // Only native coverage is evicted. The town's authoritative winter,
        // dent depths and recovery timestamps continue to simulate indoors.
        if(NativeShadows.Stats(14)==0)return true;
        var town=cachedTown??snowDemandTown;
        if(town==null)return false;
        var snow=town.Traffic.Snow;
        bool released=NativeShadows.Snow(snow.Packed,snow.Revision,0)==1;
        if(released)DebugOverlay.WriteLine("ANIMALTOWN SNOW SUSPENDED reclaimed bytes "+SnowMaskBytes);
        return released;
    }
}
