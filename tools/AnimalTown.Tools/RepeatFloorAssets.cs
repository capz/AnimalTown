using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using SkiaSharp;
using AnimalTown;

internal static class RepeatFloorAssets
{
    internal static readonly string[] Names={"grass","water","wood","sand","rock","pavement","dirt","floor","building","terracotta","stone_pavement","sand_patch","pond"};
    internal static void Build(string root)
    {
        var config=JsonNode.Parse(File.ReadAllText(Path.Combine(root,"assets/terrain/terrain.json")))!;
        string output=Path.Combine(root,"out/disc");Directory.CreateDirectory(output);
        string forced=(string)config["season"]!;int force=forced=="auto"?-1:Array.IndexOf(new[]{"spring","summer","fall","winter"},forced);if(force<0&&forced!="auto")throw new InvalidDataException("Unknown floor season");
        File.WriteAllBytes(Path.Combine(output,"terrain.cfg"),BitConverter.GetBytes(force));
        string retired=Path.Combine(root,"out/retired-terrain");Directory.CreateDirectory(retired);
        foreach(string pattern in new[]{"terrain-*.bin","terrain-*.dndtex","floor-*.bin","floor-*.dndtex"})foreach(string old in Directory.GetFiles(output,pattern))File.Move(old,Path.Combine(retired,Path.GetFileName(old)),true);
        var provenance=new List<object>();
        foreach(string season in new[]{"spring","summer","fall","winter"}){
            var packages=new List<byte[]>();var identifiers=new Dictionary<string,int>();var styles=new List<int[]>();
            foreach(string name in Names){
                var baseStyle=config["surfaces"]![name]!;var style=config["seasons"]?[season]?[name]??baseStyle;
                var sources=(style["repeatSources"]??baseStyle["repeatSources"]??style["sources"]??baseStyle["sources"])!.AsArray();
                bool center=style["repeatSources"]==null&&baseStyle["repeatSources"]==null&&(string?)style["mode"]=="nineSliceStretch";
                // Existing patch centers are usable as repeating materials. Keep
                // provenance explicit; only artist-authored repeat packs claim seamlessness.
                if(sources.Count==0){string alias=name=="water"?"pond":name=="sand"?"sand_patch":name=="pavement"||name=="rock"?"stone_pavement":name=="building"||name=="dirt"?"grass":"";
                    var other=config["seasons"]?[season]?[alias]??config["surfaces"]?[alias];
                    if(alias!=""&&other!=null){var candidates=(other["repeatSources"]??other["sources"])?.AsArray();if(candidates!=null&&candidates.Count>0){sources=candidates;center=other["repeatSources"]==null&&(string?)other["mode"]=="nineSliceStretch";}}
                }
                if(sources.Count>4)throw new InvalidDataException("Floor supports at most four variants per surface");
                var slots=new List<int>();
                for(int variant=0;variant<Math.Max(1,sources.Count);variant++){
                    byte[] package;string? imageFile=null,imageSha=null;int[]? crop=null;
                    if(sources.Count==0){using var solid=new SKBitmap(4,4);solid.Erase(SKColor.Parse((string?)(style["color"]??baseStyle["color"])??"#808080"));package=Encode(solid,new SKRectI(0,0,4,4));}
                    else{
                        var source=sources[variant]!;imageFile=(string)source["file"]!;string path=Path.GetFullPath(Path.Combine(root,imageFile));
                        if(!path.StartsWith(Path.GetFullPath(root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Floor source escapes repository");
                        imageSha=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
                        using var image=SKBitmap.Decode(path)??throw new InvalidDataException("Cannot decode floor source: "+imageFile);
                        if(image.Width>4096||image.Height>4096)throw new InvalidDataException("Floor import dimensions exceed budget");
                        var rect=source["rect"]?.AsArray();int x=rect==null?0:(int)rect[0]!,y=rect==null?0:(int)rect[1]!,w=rect==null?image.Width:(int)rect[2]!,h=rect==null?image.Height:(int)rect[3]!;
                        if(x<0||y<0||w<4||h<4||x+w>image.Width||y+h>image.Height)throw new InvalidDataException("Invalid floor source crop");
                        if(center){if(w%3!=0||h%3!=0)throw new InvalidDataException("Nine-slice floor source needs a 3x3 grid");x+=w/3;y+=h/3;w/=3;h/=3;}
                        crop=new[]{x,y,w,h};package=Encode(image,new SKRectI(x,y,x+w,y+h));
                    }
                    string digest=Convert.ToHexString(SHA256.HashData(package));
                    if(!identifiers.TryGetValue(digest,out int slot)){slot=packages.Count;identifiers.Add(digest,slot);packages.Add(package);File.WriteAllBytes(Path.Combine(output,$"floor-{season}-{slot}.dndtex"),package);}
                    slots.Add(slot);provenance.Add(new{season,surface=name,variant,imageFile,imageSha,crop,source="dvd:/floor-"+season+"-"+slot+".dndtex",sourceSha=digest,derivedFromTransitionCenter=center});
                }
                styles.Add(slots.ToArray());
            }
            using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
            writer.Write(0x32525441);writer.Write(1);writer.Write(Names.Length);writer.Write(packages.Count);writer.Write(FloorTexture.Maximum);
            foreach(var style in styles){writer.Write(style.Length);for(int i=0;i<4;i++)writer.Write(i<style.Length?style[i]:-1);}
            var patches=config["patches"]!.AsArray();writer.Write(patches.Count);
            foreach(var p in patches){writer.Write(Array.IndexOf(Names,(string)p!["surface"]!));writer.Write(Array.IndexOf(Names,(string)p["terrain"]!));foreach(string field in new[]{"x","z","width","height","border"})writer.Write((int)p[field]!);}
            File.WriteAllBytes(Path.Combine(output,$"floor-{season}.bin"),stream.ToArray());
            int nativeBytes=0;foreach(var package in packages)nativeBytes+=FloorTexture.Prepare(package).Length-32;
            if(nativeBytes>512*1024)throw new InvalidDataException("Active repeating floor exceeds 512 KiB budget");
            Console.WriteLine($"Repeat floor {season}: {packages.Count} unique sources, {nativeBytes} resident bytes with load-time compression/mips");
        }
        File.WriteAllText(Path.Combine(root,"out/floor-sources.json"),JsonSerializer.Serialize(provenance,new JsonSerializerOptions{WriteIndented=true}));
    }
    private static byte[] Encode(SKBitmap image,SKRectI source)
    {
        int w=4,h=4;while(w*2<=source.Width&&w<1024)w*=2;while(h*2<=source.Height&&h<1024)h*=2;
        for(int y=source.Top;y<source.Bottom;y++)for(int x=source.Left;x<source.Right;x++)if(image.GetPixel(x,y).Alpha!=255)throw new InvalidDataException("Floor source must be opaque");
        using var sized=new SKBitmap(w,h,SKColorType.Rgba8888,SKAlphaType.Opaque);
        using(var canvas=new SKCanvas(sized))using(var bitmap=SKImage.FromBitmap(image))canvas.DrawImage(bitmap,source,new SKRect(0,0,w,h),new SKSamplingOptions(SKFilterMode.Linear));
        var bytes=new byte[32+w*h*2];int[] header={0x58544e44,1,4,w,h,bytes.Length-32,0,0};for(int i=0;i<8;i++)BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i*4),header[i]);
        int at=32;for(int by=0;by<h;by+=4)for(int bx=0;bx<w;bx+=4)for(int y=by;y<by+4;y++)for(int x=bx;x<bx+4;x++){var c=sized.GetPixel(x,y);int rgb=(c.Red>>3)<<11|(c.Green>>2)<<5|c.Blue>>3;bytes[at++]=(byte)(rgb>>8);bytes[at++]=(byte)rgb;}
        return bytes;
    }
}
