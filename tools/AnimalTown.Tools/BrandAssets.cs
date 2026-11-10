using System.Buffers.Binary;
using SkiaSharp;

// Build-time rasterization only. The GameCube loads baked atlases; no font
// rasterizer or desktop drawing dependency is linked into the game.
internal static class BrandAssets
{
    private static readonly SKColor Paper=new(255,245,221),Moss=new(53,105,84),Ink=new(49,72,62),Clay=new(195,109,84),Honey=new(232,183,86),Mist=new(149,198,186);
    private static SKTypeface Title(string root)=>SKTypeface.FromFile(Path.Combine(root,"assets/fonts/gorditas/Gorditas-Bold.ttf"))??throw new Exception("Gorditas font missing");
    private static void SavePng(SKBitmap bitmap,string path)
    { using var image=SKImage.FromBitmap(bitmap);using var data=image.Encode(SKEncodedImageFormat.Png,100);File.WriteAllBytes(path,data.ToArray()); }
    private static void Type(SKCanvas c,string text,float x,float baseline,SKTypeface face,float size,SKColor color)
    { using var font=new SKFont(face,size);using var paint=new SKPaint{Color=color,IsAntialias=true};c.DrawText(text,x,baseline,SKTextAlign.Left,font,paint); }
    private static void Capsule(SKCanvas c,SKRect rect,float radius,SKColor color)
    { using var paint=new SKPaint{Color=color,IsAntialias=true};c.DrawRoundRect(rect,radius,radius,paint); }
    private static void Mark(SKCanvas c,float x,float y,float scale)
    {
        c.Save();c.Translate(x,y);c.Scale(scale);
        // A small welcoming doorway, resting on two curved meadow lines.
        Capsule(c,new SKRect(-21,-14,21,23),15,Moss);
        Capsule(c,new SKRect(-8,1,8,27),8,Paper);
        using var paint=new SKPaint{Color=Honey,IsAntialias=true};
        c.Save();c.RotateDegrees(-32);c.DrawOval(new SKRect(-20,-36,-2,-23),paint);c.Restore();
        c.Save();c.RotateDegrees(32);c.DrawOval(new SKRect(2,-36,20,-23),paint);c.Restore();
        paint.Color=Clay;paint.Style=SKPaintStyle.Stroke;paint.StrokeWidth=3;paint.StrokeCap=SKStrokeCap.Round;
        using var builder=new SKPathBuilder();builder.MoveTo(-34,30);builder.QuadTo(0,18,34,30);using var path=builder.Detach();c.DrawPath(path,paint);
        c.Restore();
    }
    public static void Build(string root)
    {
        Directory.CreateDirectory(Path.Combine(root,"out/brand"));Directory.CreateDirectory(Path.Combine(root,"out/disc"));
        using var title=Title(root);
        ItemIcons.Build(root);
        SplashAssets.Check(root);
        Font(root,"gorditas-bold",title,24);
        foreach(string obsolete in new[]{"fredoka-medium.dndtex","fredoka-medium.metrics","fredoka-semibold.dndtex","fredoka-semibold.metrics","Fredoka-OFL.txt"})File.Delete(Path.Combine(root,"out/disc",obsolete));
        using(var wordmark=new SKBitmap(1024,256,SKColorType.Rgba8888,SKAlphaType.Unpremul))
        {wordmark.Erase(SKColors.Transparent);using var wc=new SKCanvas(wordmark);using var wf=new SKFont(title,128);Type(wc,"AnimalTown",(1024-wf.MeasureText("AnimalTown"))/2,175,title,128,SKColors.White);SavePng(wordmark,Path.Combine(root,"out/brand/wordmark.png"));Texture(root,"wordmark",wordmark);}
        File.Copy(Path.Combine(root,"assets/fonts/gorditas/OFL.txt"),Path.Combine(root,"out/disc/Gorditas-OFL.txt"),true);
        using var banner=DrawBanner(root,96);SavePng(banner,Path.Combine(root,"out/brand/disc-banner.png"));
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
    private static void Texture(string root,string name,SKBitmap bitmap)
    {
        var package=new byte[32+bitmap.Width*bitmap.Height*2];BinaryPrimitives.WriteInt32LittleEndian(package.AsSpan(0),0x58544e44);BinaryPrimitives.WriteInt32LittleEndian(package.AsSpan(4),1);BinaryPrimitives.WriteInt32LittleEndian(package.AsSpan(8),3);BinaryPrimitives.WriteInt32LittleEndian(package.AsSpan(12),bitmap.Width);BinaryPrimitives.WriteInt32LittleEndian(package.AsSpan(16),bitmap.Height);BinaryPrimitives.WriteInt32LittleEndian(package.AsSpan(20),package.Length-32);
        int p=32;for(int ty=0;ty<bitmap.Height;ty+=4)for(int tx=0;tx<bitmap.Width;tx+=4)for(int y=ty;y<ty+4;y++)for(int x=tx;x<tx+4;x++){package[p++]=bitmap.GetPixel(x,y).Alpha;package[p++]=255;}
        File.WriteAllBytes(Path.Combine(root,"out/disc/"+name+".dndtex"),package);
    }
    private static SKBitmap DrawBanner(string root,int width)
    {
        var bitmap=new SKBitmap(width,32);using var c=new SKCanvas(bitmap);c.Clear(Paper);
        Capsule(c,new SKRect(-8,20,width+8,50),18,Mist);
        if(width==32){Mark(c,16,16,.45f);return bitmap;}
        using var title=Title(root);using var font=new SKFont(title,14);float textWidth=font.MeasureText("AnimalTown");Type(c,"AnimalTown",(width-textWidth)/2,20,title,14,Ink);
        using var paint=new SKPaint{Color=Honey,IsAntialias=true};c.DrawCircle(7,7,2,paint);c.DrawCircle(width-7,7,2,paint);return bitmap;
    }
    public static byte[] Banner(string root,int width)
    {
        using var bitmap=DrawBanner(root,width);var bytes=new byte[width*32*2];int at=0;
        for(int ty=0;ty<32;ty+=4)for(int tx=0;tx<width;tx+=4)for(int y=ty;y<ty+4;y++)for(int x=tx;x<tx+4;x++)
        {var c=bitmap.GetPixel(x,y);ushort p=(ushort)(0x8000|((c.Red>>3)<<10)|((c.Green>>3)<<5)|(c.Blue>>3));bytes[at++]=(byte)(p>>8);bytes[at++]=(byte)p;}
        return bytes;
    }
}
