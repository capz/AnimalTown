using System.Security.Cryptography;
using System.Text.Json;
using AnimalTown;
using SkiaSharp;

// Authoring/import is offline. Only the native mip chain ships on the disc.
internal static class PathAssets
{
    internal const string Source="assets/textures/environment/paths/dirt-64.png";
    internal const string DiscName="path-dirt.dndtex";
    internal static void Import(string root,string input)
    {
        using var image=SKBitmap.Decode(input)??throw new InvalidDataException("Cannot decode path artwork");
        using var tile=new SKBitmap(64,64,SKColorType.Rgba8888,SKAlphaType.Opaque);
        using(var canvas=new SKCanvas(tile))canvas.DrawBitmap(image,new SKRect(0,0,64,64));
        // Match opposite edge texels, fading the correction over four pixels.
        // This is source preparation, never a runtime overlay or noise layer.
        for(int axis=0;axis<2;axis++)for(int row=0;row<64;row++){
            SKColor Read(int col)=>axis==0?tile.GetPixel(col,row):tile.GetPixel(row,col);
            var a=Read(0);var b=Read(63);
            for(int d=0;d<4;d++){
                float blend=(4-d)/4f;
                for(int side=0;side<2;side++){
                    int col=side==0?d:63-d;var c=Read(col);
                    byte Channel(byte v,byte x,byte y)=>(byte)Math.Clamp((int)MathF.Round(v+((x+y)*.5f-v)*blend),0,255);
                    var corrected=new SKColor(Channel(c.Red,a.Red,b.Red),Channel(c.Green,a.Green,b.Green),Channel(c.Blue,a.Blue,b.Blue),255);
                    if(axis==0)tile.SetPixel(col,row,corrected);else tile.SetPixel(row,col,corrected);
                }
            }
        }
        string path=Path.Combine(root,Source);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var encoded=tile.Encode(SKEncodedImageFormat.Png,100);File.WriteAllBytes(path,encoded.ToArray());
        Build(root);
    }
    internal static void Build(string root)
    {
        string path=Path.Combine(root,Source);using var tile=SKBitmap.Decode(path)??throw new InvalidDataException("Missing painted path tile");
        if(tile.Width!=64||tile.Height!=64)throw new InvalidDataException("Painted path must be 64x64");
        var pixels=new byte[64*64*4];
        for(int y=0;y<64;y++)for(int x=0;x<64;x++){
            var c=tile.GetPixel(x,y);if(c.Alpha!=255)throw new InvalidDataException("Painted path must be opaque");
            int at=(y*64+x)*4;pixels[at]=c.Red;pixels[at+1]=c.Green;pixels[at+2]=c.Blue;pixels[at+3]=255;
            if((x==0&&c!=tile.GetPixel(63,y))||(y==0&&c!=tile.GetPixel(x,63)))throw new InvalidDataException("Painted path edges must wrap");
        }
        byte[] package=FloorTexture.Encode(pixels,64,64,fullMipChain:true);
        if(package.Length-32>12*1024)throw new InvalidDataException("Path page exceeds resident pixel cap");
        string output=Path.Combine(root,"out/disc");Directory.CreateDirectory(output);File.WriteAllBytes(Path.Combine(output,DiscName),package);
        Directory.CreateDirectory(Path.Combine(root,"out/terrain"));
        using var repeat=new SKBitmap(256,256);using(var canvas=new SKCanvas(repeat))for(int y=0;y<4;y++)for(int x=0;x<4;x++)canvas.DrawBitmap(tile,x*64,y*64);
        using(var png=repeat.Encode(SKEncodedImageFormat.Png,100))File.WriteAllBytes(Path.Combine(root,"out/terrain/path-repeat.png"),png.ToArray());
        File.WriteAllText(Path.Combine(root,"out/path-source.json"),JsonSerializer.Serialize(new{source=Source,sourceSha=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),disc=DiscName,width=64,height=64,pixelBytes=package.Length-32,packageBytes=package.Length,sha=Convert.ToHexString(SHA256.HashData(package)),runtimeNoise=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"Painted path: 64x64, {package.Length-32} native mip bytes, one page for every road shape");
    }
}
