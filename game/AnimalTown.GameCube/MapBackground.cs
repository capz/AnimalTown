using AnimalTown;
using Dolphin.Graphics;

// Sampled terrain only; live markers/grid stay in their original overlay order.
internal sealed class MapBackground
{
    // Rivers are three fine cells wide. A five-cell stride can miss entire
    // segments; sample every two cells while retaining a single cached quad.
    private const int Samples=(Town.Width+1)/2;
    private const int TextureSize=Samples<=32?32:Samples<=64?64:128;
    internal const float Size=280,Scale=Size/Town.Width;
    private readonly byte[] rgba=new byte[TextureSize*TextureSize*4];
    private Texture2D? texture;
    private Town? town;
    private int version=-1;
    internal void Prepare(Town current)
    {
        if(town==current&&version==current.TerrainVersion)return;
        for(int z=0;z<Samples;z++)for(int x=0;x<Samples;x++){
            var c=TileColor(WaterSurface.IsWater(current,x*2+1,z*2+1)?(int)Terrain.Sea:current.At(x*2+1,z*2+1));int p=(z*TextureSize+x)*4;rgba[p]=c.R;rgba[p+1]=c.G;rgba[p+2]=c.B;rgba[p+3]=255;
        }
        if(texture==null)texture=Texture2D.CreateNative(TextureSize,TextureSize,NativeTextureFormat.Rgba8,false);
        texture.ReplaceRgba(rgba);
        town=current;version=current.TerrainVersion;
    }
    internal static Color32 TileColor(int t)=>t==1||t==(int)Terrain.Sea?new Color32(75,154,187):t==2?new Color32(167,119,73):t==3?new Color32(229,209,150):t==4||WaterTerrain.IsCoast(t)?new Color32(130,127,92):t==5?new Color32(191,179,147):new Color32(119,168,88);
    internal void Draw(Draw2D ui)=>Draw(ui,new RectF(160,115,Size,Size));
    internal void Draw(Draw2D ui,RectF bounds)=>ui.DrawTexture(texture!,new RectF(0,0,Samples,Samples),bounds,Color32.White);
}
