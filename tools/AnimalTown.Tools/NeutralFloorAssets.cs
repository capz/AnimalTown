using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using AnimalTown;
using SkiaSharp;

internal static class NeutralFloorAssets
{
    internal static void Build(string root,JsonNode config,string output)
    {
        var neutral=config["neutralFloor"]!;string file=(string)neutral["source"]!;
        string path=Path.GetFullPath(Path.Combine(root,file));
        if(!path.StartsWith(Path.GetFullPath(root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Neutral floor source escapes repository");
        string imageSha=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        using var image=SKBitmap.Decode(path)??throw new InvalidDataException("Cannot decode neutral floor");
        if(image.Width<4||image.Height<4||image.Width>4096||image.Height>4096)throw new InvalidDataException("Invalid neutral floor dimensions");
        float floor=(float?)neutral["brightnessFloor"]??.86f;if(!float.IsFinite(floor)||floor<0||floor>=1)throw new InvalidDataException("Invalid neutral brightness floor");
        int min=255,max=0;for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++){var c=image.GetPixel(x,y);if(c.Alpha!=255)throw new InvalidDataException("Neutral floor must be opaque");int l=Luma(c);min=Math.Min(min,l);max=Math.Max(max,l);}
        // One bright grayscale master supports white snow through ordinary GX
        // modulation. The artist's source file is preserved byte for byte.
        using var master=new SKBitmap(image.Width,image.Height);
        for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++){float l=max==min?1:(Luma(image.GetPixel(x,y))-min)/(float)(max-min);byte v=(byte)Math.Clamp((int)MathF.Round((floor+(1-floor)*l)*255),0,255);master.SetPixel(x,y,new SKColor(v,v,v));}
        byte[] package=RepeatFloorAssets.Encode(master,new SKRectI(0,0,master.Width,master.Height));
        // Keep the 1024px source option without reading a 2 MiB RGB565 staging
        // image into the console's 4 MiB managed heap. The runtime still chooses
        // its requested size and regenerates the two native target mip levels.
        package=FloorTexture.Prepare(package,TextureResolution.Size1024);
        File.WriteAllBytes(Path.Combine(output,"floor-outdoor-neutral.dndtex"),package);string digest=Convert.ToHexString(SHA256.HashData(package));
        var provenance=new List<object>();
        foreach(string season in new[]{"spring","summer","fall","winter"}){
            // Indoor floors retain their own artwork/color, outside the shared
            // outdoor texture. The active indoor configuration is a solid color.
            var indoor=config["seasons"]?[season]?["floor"]??config["surfaces"]!["floor"]!;
            if((indoor["sources"]?.AsArray().Count??0)>0||(indoor["repeatSources"]?.AsArray().Count??0)>0)throw new InvalidDataException("Neutral mode indoor floor currently requires a color");
            using var solid=new SKBitmap(4,4);solid.Erase(Parse((string?)indoor["color"]??"#BF9769"));
            var inside=RepeatFloorAssets.Encode(solid,new SKRectI(0,0,4,4));File.WriteAllBytes(Path.Combine(output,$"floor-{season}-1.dndtex"),inside);
            var colors=new List<uint>();var textureSlots=new List<int>();var styles=new List<int>();var lookup=new Dictionary<(int,uint),int>();
            foreach(string name in RepeatFloorAssets.Names){
                bool interior=name=="floor";var color=interior?SKColors.White:Parse((string?)(neutral["seasons"]?[season]?[name]??neutral["tints"]?[name])??throw new InvalidDataException("Missing neutral tint: "+name));
                uint tint=(uint)(color.Red<<24|color.Green<<16|color.Blue<<8|255);int texture=interior?1:0;
                if(!lookup.TryGetValue((texture,tint),out int material)){material=colors.Count;lookup.Add((texture,tint),material);colors.Add(tint);textureSlots.Add(texture);}styles.Add(material);
                provenance.Add(new{season,surface=name,variant=0,imageFile=interior?null:file,imageSha=interior?null:imageSha,crop=interior?new[]{0,0,4,4}:new[]{0,0,image.Width,image.Height},source=interior?$"dvd:/floor-{season}-1.dndtex":"dvd:/floor-outdoor-neutral.dndtex",sourceSha=interior?Convert.ToHexString(SHA256.HashData(inside)):digest,derivedFromTransitionCenter=false,tint=$"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}",brightnessFloor=interior?0:floor});
            }
            using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
            writer.Write(0x32525441);writer.Write(2);writer.Write(RepeatFloorAssets.Names.Length);writer.Write(colors.Count);writer.Write(FloorTexture.Maximum);
            foreach(int slot in styles){writer.Write(1);writer.Write(slot);for(int i=0;i<3;i++)writer.Write(-1);}
            var patches=config["patches"]!.AsArray();writer.Write(patches.Count);
            foreach(var p in patches){writer.Write(Array.IndexOf(RepeatFloorAssets.Names,(string)p!["surface"]!));writer.Write(Array.IndexOf(RepeatFloorAssets.Names,(string)p["terrain"]!));foreach(string field in new[]{"x","z","width","height","border"})writer.Write((int)p[field]!);}
            for(int i=0;i<colors.Count;i++){writer.Write(textureSlots[i]);writer.Write(colors[i]);}
            var data=stream.ToArray();FloorStrips.Validate(data);File.WriteAllBytes(Path.Combine(output,$"floor-{season}.bin"),data);
            Console.WriteLine($"Shared neutral floor {season}: 1 outdoor texture, {colors.Count-1} outdoor tints; {FloorTexture.Prepare(package).Length-32} resident outdoor bytes");
        }
        File.WriteAllText(Path.Combine(root,"out/floor-sources.json"),JsonSerializer.Serialize(provenance,new JsonSerializerOptions{WriteIndented=true}));
    }
    private static int Luma(SKColor c)=>(c.Red*77+c.Green*150+c.Blue*29+128)>>8;
    private static SKColor Parse(string text)
    {if(text.Length!=7||text[0]!='#'||!SKColor.TryParse(text,out var c)||c.Alpha!=255)throw new InvalidDataException("Tint must be #RRGGBB");return c;}
}
