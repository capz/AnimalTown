using System.Security.Cryptography;
using SkiaSharp;
using AnimalTown;

// Cook exactly the five independently edited PNG source levels. A generic
// texture compressor/mipmap generator must NEVER touch this artist-authored
// chain: the empty/highlight/bright-band/fade/empty sequence *is* the effect.
internal static class SunshineWaterAssets
{
    internal const string DiscName="sunshine-water.dndtex";
    internal const string SourceDirectory="assets/textures/environment/water";

    internal static void Build(string root)
    {
        var levels=new byte[SunshineWater.MipLevels][];
        for(int level=0;level<SunshineWater.MipLevels;level++){
            int size=SunshineWater.Edge(level);
            string source=Path.Combine(root,SourceDirectory,
                $"sunshine-wave-mip{level}-{size}.png");
            using var image=SKBitmap.Decode(source)??throw new InvalidDataException("Invalid authored Sunshine PNG: "+source);
            if(image.Width!=size||image.Height!=size)
                throw new InvalidDataException("Sunshine mip must be "+size+"x"+size+": "+source);
            var luminance=levels[level]=new byte[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++){
                var c=image.GetPixel(x,y);
                if(c.Alpha!=255||c.Red!=c.Green||c.Green!=c.Blue)
                    throw new InvalidDataException("Sunshine source PNG must be opaque grayscale: "+source);
                luminance[y*size+x]=c.Red;
            }
        }
        byte[] native=SunshineWater.PackLevels(levels);
        SunshineWater.ValidatePackage(native);
        string directory=Path.Combine(root,"out/disc");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory,DiscName),native);
        Console.WriteLine($"Sunshine authored GX IA8 mips: {SunshineWater.MipLevels} levels, {native.Length-32} GPU bytes, SHA256 {Convert.ToHexString(SHA256.HashData(native))}");
    }
}
