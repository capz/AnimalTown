using System;
using System.IO;
using AnimalTown;
using Dolphin.Graphics;

// Shared repeating textures and merged strips; white padding enables indexed GX.
internal sealed class TerrainRenderer
{
    private readonly FloorStrips strips=new FloorStrips();
    private readonly DynamicMesh?[] meshes=new DynamicMesh?[FloorStrips.MaximumMaterials];
    private readonly VertexPositionColorTexture[]?[] buffers=new VertexPositionColorTexture[]?[FloorStrips.MaximumMaterials];
    private readonly Material[] materials=new Material[FloorStrips.MaximumMaterials];
    private readonly CachedTexture?[] textures=new CachedTexture?[FloorStrips.MaximumMaterials];
    private readonly int[] used=new int[FloorStrips.MaximumMaterials],capacities=new int[FloorStrips.MaximumMaterials];
    private byte[] data=new byte[0];
    private int season=-1,forced,slots,cachedRoom=-1,cachedTier=-1,cachedTerrainVersion=-1;
    private float cachedX,cachedZ;
    private Town? cachedTown;
    internal int VisibleTriangles,RejectedQuads;
    internal TerrainRenderer()
    {
        var cfg=File.ReadAllBytes("dvd:/terrain.cfg");if(cfg.Length!=4)throw new IOException("Invalid floor season config");forced=FloorStrips.Read(cfg,0);if(forced< -1||forced>3)throw new IOException("Invalid floor season");
        for(int i=0;i<materials.Length;i++)materials[i]=new Material{Cull=CullMode.Back,Lighting=LightingMode.Unlit};
    }
    internal void Prepare(GameState state,int room,int month)
    {
        int next=TerrainTiles.Season(month,forced);bool changed=next!=season;
        if(changed){
            string name="floor-"+TerrainTiles.SeasonName(next);data=File.ReadAllBytes("dvd:/"+name+".bin");FloorStrips.Validate(data);
            for(int i=0;i<textures.Length;i++)if(textures[i]!=null){TextureCache.Release(textures[i]!);textures[i]=null;materials[i].Texture=null;}
            slots=FloorStrips.Read(data,12);
            for(int i=0;i<slots;i++){
                int source=FloorStrips.TextureSlot(data,i);bool neutral=FloorStrips.Read(data,4)==2&&source==0;
                if(textures[source]==null)textures[source]=TextureCache.LoadFloor(neutral?"dvd:/floor-outdoor-neutral.dndtex":"dvd:/"+name+"-"+source+".dndtex",repeat:neutral);
                materials[i].Texture=textures[source]!.Pages[0];uint tint=FloorStrips.Tint(data,i);materials[i].Color=new Color32((byte)(tint>>24),(byte)(tint>>16),(byte)(tint>>8));
            }
            season=next;
        }
        if(!changed&&cachedX==state.X&&cachedZ==state.Z&&cachedRoom==room&&cachedTier==state.HouseTier&&cachedTown==state.Town&&cachedTerrainVersion==state.Town.TerrainVersion)return;
        cachedX=state.X;cachedZ=state.Z;cachedRoom=room;cachedTier=state.HouseTier;cachedTown=state.Town;
        cachedTerrainVersion=state.Town.TerrainVersion;
        strips.Prepare(state,room,data);VisibleTriangles=strips.Count*2;RejectedQuads=strips.RejectedQuads;
        for(int i=0;i<used.Length;i++)used[i]=0;
        int previousZ=int.MinValue;var a=new CylinderPose(0,0,1,0,0);var b=a;
        for(int n=0;n<strips.Count;n++){
            int slot=strips.Material[n],z=strips.Z0[n];
            if(previousZ!=z){a=room==0?CylinderWorld.Surface(z,state.Z):new CylinderPose(0,z,1,0,0);b=room==0?CylinderWorld.Surface(strips.Z1[n],state.Z):new CylinderPose(0,strips.Z1[n],1,0,0);previousZ=z;}
            int at=used[slot],required=at+6;var buffer=buffers[slot];
            if(buffer==null||buffer.Length<required){int size=buffer==null?6:buffer.Length;while(size<required)size*=2;if(size>FloorStrips.Capacity*12)throw new IOException("Floor material mesh exceeds budget");var replacement=new VertexPositionColorTexture[size];if(buffer!=null)for(int i=0;i<at;i++)replacement[i]=buffer[i];buffers[slot]=buffer=replacement;}
            float x0=strips.X0[n],x1=strips.X1[n],u0=x0*.5f,u1=x1*.5f,v0=z*.5f,v1=strips.Z1[n]*.5f;
            var p0=Elevated(state,room,(int)x0,z,x0,z,a);var p1=Elevated(state,room,(int)x0,z,x1,z,a);var p2=Elevated(state,room,(int)x0,z,x1,z+1,b);var p3=Elevated(state,room,(int)x0,z,x0,z+1,b);
            buffer[at++]=Vertex(x0,p0,u0,v0);buffer[at++]=Vertex(x1,p1,u1,v0);buffer[at++]=Vertex(x1,p2,u1,v1);
            buffer[at++]=Vertex(x0,p0,u0,v0);buffer[at++]=Vertex(x1,p2,u1,v1);buffer[at++]=Vertex(x0,p3,u0,v1);used[slot]=at;
        }
        for(int slot=0;slot<meshes.Length;slot++){
            if(used[slot]==0){meshes[slot]?.Dispose();meshes[slot]=null;buffers[slot]=null;capacities[slot]=0;continue;}
            var buffer=buffers[slot]!;for(int i=used[slot];i<buffer.Length;i++)buffer[i]=new VertexPositionColorTexture(Vector3.Zero,Color32.White,Vector2.Zero);
            if(capacities[slot]!=buffer.Length){meshes[slot]?.Dispose();meshes[slot]=DynamicMesh.Create(buffer);capacities[slot]=buffer.Length;}else meshes[slot]!.SetVertices(buffer);
        }
    }
    private static VertexPositionColorTexture Vertex(float x,CylinderPose p,float u,float v)=>new VertexPositionColorTexture(new Vector3(x,p.Y,p.Z),Color32.White,new Vector2(u,v));
    private static CylinderPose Elevated(GameState state,int room,int cx,int cz,float x,float z,CylinderPose p)
    {float h=room==0?TerrainElevation.AtCell(state.Town,cx,cz,x,z):0;return new CylinderPose(p.Y+h*p.UpY,p.Z+h*p.UpZ,p.UpY,p.UpZ,p.Pitch);}
    internal void Draw(Renderer renderer,Transform parent,int room){for(int i=0;i<slots;i++)if(used[i]>0)renderer.DrawMesh(meshes[i]!,materials[i],parent);}
}
