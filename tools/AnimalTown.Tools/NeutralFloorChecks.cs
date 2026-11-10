using System.Buffers.Binary;
using System.Text.Json.Nodes;
using AnimalTown;
using SkiaSharp;

internal static class NeutralFloorChecks
{
    internal static void Run(string project)
    {
        string root=Path.Combine(project,"out/neutral-floor-check");Directory.CreateDirectory(Path.Combine(root,"assets/terrain"));
        string imageFile=Path.Combine(root,"assets/neutral.png");
        using(var image=new SKBitmap(256,256)){
            for(int y=0;y<256;y++)for(int x=0;x<256;x++){
                byte c=(byte)((x/8+y/8)%2==0?100:150);
                image.SetPixel(x,y,new SKColor(c,c,c));
            }
            for(int y=0;y<4;y++)for(int x=2;x<4;x++)image.SetPixel(x,y,new SKColor(150,150,150));
            using var png=SKImage.FromBitmap(image).Encode(SKEncodedImageFormat.Png,100);
            File.WriteAllBytes(imageFile,png.ToArray());
        }
        var source=File.ReadAllBytes(imageFile);
        var config=JsonNode.Parse("""{"version":1,"season":"auto","patches":[],"surfaces":{"floor":{"color":"#BF9769","sources":[]}},"neutralFloor":{"source":"assets/neutral.png","brightnessFloor":0.86,"tints":{},"seasons":{"fall":{"grass":"#AD9694"},"winter":{"grass":"#FFFFFF"}}}}""")!;
        foreach(string name in RepeatFloorAssets.Names)if(name!="floor")config["neutralFloor"]!["tints"]![name]="#72B955";
        Build();string disc=Path.Combine(root,"out/disc");var winter=File.ReadAllBytes(Path.Combine(disc,"floor-winter.bin"));var spring=File.ReadAllBytes(Path.Combine(disc,"floor-spring.bin"));
        Check(File.ReadAllBytes(imageFile).SequenceEqual(source),"original neutral PNG preserved");
        Check(Directory.GetFiles(disc,"floor-outdoor-*.dndtex").Length==1&&!File.Exists(Path.Combine(disc,"floor-winter-0.dndtex")),"only one outdoor texture across seasons");
        foreach(string season in new[]{"spring","summer","fall","winter"}){
            var data=File.ReadAllBytes(Path.Combine(disc,$"floor-{season}.bin"));FloorStrips.Validate(data);
            for(int surface=0;surface<TerrainTiles.SurfaceCount;surface++){int slot=FloorStrips.Read(data,24+surface*20);Check(FloorStrips.TextureSlot(data,slot)==(surface==TerrainTiles.Floor?1:0),"all outdoor materials reference shared texture; indoor stays separate");}
        }
        int grass=FloorStrips.Read(winter,24);Check(FloorStrips.Tint(winter,grass)==0xffffffffu,"winter grass bright-white tint");Check(FloorStrips.Tint(spring,FloorStrips.Read(spring,24))==0x72b955ffu,"spring grass green tint");
        var fall=File.ReadAllBytes(Path.Combine(disc,"floor-fall.bin"));Check(FloorStrips.Tint(fall,FloorStrips.Read(fall,24))==0xad9694ffu,"fall grass reddish-gray tint");
        var package=File.ReadAllBytes(Path.Combine(disc,"floor-outdoor-neutral.dndtex"));
        Check(TexturePixels.Width(package)==256&&TexturePixels.Height(package)==256&&TexturePixels.Int(package,8)==14,"neutral floor ships as native 256x256 CMPR");
        Check(TexturePixels.Int(package,24)==7&&package.Length==32+43712,"neutral floor ships seven levels within its native payload budget");
        Check(ReferenceEquals(FloorTexture.Prepare(package,fullMipChain:true),package),"default ground upload keeps the cooked mip bytes");
        var smaller=FloorTexture.Prepare(package,TextureResolution.Size128,fullMipChain:true);
        Check(TexturePixels.Width(smaller)==128&&TexturePixels.Int(smaller,24)==6,"a smaller floor limit still rebuilds the mip chain at that size");
        uint dark=TexturePixels.Pixel(package,4,0),light=TexturePixels.Pixel(package,8,0);Check((dark>>24)>=210&&(light>>24)>(dark>>24),"bright neutral master retains source contrast for snow");
        var mip=new byte[32+64*64/2];
        Array.Copy(package,mip,32);
        BinaryPrimitives.WriteInt32LittleEndian(mip.AsSpan(12),64);
        BinaryPrimitives.WriteInt32LittleEndian(mip.AsSpan(16),64);
        BinaryPrimitives.WriteInt32LittleEndian(mip.AsSpan(20),mip.Length-32);
        BinaryPrimitives.WriteInt32LittleEndian(mip.AsSpan(24),1);
        Array.Copy(package,32+256*256/2+128*128/2,mip,32,mip.Length-32);
        uint filtered=TexturePixels.Pixel(mip,0,0);
        Check(filtered>dark&&filtered<light&&TexturePixels.Pixel(mip,1,0)==dark&&
            TexturePixels.Pixel(mip,2,0)==light&&TexturePixels.Pixel(mip,0,2)==light,
            "64x64 level contains the same pattern box-filtered at one-quarter scale");
        var broken=(byte[])winter.Clone();int materialAt=FloorStrips.Records;broken[materialAt]=2;Reject(()=>FloorStrips.Validate(broken),"invalid texture reference");broken=(byte[])winter.Clone();broken[materialAt+4]=0;Reject(()=>FloorStrips.Validate(broken),"transparent tint");
        config["neutralFloor"]!["brightnessFloor"]=1;Reject(Build,"invalid brightness floor");config["neutralFloor"]!["brightnessFloor"]=.86;
        config["neutralFloor"]!["tints"]!["grass"]="#12";Reject(Build,"invalid tint");config["neutralFloor"]!["tints"]!["grass"]="#72B955";
        config["neutralFloor"]!["source"]="../outside.png";Reject(Build,"source path escape");
        Console.WriteLine("Shared neutral floor checks passed: source preservation, single outdoor texture, independent indoor floor, seasonal tints, brightness and metadata validation");
        void Build(){File.WriteAllText(Path.Combine(root,"assets/terrain/terrain.json"),config.ToJsonString());RepeatFloorAssets.Build(root);}
        static void Check(bool value,string message){if(!value)throw new InvalidDataException(message);}
        static void Reject(Action action,string label){bool rejected=false;try{action();}catch(InvalidDataException){rejected=true;}catch(InvalidOperationException){rejected=true;}Check(rejected,label+" rejected");}
    }
}
