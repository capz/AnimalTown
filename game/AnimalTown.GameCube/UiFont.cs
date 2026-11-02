using System;
using System.IO;
using Dolphin.Graphics;

// Proportional bitmap text: host-baked Gorditas headings and native IPL body text.
internal sealed class UiFont
{
    private readonly Texture2D texture;
    private readonly byte[] metrics;
    private readonly IplFont? firmware;
    private UiFont(Texture2D texture,byte[] metrics){this.texture=texture;this.metrics=metrics;}
    private UiFont(IplFont font){firmware=font;texture=font.Texture;metrics=new byte[0];}
    internal static UiFont LoadSystem()=>new UiFont(IplFont.Load());
    internal static UiFont Load(string name)
    {
        var metrics=File.ReadAllBytes("dvd:/"+name+".metrics");
        if(metrics.Length!=784||metrics[0]!=65||metrics[1]!=84||metrics[2]!=70||metrics[3]!=50)throw new IOException("Invalid font metrics");
        return new UiFont(Texture2D.LoadAsync("dvd:/"+name+".dndtex",default,true).GetAwaiter().GetResult(),metrics);
    }
    private int Index(char c)=>c>=32&&c<128?c-32:'?'-32;
    private int Get(int at)=>metrics[at]|metrics[at+1]<<8;
    private float Factor=>metrics[8]/(float)metrics[4];
    internal float Measure(string text,float scale=1)
    { if(firmware!=null)return firmware.MeasureText(text,scale*22/firmware.CellHeight).X;float x=0,max=0;for(int i=0;i<text.Length;i++){if(text[i]=='\n'){max=Math.Max(max,x);x=0;}else x+=Get(16+Index(text[i])*8+6)*scale*Factor;}return Math.Max(max,x); }
    internal void Draw(Draw2D ui,string text,float x,float y,Color32 color,float scale=1,float rotation=0)
    {
        if(firmware!=null){ui.DrawText(firmware,text,new Vector2(x,y),color,scale*22/firmware.CellHeight);return;}
        float cursor=x,pivotX=x+Measure(text,scale)*.5f,pivotY=y+16*scale;scale*=Factor;
        for(int i=0;i<text.Length;i++)
        {
            char c=text[i];if(c=='\r')continue;if(c=='\n'){cursor=x;y+=64*scale;continue;}
            int at=16+Index(c)*8;int width=Get(at+4);
            if(c!=' ')ui.DrawTexture(texture,new RectF(Get(at),Get(at+2),width,64),new RectF(cursor,y,width*scale,64*scale),color,rotation,rotation==0?Vector2.Zero:new Vector2(pivotX-cursor,pivotY-y));
            cursor+=Get(at+6)*scale;
        }
    }
}
