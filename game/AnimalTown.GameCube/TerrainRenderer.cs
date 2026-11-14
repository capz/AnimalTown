using System;
using System.IO;
using System.Threading.Tasks;
using Dolphin.Diagnostics;
using Dolphin.Timing;
using AnimalTown;
using Dolphin.Graphics;
using Hypercube.GameCube;

// Ordinary play draws immutable sections baked once at town/season load.
// The old rolling builder remains available only for explicit profiler controls.
internal sealed class TerrainRenderer : IDisposable
{
    private TerrainArchive? archive;
    private bool legacy=false; // Default: immutable ARAM archive and fixed MEM1 views.
    internal bool SuspendForSave()=>!legacy&&archive!=null&&archive.SuspendForSave();
    internal void ResumeAfterSave(){archive?.ResumeAfterSave();}
    internal TerrainSection CreateSection()=>new TerrainSection(soil,waterMaterial);
    private readonly TerrainStream stream=new TerrainStream();
    private readonly TerrainSection?[] sections=new TerrainSection?[TerrainStream.Capacity];
    private readonly FloorStrips staging=new FloorStrips(TerrainStream.Width*TerrainStream.Height);
    private readonly TerrainSoil soil=new TerrainSoil();
    private readonly WaterMaterial waterMaterial=new WaterMaterial();
    private readonly LocalOceanWaves oceanWaves=new LocalOceanWaves();
    private readonly TerrainWalls gallery;
    internal readonly Transform ScrollTransform=new Transform();
    private readonly CloudShadows clouds=new CloudShadows();
    private readonly ObjectShadows shadows;
    private readonly CliffShadows cliffs=new CliffShadows();
    private readonly Color32[] baseTints=new Color32[FloorStrips.MaximumMaterials];
    private readonly Material[] materials=new Material[FloorStrips.MaximumMaterials];
    private readonly CachedTexture?[] textures=new CachedTexture?[FloorStrips.MaximumMaterials];
    private enum GroundLoad { Unloaded,Preview,Visible,Reading,Complete,Failed }
    private GroundLoad groundLoad;
    private Task<byte[]>? pendingGround;
    private readonly Mesh[] batch=new Mesh[128];
    private readonly Transform[] poses=new Transform[128];
    private int batchCount;
    private ulong visibleGroundMask;
    private long geometryRevision;
    internal long GeometryRevision=>geometryRevision;
    internal bool Ready=>!resetting&&(legacy?stream.Presentable:archive?.Presentable??false);
    internal bool ArchiveReady=>archive?.Ready??false;
    internal bool CacheSuspended=>archive?.CacheSuspended??false;
    internal int CacheResidencyError=>archive?.ResidencyError??0;
    internal bool DiscardTerrainCacheWhenIndoors;
    private bool resetting;
    internal bool SuspendOutdoorCache()=>!DiscardTerrainCacheWhenIndoors||legacy||archive==null||archive.Suspend();
    // A not-yet-baked landscape follows the existing initial loading path.
    // Only a previously built cold cache must be prefetched before door commit.
    internal bool PrepareOutdoorResidency(float x,float z,long deadline,bool refreshSelection=false)
        =>legacy||archive==null||!archive.Ready||(!refreshSelection&&archive.Presentable)||archive.PrepareAsync(x,z,Rows,deadline,waterMaterial.IsSunshine);
    internal int LoadingRemaining=>legacy?stream.MissingWanted:archive?.Remaining??TerrainArchivePlan.Count;
    internal int LoadingTotal=>legacy?stream.WantedCount:TerrainArchivePlan.Count;
    internal int MissingVisible=>legacy?stream.MissingVisible:Ready?0:1;
    internal int UrgentStages {get;private set;}
    internal bool FreezeMotion,RecreateMeshes=false;
    internal CylinderRows Rows=>stream.CylinderRows;
    private byte[] data=new byte[0];
    private int season=-1,forced,slots,cachedTerrainVersion=-1;
    private Town? cachedTown;
    private GroundRingRenderer? groundRing;
    private bool disposed;
    internal bool GroundRingRequested {get;private set;}=false; // Legacy comparison only.
    internal int SharedFlatMaterial {get;private set;}=-1;
    internal bool GroundRingActive=>SharedFlatMaterial>=0;
    internal bool GroundRingUploaded=>groundRing?.Uploaded??false;
    internal void ToggleGroundRing(){GroundRingRequested=!GroundRingRequested;}
    internal int VisibleTriangles,RejectedQuads;
    internal int BuiltThisFrame {get;private set;}
    internal int MaterialRevision {get;private set;}
    internal int SeasonOverride=-1;
    internal bool GroundMipmaps=true;
    internal int GroundMipDistance;
    internal WaterRenderStyle WaterStyle {get=>waterMaterial.Style;set=>waterMaterial.Style=value;}
    private bool appliedGroundMipmaps;
    private int appliedGroundMipDistance=-1;
    internal int RenderSeason=>season;
    internal bool ObjectShadowsEnabled {get=>shadows.Enabled;set=>shadows.Enabled=value;}
    internal int GroundSlot(GameState state,int x,int z)=>FloorStrips.At(state,data,x,z,0);
    internal Material GroundMaterial(int slot)=>materials[slot];
    internal TerrainRenderer()
    {
        gallery=new TerrainWalls(soil);
        shadows=new ObjectShadows();
        var cfg=File.ReadAllBytes("dvd:/terrain.cfg");if(cfg.Length!=4)throw new IOException("Invalid floor season config");forced=FloorStrips.Read(cfg,0);if(forced< -1||forced>3)throw new IOException("Invalid floor season");
        for(int i=0;i<materials.Length;i++)materials[i]=new Material{Cull=CullMode.Back,Lighting=LightingMode.Unlit,SecondarySampler=new SamplerState(TextureAddressMode.Repeat,TextureAddressMode.Repeat,TextureFilter.Linear)};
        for(int i=0;i<poses.Length;i++)poses[i]=ScrollTransform;
    }
    public void Dispose()
    {
        if(disposed)return;
        if(GraphicsDevice.Default.IsFrameActive)throw new InvalidOperationException("Dispose terrain after EndFrame");
        // Finish any byte-only read before the discarded session loses its
        // final root. Model/texture creation never runs in this read's worker.
        if(pendingGround!=null){try{pendingGround.GetAwaiter().GetResult();}catch(Exception){if(!pendingGround.IsCompleted)throw;}pendingGround=null;}
        // Session teardown is a blocking lifetime boundary, unlike ordinary
        // frame pumping. Drain only this archive's outstanding DMA ticket.
        while(archive!=null&&!archive.TryDispose()){}
        archive=null;
        for(int i=0;i<sections.Length;i++){sections[i]?.Dispose();sections[i]=null;}
        groundRing?.Dispose();groundRing=null;gallery.Dispose();oceanWaves.Dispose();
        Array.Clear(batch,0,batch.Length);batchCount=0;
        // Cloud/water effects borrow texture bindings, so retire them before
        // releasing the cache leases supplying their ground and seabed art.
        clouds.Dispose();waterMaterial.Dispose();soil.Dispose();
        for(int i=0;i<textures.Length;i++)if(textures[i]!=null){TextureCache.Release(textures[i]!);textures[i]=null;}
        for(int i=0;i<materials.Length;i++){materials[i].Texture=null;materials[i].SecondaryTexture=null;}
        data=new byte[0];cachedTown=null;stream.Reset();disposed=true;
    }
    internal void Prepare(GameState state,int room,int month,VillagerShadows movingShadows,long deadline,bool cliffPreview=false)
    {
        BuiltThisFrame=UrgentStages=0;
        if(waterMaterial.IsSunshine)waterMaterial.PrepareSunshine();
        else waterMaterial.ReleaseCapture();
        waterMaterial.PrepareRoughOcean();
        // Sunlit translucent water is drawn once, not again through the storm
        // mesh, so its alpha never accumulates over the same surface.
        oceanWaves.Prepare(state,waterMaterial.RoughOcean||waterMaterial.IsSunshine,
            cliffPreview,waterMaterial.Tint(1),waterMaterial.IsSunshine);
        int next=TerrainTiles.Season(month,SeasonOverride>=0?SeasonOverride:forced);bool changed=next!=season;
        if(changed){
            string name="floor-"+TerrainTiles.SeasonName(next);data=File.ReadAllBytes("dvd:/"+name+".bin");FloorStrips.Validate(data);
            for(int i=0;i<textures.Length;i++)if(textures[i]!=null){TextureCache.Release(textures[i]!);textures[i]=null;materials[i].Texture=null;}
            slots=FloorStrips.Read(data,12);
            if(FloorStrips.Read(data,4)==2)waterMaterial.SetTerrainTint(FloorStrips.Tint(data,FloorStrips.Read(data,24+TerrainTiles.Water*20)));
            for(int i=0;i<slots;i++){
                int source=FloorStrips.TextureSlot(data,i);bool neutral=FloorStrips.Read(data,4)==2&&source==0;
                if(textures[source]==null){
                    bool preview=neutral&&groundLoad!=GroundLoad.Complete;
                    textures[source]=TextureCache.LoadFloor(preview?"dvd:/floor-outdoor-neutral-64.dndtex":neutral?"dvd:/floor-outdoor-neutral.dndtex":"dvd:/"+name+"-"+source+".dndtex",repeat:neutral,fullMipChain:true);
                    if(preview&&groundLoad==GroundLoad.Unloaded){
                        groundLoad=GroundLoad.Preview;
                        DebugOverlay.WriteLine("GROUND PREVIEW 64 bytes "+textures[source]!.Bytes);
                    }
                }
                materials[i].Sampler=new SamplerState(neutral?TextureAddressMode.Repeat:TextureAddressMode.Clamp,neutral?TextureAddressMode.Repeat:TextureAddressMode.Clamp);
                materials[i].Texture=textures[source]!.Pages[0];uint tint=FloorStrips.Tint(data,i);baseTints[i]=materials[i].Color=new Color32((byte)(tint>>24),(byte)(tint>>16),(byte)(tint>>8));
            }
            season=next;
            MaterialRevision++;
        }
        bool upgraded=AdvanceGroundTexture();
        // The completed task and package arrays are out of the upgrade frame.
        if(upgraded)AnimalTown.GameCube.NativeTexturePages.Collect();
        if(changed||upgraded||appliedGroundMipmaps!=GroundMipmaps||appliedGroundMipDistance!=GroundMipDistance){
            for(int source=0;source<textures.Length;source++)if(textures[source]!=null){
                var texture=textures[source]!.Pages[0];
                var wrap=FloorStrips.Read(data,4)==2&&source==0?TextureWrapMode.Repeat:TextureWrapMode.Mirror;
                bool mip=GroundMipmaps&&texture.MipLevels>1;
                texture.ConfigureSampler(wrap,wrap,mip?TextureMinFilter.LinearMipmapLinear:TextureMinFilter.Linear,TextureMagFilter.Linear,mip?RenderDebugOptions.GroundMipBias(texture.Width,texture.Height,GroundMipDistance):0);
            }
            appliedGroundMipmaps=GroundMipmaps;appliedGroundMipDistance=GroundMipDistance;
        }
        int flat=GroundRingRequested&&!cliffPreview&&FloorStrips.Read(data,4)==2?FloorStrips.Read(data,24):-1;
        if(flat>=0&&FloorStrips.TextureSlot(data,flat)!=0)flat=-1;
        bool nextLegacy=RecreateMeshes||GroundRingRequested;
        bool invalid=resetting||legacy!=nextLegacy||changed||cachedTown!=state.Town||cachedTerrainVersion!=state.Town.TerrainVersion||flat!=SharedFlatMaterial;
        SharedFlatMaterial=flat;
        if(flat<0){groundRing?.Dispose();groundRing=null;}
        if(invalid){
            resetting=true;
            if(archive!=null&&!archive.TryDispose())return;
            archive=null;
            for(int i=0;i<sections.Length;i++){sections[i]?.Dispose();sections[i]=null;}
            stream.Reset();legacy=nextLegacy;
            if(!legacy){archive=new TerrainArchive();archive.Reset();}
            cachedTown=state.Town;cachedTerrainVersion=state.Town.TerrainVersion;
            resetting=false;
        }
        if(GroundRingActive){if(groundRing==null)groundRing=new GroundRingRenderer();groundRing.Prepare(state,data,flat,invalid);}
        var scroll=CylinderWorld.Scroll(0,state.Z);
        ScrollTransform.LocalPosition=new Vector3(0,scroll.Y,scroll.Z);
        ScrollTransform.LocalRotation=new Quaternion(scroll.HalfSin,0,0,scroll.HalfCos);
        if(!legacy){
            // Initial loading can spend 12ms/256 small stages per frame. Walking
            // never runs a build stage, mutates geometry or uploads a mesh.
            if(!archive!.Ready){
                long bakeDeadline=Clock.Timestamp+12000;
                for(int n=0;n<256&&!archive.Ready&&(n==0||Clock.Timestamp<bakeDeadline);n++){archive.Bake(state,this,staging,data);BuiltThisFrame++;}
            }
            long at=FrameProfile.Start();archive.Prepare(state.X,state.Z,Rows,waterMaterial.IsSunshine);FrameProfile.DetailEnd(0,at);
            shadows.Prepare(state);
            int work=0;while(work<8&&Clock.Timestamp<deadline){if(!movingShadows.Advance())break;work++;}
            while(work<8&&Clock.Timestamp<deadline){if(!shadows.Advance())break;work++;}
            gallery.PrepareGallery(state,this,cliffPreview);
            VisibleTriangles=gallery.Triangles+archive.Triangles+oceanWaves.Triangles;visibleGroundMask=archive.GroundMask;RejectedQuads=0;
            return;
        }
        int steps=0;long selectAt=FrameProfile.Start();stream.Prepare(state.X,state.Z);
        shadows.Prepare(state);cliffs.Prepare(state.Town);
        FrameProfile.DetailEnd(0,selectAt);
        // Landscape readiness takes priority. Moving shadows use spare stages
        // after terrain. Masks publish first so an indivisible terrain stage
        // cannot consume their reservation. New receivers publish next frame.
        while(steps<8&&(steps==0||Clock.Timestamp<deadline)){
            if(!AdvanceSection(state))break;steps++;
        }
        // A brief CPU stall must not replace established travel with a loading
        // panel or a hole. Reserve enough stages for the currently missing
        // visible jobs: each has at most eleven stages, including uploads. A fixed
        // sixteen-stage cutoff could leave a hole after a starved diagonal turn.
        int repairs=stream.RepairStages;
        while(stream.Presentable&&stream.MissingVisible>0&&UrgentStages<repairs){if(!AdvanceSection(state))break;UrgentStages++;}
        while(steps<8&&Clock.Timestamp<deadline){if(!movingShadows.Advance())break;steps++;}
        while(steps<8&&Clock.Timestamp<deadline){if(!cliffs.Advance())break;steps++;}
        while(steps<8&&Clock.Timestamp<deadline){if(!shadows.Advance())break;steps++;}
        gallery.PrepareGallery(state,this,cliffPreview);
        VisibleTriangles=gallery.Triangles+oceanWaves.Triangles+(GroundRingActive?GroundRingPlan.IndexCount/3:0);RejectedQuads=0;visibleGroundMask=0;
        for(int i=0;i<stream.Count;i++)if(stream.Visible[i]){
            var section=sections[i]!;VisibleTriangles+=section.Triangles+(waterMaterial.IsSunshine?section.Water.BedTriangles:0);
            visibleGroundMask|=section.GroundMask|section.Walls.GroundMask;
        }
    }
    private bool AdvanceGroundTexture()
    {
        // Draw marks the first preview frame. Its EndFrame has retired before
        // this next Prepare, so even immediately completed reads show the preview.
        if(groundLoad==GroundLoad.Visible){
            try{
                pendingGround=Dolphin.IO.File.ReadAllBytesAsync("dvd:/floor-outdoor-neutral.dndtex");
                groundLoad=GroundLoad.Reading;
                DebugOverlay.WriteLine("GROUND PREVIEW PRESENTED read full");
            }catch(Exception error){GroundTextureFailed(error);}
            return false;
        }
        if(groundLoad!=GroundLoad.Reading||!pendingGround!.IsCompleted)return false;
        CachedTexture full;
        try{
            var source=pendingGround.GetAwaiter().GetResult();
            TexturePixels.Validate(source);
            if(TexturePixels.Width(source)!=FloorTexture.Maximum||TexturePixels.Height(source)!=FloorTexture.Maximum)
                throw new InvalidDataException("Full ground texture must be 256x256");
            full=TextureCache.LoadFloor("dvd:/floor-outdoor-neutral.dndtex",repeat:true,fullMipChain:true,source:source);
        }catch(Exception error){GroundTextureFailed(error);return false;}
        pendingGround=null;
        var preview=textures[0]!;
        int previewBytes=preview.Bytes;
        for(int i=0;i<slots;i++)if(FloorStrips.TextureSlot(data,i)==0)materials[i].Texture=full.Pages[0];
        textures[0]=full;
        groundLoad=GroundLoad.Complete;
        int before=GraphicsDevice.Default.NativeBytes;
        TextureCache.Release(preview,evict:true);
        DebugOverlay.WriteLine("GROUND FULL 256 bytes "+full.Bytes);
        DebugOverlay.WriteLine("GROUND PREVIEW FREED "+previewBytes+" native "+before+" -> "+GraphicsDevice.Default.NativeBytes);
        return true;
    }
    private void GroundTextureFailed(Exception error)
    {
        pendingGround=null;groundLoad=GroundLoad.Failed;
        DebugOverlay.WriteLine("GROUND FULL LOAD FAILED keep preview: "+error.Message);
    }
    internal void WarmCliffs(Town town,long deadline)
    {
        cliffs.Prepare(town);int steps=0;
        while(steps<8&&Clock.Timestamp<deadline){if(!cliffs.Advance())break;steps++;}
    }
    private bool AdvanceSection(GameState state)
    {
        if(stream.BuildSlot<0)return false;
        int building=stream.BuildSlot;long at=FrameProfile.Start();bool publishing=stream.InProgress(building)&&sections[building]!.Publishing;
        if(!stream.InProgress(building)){
            stream.Hold();if(sections[building]==null)sections[building]=new TerrainSection(soil,waterMaterial);
            else FrameProfile.RecycledSections++;
            sections[building]!.Begin(state,stream.X[building],stream.Z[building],Rows);
        }
        bool complete=sections[building]!.Advance(state,this,staging,data);geometryRevision++;BuiltThisFrame++;
        FrameProfile.DetailEnd(publishing?2:1,at);
        if(complete){at=FrameProfile.Start();stream.Complete();stream.Prepare(state.X,state.Z);FrameProfile.DetailEnd(0,at);}
        return true;
    }
    internal void CycleCloudCover()=>clouds.Cover=clouds.Cover==.5f?0:clouds.Cover==0?1:.5f;
    internal float CloudCover=>clouds.Cover;
    internal bool RoughOcean {get=>waterMaterial.RoughOcean;set=>waterMaterial.RoughOcean=value;}
    internal void Animate(double seconds)
    {
        long at=FrameProfile.Start();
        if(!FreezeMotion){waterMaterial.Animate(seconds);oceanWaves.Update(seconds);clouds.Animate(seconds);}
        for(int i=0;i<slots;i++)clouds.Apply(materials[i],baseTints[i],true);
        clouds.ReleaseUnused(materials);
        FrameProfile.DetailEnd(3,at);
    }
    private void Add(Renderer renderer,Material material,Mesh? mesh)
    {
        if(mesh==null)return;batch[batchCount++]=mesh;
        if(batchCount==batch.Length)Flush(renderer,material);
    }
    private void Flush(Renderer renderer,Material material)
    {if(batchCount==0)return;renderer.DrawMeshes(batch,poses,material,0,batchCount);batchCount=0;}
    private void AddArchive(Renderer renderer,Material material,int kind)
    {for(int i=archive!.First(kind);i>=0;i=archive.Next(i))Add(renderer,material,archive.Mesh(i));Flush(renderer,material);}
    private void AddLegacyBed(Renderer renderer)
    {
        for(int i=0;i<stream.Count;i++)if(stream.Visible[i])Add(renderer,waterMaterial.Bed,sections[i]!.Water.BedMesh);
        Flush(renderer,waterMaterial.Bed);
    }
    private void DrawArchiveWater(Renderer renderer)
    {
        // The original mode retains its exact draw count. Sunshine first lays
        // a very thin tropical tint over the shared dark-blue bed, then its
        // alpha-cutout caustic/reflection layer and the original shore foam.
        if(waterMaterial.IsSunshine){
            AddArchive(renderer,waterMaterial.SunshineBody,104);
            AddArchive(renderer,waterMaterial.SunshineBody,107);
        }
        AddArchive(renderer,waterMaterial.Surface,104);
        AddArchive(renderer,waterMaterial.OceanSurface,107);
        oceanWaves.Draw(renderer,waterMaterial.IsSunshine?waterMaterial.CloseWaves:waterMaterial.OceanSurface,ScrollTransform);
        AddArchive(renderer,waterMaterial.Foam,105);
    }
    private void DrawLegacyWater(Renderer renderer)
    {
        if(waterMaterial.IsSunshine){
            for(int i=0;i<stream.Count;i++)if(stream.Visible[i])Add(renderer,waterMaterial.SunshineBody,sections[i]!.Water.SurfaceMesh);
            Flush(renderer,waterMaterial.SunshineBody);
            for(int i=0;i<stream.Count;i++)if(stream.Visible[i])Add(renderer,waterMaterial.SunshineBody,sections[i]!.Water.OceanMesh);
            Flush(renderer,waterMaterial.SunshineBody);
        }
        NativeShadows.Scope(3);
        try{
            for(int i=0;i<stream.Count;i++)if(stream.Visible[i])Add(renderer,waterMaterial.Surface,sections[i]!.Water.SurfaceMesh);
            Flush(renderer,waterMaterial.Surface);
        }finally{NativeShadows.Scope(0);}
        for(int i=0;i<stream.Count;i++)if(stream.Visible[i])Add(renderer,waterMaterial.OceanSurface,sections[i]!.Water.OceanMesh);
        Flush(renderer,waterMaterial.OceanSurface);
        oceanWaves.Draw(renderer,waterMaterial.IsSunshine?waterMaterial.CloseWaves:waterMaterial.OceanSurface,ScrollTransform);
        for(int i=0;i<stream.Count;i++)if(stream.Visible[i])Add(renderer,waterMaterial.Foam,sections[i]!.Water.FoamMesh);
        Flush(renderer,waterMaterial.Foam);
    }

