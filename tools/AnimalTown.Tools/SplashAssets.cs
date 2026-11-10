using System.Buffers.Binary;
using AnimalTown;
using SkiaSharp;

// Offline conversion. Keep approved originals in Git; ship only GX-tiled RGB565.
internal static class SplashAssets
{
    // Same 384x256 content as the supplied DolphinDotNet image. GX packages
    // require power-of-two edges; right padding is excluded by runtime UVs.
    internal const int Width=384,Height=256,TextureWidth=512,Bytes=TextureWidth*Height*2;
    internal static readonly string[] Names={"dolphindotnet","hypercube-engine"};
    internal static void ImportEngine(string root,string path)
    {
        using var source=SKBitmap.Decode(path)??throw new InvalidDataException("Engine splash missing");
        using var bitmap=new SKBitmap(Width,Height,SKColorType.Rgba8888,SKAlphaType.Opaque);
        using(var canvas=new SKCanvas(bitmap)){
            canvas.Clear(SKColors.Black);
            float scale=Math.Min((float)Width/source.Width,(float)Height/source.Height);
            float w=source.Width*scale,h=source.Height*scale;
            canvas.DrawBitmap(source,new SKRect((Width-w)/2,(Height-h)/2,(Width+w)/2,(Height+h)/2),new SKSamplingOptions(SKFilterMode.Linear,SKMipmapMode.None));
        }
        // Normalize sub-quantization background noise to exact RGB(0,0,0).
        // Keep the logo's bright colors and antialiased silhouette.
        for(int y=0;y<Height;y++)for(int x=0;x<Width;x++){
            var p=bitmap.GetPixel(x,y);if(p.Red<=8&&p.Green<=8&&p.Blue<=8)bitmap.SetPixel(x,y,SKColors.Black);
        }
        using var image=SKImage.FromBitmap(bitmap);using var png=image.Encode(SKEncodedImageFormat.Png,100);
        File.WriteAllBytes(Path.Combine(root,"assets/textures/splash/hypercube-engine.png"),png.ToArray());
    }
    internal static void Build(string root)
    {
        Directory.CreateDirectory(Path.Combine(root,"out/disc"));
        Directory.CreateDirectory(Path.Combine(root,"out/brand"));
        foreach(string name in Names){
            using var bitmap=Compose(root,name);
            File.WriteAllBytes(Path.Combine(root,"out/disc/splash-"+name+".dndtex"),Encode(bitmap));
            using var preview=new SKBitmap();
            if(!bitmap.ExtractSubset(preview,new SKRectI(0,0,Width,Height)))throw new InvalidDataException("Splash preview crop failed");
            using var image=SKImage.FromBitmap(preview);
            using var png=image.Encode(SKEncodedImageFormat.Png,100);
            File.WriteAllBytes(Path.Combine(root,"out/brand/splash-"+name+".png"),png.ToArray());
        }
    }
    private static SKBitmap Compose(string root,string name)
    {
        using var source=SKBitmap.Decode(Path.Combine(root,"assets/textures/splash/"+name+".png"))??throw new InvalidDataException("Missing approved splash: "+name);
        if(source.Width!=Width||source.Height!=Height)throw new InvalidDataException("Both splash PNG files must be exactly 384x256");
        var bitmap=new SKBitmap(TextureWidth,Height,SKColorType.Rgba8888,SKAlphaType.Opaque);
        using var canvas=new SKCanvas(bitmap);
        canvas.Clear(name=="dolphindotnet"?SKColors.White:SKColors.Black);
        float scale=Math.Min((float)Width/source.Width,(float)Height/source.Height);
        float w=source.Width*scale,h=source.Height*scale;
        canvas.DrawBitmap(source,new SKRect((Width-w)/2,(Height-h)/2,(Width+w)/2,(Height+h)/2),new SKSamplingOptions(SKFilterMode.Linear,SKMipmapMode.None));
        return bitmap;
    }
    private static byte[] Encode(SKBitmap bitmap)
    {
        var data=new byte[32+Bytes];
        int[] header={0x58544e44,1,4,TextureWidth,Height,Bytes,0,0};
        for(int i=0;i<header.Length;i++)BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(i*4),header[i]);
        int at=32;
        for(int ty=0;ty<Height;ty+=4)for(int tx=0;tx<TextureWidth;tx+=4)
        for(int y=ty;y<ty+4;y++)for(int x=tx;x<tx+4;x++){
            var p=bitmap.GetPixel(x,y);ushort value=(ushort)(((p.Red>>3)<<11)|((p.Green>>2)<<5)|(p.Blue>>3));
            data[at++]=(byte)(value>>8);data[at++]=(byte)value;
        }
        return data;
    }
    internal static void Check(string root)
    {
        Build(root);
        foreach(string name in Names){
            if(name=="hypercube-engine"){
                using var source=SKBitmap.Decode(Path.Combine(root,"assets/textures/splash/"+name+".png"));
                for(int y=0;y<source.Height;y++)for(int x=0;x<source.Width;x++){
                    var p=source.GetPixel(x,y);
                    bool margin=x<16||x>=source.Width-16||y<16||y>=source.Height-16;
                    if(margin&&(p.Red!=0||p.Green!=0||p.Blue!=0))throw new InvalidDataException("Engine splash background must be flat black");
                }
            }
            byte[] data=File.ReadAllBytes(Path.Combine(root,"out/disc/splash-"+name+".dndtex"));
            TexturePixels.Validate(data);
            // The SDK's v1 loader requires both reserved words to be zero (the
            // general pixel decoder also accepts v2 and does not enforce this).
            if(data.Length!=32+Bytes||TexturePixels.Int(data,4)!=1||TexturePixels.Int(data,8)!=4||TexturePixels.Width(data)!=TextureWidth||TexturePixels.Height(data)!=Height||TexturePixels.Int(data,24)!=0||TexturePixels.Int(data,28)!=0)throw new InvalidDataException("Splash exceeds native budget or violates the SDK v1 header");
            using var expected=Compose(root,name);
            var rgba=new byte[TextureWidth*Height*4];TexturePixels.Resample(data,0,0,TextureWidth,Height,rgba,TextureWidth,Height);
            for(int y=0;y<Height;y++)for(int x=0;x<TextureWidth;x++){
                var pixel=expected.GetPixel(x,y);int at=(y*TextureWidth+x)*4;
                if(Math.Abs(rgba[at]-pixel.Red)>7||Math.Abs(rgba[at+1]-pixel.Green)>3||Math.Abs(rgba[at+2]-pixel.Blue)>7||rgba[at+3]!=255)throw new InvalidDataException("Splash RGB565 tile/channel round trip failed");
            }
        }
        Console.WriteLine("Splash packages: 384x256 content, 512x256 RGB565, 262144 resident bytes (one at a time); pixel round trips passed");
    }
}
