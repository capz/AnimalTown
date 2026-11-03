using System.Buffers.Binary;
using System.Text.Json.Nodes;
using SkiaSharp;

internal static class TerrainChecks
{
    public static void Run(string project)
    {
        string root=Path.Combine(project,"out/terrain-import-check");Directory.CreateDirectory(Path.Combine(root,"assets/terrain"));
        string imagePath=Path.Combine(root,"assets/tiles.png"),manifest=Path.Combine(root,"assets/terrain/terrain.json");
        using(var bitmap=new SKBitmap(64,32)){bitmap.Erase(SKColors.Red);for(int y=0;y<32;y++)for(int x=32;x<64;x++)bitmap.SetPixel(x,y,SKColors.Blue);using var encoded=SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png,100);File.WriteAllBytes(imagePath,encoded.ToArray());}
        var config=JsonNode.Parse("""{"version":1,"season":"auto","tilePixels":32,"surfaces":{},"seasons":{},"patches":[]}""")!;
        foreach(string name in new[]{"grass","water","wood","sand","rock","pavement","dirt","floor","building","terracotta","stone_pavement","sand_patch","pond"})config["surfaces"]![name]=JsonNode.Parse("""{"color":"#FFFFFF","sources":[]}""");
        config["surfaces"]!["grass"]!["sources"]=JsonNode.Parse("""[{"file":"assets/tiles.png","rect":[0,0,32,32]},{"file":"assets/tiles.png","rect":[32,0,32,32]}]""");
        Write();TerrainAssets.Build(root);
        var data=File.ReadAllBytes(Path.Combine(root,"out/disc/terrain-spring.bin"));var texture=File.ReadAllBytes(Path.Combine(root,"out/disc/terrain-spring.dndtex"));
        Check(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(32))==2,"repeat source count");
        Check(BinaryPrimitives.ReadInt32LittleEndian(texture.AsSpan(8))==4&&texture.Length==32+256*256*2,"bounded RGB565 texture");
        Check(Pixel(0,0)==0xF800&&Pixel(35,35)==0xF800&&Pixel(36,0)==0x001F&&Pixel(71,35)==0x001F,"source crop, native swizzle and duplicated gutters");
        RepeatFloorAssets.Build(root);var repeat=File.ReadAllBytes(Path.Combine(root,"out/disc/floor-spring.bin"));AnimalTown.FloorStrips.Validate(repeat);Check(AnimalTown.FloorStrips.Read(repeat,20)==2,"repeating floor preserves authored variants");
        var floor=AnimalTown.FloorTexture.Prepare(File.ReadAllBytes(Path.Combine(root,"out/disc/floor-spring-0.dndtex")));Check(AnimalTown.TexturePixels.Width(floor)<=256&&AnimalTown.TexturePixels.Int(floor,24)==2,"floor source becomes base plus one half-size native mip at load");
        config["atlasSize"]=1024;Reject("resident atlas budget");config.AsObject().Remove("atlasSize");
        config["season"]="sprint";Reject("unknown season");config["season"]="auto";
        config["tilePixels"]=128;Reject("texture budget");config["tilePixels"]=32;
        config["surfaces"]!["grass"]!["sources"]![0]!["file"]="../outside.png";Reject("source path escape");config["surfaces"]!["grass"]!["sources"]![0]!["file"]="assets/tiles.png";
        config["surfaces"]!["grass"]!["sources"]![0]!["rect"]=JsonNode.Parse("[0,0,128,32]");Reject("source bounds");
        config["surfaces"]!["grass"]!["sources"]![0]!["rect"]=JsonNode.Parse("[0,0,32,32]");
        config["surfaces"]!["grass"]!["mode"]="nineSliceStretch";Reject("multiple nine-slice sources");config["surfaces"]!["grass"]!.AsObject().Remove("mode");
        using(var transparent=new SKBitmap(64,32)){transparent.Erase(SKColors.Transparent);using var png=SKImage.FromBitmap(transparent).Encode(SKEncodedImageFormat.Png,100);File.WriteAllBytes(imagePath,png.ToArray());}Reject("transparent floor source");
        SKColor[] colors={SKColors.Red,SKColors.Lime,SKColors.Blue,SKColors.Yellow,SKColors.Magenta,SKColors.Cyan,SKColors.White,SKColors.Gray,SKColors.Black};
        using(var sheet=new SKBitmap(96,96)){for(int y=0;y<96;y++)for(int x=0;x<96;x++)sheet.SetPixel(x,y,colors[y/32*3+x/32]);using var png=SKImage.FromBitmap(sheet).Encode(SKEncodedImageFormat.Png,100);File.WriteAllBytes(imagePath,png.ToArray());}
        config["surfaces"]!["grass"]!["mode"]="nineSliceStretch";config["surfaces"]!["grass"]!["sources"]=JsonNode.Parse("""[{"file":"assets/tiles.png"}]""");
        config["patches"]=JsonNode.Parse("""[{"surface":"grass","terrain":"grass","x":20,"z":20,"width":8,"height":7,"border":1}]""");Write();TerrainAssets.Build(root);
        texture=File.ReadAllBytes(Path.Combine(root,"out/disc/terrain-spring.dndtex"));ushort[] expected={0xF800,0x07E0,0x001F,0xFFE0,0xF81F,0x07FF,0xFFFF,0x8410,0x0000};
        for(int i=0;i<9;i++)Check(Pixel(i%7*36+18,i/7*36+18)==expected[i],"nine-slice row order and import color");
        config["patches"]![0]!["width"]=2;Reject("patch corners overlap");config["patches"]![0]!["width"]=8;
        var overBudget=new JsonArray();
        foreach(var surface in config["surfaces"]!.AsObject()){
            surface.Value!["mode"]="nineSliceStretch";surface.Value["sources"]=JsonNode.Parse("""[{"file":"assets/tiles.png"}]""");
            overBudget.Add(new JsonObject{["surface"]=surface.Key,["terrain"]="grass",["x"]=20,["z"]=20,["width"]=8,["height"]=7,["border"]=1});
        }
        config["patches"]=overBudget;Reject("atlas slot overflow");
        Console.WriteLine("Terrain import checks passed: repeat variants, crops, GX pixels/gutters, nine-slice order, season, atlas budgets, source confinement, bounds and opacity");
        void Write()=>File.WriteAllText(manifest,config.ToJsonString());
        void Reject(string label){Write();bool rejected=false;try{TerrainAssets.Build(root);}catch(InvalidDataException){rejected=true;}Check(rejected,label+" rejected");}
        void Check(bool value,string label){if(!value)throw new InvalidDataException("Terrain import check failed: "+label);}
        ushort Pixel(int x,int y){int at=32+((y/4)*64+x/4)*32+((y%4)*4+x%4)*2;return (ushort)(texture[at]<<8|texture[at+1]);}
    }
}
