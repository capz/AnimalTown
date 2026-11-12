using System.Security.Cryptography;
using SkiaSharp;
using AnimalTown;

// Preserve all five independent surface sources. The near-wave and indirect
// textures reuse our caustic art in separate conventional mip chains; they do
// not replace or regenerate the artist-selected surface levels.
internal static class SunshineWaterAssets
{
    internal const string DiscName="sunshine-water.dndtex";
    internal const string NearWaveDiscName="sunshine-wave.dndtex";
    internal const string DistortionDiscName="sunshine-distortion.dndtex";
    internal const string SourceDirectory="assets/textures/environment/water";

    internal static void Build(string root)
    {
        var levels=new byte[SunshineWater.MipLevels][];
        for(int level=0;level<SunshineWater.MipLevels;level++){
            int size=SunshineWater.SourceEdge(level);
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
        byte[] nearWave=SunshineWater.PackNearWave(levels[1]);
        SunshineWater.ValidateNearWavePackage(nearWave);
        byte[] distortion=SunshineWater.PackDistortion(levels[1]);
        SunshineWater.ValidateDistortionPackage(distortion);
        string directory=Path.Combine(root,"out/disc");
        Directory.CreateDirectory(directory);
        Write(DiscName,native);Write(NearWaveDiscName,nearWave);Write(DistortionDiscName,distortion);
        void Write(string name,byte[] data)
        {
            File.WriteAllBytes(Path.Combine(directory,name),data);
            Console.WriteLine($"Sunshine GX I4 {name}: {TexturePixels.Int(data,12)}x{TexturePixels.Int(data,16)}, {TexturePixels.Int(data,24)} levels, {data.Length-32} GPU bytes, SHA256 {Convert.ToHexString(SHA256.HashData(data))}");
        }
    }
}
