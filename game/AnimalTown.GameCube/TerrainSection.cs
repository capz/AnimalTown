using System;
using AnimalTown;
using Dolphin.Graphics;
using Hypercube.GameCube;

// One recyclable 8x4m section. Active buffers retain capacity; changing
// its address updates only this section. Textures and materials belong to owner.
internal sealed class TerrainSection
{
    private readonly DynamicMeshBuffer?[] meshes=new DynamicMeshBuffer?[FloorStrips.MaximumMaterials];
    private readonly VertexPositionColorTexture[]?[] buffers=new VertexPositionColorTexture[]?[FloorStrips.MaximumMaterials];
    private readonly int[] used=new int[FloorStrips.MaximumMaterials];
    internal readonly TerrainWalls Walls;
    internal readonly WaterRenderer Water;
    private long revision;
    private int left,top,row,phase;
    internal int Triangles {get;private set;}
    internal TerrainSection(TerrainSoil soil,WaterMaterial water){Walls=new TerrainWalls(soil);Water=new WaterRenderer(water);}
    internal Mesh? GroundMesh(int slot)=>used[slot]==0?null:meshes[slot]?.Mesh;
    internal void Begin(GameState state,int x,int z)
    {
        left=x;top=z;row=phase=0;Triangles=0;
        for(int i=0;i<used.Length;i++)used[i]=0;
        Water.Begin(state,x,z);Walls.Begin(state,z);
    }
    internal bool Advance(GameState state,TerrainRenderer owner,FloorStrips strips,byte[] data)
    {
        if(row<TerrainStream.Height){
            if(phase==0){strips.PrepareRegion(state,data,left,top+row,TerrainStream.Width,1);AppendGround(state,strips,owner.SharedFlatMaterial);}
            else if(phase==1)Water.Append(strips);
            else Walls.AppendRow(state,owner,left,top+row);
            phase++;if(phase==3){phase=0;row++;}return false;
        }
        revision++;
        for(int slot=0;slot<used.Length;slot++){
            if(used[slot]>0)meshes[slot]!.Commit(used[slot],revision,FrameProfile.Counters);
            else if(meshes[slot]!=null){meshes[slot]!.Dispose();meshes[slot]=null;buffers[slot]=null;}
        }
        Water.Finish();Walls.Finish();Triangles+=Water.Triangles+Walls.Triangles;return true;
    }
    private void AppendGround(GameState state,FloorStrips strips,int sharedFlatMaterial)
    {
        for(int n=0;n<strips.Count;n++){
            if(strips.Water[n]||GroundRingPlan.Uses(state.Town,strips.X0[n],strips.Z0[n],strips.Material[n],sharedFlatMaterial))continue;
            Triangles+=2;int slot=strips.Material[n],z=strips.Z0[n],at=used[slot];
            if(meshes[slot]==null){
                for(int old=0;old<used.Length;old++)if(used[old]==0&&meshes[old]!=null){meshes[slot]=meshes[old];buffers[slot]=buffers[old];meshes[old]=null;buffers[old]=null;break;}
                if(meshes[slot]==null)meshes[slot]=new DynamicMeshBuffer(TerrainStream.Width*TerrainStream.Height*6);
            }
            var buffer=buffers[slot]=GeometryStaging.Reserve(meshes[slot]!,buffers[slot]??new VertexPositionColorTexture[0],at,at+6,FrameProfile.Counters);
            var a=CylinderWorld.Surface(z,0);var b=CylinderWorld.Surface(z+1,0);
            float x0=strips.X0[n],x1=strips.X1[n],u0=x0*.5f,u1=x1*.5f,v0=z*.5f,v1=(z+1)*.5f;
            var p0=Elevated(state,(int)x0,z,x0,z,a);var p1=Elevated(state,(int)x0,z,x1,z,a);var p2=Elevated(state,(int)x0,z,x1,z+1,b);var p3=Elevated(state,(int)x0,z,x0,z+1,b);
            bool wet=state.Town.At((int)x0,z)==(int)Terrain.Beach&&(WaterTerrain.IsSea(state.Town,(int)x0-1,z)||WaterTerrain.IsSea(state.Town,(int)x0+1,z)||WaterTerrain.IsSea(state.Town,(int)x0,z-1)||WaterTerrain.IsSea(state.Town,(int)x0,z+1));
            var tint=wet?new Color32(205,199,181):Color32.White;
            buffer[at++]=Vertex(x0,p0,u0,v0,tint);buffer[at++]=Vertex(x1,p1,u1,v0,tint);buffer[at++]=Vertex(x1,p2,u1,v1,tint);
            buffer[at++]=Vertex(x0,p0,u0,v0,tint);buffer[at++]=Vertex(x1,p2,u1,v1,tint);buffer[at++]=Vertex(x0,p3,u0,v1,tint);used[slot]=at;
        }
    }
    private static VertexPositionColorTexture Vertex(float x,CylinderPose p,float u,float v,Color32 tint)=>new VertexPositionColorTexture(new Vector3(x,p.Y,p.Z),tint,new Vector2(u,v));
    private static CylinderPose Elevated(GameState state,int cx,int cz,float x,float z,CylinderPose p)
    {
        float h=GroundSurface.HeightAt(state.Town,cx,cz,x,z);
        return new CylinderPose(p.Y+h*p.UpY,p.Z+h*p.UpZ,p.UpY,p.UpZ,p.Pitch);
    }
}
