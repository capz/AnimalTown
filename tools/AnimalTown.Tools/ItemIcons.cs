using System.Buffers.Binary;
using SkiaSharp;

internal static class ItemIcons
{
    internal static void Build(string root)
    {
        using var bitmap=new SKBitmap(512,256,SKColorType.Rgba8888,SKAlphaType.Unpremul);bitmap.Erase(SKColors.Transparent);using var c=new SKCanvas(bitmap);
        var ink=new SKColor(49,72,62);var leaf=new SKColor(53,105,84);var clay=new SKColor(195,109,84);var honey=new SKColor(232,183,86);var paper=new SKColor(255,245,221);var mist=new SKColor(149,198,186);
        using var fill=new SKPaint{IsAntialias=true};using var line=new SKPaint{IsAntialias=true,Style=SKPaintStyle.Stroke,StrokeWidth=4,StrokeCap=SKStrokeCap.Round,Color=ink};
        void Oval(float x,float y,float w,float h,SKColor color){fill.Color=color;c.DrawOval(new SKRect(x,y,x+w,y+h),fill);}
        void Rect(float x,float y,float w,float h,float r,SKColor color){fill.Color=color;c.DrawRoundRect(new SKRect(x,y,x+w,y+h),r,r,fill);}
        void Triangle(float ax,float ay,float bx,float by,float cx,float cy,SKColor color){fill.Color=color;using var p=new SKPathBuilder();p.MoveTo(ax,ay);p.LineTo(bx,by);p.LineTo(cx,cy);p.Close();using var path=p.Detach();c.DrawPath(path,fill);}
        for(int id=1;id<=25;id++)
        {
            c.Save();c.Translate(id%8*64,id/8*64);
            if(id==1){Oval(12,20,27,32,clay);Oval(29,20,23,32,clay);Oval(33,9,19,11,leaf);c.DrawLine(32,22,33,12,line);}
            else if(id==2){c.DrawLine(32,33,32,55,line);Oval(12,40,20,10,leaf);for(int n=0;n<5;n++){float a=n*1.256637f;Oval(23+(float)Math.Cos(a)*12,18+(float)Math.Sin(a)*12,18,18,clay);}Oval(25,20,15,15,honey);}
            else if(id==3){Rect(28,38,8,19,3,clay);Triangle(32,6,8,46,56,46,leaf);Triangle(32,4,13,30,51,30,leaf);}
            else if(id==4){Rect(20,20,24,34,5,mist);Triangle(20,18,7,29,19,37,mist);Triangle(44,18,57,29,45,37,mist);Oval(25,13,14,12,paper);}
            else if(id>=5&&id<=9)
            {
                c.DrawLine(19,55,37,17,line);
                if(id==5){c.DrawLine(37,17,52,10,line);c.DrawLine(52,10,52,41,line);Oval(47,40,8,8,clay);}
                if(id==6){c.Save();c.RotateDegrees(20,40,18);c.DrawOval(new SKRect(24,3,57,30),line);line.StrokeWidth=1;for(int k=0;k<4;k++)c.DrawLine(27+k*7,8,27+k*7,27,line);line.StrokeWidth=4;c.Restore();}
                if(id==7){Rect(29,7,17,24,7,mist);c.DrawOval(new SKRect(11,47,26,59),line);}
                if(id==8){Rect(18,29,29,24,7,mist);c.DrawOval(new SKRect(13,17,42,42),line);Triangle(43,35,59,24,49,46,mist);}
                if(id==9){Triangle(32,10,57,7,50,30,mist);}
            }
            else if(id==10){Rect(15,9,34,22,5,clay);Rect(13,33,38,8,3,honey);Rect(17,39,6,18,2,clay);Rect(41,39,6,18,2,clay);}
            else if(id==11){Rect(8,21,48,10,5,clay);Rect(14,29,7,28,3,ink);Rect(44,29,7,28,3,ink);}
            else if(id==12){Oval(17,11,30,40,clay);Oval(24,22,5,7,ink);Oval(36,22,5,7,ink);Rect(24,36,17,5,2,honey);}
            else if(id==13){Rect(10,33,44,23,4,clay);Rect(8,10,48,18,5,mist);for(int k=0;k<4;k++)Rect(10+k*12,10,6,18,2,paper);c.DrawLine(13,28,13,34,line);c.DrawLine(51,28,51,34,line);}
            else if(id==14){Oval(9,19,36,27,mist);Triangle(39,31,56,15,56,49,clay);Oval(17,26,5,5,ink);}
            else if(id==15){Oval(6,10,27,27,clay);Oval(31,10,27,27,clay);Oval(12,31,21,22,honey);Oval(31,31,21,22,honey);Rect(29,15,6,35,3,ink);}
            else if(id==16){Oval(12,10,40,44,paper);c.DrawArc(new SKRect(17,14,48,47),0,300,false,line);c.DrawArc(new SKRect(25,24,43,40),0,300,false,line);}
            else if(id==17){Oval(8,14,48,38,paper);Rect(22,45,20,10,4,clay);for(int n=0;n<5;n++)c.DrawLine(32,48,12+n*10,22,line);}
            else if(id==18){Rect(10,17,44,36,5,clay);Rect(28,17,8,36,2,honey);Rect(10,29,44,8,2,honey);}
            else if(id==19){Rect(7,17,50,34,5,paper);c.DrawLine(10,21,32,37,line);c.DrawLine(32,37,54,21,line);}
            else if(id==20){c.DrawLine(33,25,33,54,line);Oval(6,8,52,32,clay);Rect(5,29,54,11,4,paper);}
            else if(id==21){for(int n=0;n<5;n++)c.DrawLine(31,56,13+n*9,13+(n%2)*9,line);Oval(8,14,18,9,leaf);Oval(38,13,18,9,leaf);}
            else if(id>=22&&id<=24){Oval(12,23,40,33,honey);Triangle(24,25,20,10,44,10,clay);Rect(23,23,20,5,2,ink);for(int n=0;n<id-21;n++)Oval(22+n*8,39,5,5,paper);}
            else {Rect(29,26,6,27,2,ink);Rect(19,52,27,6,3,clay);Triangle(32,7,10,34,54,34,honey);}
            c.Restore();
        }
        using(var image=SKImage.FromBitmap(bitmap))using(var png=image.Encode(SKEncodedImageFormat.Png,100))File.WriteAllBytes(Path.Combine(root,"out/brand/item-icons.png"),png.ToArray());
        var data=new byte[32+512*256*2];BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0),0x58544e44);BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4),1);BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8),5);BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12),512);BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(16),256);BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(20),data.Length-32);
        int at=32;for(int ty=0;ty<256;ty+=4)for(int tx=0;tx<512;tx+=4)for(int y=ty;y<ty+4;y++)for(int x=tx;x<tx+4;x++)
        {var p=bitmap.GetPixel(x,y);ushort value=p.Alpha>=224?(ushort)(0x8000|((p.Red>>3)<<10)|((p.Green>>3)<<5)|(p.Blue>>3)):(ushort)(((p.Alpha>>5)<<12)|((p.Red>>4)<<8)|((p.Green>>4)<<4)|(p.Blue>>4));data[at++]=(byte)(value>>8);data[at++]=(byte)value;}
        File.WriteAllBytes(Path.Combine(root,"out/disc/item-icons.dndtex"),data);
    }
}
