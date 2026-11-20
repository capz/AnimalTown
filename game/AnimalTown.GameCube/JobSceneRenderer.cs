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
    internal static VertexPositionNormalColorTexture[] Sphere()
    {
        var vertices=new VertexPositionNormalColorTexture[8*6*6];int at=0;
        VertexPositionNormalColorTexture V(int ring,int side){double lat=-Math.PI*.5+ring*Math.PI/6,lon=side*Math.PI/4;return Vertex((float)(Math.Cos(lat)*Math.Cos(lon)),(float)Math.Sin(lat),(float)(Math.Cos(lat)*Math.Sin(lon)),Color32.White);}
        for(int r=0;r<6;r++)for(int s=0;s<8;s++){vertices[at++]=V(r,s);vertices[at++]=V(r+1,s+1);vertices[at++]=V(r+1,s);vertices[at++]=V(r,s);vertices[at++]=V(r,s+1);vertices[at++]=V(r+1,s+1);}return vertices;
    }
    internal static VertexPositionNormalColorTexture[] Shirt()
    {
        var v=new VertexPositionNormalColorTexture[8*4*6];int at=0;
        VertexPositionNormalColorTexture V(int ring,int side,Color32 color){float y=ring*.25f,rad=ring==4?.78f:1;double a=side*Math.PI/4;return Vertex((float)Math.Cos(a)*rad,y,(float)Math.Sin(a)*rad*.82f,color);}
        for(int r=0;r<4;r++)for(int s=0;s<8;s++){var c=r==1?new Color32(248,241,209):Color32.White;v[at++]=V(r,s,c);v[at++]=V(r+1,s+1,c);v[at++]=V(r+1,s,c);v[at++]=V(r,s,c);v[at++]=V(r,s+1,c);v[at++]=V(r+1,s+1,c);}return v;
    }
    internal void Ball(Renderer renderer,Vector3 position,Quaternion rotation,float radius,bool snow)
    {if(sphere==null)return;pose.LocalPosition=position;pose.LocalRotation=rotation;pose.LocalScale=new Vector3(radius,radius,radius);material.Color=snow?new Color32(238,246,248):new Color32(232,155,91);renderer.DrawMesh(sphere,material,pose);}
    internal static void OutfitFit(int species,out float bottom,out float width,out float height)
    {
        // The cooked root normalizes every character to one metre. Four
        // rigged sources retain 1.7-metre local vertices beneath that root.
        bottom=species==0?.28f:species==1?.2f:.43f;
        width=species==0?.205f:species==1?.19f:species==2?.3f:.34f;height=species<2?.3f:.44f;
        if(species>=2){bottom/=1.7f;width/=1.7f;height/=1.7f;}
    }
    internal void Outfit(Renderer renderer,int item,int species,Vector3 position,Quaternion rotation)
    {
        if(shirt==null||!Catalog.Has(item,ItemFlags.Wear))return;
        OutfitFit(species,out float bottom,out float width,out float height);
        float up=bottom*1.8f,tx=-2*rotation.Z*up,tz=2*rotation.X*up;
        pose.LocalPosition=new Vector3(position.X+rotation.W*tx+rotation.Y*tz,position.Y+up+rotation.Z*tx-rotation.X*tz,position.Z+rotation.W*tz-rotation.Y*tx);pose.LocalRotation=rotation;pose.LocalScale=new Vector3(width*1.8f,height*1.8f,width*1.8f);
        material.Color=item==Catalog.WorkShirt?new Color32(96,148,92):item==Catalog.StripedShirt?new Color32(94,143,185):item==Catalog.LeafShirt?new Color32(116,160,88):item==Catalog.LinenShirt?new Color32(212,198,155):item==Catalog.BerryShirt?new Color32(174,91,123):new Color32(112,177,195);renderer.DrawMesh(shirt,material,pose);
    }
}
