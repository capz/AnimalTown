using System;
using AnimalTown;
using Dolphin.Graphics;
using Hypercube.GameCube;

// Reuses the floor's visibility selection. Cells keep depth tint continuous;
// Geometry is baked at the terrain window's reference Z, then scrolled by the
// same rigid transform as dry ground and banks. Waves only animate materials.
internal sealed class WaterRenderer
{
    private readonly WaterMaterial material;
    internal WaterRenderer(WaterMaterial material){this.material=material;}
    private VertexPositionColorTexture[] surface=new VertexPositionColorTexture[0],foam=new VertexPositionColorTexture[0];
    private DynamicMeshBuffer? surfaceMesh,foamMesh;
    private long revision;
    private int surfaceCount,foamCount;
    private GameState state=null!;
    private readonly Color32[] cornerTint=new Color32[(TerrainStream.Width+1)*(TerrainStream.Height+1)];
    private readonly bool[] tintReady=new bool[(TerrainStream.Width+1)*(TerrainStream.Height+1)];
    private int originX,originZ,cachedRow=int.MinValue;
    private CylinderPose rowA,rowB;
    private CylinderRows rows=null!;
    private bool deepSea;
    private Color32 deepTint;
    internal int Triangles=>(surfaceCount+foamCount)/3;
    internal Mesh? SurfaceMesh=>surfaceCount==0?null:surfaceMesh?.Mesh;
    internal Mesh? FoamMesh=>foamCount==0?null:foamMesh?.Mesh;
    private int cells;
    internal void Begin(GameState next,int left,int top,CylinderRows cache,bool deep)
    {
        state=next;surfaceCount=foamCount=cells=0;cachedRow=int.MinValue;rows=cache;deepSea=deep;deepTint=material.Tint(1);
        Array.Clear(tintReady,0,tintReady.Length);
        originX=left;originZ=top;
    }
    internal void AppendDeepRow(int z)=>Quad(ref surface,ref surfaceCount,originX,z,originX,z,originX+TerrainStream.Width,z,originX+TerrainStream.Width,z+1,originX,z+1,false);
    internal void Append(FloorStrips strips)
    {
        for(int n=0;n<strips.Count;n++){
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
                if(state.Town.At(x,z)==(int)Terrain.Water){RiverFoam(x,z);continue;}
                float w=WaterSurface.FoamWidth;
                if(WaterSurface.IsBank(state.Town,x,z-1))Quad(ref foam,ref foamCount,x,z,x,z,x+1,z,x+1,z+w,x,z+w,true);
                if(WaterSurface.IsBank(state.Town,x,z+1))Quad(ref foam,ref foamCount,x,z,x,z+1-w,x+1,z+1-w,x+1,z+1,x,z+1,true);
                // Trim ends on the other sides so corner foam does not overlap.
                float lo=WaterSurface.IsBank(state.Town,x,z-1)?w:0,hi=WaterSurface.IsBank(state.Town,x,z+1)?1-w:1;
                if(WaterSurface.IsBank(state.Town,x-1,z))Quad(ref foam,ref foamCount,x,z,x,z+lo,x+w,z+lo,x+w,z+hi,x,z+hi,true);
                if(WaterSurface.IsBank(state.Town,x+1,z))Quad(ref foam,ref foamCount,x,z,x+1-w,z+lo,x+1,z+lo,x+1,z+hi,x+1-w,z+hi,true);
            }
        }
    }
    internal void Finish()
    {
        revision++;
        if(surfaceCount>0)surfaceMesh!.Commit(surfaceCount,revision,FrameProfile.Counters);
        else if(surfaceMesh!=null){surfaceMesh.Dispose();surfaceMesh=null;surface=new VertexPositionColorTexture[0];}
        if(foamCount>0)foamMesh!.Commit(foamCount,revision,FrameProfile.Counters);
        else if(foamMesh!=null){foamMesh.Dispose();foamMesh=null;foam=new VertexPositionColorTexture[0];}
    }
    internal void Archive(int section)
    {
        TerrainSection.ArchivePart(section,104,SurfaceMesh,surfaceCount);
        TerrainSection.ArchivePart(section,105,FoamMesh,foamCount);
    }
    internal void Dispose(){surfaceMesh?.Dispose();foamMesh?.Dispose();surfaceMesh=foamMesh=null;}
    private void RiverFoam(int x,int z)
    {
        for(int side=0;side<4;side++)if(RiverBankGeometry.Foam(state.Town,x,z,side,out var a,out var b,out var c,out var d))
            Quad(ref foam,ref foamCount,x,z,a.X,a.Z,b.X,b.Z,c.X,c.Z,d.X,d.Z,true);
        for(int corner=0;corner<4;corner++)if(RiverBankGeometry.Corner(state.Town,x,z,corner,out var a,out var b,out var c)){
            var ob=RiverBankGeometry.FoamOuter(a,b);var oc=RiverBankGeometry.FoamOuter(a,c);
            Quad(ref foam,ref foamCount,x,z,c.X,c.Z,b.X,b.Z,ob.X,ob.Z,oc.X,oc.Z,true);
        }
    }

    private void Mouth(int cx,int cz,float ax,float az,float bx,float bz,int nx,int nz)
    {
        if(state.Town.At(nx,nz)!=(int)Terrain.Water)return;
        float topA=TerrainElevation.AtCell(state.Town,cx,cz,ax,az),topB=TerrainElevation.AtCell(state.Town,cx,cz,bx,bz);
        float lowA=TerrainElevation.AtCell(state.Town,nx,nz,ax,az),lowB=TerrainElevation.AtCell(state.Town,nx,nz,bx,bz);
        if(topA<=lowA&&topB<=lowB)return;
        var a=AtHeight(cx,cz,ax,az,topA,false);var b=AtHeight(cx,cz,bx,bz,topB,false);
        var c=AtHeight(cx,cz,bx,bz,lowB,false);var d=AtHeight(cx,cz,ax,az,lowA,false);
        if(surfaceMesh==null)surfaceMesh=new DynamicMeshBuffer(TerrainStream.Width*TerrainStream.Height*54);
        surface=GeometryStaging.Reserve(surfaceMesh,surface,surfaceCount,surfaceCount+12,FrameProfile.Counters);
        surface[surfaceCount++]=a;surface[surfaceCount++]=b;surface[surfaceCount++]=c;surface[surfaceCount++]=a;surface[surfaceCount++]=c;surface[surfaceCount++]=d;
        surface[surfaceCount++]=a;surface[surfaceCount++]=c;surface[surfaceCount++]=b;surface[surfaceCount++]=a;surface[surfaceCount++]=d;surface[surfaceCount++]=c;
    }
    internal void Animate(double seconds)=>material.Animate(seconds);
    internal void SetTerrainTint(uint rgba)=>material.SetTerrainTint(rgba);
    private void Quad(ref VertexPositionColorTexture[] buffer,ref int used,int cx,int cz,float ax,float az,float bx,float bz,float dx,float dz,float ex,float ez,bool isFoam)
    {
        if(isFoam&&foamMesh==null)foamMesh=new DynamicMeshBuffer(TerrainStream.Width*TerrainStream.Height*24);
        if(!isFoam&&surfaceMesh==null)surfaceMesh=new DynamicMeshBuffer(TerrainStream.Width*TerrainStream.Height*54);
        buffer=GeometryStaging.Reserve(isFoam?foamMesh!:surfaceMesh!,buffer,used,used+6,FrameProfile.Counters);
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
        if(cachedRow!=cz){rowA=rows.At(cz);rowB=rows.At(cz+1);cachedRow=cz;}
        float ay=rowA.Y+height*rowA.UpY,az=rowA.Z+height*rowA.UpZ,by=rowB.Y+height*rowB.UpY,bz=rowB.Z+height*rowB.UpZ,t=z-cz;
        var color=isFoam?Color32.White:Tint((int)x,(int)z);
        return new VertexPositionColorTexture(new Vector3(x,ay+(by-ay)*t,az+(bz-az)*t),color,new Vector2(x*WaterSurface.UvScale,z*WaterSurface.UvScale));
    }
    private Color32 Tint(int x,int z)
    {
        if(deepSea)return deepTint;
        int dx=x-originX,dz=z-originZ;
        if((uint)dx>TerrainStream.Width||(uint)dz>TerrainStream.Height)return material.Tint(WaterSurface.Depth(state.Town,x,z));
        int at=dz*(TerrainStream.Width+1)+dx;
        if(!tintReady[at]){cornerTint[at]=material.Tint(WaterSurface.Depth(state.Town,x,z));tintReady[at]=true;}
        return cornerTint[at];
    }
    internal void Draw(Renderer renderer,Transform parent)
    {
        if(surfaceCount>0)renderer.DrawMesh(surfaceMesh!.Mesh!,material.Surface,parent);
        if(foamCount>0)renderer.DrawMesh(foamMesh!.Mesh!,material.Foam,parent);
    }
}
