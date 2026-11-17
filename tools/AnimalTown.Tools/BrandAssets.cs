using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using AnimalTown;
using SkiaSharp;

// Build-time rasterization only. The GameCube loads baked atlases; no font
// rasterizer or desktop drawing dependency is linked into the game.
internal static class BrandAssets
{
    private static SKTypeface Title(string root)=>SKTypeface.FromFile(Path.Combine(root,"assets/fonts/gorditas/Gorditas-Bold.ttf"))??throw new Exception("Gorditas font missing");
    private static void SavePng(SKBitmap bitmap,string path)
    { using var image=SKImage.FromBitmap(bitmap);using var data=image.Encode(SKEncodedImageFormat.Png,100);File.WriteAllBytes(path,data.ToArray()); }
    public static void Build(string root)
    {
        Directory.CreateDirectory(Path.Combine(root,"out/brand"));Directory.CreateDirectory(Path.Combine(root,"out/disc"));
        using var title=Title(root);
        ItemIcons.Build(root);
        PickerPortraits(root);
        SplashAssets.Check(root);
        Font(root,"gorditas-bold",title,24);
        foreach(string obsolete in new[]{"fredoka-medium.dndtex","fredoka-medium.metrics","fredoka-semibold.dndtex","fredoka-semibold.metrics","Fredoka-OFL.txt"})File.Delete(Path.Combine(root,"out/disc",obsolete));
        BrandTextureAssets.Check(root);
        File.Copy(Path.Combine(root,"assets/fonts/gorditas/OFL.txt"),Path.Combine(root,"out/disc/Gorditas-OFL.txt"),true);
    }
    private static void Font(string root,string name,SKTypeface face,float size)
    {
        using var bitmap=new SKBitmap(512,512,SKColorType.Rgba8888,SKAlphaType.Unpremul);bitmap.Erase(SKColors.Transparent);
        using var c=new SKCanvas(bitmap);using var font=new SKFont(face,48){Subpixel=true,Edging=SKFontEdging.Antialias};using var paint=new SKPaint{Color=SKColors.White,IsAntialias=true};
        var metrics=new byte[16+96*8];"ATF2"u8.CopyTo(metrics);BinaryPrimitives.WriteInt32LittleEndian(metrics.AsSpan(4),48);BinaryPrimitives.WriteInt32LittleEndian(metrics.AsSpan(8),(int)size);BinaryPrimitives.WriteInt32LittleEndian(metrics.AsSpan(12),64);
        int x=0,y=0;
        for(int i=0;i<96;i++)
        {
            string ch=((char)(i+32)).ToString();int advance=(int)Math.Ceiling(font.MeasureText(ch)),width=advance+4;
            if(x+width>512){x=0;y+=64;}if(y+64>512)throw new Exception("Font atlas budget exceeded");
            int at=16+i*8;BinaryPrimitives.WriteUInt16LittleEndian(metrics.AsSpan(at),(ushort)x);BinaryPrimitives.WriteUInt16LittleEndian(metrics.AsSpan(at+2),(ushort)y);BinaryPrimitives.WriteUInt16LittleEndian(metrics.AsSpan(at+4),(ushort)width);BinaryPrimitives.WriteUInt16LittleEndian(metrics.AsSpan(at+6),(ushort)advance);
            c.DrawText(ch,x+1,y+51,SKTextAlign.Left,font,paint);x+=width;
        }
        SavePng(bitmap,Path.Combine(root,"out/brand/"+name+".png"));
        Texture(root,name,bitmap);File.WriteAllBytes(Path.Combine(root,"out/disc/"+name+".metrics"),metrics);
    }
    private static void PickerPortraits(string root)
    {
        string directory=Path.Combine(root,"assets/textures/ui/villagers");
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"provenance.json")));
        foreach(string name in new[]{"fox","bunny","giraffe","cat","hamster","spaniel"}){
            var entry=manifest.RootElement.GetProperty(name);string path=Path.Combine(directory,name+".png");
            string Hash(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant();
            if(Hash(Path.Combine(root,entry.GetProperty("source").GetString()!))!=entry.GetProperty("sourceSha256").GetString()||
               Hash(path)!=entry.GetProperty("pngSha256").GetString())throw new InvalidDataException("Rebake changed picker portrait/source: "+name);
            using var bitmap=SKBitmap.Decode(path)??throw new InvalidDataException("Picker portrait missing: "+name);
            if(bitmap.Width!=256||bitmap.Height!=256)throw new InvalidDataException("Picker portraits must be 256px square");
            var rgba=new byte[256*256*4];int at=0,visible=0,clear=0;
            for(int y=0;y<256;y++)for(int x=0;x<256;x++){
                var p=bitmap.GetPixel(x,y);rgba[at++]=p.Red;rgba[at++]=p.Green;rgba[at++]=p.Blue;rgba[at++]=p.Alpha;
                if(p.Alpha>128)visible++;if(p.Alpha==0)clear++;
            }
            if(visible<3000||clear<1000)throw new InvalidDataException("Picker portrait must have a visible character and transparent background: "+name);
            byte[] pixels=TexturePixels.Encode(rgba,256,256,5),package=new byte[32+pixels.Length];
            int[] header={0x58544e44,1,5,256,256,pixels.Length,0,0};
            for(int i=0;i<header.Length;i++)BinaryPrimitives.WriteInt32LittleEndian(package.AsSpan(i*4),header[i]);
            pixels.CopyTo(package,32);TexturePixels.Validate(package);
            File.WriteAllBytes(Path.Combine(root,"out/disc/picker-"+name+".dndtex"),package);
        }
    }
    private static void Texture(string root,string name,SKBitmap bitmap)
    {
        var package=new byte[32+bitmap.Width*bitmap.Height*2];BinaryPrimitives.WriteInt32LittleEndian(package.AsSpan(0),0x58544e44);BinaryPrimitives.WriteInt32LittleEndian(package.AsSpan(4),1);BinaryPrimitives.WriteInt32LittleEndian(package.AsSpan(8),3);BinaryPrimitives.WriteInt32LittleEndian(package.AsSpan(12),bitmap.Width);BinaryPrimitives.WriteInt32LittleEndian(package.AsSpan(16),bitmap.Height);BinaryPrimitives.WriteInt32LittleEndian(package.AsSpan(20),package.Length-32);
        int p=32;for(int ty=0;ty<bitmap.Height;ty+=4)for(int tx=0;tx<bitmap.Width;tx+=4)for(int y=ty;y<ty+4;y++)for(int x=tx;x<tx+4;x++){package[p++]=bitmap.GetPixel(x,y).Alpha;package[p++]=255;}
        File.WriteAllBytes(Path.Combine(root,"out/disc/"+name+".dndtex"),package);
    }
    public static byte[] Banner(string root,int width)=>BrandTextureAssets.Banner(root,width);
}
