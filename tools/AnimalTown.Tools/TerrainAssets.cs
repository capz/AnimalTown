using System.Buffers.Binary;
using System.Text.Json.Nodes;
using AnimalTown;
using SkiaSharp;

internal static class TerrainAssets
{
    private static readonly string[] Names={"grass","water","wood","sand","rock","pavement","dirt","floor","building","terracotta","stone_pavement","sand_patch","pond"};
    public static void Build(string root)
    {
        var config=JsonNode.Parse(File.ReadAllText(Path.Combine(root,"assets/terrain/terrain.json")))!.AsObject();
        if(config["version"]!.GetValue<int>()!=1)throw new InvalidDataException("Unsupported terrain manifest version");
        foreach(var entry in config["surfaces"]!.AsObject())if(!Names.Contains(entry.Key))throw new InvalidDataException("Unknown terrain surface: "+entry.Key);
        foreach(string name in Names)if(config["surfaces"]?[name]==null)throw new InvalidDataException("Missing terrain surface: "+name);
        foreach(var seasonEntry in config["seasons"]!.AsObject()){
            if(!new[]{"spring","summer","fall","winter"}.Contains(seasonEntry.Key))throw new InvalidDataException("Unknown terrain season: "+seasonEntry.Key);
            foreach(var style in seasonEntry.Value!.AsObject())if(!Names.Contains(style.Key))throw new InvalidDataException("Unknown seasonal surface: "+style.Key);
        }
        int pixels=config["tilePixels"]!.GetValue<int>();if(pixels!=32&&pixels!=64)throw new InvalidDataException("Terrain tilePixels must be 32 or 64");
        int size=config["atlasSize"]?.GetValue<int>()??(pixels==32?256:512);if(size!=256&&size!=512)throw new InvalidDataException("Terrain atlasSize must be 256 or 512");
        string forced=config["season"]!.GetValue<string>();int force=forced=="auto"?-1:Array.IndexOf(new[]{"spring","summer","fall","winter"},forced);
        if(force<0&&forced!="auto")throw new InvalidDataException("Unknown terrain season");
        Directory.CreateDirectory(Path.Combine(root,"out/disc"));Directory.CreateDirectory(Path.Combine(root,"out/terrain"));
        File.WriteAllBytes(Path.Combine(root,"out/disc/terrain.cfg"),BitConverter.GetBytes(force));
        var patches=config["patches"]!.AsArray();
        if(patches.Count>32)throw new InvalidDataException("At most 32 terrain patches are supported");
        foreach(var patch in patches){var p=patch!;int s=Array.IndexOf(Names,p["surface"]!.GetValue<string>());int x=(int)p["x"]!,z=(int)p["z"]!,w=(int)p["width"]!,h=(int)p["height"]!,b=(int)p["border"]!;
            int required=Array.IndexOf(Names,p["terrain"]!.GetValue<string>());
            if(s<0||required<0||required>8||x<0||z<0||w<=2*b||h<=2*b||b<1||x+w>Town.Width||z+h>Town.Height)throw new InvalidDataException("Invalid terrain patch bounds/border/terrain");}
        foreach(string season in new[]{"spring","summer","fall","winter"})
        {
            int stride=pixels+4,columns=size/stride,slot=0;
            using var atlas=new SKBitmap(size,size,SKColorType.Rgba8888,SKAlphaType.Premul);atlas.Erase(SKColors.Black);
            using var meta=new MemoryStream();using var writer=new BinaryWriter(meta);
            writer.Write(0x314E5254);writer.Write(1);writer.Write(size);writer.Write(pixels);writer.Write(stride);writer.Write(Names.Length);writer.Write(patches.Count);
            for(int s=0;s<Names.Length;s++)
            {
                var baseStyle=config["surfaces"]![Names[s]]!.AsObject();var style=config["seasons"]?[season]?[Names[s]]?.AsObject()??baseStyle;
                string mode=style["mode"]?.GetValue<string>()??"repeat";if(mode!="repeat"&&mode!="nineSliceStretch")throw new InvalidDataException("Unknown terrain mode");
                var sources=style["sources"]!.AsArray();bool nine=mode=="nineSliceStretch";int count=Math.Max(1,sources.Count);
                if(count>4||(nine&&sources.Count!=1))throw new InvalidDataException("Terrain supports 1-4 repeat variants or one nine-slice source");
                writer.Write(nine?1:0);writer.Write(count);writer.Write(slot);
                if(nine&&!patches.Any(p=>p!["surface"]!.GetValue<string>()==Names[s]))throw new InvalidDataException("Nine-slice surface needs an explicit rectangular patch");
                for(int i=0;i<count;i++)
                {
                    if(sources.Count==0){using var solid=new SKBitmap(pixels,pixels);solid.Erase(SKColor.Parse(style["color"]?.GetValue<string>()??baseStyle["color"]!.GetValue<string>()));Add(solid,new SKRectI(0,0,pixels,pixels));continue;}
                    var source=sources[i]!;string relative=source["file"]!.GetValue<string>();string path=Path.GetFullPath(Path.Combine(root,relative));
                    if(!path.StartsWith(Path.GetFullPath(root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Terrain source must stay inside the repository");
                    if(!nine&&relative.Replace('\\','/').StartsWith("assets/textures/environment/transitions/",StringComparison.Ordinal)&&relative.EndsWith("_9slice.png",StringComparison.Ordinal))throw new InvalidDataException("Supplied transition illustrations are stretch-only, not repeating tiles");
                    using var image=SKBitmap.Decode(path)??throw new InvalidDataException("Cannot decode terrain source: "+relative);
                    if(image.Width>4096||image.Height>4096)throw new InvalidDataException("Terrain source exceeds host import budget");
                    var rect=source["rect"]?.AsArray();int rx=rect==null?0:(int)rect[0]!,ry=rect==null?0:(int)rect[1]!,rw=rect==null?image.Width:(int)rect[2]!,rh=rect==null?image.Height:(int)rect[3]!;
                    if(rx<0||ry<0||rw<1||rh<1||rx+rw>image.Width||ry+rh>image.Height||(nine&&(rw%3!=0||rh%3!=0)))throw new InvalidDataException("Invalid terrain source rectangle");
                    for(int y=ry;y<ry+rh;y++)for(int x=rx;x<rx+rw;x++)if(image.GetPixel(x,y).Alpha!=255)throw new InvalidDataException("Floor sources must be opaque: "+relative);
                    if(nine){for(int y=0;y<3;y++)for(int x=0;x<3;x++)Add(image,new SKRectI(rx+x*rw/3,ry+y*rh/3,rx+(x+1)*rw/3,ry+(y+1)*rh/3));}
                    else Add(image,new SKRectI(rx,ry,rx+rw,ry+rh));
                }
            }
            foreach(var patch in patches){var p=patch!;writer.Write(Array.IndexOf(Names,p["surface"]!.GetValue<string>()));writer.Write(Array.IndexOf(Names,p["terrain"]!.GetValue<string>()));foreach(string field in new[]{"x","z","width","height","border"})writer.Write((int)p[field]!);}
            File.WriteAllBytes(Path.Combine(root,$"out/disc/terrain-{season}.bin"),meta.ToArray());
            using(var encoded=SKImage.FromBitmap(atlas).Encode(SKEncodedImageFormat.Png,100))File.WriteAllBytes(Path.Combine(root,$"out/terrain/{season}.png"),encoded.ToArray());
            var bytes=new byte[32+size*size*2];int[] header={0x58544E44,1,4,size,size,bytes.Length-32};for(int i=0;i<header.Length;i++)BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i*4),header[i]);
            int at=32;for(int by=0;by<size;by+=4)for(int bx=0;bx<size;bx+=4)for(int y=by;y<by+4;y++)for(int x=bx;x<bx+4;x++){var c=atlas.GetPixel(x,y);ushort v=(ushort)((c.Red>>3)<<11|(c.Green>>2)<<5|(c.Blue>>3));bytes[at++]=(byte)(v>>8);bytes[at++]=(byte)v;}
            File.WriteAllBytes(Path.Combine(root,$"out/disc/terrain-{season}.dndtex"),bytes);
            Console.WriteLine($"Terrain {season}: {slot}/{columns*columns} padded slots, {size*size*2} resident texture bytes");
            void Add(SKBitmap image,SKRectI source)
            {
                if(slot>=columns*columns)throw new InvalidDataException("Terrain atlas budget exceeded");
                using var tile=new SKBitmap(pixels,pixels,SKColorType.Rgba8888,SKAlphaType.Premul);
                using(var canvas=new SKCanvas(tile))using(var sourceImage=SKImage.FromBitmap(image))canvas.DrawImage(sourceImage,source,new SKRect(0,0,pixels,pixels),new SKSamplingOptions(SKFilterMode.Linear));
                int dx=slot%columns*stride,dy=slot/columns*stride;
                for(int y=0;y<stride;y++)for(int x=0;x<stride;x++)atlas.SetPixel(dx+x,dy+y,tile.GetPixel(Math.Clamp(x-2,0,pixels-1),Math.Clamp(y-2,0,pixels-1)));
                slot++;
            }
        }
    }
}
