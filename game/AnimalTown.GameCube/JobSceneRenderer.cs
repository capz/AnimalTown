using System;
using AnimalTown;
using AnimalTown.Jobs;
using Dolphin.Graphics;

// Reusable vertex-colour geometry: no model or texture copy per shirt/job.
internal sealed class JobSceneRenderer : IDisposable
{
    private Mesh? sphere,shirt;
    private readonly Transform pose=new Transform();
    private readonly Material material=new Material{Lighting=LightingMode.Diffuse,Cull=CullMode.Back};
    internal void Prepare(){if(sphere==null)sphere=Mesh.Create(Sphere());if(shirt==null)shirt=Mesh.Create(Shirt());}
    public void Dispose(){sphere?.Dispose();shirt?.Dispose();sphere=shirt=null;}
    private static VertexPositionNormalColorTexture Vertex(float x,float y,float z,Color32 color)
    {float length=(float)Math.Sqrt(x*x+y*y+z*z);return new VertexPositionNormalColorTexture(new Vector3(x,y,z),new Vector3(x/length,y/length,z/length),color,new Vector2(0,0));}
    private static VertexPositionNormalColorTexture[] Sphere()
    {
        var vertices=new VertexPositionNormalColorTexture[8*6*6];int at=0;
        VertexPositionNormalColorTexture V(int ring,int side){double lat=-Math.PI*.5+ring*Math.PI/6,lon=side*Math.PI/4;return Vertex((float)(Math.Cos(lat)*Math.Cos(lon)),(float)Math.Sin(lat),(float)(Math.Cos(lat)*Math.Sin(lon)),Color32.White);}
        for(int r=0;r<6;r++)for(int s=0;s<8;s++){vertices[at++]=V(r,s);vertices[at++]=V(r+1,s);vertices[at++]=V(r+1,s+1);vertices[at++]=V(r,s);vertices[at++]=V(r+1,s+1);vertices[at++]=V(r,s+1);}return vertices;
    }
    private static VertexPositionNormalColorTexture[] Shirt()
    {
        var v=new VertexPositionNormalColorTexture[8*4*6];int at=0;
        VertexPositionNormalColorTexture V(int ring,int side,Color32 color){float y=ring*.25f,rad=ring==4?.78f:1;double a=side*Math.PI/4;return Vertex((float)Math.Cos(a)*rad,y,(float)Math.Sin(a)*rad*.82f,color);}
        for(int r=0;r<4;r++)for(int s=0;s<8;s++){var c=r==1?new Color32(248,241,209):Color32.White;v[at++]=V(r,s,c);v[at++]=V(r+1,s,c);v[at++]=V(r+1,s+1,c);v[at++]=V(r,s,c);v[at++]=V(r+1,s+1,c);v[at++]=V(r,s+1,c);}return v;
    }
    internal void Ball(Renderer renderer,Vector3 position,Quaternion rotation,float radius,bool snow)
    {if(sphere==null)return;pose.LocalPosition=position;pose.LocalRotation=rotation;pose.LocalScale=new Vector3(radius,radius,radius);material.Color=snow?new Color32(238,246,248):new Color32(232,155,91);renderer.DrawMesh(sphere,material,pose);}
    internal void Outfit(Renderer renderer,int item,int species,Vector3 position,Quaternion rotation)
    {
        if(shirt==null||!Catalog.Has(item,ItemFlags.Wear))return;
        // Authored models share a one-metre body apart from the tall static set.
        float bottom=species==0?.28f:species==1?.2f:species==2?.43f:.43f;
        float width=species==0?.205f:species==1?.19f:species==2?.3f:.34f,height=species<2?.3f:.44f;
        var offset=new Vector3(0,bottom*1.8f,0);pose.LocalPosition=new Vector3(position.X,position.Y+offset.Y,position.Z);pose.LocalRotation=rotation;pose.LocalScale=new Vector3(width*1.8f,height*1.8f,width*1.8f);
        material.Color=item==Catalog.WorkShirt?new Color32(96,148,92):item==Catalog.StripedShirt?new Color32(94,143,185):item==Catalog.LeafShirt?new Color32(116,160,88):item==Catalog.LinenShirt?new Color32(212,198,155):item==Catalog.BerryShirt?new Color32(174,91,123):new Color32(112,177,195);renderer.DrawMesh(shirt,material,pose);
    }
}
