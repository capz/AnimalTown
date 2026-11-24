namespace AnimalTown;

// Small, periodic native-texture fallbacks. They are generated only while the
// water materials are created; the GPU scrolls the resulting immutable maps.
public static class ShallowWaterPattern
{
    public const int TextureSize=64,BedTextureSize=128;
    private const double Tau=6.283185307179586;

    // Opacity is a property of the water depth, independent of an I8 ripple's
    // intensity/alpha. Leave the floor legible even in the middle of a river.
    public static byte Opacity(float depth)
    {
        depth=Math.Clamp(depth,0,1);
        return (byte)(72+72*depth);
    }

    public static byte RippleSample(int x,int y)
    {
        x=Wrap(x,TextureSize);y=Wrap(y,TextureSize);double value=0;
        // Isolated tapered arcs leave broad calm areas. Their varying lengths
        // avoid the continuous cellular foam used by the open sea.
        for(int i=0;i<10;i++){
            uint hash=Hash(i,19);
            double cx=(hash&63)+.35,cy=((hash>>6)&63)+.65;
            double dx=Delta(x-cx,TextureSize),dy=Delta(y-cy,TextureSize);
            double length=4.0+((hash>>12)&7)*.65;
            double bend=((hash>>15)&1)==0?.043:-.043;
            double taper=Math.Max(0,1-dx*dx/(length*length));
            double ridge=Math.Max(0,1-Math.Abs(dy-bend*dx*dx)/1.15);
            value=Math.Max(value,ridge*taper*taper*(.78+((hash>>16)&3)*.07));
        }
        return (byte)(255*value);
    }

    // GX indirect fetches .abg: S=alpha, T=blue, U=green. RGB565 has opaque
    // alpha, so the material's custom matrix uses only T/U and biases both.
    // Red is deliberately unused. A grey authored map remains near neutral.
    public static ushort DistortionSample(int x,int y)
    {
        x=Wrap(x,TextureSize);y=Wrap(y,TextureSize);
        double a=Tau*(2*x+y)/TextureSize,b=Tau*(x-2*y)/TextureSize;
        int blue=(int)Math.Round(16+5*Math.Sin(a));
        int green=(int)Math.Round(32+10*Math.Cos(b));
        return (ushort)((16<<11)|(green<<5)|blue);
    }

    public static ushort BedSample(int x,int y)
    {
        x=Wrap(x,BedTextureSize);y=Wrap(y,BedTextureSize);
        // A quiet sandy base, with small, softly faceted stones rather than a
        // photograph. Real larger rocks are supplied by the separate mesh.
        double variation=3*Math.Sin(Tau*(2*x+y)/BedTextureSize)+
                         2*Math.Cos(Tau*(x-3*y)/BedTextureSize);
        int noise=(int)(Hash(x,y)&3)-1;
        double red=178+variation+noise,green=180+variation+noise,blue=158+variation+noise;
        int cellX=x/16,cellY=y/16;
        for(int oy=-1;oy<=1;oy++)for(int ox=-1;ox<=1;ox++){
            int cx=cellX+ox,cy=cellY+oy;uint hash=Hash(Wrap(cx,8),Wrap(cy,8));
            if((hash&7)>3)continue;
            double centerX=cx*16+3+((hash>>3)&7),centerY=cy*16+3+((hash>>6)&7);
            double dx=x-centerX,dy=y-centerY;
            double rx=2.8+((hash>>9)&3)*.55,ry=2.0+((hash>>11)&3)*.45;
            double radius=dx*dx/(rx*rx)+dy*dy/(ry*ry);
            if(radius>=1.22)continue;
            // An understated rim and two broad planar tones read at 480p.
            double shade=radius>1?-8:dy>dx*.45?-14:4;
            double color=((hash>>14)&3)*5;
            red=157+color+shade;green=164+color+shade;blue=151+color+shade;
        }
        return Rgb565((int)red,(int)green,(int)blue);
    }

    public static byte CausticSample(int x,int y)
    {
        x=Wrap(x,TextureSize);y=Wrap(y,TextureSize);
        // A broad, soft cellular light pattern. It is added at low strength
        // below the water, with no alpha or surface-outline semantics.
        double first=double.MaxValue,second=double.MaxValue;
        int cellX=x/16,cellY=y/16;
        for(int oy=-1;oy<=1;oy++)for(int ox=-1;ox<=1;ox++){
            int cx=cellX+ox,cy=cellY+oy;uint hash=Hash(Wrap(cx,4)+23,Wrap(cy,4)+41);
            double dx=x-(cx*16+4+((hash>>3)&7)),dy=y-(cy*16+4+((hash>>7)&7));
            double distance=Math.Sqrt(dx*dx+dy*dy);
            if(distance<first){second=first;first=distance;}else if(distance<second)second=distance;
        }
        double edge=Math.Max(0,1-(second-first)/2.4);
        double light=.60+.20*Math.Sin(Tau*(x+y)/TextureSize)+.20*Math.Cos(Tau*(x-2*y)/TextureSize);
        return (byte)(255*edge*edge*light);
    }

    public static byte[] RippleTexture()=>PackI8(false);
    public static byte[] CausticTexture()=>PackI8(true);
    private static byte[] PackI8(bool caustic)
    {
        var result=new byte[TextureSize*TextureSize];int at=0;
        // GX I8 uses 8x4 texel blocks.
        for(int by=0;by<TextureSize;by+=4)for(int bx=0;bx<TextureSize;bx+=8)
            for(int y=by;y<by+4;y++)for(int x=bx;x<bx+8;x++)
                result[at++]=caustic?CausticSample(x,y):RippleSample(x,y);
        return result;
    }
    public static byte[] DistortionTexture()=>PackRgb565(false);
    public static byte[] BedTexture()=>PackRgb565(true);
    private static byte[] PackRgb565(bool bed)
    {
        int size=bed?BedTextureSize:TextureSize;var result=new byte[size*size*2];int at=0;
        // GX RGB565 uses big-endian texels inside 4x4 blocks.
        for(int by=0;by<size;by+=4)for(int bx=0;bx<size;bx+=4)
            for(int y=by;y<by+4;y++)for(int x=bx;x<bx+4;x++){
                ushort sample=bed?BedSample(x,y):DistortionSample(x,y);
                result[at++]=(byte)(sample>>8);result[at++]=(byte)sample;
            }
        return result;
    }
    private static ushort Rgb565(int r,int g,int b)=>(ushort)(((r>>3)<<11)|((g>>2)<<5)|(b>>3));
    private static int Wrap(int value,int size)=>(value%size+size)%size;
    private static double Delta(double value,int size)=>value-size*Math.Floor((value+size*.5)/size);
    private static uint Hash(int x,int y)
    {
        uint value=unchecked((uint)x*0x9e3779b9u+(uint)y*0x85ebca6bu+0x68bc21ebu);
        value^=value>>16;value*=0x7feb352du;value^=value>>15;value*=0x846ca68bu;
        return value^(value>>16);
    }
}
