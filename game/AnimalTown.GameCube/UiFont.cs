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
    // Flat storage avoids hundreds of per-glyph/layout objects in the console
    // managed heap. Direct-mapped layout slots have bounded lookup and memory.
    private readonly int regionStride;
    private readonly Texture2D?[] regionTextures;
    // Per page slice: offset XY, size WH, normalized UV/size.
    private readonly float[] regionData;
    private readonly float[] advances=new float[96];
    private readonly int[] regionCounts=new int[96];
    private readonly UiTextLayoutCache layouts=new UiTextLayoutCache();
    private readonly Texture2D[] quadTextures=new Texture2D[128];
    private readonly float[] quadVertices=new float[128*24];
    private readonly int[] pages,order,layoutOrderCounts;
    private readonly byte[] layoutOrder;
    private readonly float[] quadScratch;
    private UiFont(CachedTexture texture,byte[] metrics):this(texture,metrics,false){}
    private UiFont(CachedTexture font):this(font,new byte[0],true){}
    private UiFont(CachedTexture texture,byte[] metrics,bool system)
    {
        this.texture=texture;this.metrics=metrics;firmware=system?texture.FontMetrics:null;
        if(system&&firmware==null)throw new IOException("Missing firmware font metrics");
        regionStride=system?1:UiTextLayoutCache.GlyphRegionStride(metrics);
        regionTextures=new Texture2D?[96*regionStride];regionData=new float[96*regionStride*8];
        layoutOrder=new byte[system?UiTextLayoutCache.Slots*UiTextLayoutCache.Characters:0];
        layoutOrderCounts=new int[system?UiTextLayoutCache.Slots:0];
        pages=new int[system?128:0];order=new int[system?128:0];quadScratch=new float[system?24:0];
        PrepareGlyphs();
    }
    private void PrepareGlyphs()
    {
        for(int i=0;i<96;i++){
            int first=i*regionStride;
            if(firmware!=null){int n=i<95?i:'?'-32;advances[i]=firmware[8+n*5+4];int m=8+n*5;regionCounts[i]=firmware[m+2]>0?1:0;Region(first,texture.Pages[n/16],n%4*32+firmware[m],n%16/4*32+firmware[m+1],firmware[m+2],firmware[m+3],firmware[m],firmware[m+1]);continue;}
            int at=16+i*8,x=Get(at),y=Get(at+2),w=Get(at+4);advances[i]=Get(at+6);
            int firstX=x/128,firstY=y/128,lastX=(x+w+127)/128,lastY=(y+64+127)/128,used=0;
            if((lastX-firstX)*(lastY-firstY)>8)throw new IOException("Glyph exceeds bounded page layout");
            for(int py=firstY;py<lastY;py++)for(int px=firstX;px<lastX;px++){
                float left=Math.Max(x,px*128),top=Math.Max(y,py*128),right=Math.Min(x+w,(px+1)*128),bottom=Math.Min(y+64,(py+1)*128);
                Region(first+used++,texture.Pages[py*((texture.SourceWidth+127)/128)+px],left-px*128,top-py*128,right-left,bottom-top,left-x,top-y);
            }
            regionCounts[i]=used;
        }
    }
    private void Region(int index,Texture2D page,float x,float y,float w,float h,float dx,float dy)
    {
        regionTextures[index]=page;int at=index*8;regionData[at]=dx;regionData[at+1]=dy;regionData[at+2]=w;regionData[at+3]=h;
        regionData[at+4]=x/page.Width;regionData[at+5]=y/page.Height;regionData[at+6]=w/page.Width;regionData[at+7]=h/page.Height;
    }
    private void Vertex(int at,float x,float y,float u,float v)
    {quadVertices[at]=x;quadVertices[at+1]=y;quadVertices[at+2]=u;quadVertices[at+3]=v;}
    private int FindLayout(string text)
    {
        int slot=layouts.Find(text,advances,firmware!=null?firmware[4]:64,firmware!=null,out bool changed);
        if(slot>=0&&changed&&firmware!=null)layoutOrderCounts[slot]=-1;
        return slot;
    }
    internal void Release()=>TextureCache.Release(texture);
    internal static UiFont LoadSystem()
    {
        const string key="firmware:IPL/ascii-paged-v2";var cached=TextureCache.Find(key,true);if(cached!=null)return new UiFont(cached);
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
    internal void Draw(Draw2D ui,string text,float x,float y,Color32 color,float scale=1,float rotation=0,int visibleCharacters=-1)
    {
        int end=visibleCharacters<0?text.Length:Math.Min(visibleCharacters,text.Length);
        if(end==0)return;
        if(!Batched){DrawLegacy(ui,text,x,y,color,scale,rotation,end);return;}
        float f=firmware!=null?scale*22/firmware[1]:scale*Factor;
        int layout=FindLayout(text);float pen=0,line=0,pivotX=x,pivotY=y+16*scale;
        int used=0;float cosine=1,sine=0;
        if(firmware==null&&rotation!=0){pivotX+=(layout>=0?layouts.Width(layout)*f:Measure(text,scale))*.5f;float a=rotation*.01745329252f;cosine=(float)Math.Cos(a);sine=(float)Math.Sin(a);}
        for(int i=0;i<end;i++){
            char c=text[i];if(c=='\r')continue;if(c=='\n'){pen=0;line+=firmware!=null?firmware[4]:64;continue;}
            int glyph=Index(c);float gx=x+(layout>=0?layouts.X(layout,i):pen)*f,gy=y+(layout>=0?layouts.Y(layout,i):line)*f;
            if(c!=' '&&(firmware==null||c!='\t'))for(int p=glyph*regionStride;p<glyph*regionStride+regionCounts[glyph];p++){
                if(used==quadTextures.Length){Flush(ui,used,color,layout);used=0;}
                int d=p*8,at=used*24;float left=gx+regionData[d]*f,top=gy+regionData[d+1]*f,right=left+regionData[d+2]*f,bottom=top+regionData[d+3]*f;
                float x0=left,y0=top,x1=right,y1=top,x2=right,y2=bottom,x3=left,y3=bottom;
                if(firmware==null&&rotation!=0){float l=left-pivotX,r=right-pivotX,t=top-pivotY,b=bottom-pivotY;x0=pivotX+l*cosine-t*sine;y0=pivotY+l*sine+t*cosine;x1=pivotX+r*cosine-t*sine;y1=pivotY+r*sine+t*cosine;x2=pivotX+r*cosine-b*sine;y2=pivotY+r*sine+b*cosine;x3=pivotX+l*cosine-b*sine;y3=pivotY+l*sine+b*cosine;}
                float u=regionData[d+4],v=regionData[d+5],u1=u+regionData[d+6],v1=v+regionData[d+7];
                Vertex(at,x0,y0,u,v);Vertex(at+4,x1,y1,u1,v);Vertex(at+8,x2,y2,u1,v1);Vertex(at+12,x0,y0,u,v);Vertex(at+16,x2,y2,u1,v1);Vertex(at+20,x3,y3,u,v1);quadTextures[used++]=regionTextures[p]!;
            }
            pen+=firmware!=null&&c=='\t'?advances[0]*4:advances[glyph];
        }
        if(used>0)Flush(ui,used,color,layout);
    }
    private void Flush(Draw2D ui,int count,Color32 color,int layout)
    {
        // One draw call's glyphs only: never cross text/widget/transparency layers.
        // Headings can overlap heavily and are small; keep their original order.
        if(firmware==null||count<3){ui.DrawTexturedQuads(quadTextures,quadVertices,count,color);return;}
        for(int i=0;i<count;i++){int page=0;while(page<texture.Pages.Length&&texture.Pages[page]!=quadTextures[i])page++;pages[i]=page;}
        if(layout>=0&&layoutOrderCounts[layout]==count){for(int i=0;i<count;i++)order[i]=layoutOrder[layout*128+i];}
        else{
            UiQuadOrder.Build(quadVertices,pages,count,order);
            if(layout>=0){layoutOrderCounts[layout]=count;for(int i=0;i<count;i++)layoutOrder[layout*128+i]=(byte)order[i];}
        }
        UiQuadOrder.Apply(quadVertices,pages,count,order,quadScratch);
        for(int i=0;i<count;i++)quadTextures[i]=texture.Pages[pages[i]];
        ui.DrawTexturedQuads(quadTextures,quadVertices,count,color);
    }
    private void DrawLegacy(Draw2D ui,string text,float x,float y,Color32 color,float scale,float rotation,int end)
    {
        if(firmware!=null){
            float f=scale*22/firmware[1],pen=x;
            for(int i=0;i<end;i++){char ch=text[i];if(ch=='\r')continue;if(ch=='\n'){pen=x;y+=firmware[4]*f;continue;}int index=ch>=32&&ch<=126?ch-32:'?'-32;
                if(ch!=' '&&ch!='\t')ui.DrawTexture(texture.Pages[index/16],new RectF(index%4*32,index%16/4*32,firmware[0],firmware[1]),new RectF(pen,y,firmware[0]*f,firmware[1]*f),color);
                pen+=firmware[8+(ch=='\t'?0:index)*5+4]*(ch=='\t'?4:1)*f;
            }return;
        }
        float cursor=x,pivotX=x+Measure(text,scale)*.5f,pivotY=y+16*scale;scale*=Factor;
        for(int i=0;i<end;i++)
        {
            char c=text[i];if(c=='\r')continue;if(c=='\n'){cursor=x;y+=64*scale;continue;}
            int at=16+Index(c)*8;int width=Get(at+4);
            if(c!=' ')texture.Draw(ui,new RectF(Get(at),Get(at+2),width,64),new RectF(cursor,y,width*scale,64*scale),color,rotation,rotation==0?Vector2.Zero:new Vector2(pivotX-cursor,pivotY-y));
            cursor+=Get(at+6)*scale;
        }
    }
}
