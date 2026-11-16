using System;
using System.IO;
using AnimalTown;
using Dolphin.Graphics;
using Hypercube.GameCube;

// Retains one small GX native texture/material per used item art.
// Geometry is built only for visible categories and reused for stationary views.
// Each category follows the existing alpha-sorted, camera-facing billboard path.
internal sealed class DroppedItemRenderer : IDisposable
{
    private const int ArtCount=(int)DroppedItemArt.Count;
    private static readonly string[] NativeNames={
        "dropped-item","dropped-money","dropped-tool",
        "dropped-clothing","dropped-quest","dropped-present"
    };
    private readonly DroppedItemPlan plan=new DroppedItemPlan();
    private readonly DynamicMeshBuffer?[] buffers=new DynamicMeshBuffer?[ArtCount];
    private readonly Texture2D?[] textures=new Texture2D?[ArtCount];
    private readonly Material[] materials=new Material[ArtCount];
    private readonly int[] counts=new int[ArtCount];
    private readonly Transform identity=new Transform();

    internal DroppedItemRenderer()
    {
        for(int i=0;i<ArtCount;i++)
            materials[i]=new Material{
                Lighting=LightingMode.Unlit,
                Blend=BlendMode.Alpha,
                DepthTest=true,
                DepthWrite=false,
                AlphaCutoff=1f/255,
                Cull=CullMode.Back
            };
    }
    internal void Load()=>LoadTexture((int)DroppedItemArt.Generic);
    private void LoadTexture(int index)
    {
        if(textures[index]!=null)return;
        var texture=Texture2D.FromPackage(File.ReadAllBytes("dvd:/"+NativeNames[index]+".dndtex"),true);
        texture.ConfigureSampler(TextureWrapMode.Clamp,TextureWrapMode.Clamp,
            TextureMinFilter.LinearMipmapLinear,TextureMagFilter.Linear);
        textures[index]=texture;materials[index].Texture=texture;
    }
    internal void Prepare(GameState state,float eyeHeight)
    {
        if(!plan.Prepare(state.Town,state.X,state.Z,eyeHeight))return;
        Array.Clear(counts,0,counts.Length);
        // The stable far-to-near order is preserved within each texture batch.
        for(int n=0;n<plan.Count;n++)counts[plan.Art[plan.Order[n]]]++;
        for(int art=0;art<ArtCount;art++){
            int count=counts[art];
            if(count==0)continue;
            LoadTexture(art);
            var buffer=buffers[art]??=new DynamicMeshBuffer(DroppedItemPlan.Capacity*6);
            var vertices=buffer.Reserve(count*6);
            int written=WriteVertices(plan,vertices,(DroppedItemArt)art);
            if(written!=count)throw new InvalidOperationException("Dropped item texture batch size mismatch");
            buffer.Commit(count*6,plan.Revision);
        }
    }
    // With no filter, retain the old single-batch order for geometry verification.
    internal static int WriteVertices(DroppedItemPlan plan,VertexPositionColorTexture[] vertices,DroppedItemArt? only=null)
    {
        int written=0;
        for(int n=0;n<plan.Count;n++){
            int index=plan.Order[n];
            if(only.HasValue&&plan.Art[index]!=(byte)only.Value)continue;
            WriteOne(plan,index,vertices,written*6);
            written++;
        }
        return written;
    }
    private static void WriteOne(DroppedItemPlan plan,int index,VertexPositionColorTexture[] vertices,int at)
    {
        float x=plan.X[index],y=plan.Y[index],z=plan.Z[index],half=DroppedItemPlan.Size*.5f;
        var facing=plan.Facing(index);float rx=facing.RightX*half,rz=facing.RightZ*half;
        float topX=x+facing.UpX*DroppedItemPlan.Size,topY=y+facing.UpY*DroppedItemPlan.Size,topZ=z+facing.UpZ*DroppedItemPlan.Size;
        var a=new VertexPositionColorTexture(new Vector3(x-rx,y,z-rz),Color32.White,new Vector2(0,1));
        var b=new VertexPositionColorTexture(new Vector3(topX-rx,topY,topZ-rz),Color32.White,new Vector2(0,0));
        var c=new VertexPositionColorTexture(new Vector3(topX+rx,topY,topZ+rz),Color32.White,new Vector2(1,0));
        var d=new VertexPositionColorTexture(new Vector3(x+rx,y,z+rz),Color32.White,new Vector2(1,1));
        vertices[at]=a;vertices[at+1]=b;vertices[at+2]=c;
        vertices[at+3]=a;vertices[at+4]=c;vertices[at+5]=d;
    }
    internal void Draw(Renderer renderer)
    {
        for(int i=0;i<ArtCount;i++)
            if(counts[i]>0&&textures[i]!=null&&buffers[i]?.Mesh!=null)
                renderer.DrawMesh(buffers[i]!.Mesh,materials[i],identity);
    }
    public void Dispose()
    {
        for(int i=0;i<ArtCount;i++){
            buffers[i]?.Dispose();buffers[i]=null;
            materials[i].Texture=null;
            textures[i]?.Dispose();textures[i]=null;
            counts[i]=0;
        }
    }
}
