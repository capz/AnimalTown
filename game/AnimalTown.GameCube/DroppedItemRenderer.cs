using System;
using System.IO;
using AnimalTown;
using Dolphin.Graphics;
using Hypercube.GameCube;

// All visible drops share one native alpha texture and one retained GX mesh.
internal sealed class DroppedItemRenderer : IDisposable
{
    private readonly DroppedItemPlan plan=new DroppedItemPlan();
    private readonly DynamicMeshBuffer buffer=new DynamicMeshBuffer(DroppedItemPlan.Capacity*6);
    private readonly Transform identity=new Transform();
    private readonly Material material=new Material{Lighting=LightingMode.Unlit,Blend=BlendMode.Alpha,DepthTest=true,DepthWrite=false,AlphaCutoff=1f/255,Cull=CullMode.Back};
    private Texture2D? texture;
    internal void Load()
    {
        if(texture!=null)return;
        texture=Texture2D.FromPackage(File.ReadAllBytes("dvd:/dropped-item.dndtex"),true);
        texture.ConfigureSampler(TextureWrapMode.Clamp,TextureWrapMode.Clamp,TextureMinFilter.LinearMipmapLinear,TextureMagFilter.Linear);
        material.Texture=texture;
    }
    internal void Prepare(GameState state,float eyeHeight)
    {
        if(!plan.Prepare(state.Town,state.X,state.Z,eyeHeight))return;
        var vertices=buffer.Reserve(plan.Count*6);WriteVertices(plan,vertices);
        buffer.Commit(plan.Count*6,plan.Revision);
    }
    internal static void WriteVertices(DroppedItemPlan plan,VertexPositionColorTexture[] vertices)
    {
        for(int n=0;n<plan.Count;n++){
            int i=plan.Order[n],at=n*6;float x=plan.X[i],y=plan.Y[i],z=plan.Z[i],half=DroppedItemPlan.Size*.5f;
            var facing=plan.Facing(i);float rx=facing.RightX*half,rz=facing.RightZ*half;
            float topX=x+facing.UpX*DroppedItemPlan.Size,topY=y+facing.UpY*DroppedItemPlan.Size,topZ=z+facing.UpZ*DroppedItemPlan.Size;
            var a=new VertexPositionColorTexture(new Vector3(x-rx,y,z-rz),Color32.White,new Vector2(0,1));
            var b=new VertexPositionColorTexture(new Vector3(topX-rx,topY,topZ-rz),Color32.White,new Vector2(0,0));
            var c=new VertexPositionColorTexture(new Vector3(topX+rx,topY,topZ+rz),Color32.White,new Vector2(1,0));
            var d=new VertexPositionColorTexture(new Vector3(x+rx,y,z+rz),Color32.White,new Vector2(1,1));
            vertices[at]=a;vertices[at+1]=b;vertices[at+2]=c;vertices[at+3]=a;vertices[at+4]=c;vertices[at+5]=d;
        }
    }
    internal void Draw(Renderer renderer){if(texture!=null&&buffer.Mesh!=null)renderer.DrawMesh(buffer.Mesh,material,identity);}
    public void Dispose(){buffer.Dispose();material.Texture=null;texture?.Dispose();texture=null;}
}
