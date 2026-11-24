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
internal sealed partial class TerrainRenderer : IDisposable
{
    private readonly PathTextureResidency paths=new PathTextureResidency();
    private readonly GroundMipMaterials mipBands=new GroundMipMaterials();
    private readonly SnowGroundVisibility snowVisibility=new SnowGroundVisibility();
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
    private readonly Material[] materials=new Material[FloorStrips.MaximumMaterials];
    private readonly CachedTexture?[] textures=new CachedTexture?[FloorStrips.MaximumMaterials];
    private enum GroundLoad { Unloaded,Preview,Visible,Reading,Complete,Failed }
    private GroundLoad groundLoad;
    private Task<byte[]>? pendingGround;
    private readonly Mesh[] batch=new Mesh[128];
    internal const float SnowHeight=SnowVisibility.Height;
    internal static readonly Color32 SnowReceiver=new Color32(255,255,255,254);
    private readonly Transform snowTransform=new Transform();
    private readonly Transform[] poses=new Transform[128],snowPoses=new Transform[128];
    private int batchCount;
    private ulong visibleGroundMask,snowGroundMask;
    private long geometryRevision;
    internal long GeometryRevision=>geometryRevision;
    internal bool Ready=>!resetting&&!floorTexturesSuspended&&snowMaskReady&&(legacy?stream.Presentable:archive?.Presentable??false);
    internal bool ArchiveReady=>archive?.Ready??false;
    internal bool CacheSuspended=>archive?.CacheSuspended??false;
    internal int CacheResidencyError=>archive?.ResidencyError??0;
    private bool resetting;
    internal bool SuspendOutdoorCache()
    {
        if(!legacy&&archive!=null&&!archive.Suspend())return false;
        SuspendFloorTextures();return SuspendSnowMask();
    }
    // A not-yet-baked landscape follows the existing initial loading path.
    // Only a previously built cold cache must be prefetched before door commit.
    internal bool PrepareOutdoorResidency(float x,float z,long deadline,bool refreshSelection=false)
    {
        if(archive!=null)archive.Memory=Memory;
        if(!RestoreFloorTextures(deadline))return false;
        if(cachedTown!=null&&(Clock.Timestamp>=deadline||!PrepareSnowMask(cachedTown)))return false;
        return legacy||archive==null||!archive.Ready||(!refreshSelection&&archive.Presentable)||archive.PrepareAsync(x,z,Rows,deadline);
    }
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
    internal bool ShowGroundMipBands {get=>mipBands.Enabled;set=>mipBands.Enabled=value;}
    internal int RenderSeason=>season;
    internal int SeasonAt(int month,int preview)=>TerrainTiles.Season(month,preview>=0?preview:forced);
    internal int BaseGroundSlot(GameState state,int x,int z)=>FloorStrips.BaseAt(state,data,x,z,0);
    internal int GroundSlot(GameState state,int x,int z)=>FloorStrips.BaseAt(state,data,x,z,0);
    internal Material GroundMaterial(int slot)=>mipBands.Select(slot,materials[slot]);
    internal bool SnowMaterial(int slot)=>(snowGroundMask&(1UL<<slot))!=0;
    internal static bool SnowTile(Town town,int x,int z)=>SnowWear.Covers(town,x,z);
    internal TerrainRenderer()
    {
        gallery=new TerrainWalls(soil);
        shadows=new ObjectShadows();
        var cfg=File.ReadAllBytes(ContentPaths.Asset("terrain.cfg"));if(cfg.Length!=4)throw new IOException("Invalid floor season config");forced=FloorStrips.Read(cfg,0);if(forced< -1||forced>3)throw new IOException("Invalid floor season");
        for(int i=0;i<materials.Length;i++)materials[i]=new Material{Cull=CullMode.Back,Lighting=LightingMode.Unlit};
        // The terrain archive is baked around the cylinder axis at Y=-Radius.
        // Scaling Y/Z about that axis offsets level ground by SnowHeight.
        float raised=(CylinderWorld.Radius+SnowHeight)/CylinderWorld.Radius;
        snowTransform.Parent=ScrollTransform;
        snowTransform.LocalScale=new Vector3(1,raised,raised);
        snowTransform.LocalPosition=new Vector3(0,SnowHeight,0);
        for(int i=0;i<poses.Length;i++){poses[i]=ScrollTransform;snowPoses[i]=snowTransform;}
    }
    public void Dispose()
    {
        if(disposed)return;
        if(GraphicsDevice.Default.IsFrameActive)throw new InvalidOperationException("Dispose terrain after EndFrame");
        // Finish any byte-only read before the discarded session loses its
        // final root. Model/texture creation never runs in this read's worker.
        if(pendingGround!=null){try{pendingGround.GetAwaiter().GetResult();}catch(Exception){if(!pendingGround.IsCompleted)throw;}pendingGround=null;}
        if(pendingFloor!=null){try{pendingFloor.GetAwaiter().GetResult();}catch(Exception){if(!pendingFloor.IsCompleted)throw;}pendingFloor=null;}
        CancelFloorReservations();
        // Session teardown is a blocking lifetime boundary, unlike ordinary
        // frame pumping. Drain only this archive's outstanding DMA ticket.
        while(archive!=null&&!archive.TryDispose()){}
        archive=null;
        for(int i=0;i<sections.Length;i++){sections[i]?.Dispose();sections[i]=null;}
        groundRing?.Dispose();groundRing=null;gallery.Dispose();oceanWaves.Dispose();
        Array.Clear(batch,0,batch.Length);batchCount=0;
        // Cloud/water effects borrow texture bindings, so retire them before
        // releasing the cache leases supplying their ground art.
        ReleasePaths();materials[TownPaths.Material].Texture=null;paths.Dispose();mipBands.Dispose();
        waterMaterial.Dispose();soil.Dispose();
        for(int i=0;i<textures.Length;i++)if(textures[i]!=null){TextureCache.Release(textures[i]!);textures[i]=null;}
        for(int i=0;i<materials.Length;i++){materials[i].Texture=null;materials[i].SecondaryTexture=null;}
        data=new byte[0];cachedTown=null;stream.Reset();disposed=true;
    }
    internal void Prepare(GameState state,int room,int month,VillagerShadows movingShadows,long deadline,bool cliffPreview=false)
    {
        if(archive!=null)archive.Memory=Memory;
        BuiltThisFrame=UrgentStages=0;
        waterMaterial.PrepareRoughOcean();
        // Only rough ocean needs the small dynamic wave patch.
        oceanWaves.Prepare(state,waterMaterial.RoughOcean,
            cliffPreview,waterMaterial.Tint(1));
        int next=SeasonAt(month,SeasonOverride);bool changed=next!=season;
        if(changed){
            string name="floor-"+TerrainTiles.SeasonName(next);
            BeginFloorSeason(File.ReadAllBytes(ContentPaths.Asset(name+".bin")),next);
            if(FloorStrips.Read(data,4)==2)waterMaterial.SetTerrainTint(FloorStrips.Tint(data,FloorStrips.Read(data,24+TerrainTiles.Water*20)));
            snowGroundMask=0;
            if(next==3){
                // Geometry tags distinguish grass from pads sharing its tint.
                int grass=20+TerrainTiles.Grass*20;
                for(int variant=0;variant<FloorStrips.Read(data,grass);variant++)
                    snowGroundMask|=1UL<<FloorStrips.Read(data,grass+4+variant*4);
            }
        }
        if(FloorGeometryChanged&&!floorGeometryRetired){
            // An old season's cache must not hold the headroom needed for the
            // new season's essential floor pages. Keep the rebuild pending
            // across byte-read frames, without creating the new cache yet.
            resetting=true;
            if(archive!=null&&!archive.TryDispose())return;
            archive=null;
            for(int i=0;i<sections.Length;i++){sections[i]?.Dispose();sections[i]=null;}
            stream.Reset();groundRing?.Dispose();groundRing=null;
            floorGeometryRetired=true;
        }
        if(floorTexturesSuspended&&!RestoreFloorTextures(deadline))return;
        var snow=state.Town.Traffic.Snow;
        snow.SetWinter(season==3);
        if(!PrepareSnowMask(state.Town))return;
        snowVisibility.Prepare(state.Town);
        bool upgraded=AdvanceGroundTexture();
        // The completed task and package arrays are out of the upgrade frame.
        if(upgraded)AnimalTown.GameCube.NativeTexturePages.Collect();
        if(changed||upgraded){
            for(int source=0;source<textures.Length;source++)if(textures[source]!=null){
                var texture=textures[source]!.Pages[0];
                var wrap=FloorStrips.Read(data,4)==2&&source==0?TextureWrapMode.Repeat:TextureWrapMode.Mirror;
                texture.ConfigureSampler(wrap,wrap,TextureMinFilter.Linear,TextureMagFilter.Linear,0);
            }
        }
        int flat=GroundRingRequested&&!cliffPreview&&FloorStrips.Read(data,4)==2?FloorStrips.Read(data,24):-1;
        if(flat>=0&&FloorStrips.TextureSlot(data,flat)!=0)flat=-1;
        bool nextLegacy=RecreateMeshes||GroundRingRequested;
        bool invalid=resetting||legacy!=nextLegacy||FloorGeometryChanged||cachedTown!=state.Town||cachedTerrainVersion!=state.Town.TerrainVersion||flat!=SharedFlatMaterial;
        SharedFlatMaterial=flat;
        if(flat<0){groundRing?.Dispose();groundRing=null;}
        if(invalid){
            resetting=true;
            if(archive!=null&&!archive.TryDispose())return;
            archive=null;
            for(int i=0;i<sections.Length;i++){sections[i]?.Dispose();sections[i]=null;}
            stream.Reset();legacy=nextLegacy;
            if(!legacy){archive=new TerrainArchive{Memory=Memory};archive.Reset();}
            if(cachedTown!=state.Town)ReleasePaths();
            cachedTown=state.Town;cachedTerrainVersion=state.Town.TerrainVersion;
            floorGeometrySeason=season;
            resetting=false;
        }
        if(GroundRingActive){if(groundRing==null)groundRing=new GroundRingRenderer();groundRing.Prepare(state,data,flat,invalid,SnowMaterial(flat));}
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
            long at=FrameProfile.Start();archive.Prepare(state.X,state.Z,Rows);FrameProfile.DetailEnd(0,at);
            shadows.Prepare(state);
            int work=0;while(work<8&&Clock.Timestamp<deadline){if(!movingShadows.Advance())break;work++;}
            while(work<8&&Clock.Timestamp<deadline){if(!shadows.Advance())break;work++;}
            gallery.PrepareGallery(state,this,cliffPreview);
            VisibleTriangles=gallery.Triangles+archive.Triangles+oceanWaves.Triangles;visibleGroundMask=archive.GroundMask;RejectedQuads=0;
            PreparePaths(state);
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
            var section=sections[i]!;VisibleTriangles+=section.Triangles;
            visibleGroundMask|=section.GroundMask|section.Walls.GroundMask;
        }
        PreparePaths(state);
    }
    private bool pathWasReady;
    private static readonly byte[] noPaths=new byte[0];
    private void ClearPathBindings(){for(int i=0;i<slots;i++)materials[i].SecondaryTexture=null;}
    private ulong PathMaterials()
    {
        ulong mask=0;
        for(int surface=0;surface<TerrainTiles.SurfaceCount;surface++){
            bool eligible=surface==TerrainTiles.Grass||surface==TerrainTiles.Dirt;
            for(int i=0;i<FloorStrips.Read(data,280)&&!eligible;i++){int p=FloorStrips.Records+i*28;int required=FloorStrips.Read(data,p+4);if(FloorStrips.Read(data,p)==surface&&(required==TerrainTiles.Grass||required==TerrainTiles.Dirt))eligible=true;}
            if(eligible){int at=20+surface*20;for(int v=0;v<FloorStrips.Read(data,at);v++)mask|=1UL<<FloorStrips.Read(data,at+4+v*4);}
        }
        return mask;
    }
    private void PreparePaths(GameState state)
    {
        bool demand=!legacy?archive!.HasPaths(state.Town):false;
        if(legacy)for(int i=0;i<stream.Count&&!demand;i++)if(stream.Visible[i]&&state.Town.Paths.Any(stream.X[i],stream.Z[i],TerrainStream.Width,TerrainStream.Height))demand=true;
        ClearPathBindings();if(!demand)mipBands.Forget(paths.Page);
        paths.Prepare(demand,materials[TownPaths.Material],null);
        bool ready=demand&&paths.Page!=null;
        if(NativePaths.Prepare(state.Town.Paths.Packed,state.Town.Traffic.Visits,ready?1:0,state.Town.Paths.Revision,state.Town.Traffic.OpacityRevision)!=1)throw new InvalidOperationException("Path mask native budget");
        if(ready){ulong mask=PathMaterials();if(!pathWasReady)DebugOverlay.WriteLine("PATH BLEND READY slots "+mask+" cells "+state.Town.Paths.Count);for(int i=0;i<slots;i++)if((mask&(1UL<<i))!=0){materials[i].SecondaryTexture=paths.Page;materials[i].SecondarySampler=materials[TownPaths.Material].Sampler;}}
        pathWasReady=ready;
        mipBands.Prepare(materials,slots,TownPaths.Material,GroundMipmaps,GroundMipDistance);
    }
    internal void ReleasePaths()
    {
        pathWasReady=false;
        ClearPathBindings();mipBands.Forget(paths.Page);
        if(NativePaths.Prepare(noPaths,noPaths,0,0,0)!=1)throw new InvalidOperationException("Path mask release failed");
        paths.Prepare(false,materials[TownPaths.Material],null);
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
    internal void AnimateShadows(double seconds,int month,int sunlight)=>clouds.Animate(seconds,TerrainTiles.Season(month,SeasonOverride>=0?SeasonOverride:forced),sunlight);
    internal bool RoughOcean {get=>waterMaterial.RoughOcean;set=>waterMaterial.RoughOcean=value;}
    internal void Animate(double seconds)
    {
        long at=FrameProfile.Start();
        if(!FreezeMotion){waterMaterial.Animate(seconds);oceanWaves.Update(seconds);}
        FrameProfile.DetailEnd(3,at);
    }
    private void Add(Renderer renderer,Material material,Mesh? mesh,bool snow=false)
    {
        if(mesh==null)return;batch[batchCount++]=mesh;
        if(batchCount==batch.Length)Flush(renderer,material,snow);
    }
    private void Flush(Renderer renderer,Material material,bool snow=false)
    {if(batchCount==0)return;renderer.DrawMeshes(batch,snow?snowPoses:poses,material,0,batchCount);batchCount=0;}
    private void AddArchive(Renderer renderer,Material material,int kind,bool snow=false)
    {
        for(int i=archive!.First(kind);i>=0;i=archive.Next(i)){
            bool strip=archive.IsGroundStrip(i);
            if(snow&&!strip)continue;
            // Only level, uninterrupted snow pages may omit their hidden base.
            // Caps, walls, mixed terrain, paths and diagnostic passes remain.
            if(!snow&&strip&&!mipBands.Enabled&&SnowMaterial(kind)&&snowVisibility.Covered(archive.Section(i)))continue;
            Add(renderer,material,archive.Mesh(i),snow);
        }
        Flush(renderer,material,snow);
    }
    private void DrawArchiveWater(Renderer renderer)
    {
        AddArchive(renderer,waterMaterial.Surface,104);
        AddArchive(renderer,waterMaterial.OceanSurface,107);
        oceanWaves.Draw(renderer,waterMaterial.OceanSurface,ScrollTransform);
        AddArchive(renderer,waterMaterial.Foam,105);
    }
    private void DrawLegacyWater(Renderer renderer)
    {
        NativeShadows.Scope(3);
        try{
            for(int i=0;i<stream.Count;i++)if(stream.Visible[i])Add(renderer,waterMaterial.Surface,sections[i]!.Water.SurfaceMesh);
            Flush(renderer,waterMaterial.Surface);
        }finally{NativeShadows.Scope(0);}
        for(int i=0;i<stream.Count;i++)if(stream.Visible[i])Add(renderer,waterMaterial.OceanSurface,sections[i]!.Water.OceanMesh);
        Flush(renderer,waterMaterial.OceanSurface);
        oceanWaves.Draw(renderer,waterMaterial.OceanSurface,ScrollTransform);
        for(int i=0;i<stream.Count;i++)if(stream.Visible[i])Add(renderer,waterMaterial.Foam,sections[i]!.Water.FoamMesh);
        Flush(renderer,waterMaterial.Foam);
    }

    private void DrawGround(Renderer renderer,bool snow)
    {
        for(int drawSlot=0;drawSlot<=slots;drawSlot++){
            int slot=drawSlot==slots?TownPaths.Material:drawSlot;
            ulong mask=1UL<<slot;
            if((visibleGroundMask&mask)==0||(snow&&!SnowMaterial(slot)))continue;
            if(!legacy){AddArchive(renderer,GroundMaterial(slot),slot,snow);continue;}
            for(int i=0;i<stream.Count;i++)if(stream.Visible[i]){
                var section=sections[i]!;
                if((section.GroundMask&mask)!=0)Add(renderer,GroundMaterial(slot),section.GroundMesh(slot),snow);
                if(!snow&&(section.Walls.GroundMask&mask)!=0)Add(renderer,GroundMaterial(slot),section.Walls.GroundMesh(slot));
            }
            Flush(renderer,GroundMaterial(slot),snow);
        }
    }

    internal void Draw(Renderer renderer,Transform parent,int room)
    {
        if(groundLoad==GroundLoad.Preview)groundLoad=GroundLoad.Visible;
        if(legacy){
            DrawLegacyWater(renderer);
            for(int i=0;i<stream.Count;i++)if(stream.Visible[i])Add(renderer,soil.Material,sections[i]!.Walls.SoilMesh);
            Flush(renderer,soil.Material);
        }else{
            DrawArchiveWater(renderer);
            AddArchive(renderer,soil.Material,106);
        }
        NativeShadows.Scope(mipBands.Enabled?6:1);
        try{
            if(GroundRingActive)groundRing!.Draw(renderer,GroundMaterial(SharedFlatMaterial),ScrollTransform);
            DrawGround(renderer,false);
            if(!mipBands.Enabled&&(visibleGroundMask&snowGroundMask)!=0){
                NativeShadows.Scope(4);
                DrawGround(renderer,true);
            }
            if(!mipBands.Enabled&&GroundRingActive&&SnowMaterial(SharedFlatMaterial)){
                NativeShadows.Scope(5); // The ring spans multiple detailed shadow pages.
                groundRing!.Draw(renderer,GroundMaterial(SharedFlatMaterial),snowTransform);
            }
        }finally{NativeShadows.Scope(0);}
        gallery.Draw(renderer,ScrollTransform,this);
    }
}
