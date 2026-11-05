namespace AnimalTown;

// One periodic, neutral GX I8 mask. World motion is independent of camera,
// cylinder projection, terrain elevation and ground texture variants.
public static class CloudShadowPattern
{
    public const int Size=64;
    public const float WorldPeriod=128,SpeedX=.18f,SpeedZ=.09f;
    public static byte Ambient(float cover)=>(byte)(255-24*Clamp(cover));
    public static byte Shade(float cover){float c=Clamp(cover);return (byte)(255-44*4*c*(1-c));}
    private static float Clamp(float x)=>float.IsNaN(x)?0:Math.Max(0,Math.Min(1,x));
    public static float Offset(double seconds,float speed)=>(float)(seconds*speed/WorldPeriod-Math.Floor(seconds*speed/WorldPeriod));
    public static byte Sample(int x,int z)
    {
        double u=(x%Size+Size)%Size*Math.PI*2/Size,v=(z%Size+Size)%Size*Math.PI*2/Size;
        double n=.5+.24*Math.Sin(u+Math.Sin(v))+.17*Math.Cos(v*2-u)+.09*Math.Sin(u*3+v*2);
        n=Math.Max(0,Math.Min(1,(n-.28)/.52));n=n*n*(3-2*n);
        return (byte)(n*255);
    }
    public static byte[] Texture()
    {
        var data=new byte[Size*Size];int at=0;
        for(int z=0;z<Size;z+=4)for(int x=0;x<Size;x+=8)for(int dz=0;dz<4;dz++)for(int dx=0;dx<8;dx++)data[at++]=Sample(x+dx,z+dz);
        return data;
    }
}
