using System;
using System.IO;
using Dolphin.Graphics;

// Proportional bitmap text: host-baked Gorditas headings and native IPL body text.
internal sealed class UiFont
{
    private readonly CachedTexture texture;
    private readonly byte[] metrics;
    private readonly int[]? firmware;
    private UiFont(CachedTexture texture,byte[] metrics){this.texture=texture;this.metrics=metrics;}
    private UiFont(CachedTexture font){firmware=font.FontMetrics;texture=font;metrics=new byte[0];}
    internal static UiFont LoadSystem()
    {
        const string key="firmware:IPL/ascii-paged-v1";var cached=TextureCache.Find(key,true);if(cached!=null)return new UiFont(cached);
        var pixels=new byte[128*128*2];var info=new int[8+95*5];
        var result=new CachedTexture{Source=key,Atlas=true,SourceWidth=512,SourceHeight=256,Pages=new Texture2D[6],FontMetrics=info};
        try{
            for(int i=0;i<6;i++){
                if(AnimalTown.GameCube.NativeTexturePages.IplPage(i,pixels,info)!=1)throw new IOException("Firmware font extraction failed");
                result.SourceHash=(result.SourceHash^AnimalTown.TexturePixels.Hash(pixels))*16777619;
                var page=Texture2D.CreateNative(128,128,NativeTextureFormat.Ia8,true);result.Pages[i]=page;page.ReplaceNative(pixels);
            }
        }catch{for(int i=0;i<6;i++)result.Pages[i]?.Dispose();throw;}
        finally{AnimalTown.GameCube.NativeTexturePages.IplPage(-1,pixels,info);}
        return new UiFont(TextureCache.Store(result));
    }
    internal static UiFont Load(string name)
    {
        var metrics=File.ReadAllBytes("dvd:/"+name+".metrics");
        if(metrics.Length!=784||metrics[0]!=65||metrics[1]!=84||metrics[2]!=70||metrics[3]!=50)throw new IOException("Invalid font metrics");
        return new UiFont(TextureCache.Load("dvd:/"+name+".dndtex",true),metrics);
    }
    private int Index(char c)=>c>=32&&c<128?c-32:'?'-32;
    private int Get(int at)=>metrics[at]|metrics[at+1]<<8;
    private float Factor=>metrics[8]/(float)metrics[4];
    internal float Measure(string text,float scale=1)
    { float x=0,max=0;for(int i=0;i<text.Length;i++){if(text[i]=='\n'){max=Math.Max(max,x);x=0;}else if(text[i]!='\r')x+=firmware!=null?firmware[8+((text[i]=='\t'?0:text[i]>=32&&text[i]<=126?text[i]-32:'?'-32))*5+4]*(text[i]=='\t'?4:1)*scale*22/firmware[1]:Get(16+Index(text[i])*8+6)*scale*Factor;}return Math.Max(max,x); }
    internal void Draw(Draw2D ui,string text,float x,float y,Color32 color,float scale=1,float rotation=0)
    {
        if(firmware!=null){
            float f=scale*22/firmware[1],pen=x;
            for(int i=0;i<text.Length;i++){char ch=text[i];if(ch=='\r')continue;if(ch=='\n'){pen=x;y+=firmware[4]*f;continue;}int index=ch>=32&&ch<=126?ch-32:'?'-32;
                if(ch!=' '&&ch!='\t')ui.DrawTexture(texture.Pages[index/16],new RectF(index%4*32,index%16/4*32,firmware[0],firmware[1]),new RectF(pen,y,firmware[0]*f,firmware[1]*f),color);
                pen+=firmware[8+(ch=='\t'?0:index)*5+4]*(ch=='\t'?4:1)*f;
            }return;
        }
        float cursor=x,pivotX=x+Measure(text,scale)*.5f,pivotY=y+16*scale;scale*=Factor;
        for(int i=0;i<text.Length;i++)
        {
            char c=text[i];if(c=='\r')continue;if(c=='\n'){cursor=x;y+=64*scale;continue;}
            int at=16+Index(c)*8;int width=Get(at+4);
            if(c!=' ')texture.Draw(ui,new RectF(Get(at),Get(at+2),width,64),new RectF(cursor,y,width*scale,64*scale),color,rotation,rotation==0?Vector2.Zero:new Vector2(pivotX-cursor,pivotY-y));
            cursor+=Get(at+6)*scale;
        }
    }
}
