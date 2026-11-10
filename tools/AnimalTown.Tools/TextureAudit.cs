using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using AnimalTown;

internal static class TextureAudit
{
    internal static void Run(string root)
    {
        var records=new List<object>();int models=0;
        foreach(string path in Directory.GetFiles(Path.Combine(root,"out/disc"),"*.dndmodel").Order())
        {
            byte[] bytes=File.ReadAllBytes(path);int Read(int at)=>checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at,4)));
            if(Read(0)!=0x444e444d||Read(8)!=bytes.Length||Read(12)!=6)throw new InvalidDataException("Invalid model texture audit header");
            if(Read(4)==3){
                byte[] catalog=File.ReadAllBytes(Path.Combine(root,"out/disc/textures.idx"));int textures=TexturePixels.Int(catalog,8),externalAt=Read(68),end=externalAt+Read(72),externalCount=Read(externalAt);externalAt+=4;
                for(int i=0;i<externalCount;i++){
                    if(externalAt+32>end)throw new InvalidDataException("Truncated external texture reference");
                    int id=Read(externalAt+24);if(id<0||id>=textures||Read(externalAt)!=TexturePixels.Int(catalog,32+id*256)||Read(externalAt+4)!=TexturePixels.Int(catalog,36+id*256)||BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(externalAt+28,4))!=unchecked((uint)TexturePixels.Int(catalog,56+id*256)))throw new InvalidDataException("External texture identity mismatch");
                    externalAt+=32;
                }
                if(externalAt!=end)throw new InvalidDataException("External texture reference trailing bytes");
                records.Add(new{source="dvd:/"+Path.GetFileName(path),sourceSha=Convert.ToHexString(SHA256.HashData(bytes)),externalTextureReferences=externalCount,embeddedTextureBytes=0});continue;
            }
            string stem=Path.GetFileNameWithoutExtension(path);int marker=stem.LastIndexOf("-tex",StringComparison.Ordinal);int limit=marker<0?256:int.Parse(stem.Substring(marker+4));TexturePolicy.Edge((TextureResolution)limit);
            int start=Read(16+3*16+4),length=Read(16+3*16+8),count=Read(start),at=start+4;
            string sourceSha=Convert.ToHexString(SHA256.HashData(bytes));
            for(int i=0;i<count;i++)
            {
                int width=Read(at),height=Read(at+4),levels=Read(at+8),size=Read(at+24);at+=28;
                if(width<4||height<4||width>limit||height>limit||size<0||at+size>start+length)throw new InvalidDataException("Model texture exceeds selected resolution: "+path);
                int expectedLevels=Math.Min(width,height)>=8?2:1;
                if(levels!=expectedLevels)throw new InvalidDataException("Model texture must carry base plus one half-size mip when dimensions allow: "+path);
                records.Add(new{source="dvd:/"+Path.GetFileName(path)+"#texture="+i,sourceSha,width,height,levels,residentBytes=size,payloadSha=Convert.ToHexString(SHA256.HashData(bytes.AsSpan(at,size)))});at+=size;
            }
            if(at!=start+length)throw new InvalidDataException("Model texture section trailing bytes");models++;
        }
        foreach(string path in Directory.GetFiles(Path.Combine(root,"out/disc"),"*.dndtex").Order())
        {
            byte[] bytes=File.ReadAllBytes(path);TexturePixels.Validate(bytes);
            if(path.EndsWith("-native.dndtex",StringComparison.Ordinal)){records.Add(new{source="dvd:/"+Path.GetFileName(path),sourceSha=Convert.ToHexString(SHA256.HashData(bytes)),runtimeWidth=TexturePixels.Width(bytes),runtimeHeight=TexturePixels.Height(bytes),format=TexturePixels.Int(bytes,8),levels=TexturePixels.Int(bytes,24),residentBytes=bytes.Length-32,policy="offline-prepared-v7"});continue;}
            int width=TexturePixels.Width(bytes),height=TexturePixels.Height(bytes);
            if(Path.GetFileName(path).StartsWith("floor-",StringComparison.Ordinal)||Path.GetFileName(path)=="cliff-soil.dndtex"){
                bool ground=Path.GetFileName(path)!="cliff-soil.dndtex";
                byte[] native=FloorTexture.Prepare(bytes,fullMipChain:ground);records.Add(new{source="dvd:/"+Path.GetFileName(path),sourceSha=Convert.ToHexString(SHA256.HashData(bytes)),width,height,runtimeWidth=TexturePixels.Width(native),runtimeHeight=TexturePixels.Height(native),format=TexturePixels.Int(native,8),levels=TexturePixels.Int(native,24),residentBytes=native.Length-32,policy=!ground?"cliff-soil-mip2-repeat-clamp-v1":Path.GetFileName(path)=="floor-outdoor-neutral.dndtex"?"floor256-full-mips-repeat-tinted-v6":"floor256-full-mips-mirror-v6"});continue;
            }
            bool atlas=Path.GetFileName(path) is "gorditas-bold.dndtex" or "wordmark.dndtex" or "item-icons.dndtex";
            records.Add(new{source="dvd:/"+Path.GetFileName(path),sourceSha=Convert.ToHexString(SHA256.HashData(bytes)),width,height,
                pageWidth=atlas?Math.Min(128,width):TexturePixels.FitWidth(width,height),pageHeight=atlas?Math.Min(128,height):TexturePixels.FitHeight(width,height),
                pages=atlas?((width+127)/128)*((height+127)/128):1});
        }
        int expected=32+BuildingCatalog.Count*7*4;if(models!=expected)throw new InvalidDataException("Expected eight legacy models plus seven parts per building definition in four texture resolutions");
        string originals=Path.Combine(root,"out/floor-sources.json");if(File.Exists(originals))records.Add(new{floorSourceProvenance=JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(originals))});
        File.WriteAllText(Path.Combine(root,"out/texture-audit.json"),JsonSerializer.Serialize(records,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"Texture audit: {models/4} imported models in 128/256/512/1024px variants; generated trees use no textures");
    }
}
