using System;
using System.IO;
using AnimalTown;
using Dolphin.Graphics;
using Hypercube.GameCube;
using Dolphin.Timing;
using Dolphin.Diagnostics;
using Hypercube;

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
    private readonly long[] lastUse=new long[ArtCount];
    private const long IdleGraceUs=3000000;
    internal const int TexturePeakBytes=16*1024;
    internal VisualMemoryPressure? Memory;
    private bool needsRetry;
    private long retryAt;
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
    internal bool Load()
    {
        int index=(int)DroppedItemArt.Generic;if(textures[index]!=null)return true;
        long now=Clock.Timestamp;if(now<retryAt)return false;int ticket=0;
        if(Memory!=null&&!Memory.TryReserve(TexturePeakBytes,now,true,out ticket))return false;
        try{var texture=ReadTexture(index);textures[index]=texture;materials[index].Texture=texture;Memory?.Complete(ticket);return true;}
        catch(Exception error){Memory?.Cancel(ticket);Memory?.AllocationFailed(TexturePeakBytes,now);retryAt=now+VisualMemoryPressure.FailureBackoffUs;DebugOverlay.WriteLine("ANIMALTOWN ITEM VISUAL RETRY "+error.Message);return false;}
    }
    private static Texture2D ReadTexture(int index)
    {
        var source=File.ReadAllBytes(ContentPaths.Asset(NativeNames[index]+".dndtex"));
        if(source.Length!=11008||TexturePixels.Width(source)!=64||TexturePixels.Height(source)!=64||TexturePixels.Int(source,8)!=5||TexturePixels.Int(source,24)!=7)
            throw new InvalidDataException("Dropped item art exceeds native admission budget");
        Texture2D? texture=null;
        try{
            texture=Texture2D.FromPackage(source,true);
            texture.ConfigureSampler(TextureWrapMode.Clamp,TextureWrapMode.Clamp,TextureMinFilter.LinearMipmapLinear,TextureMagFilter.Linear);
            var ready=texture;texture=null;return ready;
        }finally{texture?.Dispose();}
    }
    internal static int GrowthPeak(int capacity,int required,bool textureMissing)
    {
        int grown=BufferGrowth.Next(capacity,required,DroppedItemPlan.Capacity*6);
        // BufferGrowth enforces the fixed 3,072-vertex ceiling. Keep the
        // bounded additions explicit for the console AOT stack analyser.
        int peak=0;
        if(grown>capacity)peak=grown*36+512;
        if(textureMissing)peak+=TexturePeakBytes;
        return peak;
    }
    internal bool Prepare(GameState state,float eyeHeight)
    {
        long now=Clock.Timestamp;
        if(needsRetry){if(now<retryAt)return false;plan.Invalidate();needsRetry=false;}
        for(int i=0;i<ArtCount;i++)if(counts[i]>0)lastUse[i]=now;
        if(!plan.Prepare(state.Town,state.X,state.Z,eyeHeight))return true;
        Array.Clear(counts,0,counts.Length);
        // The stable far-to-near order is preserved within each texture batch.
        for(int n=0;n<plan.Count;n++)counts[plan.Art[plan.Order[n]]]++;
        for(int art=0;art<ArtCount;art++){
            int count=counts[art];
            if(count==0)continue;
            lastUse[art]=now;
            var previous=buffers[art];int capacity=previous?.Capacity??0,required=count*6;
            int peak=GrowthPeak(capacity,required,textures[art]==null),ticket=0;
            if(Memory!=null&&!Memory.TryReserve(peak,now,true,out ticket)){needsRetry=true;retryAt=now+100000;return false;}
            Texture2D? newTexture=null;DynamicMeshBuffer? replacement=null;
            try{
                if(textures[art]==null)newTexture=ReadTexture(art);
                var buffer=previous;
                if(required>capacity)buffer=replacement=new DynamicMeshBuffer(DroppedItemPlan.Capacity*6);
                var vertices=buffer!.Reserve(required);
                int written=WriteVertices(plan,vertices,(DroppedItemArt)art);
                if(written!=count)throw new InvalidOperationException("Dropped item texture batch size mismatch");
                buffer.Commit(required,plan.Revision);
                if(replacement!=null){buffers[art]=replacement;replacement=null;previous?.Dispose();}
                if(newTexture!=null){textures[art]=newTexture;materials[art].Texture=newTexture;newTexture=null;}
                Memory?.Complete(ticket);
            }catch(Exception error){
                replacement?.Dispose();newTexture?.Dispose();Memory?.Cancel(ticket);Memory?.AllocationFailed(peak,now);
                needsRetry=true;retryAt=now+VisualMemoryPressure.FailureBackoffUs;
                DebugOverlay.WriteLine("ANIMALTOWN ITEM BATCH RETRY "+error.Message);return false;
            }
        }
        return true;
    }
    internal bool ReleaseUnused(bool outdoorDemand,long now,bool pressure)
    {
        for(int art=0;art<ArtCount;art++){
            if(outdoorDemand&&counts[art]>0){lastUse[art]=now;continue;}
            if((buffers[art]==null&&textures[art]==null)||(!pressure&&now-lastUse[art]<IdleGraceUs))continue;
            buffers[art]?.Dispose();buffers[art]=null;materials[art].Texture=null;
            textures[art]?.Dispose();textures[art]=null;counts[art]=0;
            // A stationary return to the same outdoor camera must republish
            // geometry even though its domain snapshot has not changed.
            plan.Invalidate();return true;
        }
        return false;
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
        for(int i=0;i<ArtCount;i++){
            var mesh=buffers[i]?.Mesh;
            if(counts[i]>0&&textures[i]!=null&&mesh!=null)
                renderer.DrawMesh(mesh,materials[i],identity);
        }
    }
    public void Dispose()
    {
        for(int i=0;i<ArtCount;i++){
            buffers[i]?.Dispose();buffers[i]=null;
            materials[i].Texture=null;
            textures[i]?.Dispose();textures[i]=null;
            counts[i]=0;
        }
        plan.Invalidate();needsRetry=false;retryAt=0;
    }
}
