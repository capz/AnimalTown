using System;
using Dolphin.Graphics;
using Hypercube.GameCube;

internal static class GeometryStaging
{
    // preview.4.1 Reserve preserves the last committed count. During a rebuild
    // the new prefix may be longer, so preserve that prefix on geometric growth.
    internal static VertexPositionColorTexture[] Reserve(DynamicMeshBuffer mesh,VertexPositionColorTexture[] previous,int written,int required,Hypercube.RenderCounters? counters=null)
    {
        var next=mesh.Reserve(required,counters);
        if(!ReferenceEquals(previous,next)&&written>0)Array.Copy(previous,next,written);
        return next;
    }
}
