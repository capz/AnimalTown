using System;
using AnimalTown;
using Dolphin.Graphics;
using Hypercube.GameCube;

internal sealed class FloorGeometry : IDisposable
{
    private readonly DynamicMeshBuffer?[] meshes=new DynamicMeshBuffer?[FloorStrips.MaximumMaterials];
    private readonly VertexPositionColorTexture[]?[] buffers=new VertexPositionColorTexture[]?[FloorStrips.MaximumMaterials];
    private readonly int[] used=new int[FloorStrips.MaximumMaterials];
    private readonly int maximumVertices;
    private long geometryRevision;
    internal int Triangles {get;private set;}
    internal ulong MaterialMask {get;private set;}
    internal FloorGeometry(int maximumVertices){this.maximumVertices=maximumVertices;}
    internal Mesh? Mesh(int slot)=>meshes[slot]?.Mesh;
    internal int ResidentBytes {get{int n=0;for(int i=0;i<meshes.Length;i++)if(meshes[i]!=null)n+=meshes[i]!.Capacity*24+(meshes[i]!.Mesh?.NativeBytes??0);return n;}}
    internal void Build(GameState state,FloorStrips strips,int room,CylinderRows rows,int sharedFlatMaterial=-1)
    {
        int VisibleTriangles=0;
        for(int i=0;i<used.Length;i++)used[i]=0;
        int previousZ=int.MinValue;var a=new CylinderPose(0,0,1,0,0);var b=a;
        for(int n=0;n<strips.Count;n++){
            if(strips.Water[n]||room==0&&GroundRingPlan.Uses(state.Town,strips.X0[n],strips.Z0[n],strips.Material[n],sharedFlatMaterial))continue;
            VisibleTriangles+=2;
            int slot=strips.Material[n],z=strips.Z0[n];
            if(previousZ!=z){a=room==0?rows.At(z):new CylinderPose(0,z,1,0,0);b=room==0?rows.At(strips.Z1[n]):new CylinderPose(0,strips.Z1[n],1,0,0);previousZ=z;}
            int at=used[slot],required=at+6;var buffer=buffers[slot];
            if(meshes[slot]==null)meshes[slot]=new DynamicMeshBuffer(maximumVertices);
            buffers[slot]=buffer=GeometryStaging.Reserve(meshes[slot]!,buffer??new VertexPositionColorTexture[0],at,required,FrameProfile.Counters);
            float x0=strips.X0[n],x1=strips.X1[n],u0=x0*.5f,u1=x1*.5f,v0=z*.5f,v1=strips.Z1[n]*.5f;
            var p0=Elevated(state,room,(int)x0,z,x0,z,a);var p1=Elevated(state,room,(int)x0,z,x1,z,a);var p2=Elevated(state,room,(int)x0,z,x1,z+1,b);var p3=Elevated(state,room,(int)x0,z,x0,z+1,b);
            bool wet=room==0&&state.Town.At((int)x0,z)==(int)Terrain.Beach&&(WaterTerrain.IsSea(state.Town,(int)x0-1,z)||WaterTerrain.IsSea(state.Town,(int)x0+1,z)||WaterTerrain.IsSea(state.Town,(int)x0,z-1)||WaterTerrain.IsSea(state.Town,(int)x0,z+1));
            var tint=wet?new Color32(205,199,181):Color32.White;
            buffer[at++]=Vertex(x0,p0,u0,v0,tint);buffer[at++]=Vertex(x1,p1,u1,v0,tint);buffer[at++]=Vertex(x1,p2,u1,v1,tint);
            buffer[at++]=Vertex(x0,p0,u0,v0,tint);buffer[at++]=Vertex(x1,p2,u1,v1,tint);buffer[at++]=Vertex(x0,p3,u0,v1,tint);used[slot]=at;
        }
        geometryRevision++;MaterialMask=0;
        for(int slot=0;slot<meshes.Length;slot++){
            // Drop inactive slots rather than retaining every seasonal/room peak.
            if(used[slot]==0){meshes[slot]?.Dispose();meshes[slot]=null;buffers[slot]=null;continue;}
            MaterialMask|=1UL<<slot;meshes[slot]!.Commit(used[slot],geometryRevision,FrameProfile.Counters);
        }
        Triangles=VisibleTriangles;
    }
    private static VertexPositionColorTexture Vertex(float x,CylinderPose p,float u,float v,Color32 tint)=>new VertexPositionColorTexture(new Vector3(x,p.Y,p.Z),tint,new Vector2(u,v));
    private static CylinderPose Elevated(GameState state,int room,int cx,int cz,float x,float z,CylinderPose p)
    {float h=room==0?TerrainElevation.AtCell(state.Town,cx,cz,x,z):0;
        if(room==0&&state.Town.At(cx,cz)==(int)Terrain.Beach){
            bool shore=x==cx&&WaterTerrain.IsSea(state.Town,cx-1,cz)||x==cx+1&&WaterTerrain.IsSea(state.Town,cx+1,cz)||z==cz&&WaterTerrain.IsSea(state.Town,cx,cz-1)||z==cz+1&&WaterTerrain.IsSea(state.Town,cx,cz+1);
            if(shore)h=WaterSurface.Height;
        }
        return new CylinderPose(p.Y+h*p.UpY,p.Z+h*p.UpZ,p.UpY,p.UpZ,p.Pitch);}
    public void Dispose(){for(int i=0;i<meshes.Length;i++){meshes[i]?.Dispose();meshes[i]=null;buffers[i]=null;}}
}
