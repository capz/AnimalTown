using System;
using Dolphin.Graphics;
using Hypercube;

// Small reusable section staging. Weld only complete, exactly equal attributes:
// a cliff, wet-sand tint or UV/normal seam keeps independent vertices.
internal sealed class TerrainIndexBuffer
{
    internal const int MaximumVertices=AnimalTown.TerrainStream.Width*AnimalTown.TerrainStream.Height*4;
    internal const int MaximumIndices=AnimalTown.TerrainStream.Width*AnimalTown.TerrainStream.Height*6;
    internal VertexPositionNormalColorTexture[] Vertices=new VertexPositionNormalColorTexture[0];
    internal ushort[] Indices=new ushort[0];
    internal int VertexCount {get;private set;}
    internal int IndexCount {get;private set;}
    internal void Reset(){VertexCount=IndexCount=0;}
    internal void Quad(VertexPositionNormalColorTexture a,VertexPositionNormalColorTexture b,VertexPositionNormalColorTexture c,VertexPositionNormalColorTexture d,RenderCounters? counters=null)
    {
        if(IndexCount>MaximumIndices-6)throw new InvalidOperationException("Terrain section index budget exceeded");
        if(Indices.Length<IndexCount+6){int size=Math.Min(MaximumIndices,Math.Max(6,Indices.Length*2));var next=new ushort[size];Array.Copy(Indices,next,IndexCount);Indices=next;Grew(counters);}
        ushort ia=Vertex(a,counters),ib=Vertex(b,counters),ic=Vertex(c,counters),id=Vertex(d,counters);
        Indices[IndexCount++]=ia;Indices[IndexCount++]=ib;Indices[IndexCount++]=ic;Indices[IndexCount++]=ia;Indices[IndexCount++]=ic;Indices[IndexCount++]=id;
    }
    private ushort Vertex(VertexPositionNormalColorTexture value,RenderCounters? counters)
    {
        // Shared row edges are recent. Bound the search so fragmented sections
        // cannot turn vertex staging into a quadratic per-frame cost.
        int first=Math.Max(0,VertexCount-8);
        for(int i=VertexCount-1;i>=first;i--)if(Same(Vertices[i],value))return (ushort)i;
        if(VertexCount>=MaximumVertices)throw new InvalidOperationException("Terrain section vertex budget exceeded");
        if(VertexCount==Vertices.Length){int size=Math.Min(MaximumVertices,Math.Max(4,Vertices.Length*2));var next=new VertexPositionNormalColorTexture[size];Array.Copy(Vertices,next,VertexCount);Vertices=next;Grew(counters);}
        Vertices[VertexCount]=value;return (ushort)VertexCount++;
    }
    private static bool Same(VertexPositionNormalColorTexture a,VertexPositionNormalColorTexture b)=>
        a.Position.X==b.Position.X&&a.Position.Y==b.Position.Y&&a.Position.Z==b.Position.Z&&a.Color.Packed==b.Color.Packed&&
        a.TextureCoordinate.X==b.TextureCoordinate.X&&a.TextureCoordinate.Y==b.TextureCoordinate.Y&&
        a.Normal.X==b.Normal.X&&a.Normal.Y==b.Normal.Y&&a.Normal.Z==b.Normal.Z;
    private static void Grew(RenderCounters? counters){if(counters!=null){counters.BufferGrowths++;counters.BufferAllocations++;}}
}
