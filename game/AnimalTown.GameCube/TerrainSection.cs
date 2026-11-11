using System;
using AnimalTown;
using Dolphin.Graphics;
using Hypercube.GameCube;

// One recyclable 8x4m section. Exact indexed meshes omit spare capacity; changing
// its address updates only this section. Textures and materials belong to owner.
internal sealed class TerrainSection
{
    private readonly TerrainMeshCache?[] meshes=new TerrainMeshCache?[FloorStrips.MaximumMaterials];
    private readonly TerrainIndexBuffer?[] buffers=new TerrainIndexBuffer?[FloorStrips.MaximumMaterials];

    internal readonly TerrainWalls Walls;
    internal readonly WaterRenderer Water;
    private long revision;
    private int left,top,row,phase,kind;
    private CylinderRows rows=null!;
    internal int Triangles {get;private set;}
    internal ulong GroundMask {get;private set;}
    internal bool Publishing=>row>=TerrainStream.Height;
    internal TerrainSection(TerrainSoil soil,WaterMaterial water){Walls=new TerrainWalls(soil);Water=new WaterRenderer(water);}
    internal Mesh? GroundMesh(int slot)=>(buffers[slot]?.IndexCount??0)==0?null:meshes[slot]?.Mesh;
    internal void Begin(GameState state,int x,int z,CylinderRows cache)
    {
        left=x;top=z;row=phase=0;Triangles=0;rows=cache;kind=TerrainSectionPlan.Kind(state.Town,x,z);
        for(int i=0;i<buffers.Length;i++)buffers[i]?.Reset();
        Water.Begin(state,x,z,cache,kind==TerrainSectionPlan.DeepSea);Walls.Begin(state,z,cache);
    }
    internal bool Advance(GameState state,TerrainRenderer owner,FloorStrips strips,byte[] data)
    {
        if(kind!=TerrainSectionPlan.Detailed&&row<TerrainStream.Height){
            int z=top+row++;
            if(kind==TerrainSectionPlan.DeepSea)Water.AppendDeepRow(z);
            else{strips.PrepareRegion(state,data,left,z,TerrainStream.Width,1);AppendGround(state,strips,owner.SharedFlatMaterial);}
            return false;
        }
        if(row<TerrainStream.Height){
            if(phase==0){
                strips.PrepareRegion(state,data,left,top+row,TerrainStream.Width,1);AppendGround(state,strips,owner.SharedFlatMaterial);Water.Append(strips);
                if(TerrainSectionPlan.Walls(state.Town,left,top+row))phase=1;else row++;
            }else{Walls.AppendRow(state,owner,left,top+row);phase=0;row++;}
            return false;
        }
        if(phase==0){
            revision++;GroundMask=0;
            for(int slot=0;slot<buffers.Length;slot++){
                if((buffers[slot]?.IndexCount??0)>0){var buffer=buffers[slot]!;GroundMask|=1UL<<slot;meshes[slot]!.Commit(buffer.Vertices,buffer.VertexCount,buffer.Indices,buffer.IndexCount,revision,FrameProfile.Counters,owner.RecreateMeshes);}
                else if(meshes[slot]!=null){meshes[slot]!.Dispose();meshes[slot]=null;}
            }
        }else if(phase==1)Water.Finish();
        else Walls.Finish();
        if(++phase<3)return false;
        Triangles+=Water.Triangles+Walls.Triangles;return true;
    }
    internal void Archive(int section,bool complete=true)
    {
        for(int slot=0;slot<meshes.Length;slot++)ArchivePart(section,slot,GroundMesh(slot),0);
        Water.Archive(section);Walls.Archive(section);
        if(complete&&NativeTerrain.Complete(section)!=1)throw new InvalidOperationException("Terrain archive section did not complete");
    }
    internal static void ArchivePart(int section,int kind,Mesh? mesh,int used)
    {
        if(mesh==null)return;int result=NativeTerrain.Append(section,kind,mesh,used);
        if(result<0)throw new InvalidOperationException("Terrain archive append: "+result+" section "+section+" bytes "+NativeTerrain.Stat(0));
    }
    internal void Dispose()
    {
        for(int i=0;i<meshes.Length;i++){meshes[i]?.Dispose();meshes[i]=null;buffers[i]=null;}
        Water.Dispose();Walls.Dispose();
    }
    private void AppendGround(GameState state,FloorStrips strips,int sharedFlatMaterial)
    {
        for(int n=0;n<strips.Count;n++){
            if(strips.Water[n]||GroundRingPlan.Uses(state.Town,strips.X0[n],strips.Z0[n],strips.Material[n],sharedFlatMaterial))continue;
            Triangles+=2;int slot=strips.Material[n],z=strips.Z0[n];
            if(meshes[slot]==null){
                if(buffers[slot]==null)for(int old=0;old<buffers.Length;old++)if(buffers[old]!=null&&buffers[old]!.IndexCount==0){meshes[slot]=meshes[old];buffers[slot]=buffers[old];meshes[old]=null;buffers[old]=null;break;}
                if(meshes[slot]==null)meshes[slot]=new TerrainMeshCache();
                if(buffers[slot]==null)buffers[slot]=new TerrainIndexBuffer();
            }
            var buffer=buffers[slot]!;
            var a=rows.At(z);var b=rows.At(z+1);
            float x0=strips.X0[n],x1=strips.X1[n],u0=x0*.5f,u1=x1*.5f,v0=z*.5f,v1=(z+1)*.5f;
            var p0=Elevated(state,(int)x0,z,x0,z,a);var p1=Elevated(state,(int)x0,z,x1,z,a);var p2=Elevated(state,(int)x0,z,x1,z+1,b);var p3=Elevated(state,(int)x0,z,x0,z+1,b);
            bool wet=state.Town.At((int)x0,z)==(int)Terrain.Beach&&(WaterTerrain.IsSea(state.Town,(int)x0-1,z)||WaterTerrain.IsSea(state.Town,(int)x0+1,z)||WaterTerrain.IsSea(state.Town,(int)x0,z-1)||WaterTerrain.IsSea(state.Town,(int)x0,z+1));
            var tint=wet?new Color32(205,199,181):Color32.White;
            buffer.Quad(Vertex(x0,p0,u0,v0,tint),Vertex(x1,p1,u1,v0,tint),Vertex(x1,p2,u1,v1,tint),Vertex(x0,p3,u0,v1,tint),FrameProfile.Counters);
        }
    }
    private static VertexPositionNormalColorTexture Vertex(float x,CylinderPose p,float u,float v,Color32 tint)=>new VertexPositionNormalColorTexture(new Vector3(x,p.Y,p.Z),new Vector3(0,1,0),tint,new Vector2(u,v));
    private static CylinderPose Elevated(GameState state,int cx,int cz,float x,float z,CylinderPose p)
    {
        float h=GroundSurface.HeightAt(state.Town,cx,cz,x,z);
        return new CylinderPose(p.Y+h*p.UpY,p.Z+h*p.UpZ,p.UpY,p.UpZ,p.Pitch);
    }
}
