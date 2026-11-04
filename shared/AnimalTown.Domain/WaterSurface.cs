namespace AnimalTown;

// Logical-space water rules. Projection onto the cylinder is a renderer concern.
public static class WaterSurface
{
    public const int TextureSize=64,MaximumCells=FloorStrips.Capacity;
    public const float Height=-.06f,UvScale=.25f,FoamWidth=.12f;
    public static bool IsWater(Town town,int x,int z)=>town.At(x,z)==(int)Terrain.Water||WaterTerrain.IsSea(town,x,z);
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
        double value=0;
        for(int i=0;i<3;i++){
            double dx=Wrap(x-(i==0?13:i==1?43:29)),dy=Wrap(y-(i==0?15:i==1?38:57));
            double length=i==1?8:6,arc=dy-dx*dx*.045;
            double weight=Math.Max(0,1-Math.Abs(arc)/1.6)*Math.Max(0,1-dx*dx/(length*length));
            value=Math.Max(value,weight);
        }
        return (byte)(value*72);
    }
    private static double Wrap(double value)=>value-64*Math.Floor((value+32)/64);
}
