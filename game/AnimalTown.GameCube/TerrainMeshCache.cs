using System;
using Dolphin.Graphics;
using Hypercube;

// Mesh objects, resource handles and native capacity survive recycled sections
// when the new indexed payload fits. Growth preserves the old mesh on failure.
internal sealed class TerrainMeshCache : IDisposable
{
    private sealed class RetainedMesh : Mesh
    {
        internal readonly int Handle;
        internal RetainedMesh(int handle):base(handle){Handle=handle;}
    }
    private RetainedMesh? mesh;
    internal Mesh? Mesh=>mesh;
    internal void Commit(VertexPositionNormalColorTexture[] vertices,int count,ushort[] indices,int indexCount,long revision,RenderCounters counters,bool recreate)
    {
        if(mesh!=null&&!recreate){int result=NativeFrame.Recycle(mesh.Handle,vertices,count,indices,indexCount);if(result<0)throw new InvalidOperationException("Invalid terrain mesh recycle");if(result==1){counters.GeometryRebuilds++;return;}}
        var next=new RetainedMesh(NativeFrame.Create(vertices,count,indices,indexCount));var previous=mesh;mesh=next;previous?.Dispose();counters.GeometryRebuilds++;
    }
    public void Dispose(){mesh?.Dispose();mesh=null;}
}
