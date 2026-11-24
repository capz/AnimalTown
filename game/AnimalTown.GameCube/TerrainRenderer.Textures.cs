using System;
using System.IO;
using System.Threading.Tasks;
using AnimalTown;
using Dolphin.Diagnostics;
using Dolphin.Graphics;
using Dolphin.Timing;

internal sealed partial class TerrainRenderer
{
    // RGB565 base+half mip, plus native resource/alignment metadata. Byte reads
    // use the existing managed heap; only adoption creates native resources.
    private const int FloorPeakBytes=192*1024;
    internal VisualMemoryPressure? Memory;
    private bool floorTexturesSuspended,discardGroundRead,discardFloorRead,floorGeometryRetired;
    private int floorGeometrySeason=-1;
    private bool FloorGeometryChanged=>floorGeometrySeason!=season;
    private Task<byte[]>? pendingFloor;
    private int pendingFloorSource=-1,floorTicket,groundTicket;
    private long floorRetryAt,groundRetryAt;

    private void BeginFloorSeason(byte[] metadata,int next)
    {
        if(GraphicsDevice.Default.IsFrameActive)throw new InvalidOperationException("Change floor season between frames");
        if(next<0||next>3)throw new ArgumentOutOfRangeException("next");
        FloorStrips.Validate(metadata);
        int count=FloorStrips.Read(metadata,12);
        if(count>TownPaths.Material)throw new InvalidOperationException("Path material slot overlaps floor metadata");
        DiscardGroundRead();DiscardFloorRead();ReleaseFloorSources(true);
        data=metadata;slots=count;season=next;
        floorTexturesSuspended=true;floorGeometryRetired=false;groundLoad=GroundLoad.Unloaded;floorRetryAt=groundRetryAt=0;
        // Updating metadata never creates texture resources. Startup, real
        // season rollovers and debug overrides share the admitted async path.
        for(int slot=0;slot<slots;slot++){
            bool neutral=FloorStrips.Read(data,4)==2&&FloorStrips.TextureSlot(data,slot)==0;
            var address=neutral?TextureAddressMode.Repeat:TextureAddressMode.Clamp;
            materials[slot].Sampler=new SamplerState(address,address);
            uint tint=FloorStrips.Tint(data,slot);
            materials[slot].Color=new Color32((byte)(tint>>24),(byte)(tint>>16),(byte)(tint>>8));
        }
        MaterialRevision++;
    }

