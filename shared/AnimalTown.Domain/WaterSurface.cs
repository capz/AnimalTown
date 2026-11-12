namespace AnimalTown;

// Logical-space water rules. Projection onto the cylinder is a renderer concern.
public static class WaterSurface
{
    public const int TextureSize=64,MaximumCells=FloorStrips.Capacity;
    public const float Height=WaterTerrain.RiverHeight,UvScale=.25f,FoamWidth=.12f;
    public static bool IsWater(Town town,int x,int z)=>town.At(x,z)==(int)Terrain.Water||WaterTerrain.IsSea(town,x,z);
    public static bool HasBed(Town town,int x,int z)=>IsWater(town,x,z)||town.At(x,z)==(int)Terrain.Bridge;
    // X is straight on the cylinder; each Z row must retain its own chord.
    // Merge only matching, X-flat height profiles and one bed depth. This
    // also crosses floor-material/variant boundaries, which do not tint beds.
    public static int BedRunEnd(Town town,int left,int z,int right)
    {
        if(right<=left)throw new ArgumentOutOfRangeException(nameof(right));
        if(!HasBed(town,left,z))return left;
        float near=HeightAt(town,left,z,left,z),far=HeightAt(town,left,z,left,z+1);
        if(near!=HeightAt(town,left,z,left+1,z)||far!=HeightAt(town,left,z,left+1,z+1))return left+1;
        bool sea=WaterTerrain.IsSea(town,left,z);int end=left+1;
        while(end<right&&HasBed(town,end,z)&&WaterTerrain.IsSea(town,end,z)==sea&&
            HeightAt(town,end,z,end,z)==near&&HeightAt(town,end,z,end+1,z)==near&&
            HeightAt(town,end,z,end,z+1)==far&&HeightAt(town,end,z,end+1,z+1)==far)end++;
        return end;
    }
    public static float HeightAt(Town town,int cx,int cz,float x,float z)=>TerrainElevation.AtCell(town,cx,cz,x,z)+(town.At(cx,cz)==(int)Terrain.Bridge?WaterTerrain.RiverHeight:0);
    // Bridges cover the river rather than creating a new bank.
    public static bool IsBank(Town town,int x,int z)=>town.Inside(x,z)&&!IsWater(town,x,z)&&town.At(x,z)!=(int)Terrain.Bridge;
    // One value per logical corner: adjacent cells always interpolate the same
    // tint, including across strip boundaries and outside the playable town.
    public static float Depth(Town town,int x,int z)
    {
        float distance=3;
        for(int dz=-3;dz<3;dz++)for(int dx=-3;dx<3;dx++)if(IsBank(town,x+dx,z+dz)){
            float sx=Math.Max(0,Math.Max(dx,-dx-1)),sz=Math.Max(0,Math.Max(dz,-dz-1));
            distance=Math.Min(distance,(float)Math.Sqrt(sx*sx+sz*sz));
        }
        return distance/3;
    }
    public static float Scroll(double seconds,float speed)=>(float)(seconds*speed-Math.Floor(seconds*speed));
    // Original neutral patterns, sampled periodically then packed in GX I8
    // 8x4 tiles. Both maps have implicit opaque alpha; ripple intensity is the
    // TEV interpolation weight, never the framebuffer opacity.
    public static byte[] Texture(bool ripples)
    {
        var pixels=new byte[TextureSize*TextureSize];int at=0;
        for(int by=0;by<TextureSize;by+=4)for(int bx=0;bx<TextureSize;bx+=8)
            for(int y=by;y<by+4;y++)for(int x=bx;x<bx+8;x++)pixels[at++]=Sample(x,y,ripples);
        return pixels;
    }
    public static byte Sample(int x,int y,bool ripples)
    {
        x=(x%64+64)%64;y=(y%64+64)%64;
        if(!ripples)return (byte)(239+8*Math.Sin((x+y)*Math.PI/32)+7*Math.Cos((x-2*y)*Math.PI/32));
        return WindWakerWaterPattern.Foam(x,y);
    }
    // Pale shoreline breakers use IA8, unlike the opaque I8 surface masks.
    // GX IA8 stores alpha first and luminance second in each 4x4 native tile.
    // The material scrolls this texture without rewriting any shoreline vertices.
    public static byte[] ShoreTexture()
    {
        var tex=new byte[TextureSize*TextureSize*2];int at=0;
        for(int by=0;by<TextureSize;by+=4)for(int bx=0;bx<TextureSize;bx+=4)
            for(int y=by;y<by+4;y++)for(int x=bx;x<bx+4;x++){
                tex[at++]=WindWakerWaterPattern.ShoreAlpha(x,y);
                tex[at++]=255;
            }
        return tex;
    }
    // Separate X/Y signed offsets stored in periodic GX RGB565 4x4 tiles.
    // Native format is big-endian u16 per texel. Generated once at startup.
    public static ushort WarpSample(int x,int y)
    {
        x=(x%TextureSize+TextureSize)%TextureSize;
        y=(y%TextureSize+TextureSize)%TextureSize;
        double a=6.283185307179586*(2*x+y)/TextureSize;
        double b=6.283185307179586*(x-2*y)/TextureSize;
        int red=(int)Math.Round(16+13*Math.Sin(a));
        int green=(int)Math.Round(32+27*Math.Cos(b));
        return (ushort)((red<<11)|(green<<5)|16);
    }
    public static byte[] WarpTexture()
    {
        var tex=new byte[TextureSize*TextureSize*2];int at=0;
        for(int by=0;by<TextureSize;by+=4)for(int bx=0;bx<TextureSize;bx+=4)
            for(int y=by;y<by+4;y++)for(int x=bx;x<bx+4;x++){
                ushort sample=WarpSample(x,y);
                tex[at++]=(byte)(sample>>8);
                tex[at++]=(byte)sample;
            }
        return tex;
    }
    // Original looping whitecaps for the optional storm material, GX I8 8x4.
    public static byte[] StormTexture()
    {
        var pixels=new byte[TextureSize*TextureSize];int at=0;
        for(int by=0;by<64;by+=4)for(int bx=0;bx<64;bx+=8)
            for(int y=by;y<by+4;y++)for(int x=bx;x<bx+8;x++)pixels[at++]=StormSample(x,y);
        return pixels;
    }
    public static byte StormSample(int x,int y)
    {
        x=(x%64+64)%64;y=(y%64+64)%64;
        double value=0;
        for(int i=0;i<4;i++){
            double dx=Wrap(x-(i==0?9:i==1?43:i==2?24:58)),dy=Wrap(y-(i==0?12:i==1?28:i==2?47:58));
            double length=i==1?13:i==2?9:11,edge=dx*dx/(length*length);
            double crest=Math.Max(0,1-Math.Abs(dy-dx*dx*.055)/1.5)*Math.Max(0,1-edge*edge);
            value=Math.Max(value,crest);
        }
        return (byte)(value*220);
    }
    private static double Wrap(double value)=>value-64*Math.Floor((value+32)/64);
}
