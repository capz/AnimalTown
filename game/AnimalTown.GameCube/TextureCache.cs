using System;
using System.IO;
using AnimalTown;
using Dolphin.Graphics;

// Cache owns GPU resources. Entries retain identity/metrics, never source pixels.
internal sealed class CachedTexture
{
    internal string Source="";
    internal uint SourceHash;
    internal int SourceWidth,SourceHeight,References,LastUse;
    internal bool Atlas;
    internal Texture2D[] Pages=new Texture2D[0];
    internal int[]? FontMetrics;
    internal int Bytes {get{int n=0;for(int i=0;i<Pages.Length;i++)n+=Pages[i].NativeBytes;return n;}}
    internal void Draw(Draw2D ui,RectF source,RectF target,Color32 color,float rotation=0,Vector2 pivot=default)
    {
        int columns=(SourceWidth+127)/128;
        for(int py=(int)source.Y/128;py<(int)Math.Ceiling((source.Y+source.Height)/128);py++)
        for(int px=(int)source.X/128;px<(int)Math.Ceiling((source.X+source.Width)/128);px++){
            float x=Math.Max(source.X,px*128),y=Math.Max(source.Y,py*128),right=Math.Min(source.X+source.Width,(px+1)*128),bottom=Math.Min(source.Y+source.Height,(py+1)*128);
            float dx=(x-source.X)*target.Width/source.Width,dy=(y-source.Y)*target.Height/source.Height;
            ui.DrawTexture(Pages[py*columns+px],new RectF(x-px*128,y-py*128,right-x,bottom-y),new RectF(target.X+dx,target.Y+dy,(right-x)*target.Width/source.Width,(bottom-y)*target.Height/source.Height),color,rotation,new Vector2(pivot.X-dx,pivot.Y-dy));
        }
    }
}
internal static class TextureCache
{
    private static readonly CachedTexture?[] entries=new CachedTexture?[256];
    private const int Budget=4*1024*1024;
    private static int clock;
    internal static int Hits,Misses,Evictions;
    internal static int ResidentBytes {get{int n=0;for(int i=0;i<entries.Length;i++)if(entries[i]!=null)n+=entries[i]!.Bytes;return n;}}
    internal static CachedTexture? Find(string source,bool atlas)
    {for(int i=0;i<entries.Length;i++){var e=entries[i];if(e!=null&&e.Source==source&&e.Atlas==atlas){e.References++;e.LastUse=++clock;Hits++;return e;}}return null;}
    internal static void Release(CachedTexture source){if(source.References<1)throw new InvalidOperationException("Texture lease released twice");source.References--;}
    internal static CachedTexture Store(CachedTexture entry)
    {
        int bytes=entry.Bytes;
        while(ResidentBytes+bytes>Budget){int oldest=-1;for(int i=0;i<entries.Length;i++)if(entries[i]!=null&&entries[i]!.References==0&&(oldest<0||entries[i]!.LastUse<entries[oldest]!.LastUse))oldest=i;
            if(oldest<0){Dispose(entry);throw new InvalidOperationException("Pinned texture cache exceeds budget");}Dispose(entries[oldest]!);entries[oldest]=null;Evictions++;}
        for(int i=0;i<entries.Length;i++)if(entries[i]==null){entry.References=1;entry.LastUse=++clock;entries[i]=entry;Misses++;return entry;}
        Dispose(entry);throw new InvalidOperationException("Texture cache entry limit reached");
    }
    private static void Dispose(CachedTexture e){for(int i=0;i<e.Pages.Length;i++)e.Pages[i]?.Dispose();}
    internal static CachedTexture Load(string path,bool atlas=false,TextureResolution resolution=TexturePolicy.Default)
    {
        // Disc URI + build commit identifies immutable original bytes before I/O.
        // Hash records the exact source package; policy/layout are separate keys.
        string key=BuildInfo.Commit+":"+path+":texture-v2:"+TexturePolicy.Edge(resolution);
        var cached=Find(key,atlas);if(cached!=null)return cached;
        var loaded=LoadAndDecode(key,path,atlas,resolution);AnimalTown.GameCube.NativeTexturePages.Collect();return loaded;
    }
    private static CachedTexture LoadAndDecode(string key,string path,bool atlas,TextureResolution resolution)=>Decode(key,File.ReadAllBytes(path),atlas,resolution);
    internal static CachedTexture LoadFloor(string path,TextureResolution resolution=TexturePolicy.Default,bool repeat=false)
    {
        string key=BuildInfo.Commit+":"+path+":floor-mip2-auto-v4:"+(repeat?"repeat:":"mirror:")+TexturePolicy.Edge(resolution);
        var cached=Find(key,false);if(cached!=null)return cached;
        var loaded=DecodeFloor(key,path,resolution,repeat);AnimalTown.GameCube.NativeTexturePages.Collect();return loaded;
    }
    private static CachedTexture DecodeFloor(string key,string path,TextureResolution resolution,bool repeat)
    {
        var source=File.ReadAllBytes(path);var package=FloorTexture.Prepare(source,resolution);
        var entry=new CachedTexture{Source=key,SourceHash=TexturePixels.Hash(source),SourceWidth=TexturePixels.Width(source),SourceHeight=TexturePixels.Height(source),Pages=new Texture2D[1]};
        try{var texture=Texture2D.FromPackage(package,true);entry.Pages[0]=texture;var wrap=repeat?TextureWrapMode.Repeat:TextureWrapMode.Mirror;texture.ConfigureSampler(wrap,wrap,texture.MipLevels>1?TextureMinFilter.LinearMipmapLinear:TextureMinFilter.Linear,TextureMagFilter.Linear);}
        catch{Dispose(entry);throw;}
        return Store(entry);
    }
    private static CachedTexture Decode(string key,byte[] source,bool atlas,TextureResolution resolution)
    {
        TexturePixels.Validate(source);int w=TexturePixels.Width(source),h=TexturePixels.Height(source),format=TexturePixels.Int(source,8);
        int columns=atlas?(w+127)/128:1,rows=atlas?(h+127)/128:1;
        var entry=new CachedTexture{Source=key,SourceHash=TexturePixels.Hash(source),SourceWidth=w,SourceHeight=h,Atlas=atlas,Pages=new Texture2D[columns*rows]};
        int edge=atlas?128:TexturePolicy.Edge(resolution);var rgba=new byte[edge*edge*4];if(format!=3&&format!=4&&format!=5)format=6;
        try{
            for(int py=0;py<rows;py++)for(int px=0;px<columns;px++){
                int sx=atlas?px*128:0,sy=atlas?py*128:0,sw=atlas?Math.Min(128,w-sx):w,sh=atlas?Math.Min(128,h-sy):h;
                int rw=atlas?sw:TexturePixels.FitWidth(w,h,resolution),rh=atlas?sh:TexturePixels.FitHeight(w,h,resolution);
                TexturePixels.Resample(source,sx,sy,sw,sh,rgba,rw,rh);
                var native=TexturePixels.Encode(rgba,rw,rh,format);
                var texture=Texture2D.CreateNative(rw,rh,(NativeTextureFormat)format,true);entry.Pages[py*columns+px]=texture;texture.ReplaceNative(native);
            }
        }catch{Dispose(entry);throw;}
        // Neither encoded originals nor decode/output scratch escape this scope.
        return Store(entry);
    }
}