    private void CancelFloorReservations()
    {
        Memory?.Cancel(floorTicket);Memory?.Cancel(groundTicket);
        floorTicket=groundTicket=0;
    }
    private void DiscardGroundRead()
    {
        if(pendingGround!=null)discardGroundRead=true;
        Memory?.Cancel(groundTicket);groundTicket=0;
        DrainDiscardedReads();
    }
    private void DiscardFloorRead()
    {
        if(pendingFloor!=null)discardFloorRead=true;
        Memory?.Cancel(floorTicket);floorTicket=0;
        DrainDiscardedReads();
    }
    private void DrainDiscardedReads()
    {
        // Disc tasks return bytes only. An indoor transition never blocks on a
        // read or adopts a result from an earlier outdoor residency generation.
        if(discardGroundRead&&pendingGround?.IsCompleted==true){
            try{pendingGround.GetAwaiter().GetResult();}catch(Exception){}
            pendingGround=null;discardGroundRead=false;
        }
        if(discardFloorRead&&pendingFloor?.IsCompleted==true){
            try{pendingFloor.GetAwaiter().GetResult();}catch(Exception){}
            pendingFloor=null;pendingFloorSource=-1;discardFloorRead=false;
        }
    }
    private int FloorOwnedBytes
    {
        get{int bytes=0;for(int i=0;i<textures.Length;i++)if(textures[i]!=null)bytes+=textures[i]!.Bytes;return bytes;}
    }
    private int ReleaseFloorSources(bool evict)
    {
        ClearPathBindings();
        // Clear ALL borrowers, including debug mip pages and the path fallback,
        // before an owning cache lease can dispose its underlying native page.
        for(int i=0;i<materials.Length;i++){materials[i].Texture=null;materials[i].SecondaryTexture=null;}
        int sourceBytes=0;
        for(int i=0;i<textures.Length;i++)if(textures[i]!=null){
            var texture=textures[i]!;
            for(int page=0;page<texture.Pages.Length;page++)mipBands.Forget(texture.Pages[page]);
            textures[i]=null;TextureCache.Release(texture,evict:evict);
            // Query the cache's actual source ownership after releasing ours;
            // clearing the renderer's reference alone is not proof of eviction.
            sourceBytes+=TextureCache.SourceBytesContaining(texture.Source);
        }
        return sourceBytes;
    }
    private void SuspendFloorTextures()
    {
        if(GraphicsDevice.Default.IsFrameActive)throw new InvalidOperationException("Suspend floor textures after EndFrame");
        DiscardGroundRead();
        DiscardFloorRead();
        if(season<0||floorTexturesSuspended&&FloorOwnedBytes==0)return;
        int before=GraphicsDevice.Default.NativeBytes,ownedBefore=FloorOwnedBytes;
        ReleasePaths();int sourceBytes=ReleaseFloorSources(true);
        groundLoad=GroundLoad.Unloaded;floorTexturesSuspended=true;
        MaterialRevision++;
        DebugOverlay.WriteLine("ANIMALTOWN FLOOR SUSPENDED owned bytes "+ownedBefore+" -> "+FloorOwnedBytes+
            " source bytes "+sourceBytes+" reclaimed bytes "+Math.Max(0,before-GraphicsDevice.Default.NativeBytes));
    }
    private void BindFloorSources()
    {
        for(int slot=0;slot<slots;slot++){
            int source=FloorStrips.TextureSlot(data,slot);
            materials[slot].Texture=textures[source]?.Pages[0];
        }
    }
    private bool RestoreFloorTextures(long deadline)
    {
        DrainDiscardedReads();
        if(!floorTexturesSuspended)return true;
        if(GraphicsDevice.Default.IsFrameActive)throw new InvalidOperationException("Restore floor textures between frames");
        long now=Clock.Timestamp;
        if(now>=deadline||now<floorRetryAt||discardFloorRead)return false;
        if(pendingFloor!=null){
            if(!pendingFloor.IsCompleted)return false;
            bool allocating=false;CachedTexture? loaded=null;
            try{
                byte[] source=pendingFloor.GetAwaiter().GetResult();pendingFloor=null;
                TexturePixels.Validate(source);
                bool neutral=FloorStrips.Read(data,4)==2&&pendingFloorSource==0;
                string path=FloorSourcePath(pendingFloorSource,true);
                allocating=true;
                loaded=TextureCache.LoadFloor(path,repeat:neutral,source:source);
                var wrap=neutral?TextureWrapMode.Repeat:TextureWrapMode.Mirror;
                loaded.Pages[0].ConfigureSampler(wrap,wrap,TextureMinFilter.Linear,TextureMagFilter.Linear,0);
                textures[pendingFloorSource]=loaded;loaded=null;
                if(neutral)groundLoad=GroundLoad.Preview;
                Memory?.Complete(floorTicket);floorTicket=0;pendingFloorSource=-1;
                BindFloorSources();MaterialRevision++;
            }catch(Exception error){
                pendingFloor=null;pendingFloorSource=-1;
                if(loaded!=null)TextureCache.Release(loaded,evict:true);
                Memory?.Cancel(floorTicket);floorTicket=0;
                if(allocating)Memory?.AllocationFailed(FloorPeakBytes,now);
                floorRetryAt=now+VisualMemoryPressure.FailureBackoffUs;
                DebugOverlay.WriteLine("ANIMALTOWN FLOOR RESTORE RETRY "+error.Message);return false;
            }
            // At most one source upload per pumping opportunity, even when the
            // next byte-only read could complete synchronously.
            return FinishFloorRestore();
        }
        int missing=-1;
        for(int slot=0;slot<slots;slot++){
            int source=FloorStrips.TextureSlot(data,slot);if(textures[source]==null){missing=source;break;}
        }
        if(missing<0)return FinishFloorRestore();
        if(Memory!=null&&!Memory.TryReserve(FloorPeakBytes,now,true,out floorTicket))return false;
        try{pendingFloorSource=missing;pendingFloor=Dolphin.IO.File.ReadAllBytesAsync(FloorSourcePath(missing,true));}
        catch(Exception error){
            pendingFloorSource=-1;Memory?.Cancel(floorTicket);floorTicket=0;
            floorRetryAt=now+VisualMemoryPressure.FailureBackoffUs;
            DebugOverlay.WriteLine("ANIMALTOWN FLOOR READ RETRY "+error.Message);
        }
        return false;
    }
    private string FloorSourcePath(int source,bool preview)
    {
        bool neutral=FloorStrips.Read(data,4)==2&&source==0;
        string name=neutral?(preview?"floor-outdoor-neutral-64":"floor-outdoor-neutral"):
            "floor-"+TerrainTiles.SeasonName(season)+"-"+source;
        return ContentPaths.Asset(name+".dndtex");
    }
    private bool FinishFloorRestore()
    {
        for(int slot=0;slot<slots;slot++)if(textures[FloorStrips.TextureSlot(data,slot)]==null)return false;
        floorTexturesSuspended=false;BindFloorSources();
        int sourceBytes=0;
        for(int i=0;i<textures.Length;i++)if(textures[i]!=null)sourceBytes+=TextureCache.SourceBytesContaining(textures[i]!.Source);
        DebugOverlay.WriteLine("ANIMALTOWN FLOOR RESTORED season "+season+" slots "+slots+
            " owned bytes "+FloorOwnedBytes+" source bytes "+sourceBytes);
        return true;
    }
    private bool AdvanceGroundTexture()
    {
        DrainDiscardedReads();
        if(floorTexturesSuspended||discardGroundRead)return false;
        long now=Clock.Timestamp;
        // Draw marks the first preview frame. Optional full detail waits until
        // system headroom recovers, while the essential preview stays drawable.
        if(groundLoad==GroundLoad.Visible){
            if(now<groundRetryAt||Memory!=null&&!Memory.TryReserve(FloorPeakBytes,now,false,out groundTicket))return false;
            try{
                pendingGround=Dolphin.IO.File.ReadAllBytesAsync(ContentPaths.Asset("floor-outdoor-neutral.dndtex"));
                groundLoad=GroundLoad.Reading;DebugOverlay.WriteLine("GROUND PREVIEW PRESENTED read full");
            }catch(Exception error){GroundTextureFailed(error,false);}
            return false;
        }
        if(groundLoad==GroundLoad.Reading&&Memory?.UnderPressure==true){
            DiscardGroundRead();groundLoad=GroundLoad.Visible;return false;
        }
        if(groundLoad!=GroundLoad.Reading||pendingGround?.IsCompleted!=true)return false;
        CachedTexture full;bool allocating=false;
        try{
            byte[] source=pendingGround.GetAwaiter().GetResult();
            TexturePixels.Validate(source);
            if(TexturePixels.Width(source)!=FloorTexture.Maximum||TexturePixels.Height(source)!=FloorTexture.Maximum)
                throw new InvalidDataException("Full ground texture must be 256x256");
            allocating=true;
            full=TextureCache.LoadFloor(ContentPaths.Asset("floor-outdoor-neutral.dndtex"),repeat:true,source:source);
        }catch(Exception error){GroundTextureFailed(error,allocating);return false;}
        pendingGround=null;
        var preview=textures[0]!;int previewBytes=preview.Bytes;
        for(int i=0;i<materials.Length;i++){
            if(materials[i].Texture==preview.Pages[0])materials[i].Texture=full.Pages[0];
            if(materials[i].SecondaryTexture==preview.Pages[0])materials[i].SecondaryTexture=full.Pages[0];
        }
        textures[0]=full;if(paths.Bytes==0)materials[TownPaths.Material].Texture=full.Pages[0];
        groundLoad=GroundLoad.Complete;Memory?.Complete(groundTicket);groundTicket=0;
        int before=GraphicsDevice.Default.NativeBytes;
        for(int page=0;page<preview.Pages.Length;page++)mipBands.Forget(preview.Pages[page]);
        TextureCache.Release(preview,evict:true);
        DebugOverlay.WriteLine("GROUND FULL 256 bytes "+full.Bytes);
        DebugOverlay.WriteLine("GROUND PREVIEW FREED "+previewBytes+" native "+before+" -> "+GraphicsDevice.Default.NativeBytes);
        return true;
    }
    private void GroundTextureFailed(Exception error,bool allocating)
    {
        pendingGround=null;Memory?.Cancel(groundTicket);groundTicket=0;
        if(allocating){
            Memory?.AllocationFailed(FloorPeakBytes,Clock.Timestamp);
            groundRetryAt=Clock.Timestamp+VisualMemoryPressure.FailureBackoffUs;groundLoad=GroundLoad.Visible;
        }else groundLoad=GroundLoad.Failed;
        DebugOverlay.WriteLine("GROUND FULL LOAD FAILED keep preview: "+error.Message);
    }
}
