using System;
using System.IO;
using AnimalTown;
using Dolphin.Graphics;

// Camera motion changes one transform. Patches own baked, shared-edge geometry;
// only terrain/material changes or newly exposed patches need vertex uploads.
internal sealed class TerrainRenderer
{
    internal const int ChunkSize=8;
    private const int CacheCapacity=96,GridMinimum=-80,GridEdge=40,TargetBytes=3*1024*1024,MaximumCandidates=512;
    private readonly TerrainChunk?[] chunks=new TerrainChunk?[CacheCapacity];
    private readonly int[] lookup=new int[GridEdge*GridEdge];
    private readonly int[] wantedX=new int[MaximumCandidates],wantedZ=new int[MaximumCandidates],visible=new int[CacheCapacity];
    private readonly int[] prefetchOrder=new int[CacheCapacity];
    private int prefetchCount;
    private readonly bool[] wantedVisible=new bool[MaximumCandidates];
    private readonly float[] distances=new float[MaximumCandidates];
    private readonly Mesh[] drawMeshes=new Mesh[CacheCapacity];
    private readonly Transform[] drawPoses=new Transform[CacheCapacity];
    private readonly Transform worldPose=new Transform();
    private readonly CylinderRows rows=new CylinderRows();
    private readonly WaterDepthCache depth=new WaterDepthCache();
    private readonly WaterMaterial waterMaterial=new WaterMaterial();
    private readonly CloudShadows clouds=new CloudShadows();
    private readonly FloorGeometry indoor=new FloorGeometry(6);
    private readonly FloorStrips indoorStrips=new FloorStrips(1);
    private readonly Color32[] baseTints=new Color32[FloorStrips.MaximumMaterials];
    private readonly Material[] materials=new Material[FloorStrips.MaximumMaterials];
    private readonly CachedTexture?[] textures=new CachedTexture?[FloorStrips.MaximumMaterials];
    private readonly Material soil=new Material{Cull=CullMode.Back,Lighting=LightingMode.Unlit};
    private CachedTexture? soilTexture;
    private byte[] data=new byte[0];
    private Town? town;
    private GroundRingRenderer? groundRing;
    private int patchBytes;
    internal bool GroundRingRequested {get;private set;}
    internal int SharedFlatMaterial {get;private set;}=-1;
    internal bool GroundRingActive=>SharedFlatMaterial>=0;
    internal bool GroundRingUploaded=>groundRing?.Uploaded??false;
    internal int ResidentBytes=>patchBytes+(groundRing?.ResidentBytes??0);
    internal void ToggleGroundRing(){GroundRingRequested=!GroundRingRequested;}
    private int season=-1,forced,slots,terrainVersion=-1,generation,selectionStamp,clock,wantedCount,visibleCount,previewX,previewZ,indoorTier=-1,indoorRevision=-1;
    private float viewX=float.NaN,viewZ,viewHeight;
    private bool preview;
    private ulong floorMask,wallMask;
    internal int VisibleTriangles,RejectedQuads,BuiltThisFrame;
    internal int MaterialRevision {get;private set;}
    internal byte[] Data=>data;
    internal int GroundSlot(GameState state,int x,int z)=>FloorStrips.At(state,data,x,z,0);
    internal void EnsureSoil(){if(soilTexture!=null)return;soilTexture=TextureCache.LoadFloor("dvd:/cliff-soil.dndtex",repeat:true,repeatV:false);soil.Texture=soilTexture.Pages[0];}
    internal TerrainRenderer()
    {
        var cfg=File.ReadAllBytes("dvd:/terrain.cfg");if(cfg.Length!=4)throw new IOException("Invalid floor season config");forced=FloorStrips.Read(cfg,0);if(forced< -1||forced>3)throw new IOException("Invalid floor season");
        for(int i=0;i<lookup.Length;i++)lookup[i]=-1;
        for(int i=0;i<materials.Length;i++)materials[i]=new Material{Cull=CullMode.Back,Lighting=LightingMode.Unlit,SecondarySampler=new SamplerState(TextureAddressMode.Repeat,TextureAddressMode.Repeat,TextureFilter.Linear)};
        for(int i=0;i<drawPoses.Length;i++)drawPoses[i]=worldPose;
    }
    internal void Prepare(GameState state,int room,int month,bool cliffPreview=false)
    {
        BuiltThisFrame=0;
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
        bool invalid=changed||town!=state.Town||terrainVersion!=state.Town.TerrainVersion||preview!=cliffPreview||flat!=SharedFlatMaterial;
        SharedFlatMaterial=flat;
        if(flat<0){groundRing?.Dispose();groundRing=null;}
        if(invalid){
            if(cliffPreview&&(!preview||town!=state.Town)){previewX=(int)state.X;previewZ=(int)state.Z;}
            town=state.Town;terrainVersion=town.TerrainVersion;preview=cliffPreview;generation++;viewX=float.NaN;
        }
        if(room!=0){
            if(indoorTier!=state.HouseTier||indoorRevision!=MaterialRevision){indoorStrips.Prepare(state,room,data);indoor.Build(state,indoorStrips,room,rows);indoorTier=state.HouseTier;indoorRevision=MaterialRevision;}
            VisibleTriangles=indoor.Triangles;RejectedQuads=0;viewX=float.NaN;return;
        }
        depth.Prepare(state.Town);clock++;
        var motion=new CylinderMotion(state.Z);
        worldPose.LocalPosition=new Vector3(0,motion.Y,motion.Z);worldPose.LocalRotation=new Quaternion(motion.HalfSin,0,0,motion.HalfCos);
        if(GroundRingActive){if(groundRing==null)groundRing=new GroundRingRenderer();groundRing.Prepare(state,data,SharedFlatMaterial,rows,invalid);}
        float eye=TerrainElevation.At(state.Town,state.X,state.Z);
        if(viewX!=state.X||viewZ!=state.Z||viewHeight!=eye){
            viewX=state.X;viewZ=state.Z;viewHeight=eye;Plan(state,motion,eye);
        }
        // One guard-band patch per frame. Required patches are built immediately
        // on startup/teleports; ordinary walking reaches prefetched geometry.
        if(ResidentBytes<TargetBytes*85/100)Prefetch(state);
        Trim();
    }
    private static int Origin(float value)=>(int)Math.Floor(value/ChunkSize)*ChunkSize;
    private static int Key(int x,int z)
    {int cx=(x-GridMinimum)/ChunkSize,cz=(z-GridMinimum)/ChunkSize;if(cx<0||cx>=GridEdge||cz<0||cz>=GridEdge)throw new InvalidOperationException("Terrain patch outside bounded grid");return cz*GridEdge+cx;}
    private void Plan(GameState state,CylinderMotion motion,float eye)
    {
        selectionStamp++;wantedCount=visibleCount=0;RejectedQuads=0;
        int minX=Origin(state.X-CylinderWorld.ViewHalfWidth-ChunkSize),maxX=Origin(state.X+CylinderWorld.ViewHalfWidth+ChunkSize);
        int minZ=Origin(state.Z-TerrainTiles.GroundRowsAhead-ChunkSize*2),maxZ=Origin(state.Z+TerrainTiles.GroundRowsBehind+ChunkSize*2);
        for(int z=minZ;z<=maxZ;z+=ChunkSize){
            var p=rows.At(z+ChunkSize/2,1);motion.Point(p.Y,p.Z,out float y,out float projectedZ);
            for(int x=minX;x<=maxX;x+=ChunkSize){
                float cx=x+ChunkSize*.5f;const float radius=8;
                bool shown=RenderView.Sphere(cx,y,projectedZ,radius,state.X,state.Z,eye)&&RenderView.AboveHorizon(y,projectedZ,radius,state.Z,eye);
                bool guard=shown||RenderView.Sphere(cx,y,projectedZ,radius+ChunkSize*1.5f,state.X,state.Z,eye)&&RenderView.AboveHorizon(y,projectedZ,radius+ChunkSize*1.5f,state.Z,eye);
                if(!shown)RejectedQuads+=ChunkSize*ChunkSize;
                if(!guard)continue;
                if(wantedCount==MaximumCandidates)throw new InvalidOperationException("Terrain candidate budget exceeded");
                wantedX[wantedCount]=x;wantedZ[wantedCount]=z;wantedVisible[wantedCount]=shown;
                float dx=cx-state.X,dz=z+ChunkSize*.5f-(state.Z-12);distances[wantedCount++]=dx*dx+dz*dz;
                int at=lookup[Key(x,z)];if(at>=0){if(shown){chunks[at]!.WantedStamp=selectionStamp;chunks[at]!.VisibleStamp=selectionStamp;}}
            }
        }
        floorMask=wallMask=0;VisibleTriangles=GroundRingActive?GroundRingPlan.IndexCount/3:0;
        for(int i=0;i<wantedCount;i++)if(wantedVisible[i]){
            if(visibleCount==CacheCapacity)throw new InvalidOperationException("Visible terrain exceeds patch budget");
            int at=Build(state,wantedX[i],wantedZ[i],true);visible[visibleCount++]=at;var patch=chunks[at]!;
            floorMask|=patch.Floor.MaterialMask;wallMask|=patch.Walls.MaterialMask;VisibleTriangles+=patch.Floor.Triangles+patch.Water.Triangles;
        }
        // Pin a bounded nearest guard band, leaving spare slots for turnover.
        // An unbounded wanted set would continually evict/rebuild guard patches.
        prefetchCount=0;int limit=Math.Max(0,CacheCapacity-visibleCount-8);
        for(int i=0;i<wantedCount;i++)if(!wantedVisible[i]){
            int at=0;while(at<prefetchCount&&distances[prefetchOrder[at]]<=distances[i])at++;
            if(at>=limit)continue;if(prefetchCount<limit)prefetchCount++;
            for(int j=prefetchCount-1;j>at;j--)prefetchOrder[j]=prefetchOrder[j-1];prefetchOrder[at]=i;
        }
        for(int i=0;i<prefetchCount;i++){int n=prefetchOrder[i],at=lookup[Key(wantedX[n],wantedZ[n])];if(at>=0)chunks[at]!.WantedStamp=selectionStamp;}
    }
    private int Build(GameState state,int x,int z,bool shown)
    {
        int key=Key(x,z),at=lookup[key];
        if(at<0){
            int oldest=-1;
            for(int i=0;i<chunks.Length;i++){
                if(chunks[i]==null){oldest=i;break;}
                var p=chunks[i]!;if(p.VisibleStamp==selectionStamp)continue;
                if(oldest<0||(p.WantedStamp!=selectionStamp&&chunks[oldest]!.WantedStamp==selectionStamp)||((p.WantedStamp==selectionStamp)==(chunks[oldest]!.WantedStamp==selectionStamp)&&p.LastUse<chunks[oldest]!.LastUse))oldest=i;
            }
            if(oldest<0)throw new InvalidOperationException("No evictable terrain patch");at=oldest;
            if(chunks[at]!=null){lookup[Key(chunks[at]!.X,chunks[at]!.Z)]=-1;patchBytes-=chunks[at]!.Bytes;chunks[at]!.Bytes=0;}
            else chunks[at]=new TerrainChunk(waterMaterial,depth,rows);
            lookup[key]=at;
        }
        var patch=chunks[at]!;
        if(patch.Generation!=generation||patch.X!=x||patch.Z!=z){
            BuiltThisFrame++;patchBytes-=patch.Bytes;patch.Build(state,this,x,z,generation,preview,previewX,previewZ,rows);patchBytes+=patch.Bytes;
        }
        patch.WantedStamp=selectionStamp;if(shown)patch.VisibleStamp=selectionStamp;patch.LastUse=clock;return at;
    }
    private void Prefetch(GameState state)
    {
        for(int i=0;i<prefetchCount;i++){
            int n=prefetchOrder[i],at=lookup[Key(wantedX[n],wantedZ[n])];
            if(at>=0&&chunks[at]!.Generation==generation)continue;
            Build(state,wantedX[n],wantedZ[n],false);return;
        }
    }
    private void Trim()
    {
        while(ResidentBytes>TargetBytes){
            int oldest=-1;for(int i=0;i<chunks.Length;i++)if(chunks[i]!=null&&chunks[i]!.VisibleStamp!=selectionStamp&&(oldest<0||chunks[i]!.LastUse<chunks[oldest]!.LastUse))oldest=i;
            if(oldest<0)return;var patch=chunks[oldest]!;patchBytes-=patch.Bytes;lookup[Key(patch.X,patch.Z)]=-1;patch.Dispose();chunks[oldest]=null;
        }
    }
    internal void CycleCloudCover()=>clouds.Cover=clouds.Cover==.5f?0:clouds.Cover==0?1:.5f;
    internal void Animate(double seconds)
    {waterMaterial.Animate(seconds);clouds.Animate(seconds);for(int i=0;i<slots;i++)clouds.Apply(materials[i],baseTints[i],!float.IsNaN(viewX));}
    private void DrawGroup(Renderer renderer,int part,int slot,Material material)
    {
        int count=0;
        for(int i=0;i<visibleCount;i++){
            var p=chunks[visible[i]]!;Mesh? mesh=part==0?p.Floor.Mesh(slot):part==1?p.Water.SurfaceMesh:part==2?p.Water.FoamMesh:part==3?p.Walls.SoilMesh:p.Walls.GroundMesh(slot);
            if(mesh!=null)drawMeshes[count++]=mesh;
        }
        if(count>0)renderer.DrawMeshes(drawMeshes,drawPoses,material,0,count);
    }
    internal void Draw(Renderer renderer,Transform parent,int room)
    {
        if(room!=0){for(int slot=0;slot<slots;slot++){var mesh=indoor.Mesh(slot);if(mesh!=null)renderer.DrawMesh(mesh,materials[slot],parent);}return;}
        if(GroundRingActive)groundRing!.Draw(renderer,materials[SharedFlatMaterial],worldPose);
        for(int slot=0;slot<slots;slot++)if((floorMask&(1UL<<slot))!=0)DrawGroup(renderer,0,slot,materials[slot]);
        DrawGroup(renderer,1,0,waterMaterial.Surface);DrawGroup(renderer,2,0,waterMaterial.Foam);DrawGroup(renderer,3,0,soil);
        for(int slot=0;slot<slots;slot++)if((wallMask&(1UL<<slot))!=0)DrawGroup(renderer,4,slot,materials[slot]);
    }
}
