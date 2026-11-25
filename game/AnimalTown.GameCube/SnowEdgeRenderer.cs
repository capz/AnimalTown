using System;
using AnimalTown;
using Dolphin.Graphics;
using Dolphin.Timing;
using Hypercube.GameCube;

// Only the selected 8 m shadow pages have edge geometry. Reuse unchanged pages;
// retire departed pages before admitting replacements. No summer owner exists.
internal sealed partial class SnowEdgeRenderer : IDisposable
{
    private const int Capacity=TerrainStream.Capacity;
    private sealed class Part
    {
        internal int X,Z,Height,Revision=-1;
        internal uint Signature;
        internal bool Wanted;
        internal TerrainIndexBuffer? Buffer;
        internal TerrainMeshCache? Mesh;
        internal void Dispose(){Mesh?.Dispose();Mesh=null;Buffer=null;Revision=-1;}
    }
    private readonly Part?[] parts=new Part?[Capacity];
    private readonly Material material=new Material{Color=Color32.White,Cull=CullMode.Back,Lighting=LightingMode.Unlit};
    internal bool HasGeometry {get;private set;}
    internal void Begin(){for(int i=0;i<parts.Length;i++)if(parts[i]!=null)parts[i]!.Wanted=false;}
    internal void Include(int x,int z,int height)
    {
        if(x>=Town.Width||x+TerrainStream.Width<=0||z>=Town.Height||z+height<=0)return;
        int empty=-1;
        for(int i=0;i<parts.Length;i++){
            var p=parts[i];if(p==null){if(empty<0)empty=i;continue;}
            if(p.X==x&&p.Z==z&&p.Height==height){p.Wanted=true;return;}
        }
        if(empty<0){for(int i=0;i<parts.Length;i++)if(!parts[i]!.Wanted){parts[i]!.Dispose();empty=i;break;}}
        if(empty<0)throw new InvalidOperationException("Snow edges exceeded the visible page budget");
        parts[empty]=new Part{X=x,Z=z,Height=height,Wanted=true};
    }
    internal bool Prepare(GameState state,SnowEdges coverage,VisualMemoryPressure? memory,Action<int> completed)
    {
        HasGeometry=false;
        for(int i=0;i<parts.Length;i++)if(parts[i]!=null&&!parts[i]!.Wanted){parts[i]!.Dispose();parts[i]=null;}
        for(int i=0;i<parts.Length;i++){
            var p=parts[i];if(p==null)continue;
            if(p.Revision==coverage.Revision){HasGeometry|=p.Mesh!=null;continue;}
            int quads=0,corners=0;uint signature=2166136261;
            for(int z=Math.Max(0,p.Z);z<Math.Min(Town.Height,p.Z+p.Height);z++)for(int x=Math.Max(0,p.X);x<Math.Min(Town.Width,p.X+TerrainStream.Width);x++){
                int cell=z*Town.Width+x,edges=coverage.Edges[cell];signature=unchecked((signature^(uint)(edges|coverage.Levels[cell]<<8))*16777619);for(int side=0;side<4;side++)if((edges&(1<<side))!=0)quads++;
                for(int side=0;side<4;side++)if((edges&((1<<side)|(1<<((side+3)%4))))==((1<<side)|(1<<((side+3)%4))))corners++;
            }
            if(p.Revision>=0&&p.Signature==signature){p.Revision=coverage.Revision;HasGeometry|=p.Mesh!=null;continue;}
            if(quads==0){p.Dispose();p.Revision=coverage.Revision;p.Signature=signature;continue;}
            int ticket=0,peak=(quads*(4*36+6*2)+corners*3*38)*3+4096;
            if(memory!=null&&!memory.TryReserve(peak,Clock.Timestamp,true,out ticket))return false;
            try{
                int cells=TerrainStream.Width*p.Height;
                p.Buffer??=new TerrainIndexBuffer(cells*28,cells*36);p.Buffer.Reset();
                for(int z=Math.Max(0,p.Z);z<Math.Min(Town.Height,p.Z+p.Height);z++)for(int x=Math.Max(0,p.X);x<Math.Min(Town.Width,p.X+TerrainStream.Width);x++){
                    int edges=coverage.Edges[z*Town.Width+x];
                    for(int side=0;side<4;side++)if((edges&(1<<side))!=0){
                        SnowEdges.Quad(x,z,edges,side,out float ax,out float az,out float bx,out float bz,out float cx,out float cz,out float dx,out float dz);
                        p.Buffer.Quad(Vertex(state.Town,x,z,ax,az,0),Vertex(state.Town,x,z,bx,bz,0),
                            Vertex(state.Town,x,z,cx,cz,SnowVisibility.Height),Vertex(state.Town,x,z,dx,dz,SnowVisibility.Height),FrameProfile.Counters);
                    }
                    // Bilinear mask corners extend slightly past the straight
                    // cut. A small white cap closes that join at snow height.
                    float left=x+SnowEdges.Width,right=x+1-SnowEdges.Width,top=z+SnowEdges.Width,bottom=z+1-SnowEdges.Width;
                    if((edges&9)==9)Cap(p.Buffer,state.Town,x,z,left,top,left+SnowEdges.Width,top,left,top+SnowEdges.Width);
                    if((edges&3)==3)Cap(p.Buffer,state.Town,x,z,right,top,right,top+SnowEdges.Width,right-SnowEdges.Width,top);
                    if((edges&6)==6)Cap(p.Buffer,state.Town,x,z,right,bottom,right-SnowEdges.Width,bottom,right,bottom-SnowEdges.Width);
                    if((edges&12)==12)Cap(p.Buffer,state.Town,x,z,left,bottom,left,bottom-SnowEdges.Width,left+SnowEdges.Width,bottom);
                }
                p.Mesh??=new TerrainMeshCache();
                p.Mesh.Commit(p.Buffer.Vertices,p.Buffer.VertexCount,p.Buffer.Indices,p.Buffer.IndexCount,coverage.Revision,FrameProfile.Counters,false);
                p.Revision=coverage.Revision;p.Signature=signature;HasGeometry=true;
                if(ticket!=0){completed(ticket);ticket=0;}
            }catch(Exception){memory?.AllocationFailed(peak,Clock.Timestamp);return false;}
            finally{if(ticket!=0)memory?.Cancel(ticket);}
        }
        return true;
    }
    private static void Cap(TerrainIndexBuffer buffer,Town town,int x,int z,float ax,float az,float bx,float bz,float cx,float cz)
        =>buffer.Triangle(Vertex(town,x,z,ax,az,SnowVisibility.Height+.0005f),Vertex(town,x,z,bx,bz,SnowVisibility.Height+.0005f),Vertex(town,x,z,cx,cz,SnowVisibility.Height+.0005f),FrameProfile.Counters);
    internal void Draw(Renderer renderer,Transform pose)
    {for(int i=0;i<parts.Length;i++)if(parts[i]?.Mesh?.Mesh!=null)renderer.DrawMesh(parts[i]!.Mesh!.Mesh!,material,pose);}
    public void Dispose(){for(int i=0;i<parts.Length;i++){parts[i]?.Dispose();parts[i]=null;}}
}
