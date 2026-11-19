namespace AnimalTown;

// Each level has a constant color so the distance-based GX sampler can mark
// which part of the ground uses full detail and which uses half detail.
public static class GroundMipPalette
{
    public const int Levels=2;
    private static readonly int[] colors={0x46ac,0xff28};
    public static uint Color(int level)
    {
        if((uint)level>=Levels)throw new ArgumentOutOfRangeException(nameof(level));
        int color=colors[level];
        return (uint)(((color>>11)*255/31)<<24|(((color>>5)&63)*255/63)<<16|(color&31)*255/31<<8|255);
    }
    public static byte[] Texture(int width,int height,int levels)
    {
        if(width<4||height<4||width>FloorTexture.Maximum||height>FloorTexture.Maximum||
            (width&(width-1))!=0||(height&(height-1))!=0||levels<1||levels>Levels||
            (width>>(levels-1))<4||(height>>(levels-1))<4)
            throw new ArgumentOutOfRangeException(nameof(levels),"Invalid ground diagnostic mip layout");
        int bytes=0;
        for(int level=0;level<levels;level++)bytes+=Size(width>>level,height>>level);
        var package=new byte[32+bytes];
        Write(package,0,0x58544e44);Write(package,4,2);Write(package,8,14);
        Write(package,12,width);Write(package,16,height);Write(package,20,bytes);Write(package,24,levels);
        int offset=32;
        for(int level=0;level<levels;level++){
            int end=offset+Size(width>>level,height>>level),color=colors[level];
            // Each CMPR sub-block uses one opaque RGB565 endpoint and selector
            // zero. This also fills the GX padding around the final 4px level.
            for(;offset<end;offset+=8){package[offset]=package[offset+2]=(byte)(color>>8);package[offset+1]=package[offset+3]=(byte)color;}
        }
        return package;
    }
    private static int Size(int width,int height)=>(width+7)/8*((height+7)/8)*32;
    private static void Write(byte[] bytes,int at,int value)
    {for(int b=0;b<4;b++)bytes[at+b]=(byte)(value>>(b*8));}
}
