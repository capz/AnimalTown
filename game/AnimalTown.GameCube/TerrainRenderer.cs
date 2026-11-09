using System;
using System.IO;
using Dolphin.Timing;
using AnimalTown;
using Dolphin.Graphics;
using Hypercube.GameCube;

// Ordinary play draws immutable sections baked once at town/season load.
// The old rolling builder remains available only for explicit profiler controls.
internal sealed class TerrainRenderer
{
    private TerrainArchive? archive;
    private bool legacy;
    internal TerrainSection CreateSection()=>new TerrainSection(soil,waterMaterial);
    private readonly TerrainStream stream=new TerrainStream();
    private readonly TerrainSection?[] sections=new TerrainSection?[TerrainStream.Capacity];
    private readonly FloorStrips staging=new FloorStrips(TerrainStream.Width*TerrainStream.Height);
    private readonly TerrainSoil soil=new TerrainSoil();
    private readonly WaterMaterial waterMaterial=new WaterMaterial();
    private readonly TerrainWalls gallery;
    internal readonly Transform ScrollTransform=new Transform();
    private readonly CloudShadows clouds=new CloudShadows();
    private readonly ObjectShadows shadows;
    private readonly CliffShadows cliffs=new CliffShadows();
    private readonly Color32[] baseTints=new Color32[FloorStrips.MaximumMaterials];
    private readonly Material[] materials=new Material[FloorStrips.MaximumMaterials];
    private readonly CachedTexture?[] textures=new CachedTexture?[FloorStrips.MaximumMaterials];
    private readonly Mesh[] batch=new Mesh[128];
    private readonly Transform[] poses=new Transform[128];
    private int batchCount;
    private ulong visibleGroundMask;
    private long geometryRevision;
    internal long GeometryRevision=>geometryRevision;
    internal bool Ready=>legacy?stream.Presentable:archive?.Ready??false;
    internal int LoadingRemaining=>legacy?stream.MissingWanted:archive?.Remaining??TerrainArchivePlan.Count;
    internal int LoadingTotal=>legacy?stream.WantedCount:TerrainArchivePlan.Count;
    internal int MissingVisible=>legacy?stream.MissingVisible:Ready?0:1;
    internal int UrgentStages {get;private set;}
    internal bool FreezeMotion,RecreateMeshes;
    internal CylinderRows Rows=>stream.CylinderRows;
    private byte[] data=new byte[0];
    private int season=-1,forced,slots,cachedTerrainVersion=-1;
    private Town? cachedTown;
    private GroundRingRenderer? groundRing;
    internal bool GroundRingRequested {get;private set;}
    internal int SharedFlatMaterial {get;private set;}=-1;
    internal bool GroundRingActive=>SharedFlatMaterial>=0;
    internal bool GroundRingUploaded=>groundRing?.Uploaded??false;
    internal void ToggleGroundRing(){GroundRingRequested=!GroundRingRequested;}
    internal int VisibleTriangles,RejectedQuads;
    internal int BuiltThisFrame {get;private set;}
    internal int MaterialRevision {get;private set;}
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
    internal void Prepare(GameState state,int room,int month,VillagerShadows movingShadows,long deadline,bool cliffPreview=false)
    {
        BuiltThisFrame=UrgentStages=0;
        int next=TerrainTiles.Season(month,forced);bool changed=next!=season;
        if(changed){
            string name="floor-"+TerrainTiles.SeasonName(next);data=File.ReadAllBytes("dvd:/"+name+".bin");FloorStrips.Validate(data);
            for(int i=0;i<textures.Length;i++)if(textures[i]!=null){TextureCache.Release(textures[i]!);textures[i]=null;materials[i].Texture=null;}
            slots=FloorStrips.Read(data,12);
            if(FloorStrips.Read(data,4)==2)waterMaterial.SetTerrainTint(FloorStrips.Tint(data,FloorStrips.Read(data,24+TerrainTiles.Water*20)));
            for(int i=0;i<slots;i++){
                int source=FloorStrips.TextureSlot(data,i);bool neutral=FloorStrips.Read(data,4)==2&&source==0;
                if(textures[source]==null)textures[source]=TextureCache.LoadFloor(neutral?"dvd:/floor-outdoor-neutral.dndtex":"dvd:/"+name+"-"+source+".dndtex",repeat:neutral);
                materials[i].Sampler=new SamplerState(neutral?TextureAddressMode.Repeat:TextureAddressMode.Clamp,neutral?TextureAddressMode.Repeat:TextureAddressMode.Clamp);
                materials[i].Texture=textures[source]!.Pages[0];uint tint=FloorStrips.Tint(data,i);baseTints[i]=materials[i].Color=new Color32((byte)(tint>>24),(byte)(tint>>16),(byte)(tint>>8));
            }
            season=next;
            MaterialRevision++;
        }
        int flat=GroundRingRequested&&!cliffPreview&&FloorStrips.Read(data,4)==2?FloorStrips.Read(data,24):-1;
        if(flat>=0&&FloorStrips.TextureSlot(data,flat)!=0)flat=-1;
        bool nextLegacy=RecreateMeshes||GroundRingRequested;
        bool invalid=legacy!=nextLegacy||changed||cachedTown!=state.Town||cachedTerrainVersion!=state.Town.TerrainVersion||flat!=SharedFlatMaterial;
        SharedFlatMaterial=flat;
        if(flat<0){groundRing?.Dispose();groundRing=null;}
        if(invalid){
            archive?.Dispose();archive=null;
            for(int i=0;i<sections.Length;i++){sections[i]?.Dispose();sections[i]=null;}
            stream.Reset();legacy=nextLegacy;
            if(!legacy){archive=new TerrainArchive();archive.Reset();}
            cachedTown=state.Town;cachedTerrainVersion=state.Town.TerrainVersion;
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
            long at=FrameProfile.Start();archive.Prepare(state.X,state.Z,Rows);FrameProfile.DetailEnd(0,at);
            shadows.Prepare(state);
            int work=0;while(work<8&&Clock.Timestamp<deadline){if(!movingShadows.Advance())break;work++;}
            while(work<8&&Clock.Timestamp<deadline){if(!shadows.Advance())break;work++;}
            gallery.PrepareGallery(state,this,cliffPreview);
            VisibleTriangles=gallery.Triangles+archive.Triangles;visibleGroundMask=archive.GroundMask;RejectedQuads=0;
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
        VisibleTriangles=gallery.Triangles+(GroundRingActive?GroundRingPlan.IndexCount/3:0);RejectedQuads=0;visibleGroundMask=0;
        for(int i=0;i<stream.Count;i++)if(stream.Visible[i]){
            var section=sections[i]!;VisibleTriangles+=section.Triangles;
            visibleGroundMask|=section.GroundMask|section.Walls.GroundMask;
        }
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
    internal void Animate(double seconds)
    {
        if(FreezeMotion)return;long at=FrameProfile.Start();
        waterMaterial.Animate(seconds);clouds.Animate(seconds);
        for(int i=0;i<slots;i++)clouds.Apply(materials[i],baseTints[i],true);
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
    internal void Draw(Renderer renderer,Transform parent,int room)
    {
        if(!legacy){
            AddArchive(renderer,waterMaterial.Surface,104);AddArchive(renderer,waterMaterial.Foam,105);AddArchive(renderer,soil.Material,106);
            NativeShadows.Scope(1);
            try{for(int slot=0;slot<slots;slot++)if((visibleGroundMask&(1UL<<slot))!=0)AddArchive(renderer,materials[slot],slot);}
            finally{NativeShadows.Scope(0);}
            gallery.Draw(renderer,ScrollTransform,this);return;
        }
        // Keep water below banks, and transparent foam below opaque soil/lips.
        NativeShadows.Scope(3);
        try{
            for(int i=0;i<stream.Count;i++)if(stream.Visible[i])Add(renderer,waterMaterial.Surface,sections[i]!.Water.SurfaceMesh);
            Flush(renderer,waterMaterial.Surface);
        }finally{NativeShadows.Scope(0);}
        for(int i=0;i<stream.Count;i++)if(stream.Visible[i])Add(renderer,waterMaterial.Foam,sections[i]!.Water.FoamMesh);
        Flush(renderer,waterMaterial.Foam);
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
        }}finally{NativeShadows.Scope(0);}
        gallery.Draw(renderer,ScrollTransform,this);
    }
}
