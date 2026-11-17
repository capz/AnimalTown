using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using AnimalTown;
using SkiaSharp;

// Conversion only: all colors and shapes come from the approved PNG artwork.
// The runtime decoder does not accept CI8/TLUT packages. RGB5A3 is its smallest
// color format with smooth alpha; CMPR would discard the antialiased silhouette.
internal static class BrandTextureAssets
{
    internal const string DiscName="wordmark.dndtex";
    internal const int Width=512,Height=256,PixelBytes=Width*Height*2;
    private const string SourceDir="assets/textures/branding";
    private const int NoiseAlpha=8,Padding=2;
    private static readonly SKColor IconBackground=new(255,245,221);

    private static void VerifySources(string root)
    {
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,SourceDir,"provenance.json")));
        foreach(var entry in manifest.RootElement.GetProperty("sources").EnumerateArray()){
            string file=entry.GetProperty("file").GetString()!;
            string hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root,SourceDir,file)))).ToLowerInvariant();
            if(hash!=entry.GetProperty("sha256").GetString())throw new InvalidDataException("Approved brand source changed; review and update provenance: "+file);
        }
    }

    private static SKBitmap Source(string root)
    {
        var bitmap=SKBitmap.Decode(Path.Combine(root,SourceDir,"smalltown-forest-logo.png"))??throw new InvalidDataException("Approved Smalltown Forest logo missing");
        if(bitmap.Width!=1774||bitmap.Height!=887){bitmap.Dispose();throw new InvalidDataException("Approved logo dimensions changed; review the acorn crop and source provenance");}
        // Generated alpha includes scattered almost-invisible background pixels.
        // Clear only alpha <= 8; retain every visible edge, letter and color.
        for(int y=0;y<bitmap.Height;y++)for(int x=0;x<bitmap.Width;x++)if(bitmap.GetPixel(x,y).Alpha<=NoiseAlpha)bitmap.SetPixel(x,y,SKColors.Transparent);
        return bitmap;
    }

    private static SKRectI ContentBounds(SKBitmap source)
    {
        int left=source.Width,top=source.Height,right=0,bottom=0;
        for(int y=0;y<source.Height;y++)for(int x=0;x<source.Width;x++)if(source.GetPixel(x,y).Alpha>0){
            left=Math.Min(left,x);top=Math.Min(top,y);right=Math.Max(right,x+1);bottom=Math.Max(bottom,y+1);
        }
        if(left>=right||top>=bottom)throw new InvalidDataException("Approved logo has no visible pixels");
        return new SKRectI(left,top,right,bottom);
    }

    // Exact area averaging in premultiplied space keeps the existing silhouette
    // smooth when the approved high-resolution art is reduced for the console.
    private static SKBitmap Fit(SKBitmap source,SKRectI crop,int width,int height,int padding)
    {
        double scale=Math.Min((double)(width-2*padding)/crop.Width,(double)(height-2*padding)/crop.Height);
        int drawWidth=Math.Max(1,(int)Math.Round(crop.Width*scale)),drawHeight=Math.Max(1,(int)Math.Round(crop.Height*scale));
        int dx=(width-drawWidth)/2,dy=(height-drawHeight)/2;
        var result=new SKBitmap(width,height,SKColorType.Rgba8888,SKAlphaType.Unpremul);result.Erase(SKColors.Transparent);
        for(int y=0;y<drawHeight;y++)for(int x=0;x<drawWidth;x++){
            double left=crop.Left+(double)x*crop.Width/drawWidth,right=crop.Left+(double)(x+1)*crop.Width/drawWidth;
            double top=crop.Top+(double)y*crop.Height/drawHeight,bottom=crop.Top+(double)(y+1)*crop.Height/drawHeight;
            double r=0,g=0,b=0,a=0,area=(right-left)*(bottom-top);
            for(int sy=(int)Math.Floor(top);sy<(int)Math.Ceiling(bottom);sy++)for(int sx=(int)Math.Floor(left);sx<(int)Math.Ceiling(right);sx++){
                double weight=(Math.Min(right,sx+1)-Math.Max(left,sx))*(Math.Min(bottom,sy+1)-Math.Max(top,sy));
                var p=source.GetPixel(sx,sy);double alpha=p.Alpha*weight;
                a+=alpha;r+=p.Red*alpha;g+=p.Green*alpha;b+=p.Blue*alpha;
            }
            if(a>0)result.SetPixel(x+dx,y+dy,new SKColor((byte)Math.Round(r/a),(byte)Math.Round(g/a),(byte)Math.Round(b/a),(byte)Math.Round(a/area)));
        }
        return result;
    }

    private static SKBitmap ComposeLogo(string root)
    {
        using var source=Source(root);var bounds=ContentBounds(source);
        if(bounds!=new SKRectI(72,75,1705,834))throw new InvalidDataException("Approved logo content bounds changed");
        return Fit(source,bounds,Width,Height,Padding);
    }

    private static SKBitmap ComposeIcon(string root)
    {
        using var source=Source(root);
        // Trace only the approved right-hand acorn, excluding the wooden boards
        // and letters behind it. Coordinates are in the unchanged source PNG.
        using var mask=new SKBitmap(source.Width,source.Height,SKColorType.Rgba8888,SKAlphaType.Premul);mask.Erase(SKColors.Transparent);
        using(var canvas=new SKCanvas(mask)){
            using var builder=new SKPathBuilder();builder.MoveTo(1447,295);
            builder.CubicTo(1468,237,1494,198,1544,185);builder.CubicTo(1596,169,1633,190,1629,225);
            builder.CubicTo(1628,246,1605,253,1572,262);builder.CubicTo(1546,269,1535,285,1530,311);
            builder.CubicTo(1598,347,1649,407,1666,461);builder.CubicTo(1686,521,1669,564,1630,589);
            builder.CubicTo(1621,595,1610,598,1600,596);builder.CubicTo(1583,660,1540,710,1476,739);
            builder.CubicTo(1433,758,1382,770,1359,769);builder.CubicTo(1328,768,1302,737,1286,692);
            builder.CubicTo(1265,636,1261,577,1282,512);builder.CubicTo(1290,488,1299,468,1308,454);
            builder.CubicTo(1277,451,1274,430,1283,409);builder.CubicTo(1303,358,1334,319,1382,302);
            builder.CubicTo(1405,295,1427,292,1447,295);builder.Close();
            using var path=builder.Detach();canvas.ClipPath(path,SKClipOperation.Intersect,true);canvas.DrawBitmap(source,new SKRect(0,0,source.Width,source.Height),new SKSamplingOptions(SKFilterMode.Nearest,SKMipmapMode.None));
        }
        using var fitted=Fit(mask,ContentBounds(mask),32,32,2);
        var icon=new SKBitmap(32,32,SKColorType.Rgba8888,SKAlphaType.Opaque);
        using(var canvas=new SKCanvas(icon)){canvas.Clear(IconBackground);canvas.DrawBitmap(fitted,new SKRect(0,0,32,32),new SKSamplingOptions(SKFilterMode.Nearest,SKMipmapMode.None));}
        return icon;
    }

    internal static SKBitmap BannerBitmap(string root,int width)
    {
        if(width==32)return ComposeIcon(root);
        if(width!=96)throw new ArgumentOutOfRangeException(nameof(width),"GameCube branding uses a 96x32 banner or 32x32 save icon");
        var banner=SKBitmap.Decode(Path.Combine(root,SourceDir,"disc-banner.png"))??throw new InvalidDataException("Approved disc banner missing");
        if(banner.Width!=96||banner.Height!=32){banner.Dispose();throw new InvalidDataException("Approved disc banner must be exactly 96x32");}
        for(int y=0;y<32;y++)for(int x=0;x<96;x++)if(banner.GetPixel(x,y).Alpha!=255){banner.Dispose();throw new InvalidDataException("Approved disc banner must be opaque");}
        return banner;
    }

    internal static byte[] Banner(string root,int width)
    {
        using var bitmap=BannerBitmap(root,width);return TexturePixels.Encode(Rgba(bitmap),width,32,5);
    }

    private static byte[] Rgba(SKBitmap bitmap)
    {
        var rgba=new byte[bitmap.Width*bitmap.Height*4];int at=0;
        for(int y=0;y<bitmap.Height;y++)for(int x=0;x<bitmap.Width;x++){
            var p=bitmap.GetPixel(x,y);rgba[at++]=p.Red;rgba[at++]=p.Green;rgba[at++]=p.Blue;rgba[at++]=p.Alpha;
        }
        return rgba;
    }

    private static byte[] Package(SKBitmap bitmap)
    {
        byte[] native=TexturePixels.Encode(Rgba(bitmap),bitmap.Width,bitmap.Height,5),result=new byte[32+native.Length];
        int[] header={0x58544e44,1,5,bitmap.Width,bitmap.Height,native.Length,0,0};
        for(int i=0;i<header.Length;i++)BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(i*4),header[i]);
        native.CopyTo(result,32);return result;
    }

    private static void Save(SKBitmap bitmap,string path)
    {using var image=SKImage.FromBitmap(bitmap);using var png=image.Encode(SKEncodedImageFormat.Png,100);File.WriteAllBytes(path,png.ToArray());}

    internal static void Build(string root)
    {
        VerifySources(root);Directory.CreateDirectory(Path.Combine(root,"out/disc"));Directory.CreateDirectory(Path.Combine(root,"out/brand"));
        using var logo=ComposeLogo(root);File.WriteAllBytes(Path.Combine(root,"out/disc",DiscName),Package(logo));Save(logo,Path.Combine(root,"out/brand/wordmark.png"));
        using var banner=BannerBitmap(root,96);Save(banner,Path.Combine(root,"out/brand/disc-banner.png"));
        using var icon=BannerBitmap(root,32);Save(icon,Path.Combine(root,"out/brand/save-icon.png"));
        File.WriteAllText(Path.Combine(root,"out/brand/brand-assets.json"),JsonSerializer.Serialize(new{
            title="Smalltown Forest",discFile=DiscName,width=Width,height=Height,format="GX_TF_RGB5A3",nativeBytes=PixelBytes,packageBytes=PixelBytes+32,
            atlasPages=8,pageWidth=128,pageHeight=128,previousWordmarkBytes=1024*256*2,
            contentBounds=new{x=2,y=10,width=508,height=236},bannerBytes=96*32*2,iconBytes=32*32*2
        },new JsonSerializerOptions{WriteIndented=true})+"\n");
    }

    internal static void Check(string root)
    {
        Build(root);byte[] data=File.ReadAllBytes(Path.Combine(root,"out/disc",DiscName));TexturePixels.Validate(data);
        if(data.Length!=32+PixelBytes||TexturePixels.Int(data,4)!=1||TexturePixels.Int(data,8)!=5||TexturePixels.Width(data)!=Width||TexturePixels.Height(data)!=Height||TexturePixels.Int(data,24)!=0||TexturePixels.Int(data,28)!=0)
            throw new InvalidDataException("Title logo violates the 512x256 RGB5A3 native budget/header");
        using var expected=ComposeLogo(root);int clear=0,opaque=0,smooth=0;
        using var decoded=new SKBitmap(Width,Height,SKColorType.Rgba8888,SKAlphaType.Unpremul);
        for(int y=0;y<Height;y++)for(int x=0;x<Width;x++){
            uint pixel=TexturePixels.Pixel(data,x,y);var actual=new SKColor((byte)(pixel>>24),(byte)(pixel>>16),(byte)(pixel>>8),(byte)pixel);var p=expected.GetPixel(x,y);
            decoded.SetPixel(x,y,actual);
            if(actual.Alpha==0)clear++;else if(actual.Alpha==255)opaque++;else smooth++;
            if(Math.Abs(actual.Alpha-p.Alpha)>31)throw new InvalidDataException("Logo native alpha round trip exceeds RGB5A3 precision");
            int tolerance=p.Alpha>=224?7:15;
            if(p.Alpha>=32&&(Math.Abs(actual.Red-p.Red)>tolerance||Math.Abs(actual.Green-p.Green)>tolerance||Math.Abs(actual.Blue-p.Blue)>tolerance))
                throw new InvalidDataException("Logo RGB5A3 tile/channel round trip failed");
            if((x<Padding||x>=Width-Padding||y<Padding||y>=Height-Padding)&&actual.Alpha!=0)
                throw new InvalidDataException("Logo must retain transparent padding on every edge");
        }
        if(clear<10000||opaque<60000||smooth<100)throw new InvalidDataException("Logo must retain visible color, a transparent background and antialiased edge pixels");
        Save(decoded,Path.Combine(root,"out/brand/wordmark-native.png"));
        // Exercise exactly the runtime's 128px atlas decoder/re-encoder. Color
        // and alpha must survive without falling back to double-size RGBA8.
        var page=new byte[128*128*4];int residentBytes=0;
        for(int py=0;py<Height;py+=128)for(int px=0;px<Width;px+=128){
            TexturePixels.Resample(data,px,py,128,128,page,128,128);byte[] encoded=TexturePixels.Encode(page,128,128,5);residentBytes+=encoded.Length;
            var check=new byte[32+encoded.Length];int[] header={0x58544e44,1,5,128,128,encoded.Length,0,0};
            for(int i=0;i<header.Length;i++)BinaryPrimitives.WriteInt32LittleEndian(check.AsSpan(i*4),header[i]);encoded.CopyTo(check,32);
            for(int y=0;y<128;y++)for(int x=0;x<128;x++){
                uint before=TexturePixels.Pixel(data,px+x,py+y),after=TexturePixels.Pixel(check,x,y);
                if((before&255)!=(after&255)||((before&255)!=0&&before!=after))throw new InvalidDataException("Runtime atlas decode changed visible logo pixels");
            }
        }
        if(residentBytes!=PixelBytes)throw new InvalidDataException("Logo atlas exceeds native memory budget");
        foreach(int width in new[]{96,32}){
            using var bitmap=BannerBitmap(root,width);byte[] native=Banner(root,width);
            if(native.Length!=width*32*2)throw new InvalidDataException("Native banner/icon byte budget is wrong");
            int offset=0;
            for(int ty=0;ty<32;ty+=4)for(int tx=0;tx<width;tx+=4)for(int y=ty;y<ty+4;y++)for(int x=tx;x<tx+4;x++){
                int value=native[offset++]<<8|native[offset++];var p=bitmap.GetPixel(x,y);
                if((value&0x8000)==0||Math.Abs(((value>>10)&31)*255/31-p.Red)>7||Math.Abs(((value>>5)&31)*255/31-p.Green)>7||Math.Abs((value&31)*255/31-p.Blue)>7)
                    throw new InvalidDataException("Banner/icon tiled RGB555 round trip failed");
            }
        }
        Console.WriteLine($"Brand assets: 512x256 RGB5A3, {residentBytes} native bytes in eight atlas pages (50% below old wordmark); {clear} clear, {smooth} smooth-alpha texels; approved 96x32 banner and 32x32 acorn icon passed native round trips");
    }
}
