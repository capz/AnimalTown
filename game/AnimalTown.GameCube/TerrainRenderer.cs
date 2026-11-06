using System;
using System.IO;
using AnimalTown;
using Dolphin.Graphics;
using Hypercube.GameCube;

// Shared repeating textures and merged strips; white padding enables indexed GX.
internal sealed class TerrainRenderer
{
    private readonly FloorStrips strips=new FloorStrips();
    internal readonly Transform ScrollTransform=new Transform();
    internal float ReferenceX=>cachedX;
    internal float ReferenceZ=>cachedZ;
    private readonly WaterRenderer water=new WaterRenderer();
    private readonly CloudShadows clouds=new CloudShadows();
    private readonly Color32[] baseTints=new Color32[FloorStrips.MaximumMaterials];
    private readonly DynamicMeshBuffer?[] meshes=new DynamicMeshBuffer?[FloorStrips.MaximumMaterials];
    private long geometryRevision;
    internal long GeometryRevision=>geometryRevision;
    private readonly VertexPositionColorTexture[]?[] buffers=new VertexPositionColorTexture[]?[FloorStrips.MaximumMaterials];
    private readonly Material[] materials=new Material[FloorStrips.MaximumMaterials];
    private readonly CachedTexture?[] textures=new CachedTexture?[FloorStrips.MaximumMaterials];
    private readonly int[] used=new int[FloorStrips.MaximumMaterials];
    private byte[] data=new byte[0];
    private int season=-1,forced,slots,cachedRoom=-1,cachedTier=-1,cachedTerrainVersion=-1;
    private float cachedX,cachedZ;
    private Town? cachedTown;
    private bool cachedPreview;
    internal int VisibleTriangles,RejectedQuads;
    internal int MaterialRevision {get;private set;}
    internal int GroundSlot(GameState state,int x,int z)=>FloorStrips.At(state,data,x,z,0);
    internal Material GroundMaterial(int slot)=>materials[slot];
    internal TerrainRenderer()
    {
        var cfg=File.ReadAllBytes("dvd:/terrain.cfg");if(cfg.Length!=4)throw new IOException("Invalid floor season config");forced=FloorStrips.Read(cfg,0);if(forced< -1||forced>3)throw new IOException("Invalid floor season");
        for(int i=0;i<materials.Length;i++)materials[i]=new Material{Cull=CullMode.Back,Lighting=LightingMode.Unlit,SecondarySampler=new SamplerState(TextureAddressMode.Repeat,TextureAddressMode.Repeat,TextureFilter.Linear)};
    }
    internal void Prepare(GameState state,int room,int month,bool cliffPreview=false)
    {
        int next=TerrainTiles.Season(month,forced);bool changed=next!=season;
        if(changed){
            string name="floor-"+TerrainTiles.SeasonName(next);data=File.ReadAllBytes("dvd:/"+name+".bin");FloorStrips.Validate(data);
            for(int i=0;i<textures.Length;i++)if(textures[i]!=null){TextureCache.Release(textures[i]!);textures[i]=null;materials[i].Texture=null;}
            slots=FloorStrips.Read(data,12);
            if(FloorStrips.Read(data,4)==2)water.SetTerrainTint(FloorStrips.Tint(data,FloorStrips.Read(data,24+TerrainTiles.Water*20)));
            for(int i=0;i<slots;i++){
                int source=FloorStrips.TextureSlot(data,i);bool neutral=FloorStrips.Read(data,4)==2&&source==0;
                if(textures[source]==null)textures[source]=TextureCache.LoadFloor(neutral?"dvd:/floor-outdoor-neutral.dndtex":"dvd:/"+name+"-"+source+".dndtex",repeat:neutral);
                materials[i].Sampler=new SamplerState(neutral?TextureAddressMode.Repeat:TextureAddressMode.Clamp,neutral?TextureAddressMode.Repeat:TextureAddressMode.Clamp);
                materials[i].Texture=textures[source]!.Pages[0];uint tint=FloorStrips.Tint(data,i);baseTints[i]=materials[i].Color=new Color32((byte)(tint>>24),(byte)(tint>>16),(byte)(tint>>8));
            }
            season=next;
            MaterialRevision++;
        }
        float referenceX=room==0?CylinderWorld.ScrollAnchor(state.X):0,referenceZ=room==0?CylinderWorld.ScrollAnchor(state.Z):0;
        var scroll=CylinderWorld.Scroll(referenceZ,state.Z);
        ScrollTransform.LocalPosition=room==0?new Vector3(0,scroll.Y,scroll.Z):new Vector3(0,0,0);
        ScrollTransform.LocalRotation=room==0?new Quaternion(scroll.HalfSin,0,0,scroll.HalfCos):Quaternion.Identity;
        if(!changed&&cachedX==referenceX&&cachedZ==referenceZ&&cachedRoom==room&&cachedTier==state.HouseTier&&cachedTown==state.Town&&cachedTerrainVersion==state.Town.TerrainVersion&&cachedPreview==cliffPreview)return;
        cachedPreview=cliffPreview;
        cachedX=referenceX;cachedZ=referenceZ;cachedRoom=room;cachedTier=state.HouseTier;cachedTown=state.Town;
        cachedTerrainVersion=state.Town.TerrainVersion;
        strips.Prepare(state,room,data,cliffPreview,scrollWindow:true);VisibleTriangles=0;RejectedQuads=strips.RejectedQuads;
        for(int i=0;i<used.Length;i++)used[i]=0;
        int previousZ=int.MinValue;var a=new CylinderPose(0,0,1,0,0);var b=a;
        for(int n=0;n<strips.Count;n++){
            if(strips.Water[n])continue;
            VisibleTriangles+=2;
            int slot=strips.Material[n],z=strips.Z0[n];
            if(previousZ!=z){a=room==0?CylinderWorld.Surface(z,cachedZ):new CylinderPose(0,z,1,0,0);b=room==0?CylinderWorld.Surface(strips.Z1[n],cachedZ):new CylinderPose(0,strips.Z1[n],1,0,0);previousZ=z;}
            int at=used[slot],required=at+6;var buffer=buffers[slot];
            if(meshes[slot]==null)meshes[slot]=new DynamicMeshBuffer(FloorStrips.Capacity*12);
            buffers[slot]=buffer=GeometryStaging.Reserve(meshes[slot]!,buffer??new VertexPositionColorTexture[0],at,required,FrameProfile.Counters);
            float x0=strips.X0[n],x1=strips.X1[n],u0=x0*.5f,u1=x1*.5f,v0=z*.5f,v1=strips.Z1[n]*.5f;
            var p0=Elevated(state,room,(int)x0,z,x0,z,a);var p1=Elevated(state,room,(int)x0,z,x1,z,a);var p2=Elevated(state,room,(int)x0,z,x1,z+1,b);var p3=Elevated(state,room,(int)x0,z,x0,z+1,b);
            bool wet=room==0&&state.Town.At((int)x0,z)==(int)Terrain.Beach&&(WaterTerrain.IsSea(state.Town,(int)x0-1,z)||WaterTerrain.IsSea(state.Town,(int)x0+1,z)||WaterTerrain.IsSea(state.Town,(int)x0,z-1)||WaterTerrain.IsSea(state.Town,(int)x0,z+1));
            var tint=wet?new Color32(205,199,181):Color32.White;
            buffer[at++]=Vertex(x0,p0,u0,v0,tint);buffer[at++]=Vertex(x1,p1,u1,v0,tint);buffer[at++]=Vertex(x1,p2,u1,v1,tint);
            buffer[at++]=Vertex(x0,p0,u0,v0,tint);buffer[at++]=Vertex(x1,p2,u1,v1,tint);buffer[at++]=Vertex(x0,p3,u0,v1,tint);used[slot]=at;
        }
        geometryRevision++;
        for(int slot=0;slot<meshes.Length;slot++){
            // Drop inactive slots rather than retaining every seasonal/room peak.
            if(used[slot]==0){meshes[slot]?.Dispose();meshes[slot]=null;buffers[slot]=null;continue;}
            meshes[slot]!.Commit(used[slot],geometryRevision,FrameProfile.Counters);
        }
        water.Prepare(state,strips,room,cachedZ);VisibleTriangles+=water.Triangles;
    }
    private static VertexPositionColorTexture Vertex(float x,CylinderPose p,float u,float v,Color32 tint)=>new VertexPositionColorTexture(new Vector3(x,p.Y,p.Z),tint,new Vector2(u,v));
    private static CylinderPose Elevated(GameState state,int room,int cx,int cz,float x,float z,CylinderPose p)
    {float h=room==0?TerrainElevation.AtCell(state.Town,cx,cz,x,z):0;
        if(room==0&&state.Town.At(cx,cz)==(int)Terrain.Beach){
            bool shore=x==cx&&WaterTerrain.IsSea(state.Town,cx-1,cz)||x==cx+1&&WaterTerrain.IsSea(state.Town,cx+1,cz)||z==cz&&WaterTerrain.IsSea(state.Town,cx,cz-1)||z==cz+1&&WaterTerrain.IsSea(state.Town,cx,cz+1);
            if(shore)h=WaterSurface.Height;
        }
        return new CylinderPose(p.Y+h*p.UpY,p.Z+h*p.UpZ,p.UpY,p.UpZ,p.Pitch);}
    internal void CycleCloudCover()=>clouds.Cover=clouds.Cover==.5f?0:clouds.Cover==0?1:.5f;
    internal void Animate(double seconds)
    {
        water.Animate(seconds);clouds.Animate(seconds);
        for(int i=0;i<slots;i++)clouds.Apply(materials[i],baseTints[i],cachedRoom==0);
    }
    internal void Draw(Renderer renderer,Transform parent,int room){for(int i=0;i<slots;i++)if(used[i]>0)renderer.DrawMesh(meshes[i]!.Mesh!,materials[i],parent);water.Draw(renderer,parent);}
}
