using AnimalTown;
using Dolphin.Graphics;

// Sampled terrain only; live markers/grid stay in their original overlay order.
internal sealed class MapBackground
{
    private const int Samples=(Town.Width+4)/5;
    private readonly byte[] rgba=new byte[128*128*4],previous=new byte[Samples*Samples];
    private Texture2D? texture;
    private Town? town;
    internal void Prepare(Town current)
    {
        bool changed=town!=current;int at=0;
        for(int z=0;z<Town.Height;z+=5)for(int x=0;x<Town.Width;x+=5){byte tile=(byte)current.At(x+2,z+2);if(previous[at]!=tile){previous[at]=tile;changed=true;}at++;}
        if(!changed)return;town=current;
        for(int z=0;z<Samples;z++)for(int x=0;x<Samples;x++){
            var c=TileColor(previous[z*Samples+x]);int p=(z*128+x)*4;rgba[p]=c.R;rgba[p+1]=c.G;rgba[p+2]=c.B;rgba[p+3]=255;
        }
        if(texture==null)texture=Texture2D.CreateNative(128,128,NativeTextureFormat.Rgba8,false);
        texture.ReplaceRgba(rgba);
    }
    internal static Color32 TileColor(int t)=>t==1?new Color32(75,154,187):t==2?new Color32(167,119,73):t==3?new Color32(229,209,150):t==4?new Color32(130,127,92):t==5?new Color32(191,179,147):new Color32(119,168,88);
    internal void Draw(Draw2D ui)=>ui.DrawTexture(texture!,new RectF(0,0,Samples,Samples),new RectF(160,115,Town.Width*.7f,Town.Height*.7f),Color32.White);
}
