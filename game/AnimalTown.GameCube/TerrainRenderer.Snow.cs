using System;
using AnimalTown;
using Dolphin.Diagnostics;
using Dolphin.Timing;

internal sealed partial class TerrainRenderer
{
    private const int SnowMaskBytes=256*1024,SnowMaskPeakBytes=SnowMaskBytes+16*1024;
    private bool snowMaskPrepared,snowMaskReady=true,snowHasWear;
    private Town? snowDemandTown;
    private int snowDemandRevision=-1,snowDemandTerrainRevision=-1,snowDemandMinZ=-1,snowDemandMaxZ=-1;
    private long snowRetryAt;

    private bool SnowNeedsStorage(Town town,int minZ,int maxZ)
    {
        var snow=town.Traffic.Snow;
        // Match logical rows before native strip projection. GX clamps the
        // snow texture at town edges; the curved world does not wrap its dents.
        // Two rows include neighboring stamps, bilinear filtering and relief.
        int first=Math.Max(0,minZ-2),past=Math.Min(Town.Height,maxZ+2);
        if(snowDemandTown!=town||snowDemandRevision!=snow.Revision||snowDemandTerrainRevision!=town.TerrainVersion||snowDemandMinZ!=first||snowDemandMaxZ!=past){
            snowDemandTown=town;snowDemandRevision=snow.Revision;snowDemandTerrainRevision=town.TerrainVersion;
            snowDemandMinZ=first;snowDemandMaxZ=past;snowHasWear=false;
            for(int z=first;z<past&&!snowHasWear;z++)for(int x=0;x<Town.Width;x++){
                int cell=z*Town.Width+x;
                if(((snow.Packed[cell/2]>>((cell%2)*4))&15)==0)continue;
                // Native stamp depth can spill from a changed terrain tile
                // onto eligible grass beside it. Keep that neighboring case.
                for(int dz=-1;dz<=1&&!snowHasWear;dz++)for(int dx=-1;dx<=1;dx++)
                    if(SnowWear.Covers(town,x+dx,z+dz)){snowHasWear=true;break;}
                if(snowHasWear)break;
            }
        }
        return snow.Winter&&snowHasWear;
    }
    internal bool PrepareSnowForExit(Town town,float x,float z)
    {
        // The visible row interval follows the selected immutable geometry;
        // legacy stream selection is finalized by the first outdoor Prepare.
        if(legacy||archive==null||!archive.Ready)return true;
        if(!archive.Presentable)return false;
        archive.GroundBounds(season==3?snowGroundMask:ulong.MaxValue,out int minZ,out int maxZ);
        return PrepareSnowMask(town,x,z,minZ,maxZ);
    }
    internal bool ReleaseSnowMask(Town town)
    {
        town.Traffic.Snow.ReleaseRecoveryStamps();
        if(!snowMaskPrepared&&NativeShadows.Stats(14)==0&&NativeShadows.Stats(15)==0){snowMaskReady=true;return true;}
        bool released=NativeShadows.Snow(town.Traffic.Snow.Packed,town.Tiles,town.Elevation,null,0,0,0,-1,0,0,0,0)==1;
        if(released){snowMaskPrepared=false;snowMaskReady=true;}
        else snowMaskReady=false;
        return released;
    }
    private bool PrepareSnowMask(Town town,float x,float z,int minZ,int maxZ)
    {
        minZ=Math.Max(0,minZ);maxZ=Math.Min(Town.Height,maxZ);
        if(!town.Traffic.Snow.Winter||minZ>=maxZ)return ReleaseSnowMask(town);
        // A far dent may exist inside the native strip's spare rows. Bypass
        // native creation entirely for a fresh visible region, so a zero-byte
        // scene never waits for a mask reservation it cannot actually use.
        if(!SnowNeedsStorage(town,minZ,maxZ))return ReleaseSnowMask(town);
        long now=Clock.Timestamp;
        if(now<snowRetryAt){snowMaskReady=false;return false;}
        // Keep main's streamed 128 KiB strip, 256 KiB fallback and optional
        // smooth transition cache. An existing strip can grow or shrink, so
        // checking only whether a mask exists would miss a real allocation.
        // Smooth transitions resume automatically after temporary memory pressure.
        int near=Math.Max(0,minZ-1),far=maxZ+1;
        int desiredBytes=far-near<=63?SnowMaskBytes/2:SnowMaskBytes,currentBytes=NativeShadows.Stats(14);
        bool preview=Memory?.UnderPressure!=true;
        int guard=SnowMaskPeakBytes-SnowMaskBytes;
        int nativeBefore=currentBytes+NativeShadows.Stats(15);
        int requestedBytes=desiredBytes!=currentBytes?Math.Max(0,desiredBytes-currentBytes)+guard:0;
        if(preview&&NativeShadows.Stats(15)==0)requestedBytes=Math.Max(requestedBytes,guard);
        int ticket=0;
        if(requestedBytes>0&&Memory!=null&&!Memory.TryReserve(requestedBytes,now,true,out ticket)){
            snowMaskReady=false;return false;
        }
        try{
            var snow=town.Traffic.Snow;int focus=-1,elapsedMs=0;
            if(!preview)snow.ReleaseRecoveryStamps();
            if(preview){
                int tileX=Math.Max(0,Math.Min(Town.Width-1,(int)Math.Floor(x)));
                int tileZ=Math.Max(0,Math.Min(Town.Height-1,(int)Math.Floor(z)));
                focus=tileZ*Town.Width+tileX;elapsedMs=unchecked((int)(now/1000));
            }
            snowMaskPrepared=true;
            if(NativeShadows.Snow(snow.Packed,town.Tiles,town.Elevation,preview?snow.RecoveryStamps:null,
                snow.Revision,town.TerrainVersion,1,focus,elapsedMs,snow.SnowfallTicks,minZ,maxZ)!=1)
                throw new InvalidOperationException("Native snow coverage allocation or publication rejected");
            // A dent elsewhere in town can make the conservative request
            // unnecessary for these streamed rows. Avoid forcing a new system
            // allocator sample when the native owner gained no storage.
            if(NativeShadows.Stats(14)+NativeShadows.Stats(15)>nativeBefore)Memory?.Complete(ticket);
            else Memory?.Cancel(ticket);
            ticket=0;
            snowMaskReady=true;return true;
        }catch(Exception error){
            Memory?.Cancel(ticket);
            if(requestedBytes>0)Memory?.AllocationFailed(requestedBytes,now);
            snowRetryAt=now+VisualMemoryPressure.FailureBackoffUs;snowMaskReady=false;
            DebugOverlay.WriteLine("ANIMALTOWN SNOW RESTORE RETRY "+error.Message);return false;
        }
    }
    private bool SuspendSnowMask()
    {
        // Authoritative dents and their sparse recovery clocks remain in town
        // state; only native streamed coverage and preview buffers are retired.
        int before=NativeShadows.Stats(14)+NativeShadows.Stats(15);
        if(before==0)return true;
        var town=cachedTown??snowDemandTown;if(town==null)return false;
        bool released=ReleaseSnowMask(town);
        if(released)DebugOverlay.WriteLine("ANIMALTOWN SNOW SUSPENDED reclaimed bytes "+
            Math.Max(0,before-NativeShadows.Stats(14)-NativeShadows.Stats(15)));
        return released;
    }
}
