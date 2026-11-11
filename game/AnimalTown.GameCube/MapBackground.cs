using AnimalTown;
using Dolphin.Graphics;

// Sampled terrain only; live markers/grid stay in their original overlay order.
internal sealed class MapBackground : System.IDisposable
{
    // Rivers are three fine cells wide. A five-cell stride can miss entire
    // segments; sample every two cells while retaining a single cached quad.
    private const int Samples=(Town.Width+1)/2;
    private const int TextureSize=Samples<=32?32:Samples<=64?64:128;
    internal const float Size=280,Scale=Size/Town.Width;
    internal const float MinimapLeft=482,MinimapTop=312,MinimapSize=138;
    internal const float MinimapRadius=30,MinimapBulge=9;
    // 40 corner segments, 40 side segments and the top/bottom edges.
    // Two fan triangles per packed quad; no frame-time tessellation/allocation.
    private const int MinimapQuads=41;
    private readonly float[] minimapVertices=new float[MinimapQuads*24];
    private readonly Texture2D[] minimapTextures=new Texture2D[MinimapQuads];
    private readonly byte[] rgba=new byte[TextureSize*TextureSize*4];
    private Texture2D? texture;
    private Town? town;
    private int version=-1;
    public void Dispose()
    {
        // All minimap quads borrow one texture.
        System.Array.Clear(minimapTextures,0,minimapTextures.Length);
        texture?.Dispose();texture=null;town=null;version=-1;
    }
    internal MapBackground()
    {
        var first=default(Vector2);var previous=default(Vector2);int vertices=0;
        for(int corner=0;corner<4;corner++){
            float cx=corner==0||corner==3?MinimapRadius:MinimapSize-MinimapRadius;
            float cy=corner<2?MinimapRadius:MinimapSize-MinimapRadius;
            for(int i=0;i<=10;i++){
                float angle=(corner+2)*1.5707964f+i*.15707964f;
                var next=new Vector2(cx+MinimapRadius*(float)Math.Cos(angle),cy+MinimapRadius*(float)Math.Sin(angle));
                if(corner==0&&i==0)first=next;else MinimapEdge(previous,next,ref vertices);
                previous=next;
            }
            if(corner==1||corner==3)for(int i=1;i<20;i++){
                float t=i/20f,bulge=MinimapBulge*(float)Math.Sin(t*3.1415927f);
                var next=corner==1?new Vector2(MinimapSize+bulge,MinimapRadius+t*(MinimapSize-2*MinimapRadius))
                    :new Vector2(-bulge,MinimapSize-MinimapRadius-t*(MinimapSize-2*MinimapRadius));
                MinimapEdge(previous,next,ref vertices);previous=next;
            }
        }
        MinimapEdge(previous,first,ref vertices);
    }
    private void MinimapEdge(Vector2 a,Vector2 b,ref int at)
    {
        MinimapVertex(new Vector2(MinimapSize*.5f,MinimapSize*.5f),ref at);
        MinimapVertex(a,ref at);MinimapVertex(b,ref at);
    }
    private void MinimapVertex(Vector2 p,ref int at)
    {
        minimapVertices[at++]=MinimapLeft+p.X;minimapVertices[at++]=MinimapTop+p.Y;
        minimapVertices[at++]=(p.X+MinimapBulge)/(MinimapSize+2*MinimapBulge)*Samples/TextureSize;
        minimapVertices[at++]=p.Y/MinimapSize*Samples/TextureSize;
    }
    internal void Prepare(Town current)
    {
        if(town==current&&version==current.TerrainVersion)return;
        for(int z=0;z<Samples;z++)for(int x=0;x<Samples;x++){
            var c=TileColor(WaterSurface.IsWater(current,x*2+1,z*2+1)?(int)Terrain.Sea:current.At(x*2+1,z*2+1));int p=(z*TextureSize+x)*4;rgba[p]=c.R;rgba[p+1]=c.G;rgba[p+2]=c.B;rgba[p+3]=255;
        }
        if(texture==null){
            texture=Texture2D.CreateNative(TextureSize,TextureSize,NativeTextureFormat.Rgba8,false);
            for(int i=0;i<MinimapQuads;i++)minimapTextures[i]=texture;
        }
        texture.ReplaceRgba(rgba);
        town=current;version=current.TerrainVersion;
    }
    internal static Color32 TileColor(int t)=>t==1||t==(int)Terrain.Sea?new Color32(75,154,187):t==2?new Color32(167,119,73):t==3?new Color32(229,209,150):t==4||WaterTerrain.IsCoast(t)?new Color32(130,127,92):t==5?new Color32(191,179,147):new Color32(119,168,88);
    internal void Draw(Draw2D ui)=>Draw(ui,new RectF(160,115,Size,Size));
    internal void Draw(Draw2D ui,RectF bounds)=>ui.DrawTexture(texture!,new RectF(0,0,Samples,Samples),bounds,Color32.White);
    internal void DrawMinimap(Draw2D ui)=>ui.DrawTexturedQuads(minimapTextures,minimapVertices,MinimapQuads,Color32.White);
    internal static Vector2 MinimapPosition(float x,float z)
    {
        // Keep edge markers inside the silhouette, using the same centre for
        // the player's dark outline and white face.
        const float margin=4;
        float y=Math.Max(margin,Math.Min(MinimapSize-margin,z*MinimapSize/Town.Height));
        float edge=Math.Min(y-margin,MinimapSize-y-margin);
        float inset=edge<MinimapRadius?MinimapRadius-(float)Math.Sqrt(MinimapRadius*MinimapRadius-(MinimapRadius-edge)*(MinimapRadius-edge))
            :-MinimapBulge*(float)Math.Sin((edge-MinimapRadius)*3.1415927f/(MinimapSize-2*MinimapRadius));
        float px=-MinimapBulge+x*(MinimapSize+2*MinimapBulge)/Town.Width;
        return new Vector2(MinimapLeft+Math.Max(inset+margin,Math.Min(MinimapSize-inset-margin,px)),MinimapTop+y);
    }
}
