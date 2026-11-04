using System.Text.Json;
using System.Security.Cryptography;
using AnimalTown;
using DolphinDotNet.Imaging;

internal static class TownGenerationPreview
{
    internal static void Run(string output,int seed,int count)
    {
        if(count<1||count>64)throw new ArgumentException("Preview count must be 1..64");Directory.CreateDirectory(output);var records=new List<object>();
        var colors=new[]{new Rgba32(134,174,105,255),new Rgba32(79,153,193,255),new Rgba32(160,114,67,255),new Rgba32(231,211,153,255),new Rgba32(104,110,100,255),new Rgba32(200,190,160,255),new Rgba32(117,77,69,255),new Rgba32(175,144,99,255)};
        for(int n=0;n<count;n++){
            int current=unchecked(seed+n);var state=new GameState(current);var town=state.Town;using var image=new RasterImage(Town.Width*4,Town.Height*4);
            for(int z=0;z<Town.Height;z++)for(int x=0;x<Town.Width;x++){
                var c=colors[town.At(x,z)];for(int dy=0;dy<4;dy++)for(int dx=0;dx<4;dx++)image[x*4+dx,z*4+dy]=x%20==0&&dx==0||z%20==0&&dy==0?new Rgba32((byte)(c.R*3/4),(byte)(c.G*3/4),(byte)(c.B*3/4),255):c;
            }
            for(int i=0;i<town.GroundCount;i++){var g=town.Ground[i];if(g.Kind==3)for(int dy=1;dy<4;dy++)for(int dx=1;dx<4;dx++)image[g.X*4+dx,g.Z*4+dy]=new Rgba32(46,94,59,255);}
            // Starter plot outline and arrival marker, without changing terrain.
            for(int z=town.HouseZ;z<town.HouseZ+13;z++)for(int x=town.HouseX;x<town.HouseX+15;x++)if(x==town.HouseX||x==town.HouseX+14||z==town.HouseZ||z==town.HouseZ+12)image[x*4,z*4]=new Rgba32(255,245,221,255);
            for(int dy=0;dy<4;dy++)for(int dx=0;dx<4;dx++)image[(int)state.X*4+dx,(int)state.Z*4+dy]=new Rgba32(255,255,255,255);
            string path=Path.Combine(output,"town-"+current+".png");using(var file=File.Create(path))image.SaveAsPng(file);
            records.Add(new{seed=current,generatorVersion=TownGenerator.Version,screenTiles=Town.ScreenTiles,acreCells=Town.ScreenTileSize,terrainSha256=Convert.ToHexString(SHA256.HashData(town.Tiles)),groundRecords=town.GroundCount,preview=Path.GetFileName(path)});
        }
        File.WriteAllText(Path.Combine(output,"generation-audit.json"),JsonSerializer.Serialize(records,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("Generated "+count+" seeded town previews: "+output);
    }
}
