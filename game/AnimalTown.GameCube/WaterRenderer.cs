using System;
using AnimalTown;
using Dolphin.Graphics;
using Hypercube.GameCube;

// Reuses the floor's visibility selection. Cells keep depth tint continuous;
// vertices change only with the camera/terrain, never to simulate waves.
internal sealed class WaterRenderer
{
    private readonly WaterMaterial material;
    private readonly WaterDepthCache depth;
    private readonly CylinderRows rows;
    internal WaterRenderer(WaterMaterial material,WaterDepthCache depth,CylinderRows rows,int maximumCells)
    {this.material=material;this.depth=depth;this.rows=rows;this.maximumCells=maximumCells;surfaceMesh=new DynamicMeshBuffer(maximumCells*54);foamMesh=new DynamicMeshBuffer(maximumCells*24);}
    internal Mesh? SurfaceMesh=>surfaceMesh.Mesh;
    internal Mesh? FoamMesh=>foamMesh.Mesh;
    internal int ResidentBytes=>surfaceMesh.Capacity*24+foamMesh.Capacity*24+(SurfaceMesh?.NativeBytes??0)+(FoamMesh?.NativeBytes??0);
    private VertexPositionColorTexture[] surface=new VertexPositionColorTexture[0],foam=new VertexPositionColorTexture[0];
    private DynamicMeshBuffer surfaceMesh,foamMesh;
    private readonly int maximumCells;
    private long revision;
    private int surfaceCount,foamCount;
    private GameState state=null!;
    internal int Triangles=>(surfaceCount+foamCount)/3;
    internal void Prepare(GameState next,FloorStrips strips,int room)
    {
        state=next;surfaceCount=foamCount=0;int cells=0;
        if(room==0)for(int n=0;n<strips.Count;n++){
            int z=strips.Z0[n];
            for(int x=strips.X0[n];x<strips.X1[n];x++){
                bool bridge=state.Town.At(x,z)==(int)Terrain.Bridge;if(!strips.Water[n]&&!bridge)continue;
                if(cells++>=WaterSurface.MaximumCells)throw new InvalidOperationException("Visible water exceeds geometry budget");
                Quad(ref surface,ref surfaceCount,x,z,x,z,x+1,z,x+1,z+1,x,z+1,false);
                if(WaterTerrain.IsSea(state.Town,x,z)){
                    Mouth(x,z,x,z,x+1,z,x,z-1);Mouth(x,z,x+1,z,x+1,z+1,x+1,z);
                    Mouth(x,z,x+1,z+1,x,z+1,x,z+1);Mouth(x,z,x,z+1,x,z,x-1,z);
                }
                if(bridge||!material.FoamEnabled)continue;
                float w=WaterSurface.FoamWidth;
                if(WaterSurface.IsBank(state.Town,x,z-1))Quad(ref foam,ref foamCount,x,z,x,z,x+1,z,x+1,z+w,x,z+w,true);
                if(WaterSurface.IsBank(state.Town,x,z+1))Quad(ref foam,ref foamCount,x,z,x,z+1-w,x+1,z+1-w,x+1,z+1,x,z+1,true);
                // Trim ends on the other sides so corner foam does not overlap.
                float lo=WaterSurface.IsBank(state.Town,x,z-1)?w:0,hi=WaterSurface.IsBank(state.Town,x,z+1)?1-w:1;
                if(WaterSurface.IsBank(state.Town,x-1,z))Quad(ref foam,ref foamCount,x,z,x,z+lo,x+w,z+lo,x+w,z+hi,x,z+hi,true);
                if(WaterSurface.IsBank(state.Town,x+1,z))Quad(ref foam,ref foamCount,x,z,x+1-w,z+lo,x+1,z+lo,x+1,z+hi,x+1-w,z+hi,true);
            }
        }
        revision++;
        if(surfaceCount==0&&surfaceMesh.Capacity>0){surfaceMesh.Dispose();surfaceMesh=new DynamicMeshBuffer(maximumCells*54);surface=new VertexPositionColorTexture[0];}
        else surfaceMesh.Commit(surfaceCount,revision,FrameProfile.Counters);
        if(foamCount==0&&foamMesh.Capacity>0){foamMesh.Dispose();foamMesh=new DynamicMeshBuffer(maximumCells*24);foam=new VertexPositionColorTexture[0];}
        else foamMesh.Commit(foamCount,revision,FrameProfile.Counters);
    }
    private void Mouth(int cx,int cz,float ax,float az,float bx,float bz,int nx,int nz)
    {
        if(state.Town.At(nx,nz)!=(int)Terrain.Water)return;
        float topA=TerrainElevation.AtCell(state.Town,cx,cz,ax,az),topB=TerrainElevation.AtCell(state.Town,cx,cz,bx,bz);
        float lowA=TerrainElevation.AtCell(state.Town,nx,nz,ax,az),lowB=TerrainElevation.AtCell(state.Town,nx,nz,bx,bz);
        if(topA<=lowA&&topB<=lowB)return;
        var a=AtHeight(cx,cz,ax,az,topA,false);var b=AtHeight(cx,cz,bx,bz,topB,false);
        var c=AtHeight(cx,cz,bx,bz,lowB,false);var d=AtHeight(cx,cz,ax,az,lowA,false);
        surface=GeometryStaging.Reserve(surfaceMesh,surface,surfaceCount,surfaceCount+12,FrameProfile.Counters);
        surface[surfaceCount++]=a;surface[surfaceCount++]=b;surface[surfaceCount++]=c;surface[surfaceCount++]=a;surface[surfaceCount++]=c;surface[surfaceCount++]=d;
        surface[surfaceCount++]=a;surface[surfaceCount++]=c;surface[surfaceCount++]=b;surface[surfaceCount++]=a;surface[surfaceCount++]=d;surface[surfaceCount++]=c;
    }
    private void Quad(ref VertexPositionColorTexture[] buffer,ref int used,int cx,int cz,float ax,float az,float bx,float bz,float dx,float dz,float ex,float ez,bool isFoam)
    {
        buffer=GeometryStaging.Reserve(isFoam?foamMesh:surfaceMesh,buffer,used,used+6,FrameProfile.Counters);
        var a=Vertex(cx,cz,ax,az,isFoam);var b=Vertex(cx,cz,bx,bz,isFoam);var d=Vertex(cx,cz,dx,dz,isFoam);var e=Vertex(cx,cz,ex,ez,isFoam);
        buffer[used++]=a;buffer[used++]=b;buffer[used++]=d;buffer[used++]=a;buffer[used++]=d;buffer[used++]=e;
    }
    private VertexPositionColorTexture Vertex(int cx,int cz,float x,float z,bool isFoam)
    {
        float height=WaterSurface.HeightAt(state.Town,cx,cz,x,z)+(isFoam?.008f:0);
        return AtHeight(cx,cz,x,z,height,isFoam);
    }
    private VertexPositionColorTexture AtHeight(int cx,int cz,float x,float z,float height,bool isFoam)
    {
        // Interpolate on the SAME row chord as the surface, not a new cylinder
        // sample inside a cell (which would float above the water chord).
        var a=rows.At(cz,height);var b=rows.At(cz+1,height);float t=z-cz;
        var color=isFoam?Color32.White:material.Tint(depth.At((int)x,(int)z));
        return new VertexPositionColorTexture(new Vector3(x,a.Y+(b.Y-a.Y)*t,a.Z+(b.Z-a.Z)*t),color,new Vector2(x*WaterSurface.UvScale,z*WaterSurface.UvScale));
    }
    internal void Dispose(){surfaceMesh.Dispose();foamMesh.Dispose();}
}