    internal bool RefractionVisible
    {
        get {
            if(!waterMaterial.IsSunshine||!Ready)return false;
            if(!legacy)return archive!.First(104)>=0||archive.First(107)>=0;
            for(int i=0;i<stream.Count;i++)if(stream.Visible[i]&&
                (sections[i]!.Water.SurfaceMesh!=null||sections[i]!.Water.OceanMesh!=null))return true;
            return false;
        }
    }
    internal void CaptureWaterScene()=>waterMaterial.CaptureScene();
    internal void ReleaseWaterCapture()=>waterMaterial.ReleaseCapture();
    internal bool TakeWaterPreparationFailure(out string error)=>waterMaterial.TakePreparationFailure(out error);
    internal void DrawRefraction(Renderer renderer)
    {
        var material=waterMaterial.Refraction;
        if(!legacy){AddArchive(renderer,material,104);AddArchive(renderer,material,107);return;}
        for(int i=0;i<stream.Count;i++)if(stream.Visible[i])Add(renderer,material,sections[i]!.Water.SurfaceMesh);
        Flush(renderer,material);
        for(int i=0;i<stream.Count;i++)if(stream.Visible[i])Add(renderer,material,sections[i]!.Water.OceanMesh);
        Flush(renderer,material);
    }

    internal void Draw(Renderer renderer,Transform parent,int room)
    {
        if(groundLoad==GroundLoad.Preview)groundLoad=GroundLoad.Visible;
        bool sunshine=waterMaterial.IsSunshine;
        if(!legacy){
            if(sunshine){
                // Solid blue merged beds first, then land, translucent water
                // and shoreline foam. All geometry uses the shared scroll.
                AddArchive(renderer,waterMaterial.Bed,WaterRenderer.BedKind);
            }else DrawArchiveWater(renderer);
            AddArchive(renderer,soil.Material,106);
            NativeShadows.Scope(1);
            try{for(int slot=0;slot<slots;slot++)if((visibleGroundMask&(1UL<<slot))!=0)AddArchive(renderer,materials[slot],slot);}
            finally{NativeShadows.Scope(0);}
            gallery.Draw(renderer,ScrollTransform,this);
            if(sunshine)DrawArchiveWater(renderer);
            return;
        }
        if(sunshine)AddLegacyBed(renderer);
        else DrawLegacyWater(renderer);
        for(int i=0;i<stream.Count;i++)if(stream.Visible[i])Add(renderer,soil.Material,sections[i]!.Walls.SoilMesh);
        Flush(renderer,soil.Material);
        NativeShadows.Scope(1);
        try{
            if(GroundRingActive)groundRing!.Draw(renderer,materials[SharedFlatMaterial],ScrollTransform);
            for(int slot=0;slot<slots;slot++){
                ulong mask=1UL<<slot;if((visibleGroundMask&mask)==0)continue;
                for(int i=0;i<stream.Count;i++)if(stream.Visible[i]){
                    var section=sections[i]!;
                    if((section.GroundMask&mask)!=0)Add(renderer,materials[slot],section.GroundMesh(slot));
                    if((section.Walls.GroundMask&mask)!=0)Add(renderer,materials[slot],section.Walls.GroundMesh(slot));
                }
                Flush(renderer,materials[slot]);
            }
        }finally{NativeShadows.Scope(0);}
        gallery.Draw(renderer,ScrollTransform,this);
        if(sunshine)DrawLegacyWater(renderer);
    }
}
