using System;
using AnimalTown;
using Dolphin.Graphics;

internal sealed partial class SnowEdgeRenderer
{
    internal static VertexPositionNormalColorTexture Vertex(Town town,int cellX,int cellZ,float x,float z,float lift)
    {
        // Follow the same row chord as the archived ground; the snow cap is
        // the exact radial transform of that chord, including on terraces.
        var a=CylinderWorld.Surface(cellZ,0,GroundSurface.HeightAt(town,cellX,cellZ,x,cellZ));
        var b=CylinderWorld.Surface(cellZ+1,0,GroundSurface.HeightAt(town,cellX,cellZ,x,cellZ+1));
        float t=z-cellZ,y=a.Y+(b.Y-a.Y)*t,worldZ=a.Z+(b.Z-a.Z)*t;
        if(lift>0){float scale=(CylinderWorld.Radius+lift)/CylinderWorld.Radius;y=(y+CylinderWorld.Radius)*scale-CylinderWorld.Radius;worldZ*=scale;}
        else{y+=a.UpY*.001f;worldZ+=a.UpZ*.001f;}
        return new VertexPositionNormalColorTexture(new Vector3(x,y,worldZ),new Vector3(0,1,0),Color32.White,new Vector2(x*.5f,z*.5f));
    }
}
