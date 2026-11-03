using System;
using System.IO;
using Dolphin.Graphics;

// Proportional bitmap text: host-baked Gorditas headings and native IPL body text.
internal sealed class UiFont
{
    private readonly CachedTexture texture;
    private readonly byte[] metrics;
    private readonly int[]? firmware;
    internal static bool Batched=true;
    private sealed class Glyph
    {
        internal TextureRegion[] Regions=new TextureRegion[0];
        internal float[] X=new float[0],Y=new float[0];
        internal float Width,Height,Advance;
    }
    private sealed class Layout
    {
        internal string Text="";
        internal readonly float[] X=new float[128],Y=new float[128];
        internal float Width;
        internal int LastUse;
    }
    private readonly Glyph[] glyphs=new Glyph[96];
    private readonly Layout[] layouts=new Layout[48];
    private readonly SpriteDraw[] sprites=new SpriteDraw[128];
    private int clock;
    private UiFont(CachedTexture texture,byte[] metrics){this.texture=texture;this.metrics=metrics;PrepareGlyphs();}
    private UiFont(CachedTexture font){firmware=font.FontMetrics;texture=font;metrics=new byte[0];PrepareGlyphs();}
    private void PrepareGlyphs()
    {
        for(int i=0;i<layouts.Length;i++)layouts[i]=new Layout();
        for(int i=0;i<glyphs.Length;i++){
            var g=new Glyph();glyphs[i]=g;
            if(firmware!=null){int n=i<95?i:'?'-32;g.Width=firmware[0];g.Height=firmware[1];g.Advance=firmware[8+n*5+4];g.Regions=new[]{new TextureRegion(texture.Pages[n/16],new RectF(n%4*32,n%16/4*32,g.Width,g.Height))};g.X=new float[1];g.Y=new float[1];continue;}
            int at=16+i*8,x=Get(at),y=Get(at+2),w=Get(at+4);g.Width=w;g.Height=64;g.Advance=Get(at+6);
            int firstX=x/128,firstY=y/128,lastX=(x+w+127)/128,lastY=(y+64+127)/128,used=0;
            g.Regions=new TextureRegion[(lastX-firstX)*(lastY-firstY)];g.X=new float[g.Regions.Length];g.Y=new float[g.Regions.Length];
            for(int py=firstY;py<lastY;py++)for(int px=firstX;px<lastX;px++){
                float left=Math.Max(x,px*128),top=Math.Max(y,py*128),right=Math.Min(x+w,(px+1)*128),bottom=Math.Min(y+64,(py+1)*128);
                g.Regions[used]=new TextureRegion(texture.Pages[py*((texture.SourceWidth+127)/128)+px],new RectF(left-px*128,top-py*128,right-left,bottom-top));g.X[used]=left-x;g.Y[used++]=top-y;
            }
        }
    }
    private Layout? FindLayout(string text)
    {
        if(text.Length>128)return null;
        int oldest=0;
        for(int i=0;i<layouts.Length;i++){
            var entry=layouts[i];if(entry.Text==text){entry.LastUse=++clock;return entry;}
            if(entry.LastUse<layouts[oldest].LastUse)oldest=i;
        }
        var result=layouts[oldest];result.Text=text;result.LastUse=++clock;float x=0,y=0,max=0;
        for(int i=0;i<text.Length;i++){
            result.X[i]=x;result.Y[i]=y;char c=text[i];
            if(c=='\r')continue;if(c=='\n'){max=Math.Max(max,x);x=0;y+=firmware!=null?firmware[4]:64;continue;}
            x+=firmware!=null&&c=='\t'?glyphs[0].Advance*4:glyphs[Index(c)].Advance;
        }
        result.Width=Math.Max(max,x);return result;
    }
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
        if(!Batched){DrawLegacy(ui,text,x,y,color,scale,rotation);return;}
        float f=firmware!=null?scale*22/firmware[1]:scale*Factor;
        var layout=FindLayout(text);float pen=0,line=0,pivotX=x+(layout!=null?layout.Width*f:Measure(text,scale))*.5f,pivotY=y+16*scale;
        int used=0;
        for(int i=0;i<text.Length;i++){
            char c=text[i];if(c=='\r')continue;if(c=='\n'){pen=0;line+=firmware!=null?firmware[4]:64;continue;}
            var g=glyphs[Index(c)];float gx=x+(layout!=null?layout.X[i]:pen)*f,gy=y+(layout!=null?layout.Y[i]:line)*f;
            if(c!=' '&&(firmware==null||c!='\t'))for(int p=0;p<g.Regions.Length;p++){
                if(used==sprites.Length){ui.DrawSprites(sprites,0,used);used=0;}
                var region=g.Regions[p];float dx=gx+g.X[p]*f,dy=gy+g.Y[p]*f;
                var sprite=new SpriteDraw(region,new RectF(dx,dy,region.Source.Width*f,region.Source.Height*f),color);
                sprite.RotationDegrees=firmware==null?rotation:0;sprite.Origin=rotation==0?Vector2.Zero:new Vector2(pivotX-dx,pivotY-dy);sprites[used++]=sprite;
            }
            pen+=firmware!=null&&c=='\t'?glyphs[0].Advance*4:g.Advance;
        }
        if(used>0)ui.DrawSprites(sprites,0,used);
    }
    private void DrawLegacy(Draw2D ui,string text,float x,float y,Color32 color,float scale,float rotation)
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
