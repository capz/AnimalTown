using System;
using Dolphin.Graphics;
using Dolphin.Timing;

// Shared by the title and loading screen. Absolute time keeps the dots moving
// continuously when the title is dismissed; no world assets are involved.
internal sealed class TitleBackdrop : IDisposable
{
    internal static readonly Color32 Color=new Color32(149,198,186);
    private readonly Texture2D dots=MakeDots();
    private readonly Texture2D[] textures=new Texture2D[1];
    private readonly float[] quad=new float[24];
    internal TitleBackdrop(){textures[0]=dots;}
    internal void Draw(Draw2D ui)
    {
        float uv=-(float)(Clock.Timestamp%16000000)/16000000;
        Vertex(0,0,0,uv,uv);Vertex(4,640,0,uv+10,uv);Vertex(8,640,480,uv+10,uv+7.5f);
        Vertex(12,0,0,uv,uv);Vertex(16,640,480,uv+10,uv+7.5f);Vertex(20,0,480,uv,uv+7.5f);
        ui.DrawTexturedQuads(textures,quad,1,Color32.White);
    }
    private void Vertex(int at,float x,float y,float u,float v){quad[at]=x;quad[at+1]=y;quad[at+2]=u;quad[at+3]=v;}
    private static Texture2D MakeDots()
    {
        var pixels=new byte[64*64*4];for(int y=0;y<64;y++)for(int x=0;x<64;x++){
            int dx=Math.Min(x,64-x),dy=Math.Min(y,64-y),bx=x-32,by=y-32;
            bool dot=dx*dx+dy*dy<=9||bx*bx+by*by<=9;int at=(y*64+x)*4;
            pixels[at]=(byte)(dot?177:149);pixels[at+1]=(byte)(dot?216:198);pixels[at+2]=(byte)(dot?205:186);pixels[at+3]=255;
        }
        var texture=Texture2D.Create(64,64,pixels);texture.ConfigureSampler(TextureWrapMode.Repeat,TextureWrapMode.Repeat,TextureMinFilter.Linear,TextureMagFilter.Linear);return texture;
    }
    public void Dispose()=>dots.Dispose();
}
