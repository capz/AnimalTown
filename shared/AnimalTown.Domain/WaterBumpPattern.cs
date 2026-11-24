namespace AnimalTown;

// A periodic derivative field, not a painted highlight or a displacement map.
// Generated once when visible-water optics initialize. IA8 preserves signed zero and
// both 8-bit components: alpha is tangent X, intensity is tangent Z. GX's
// indirect .abg fetch therefore reads these through S and T respectively.
public static class WaterBumpPattern
{
    public const int Size=64,NativeBytes=Size*Size*2;
    private const double Tau=6.283185307179586;

    public static ushort Sample(int x,int z)
    {
        x=(x%Size+Size)%Size;z=(z%Size+Size)%Size;
        double a=Tau*(2*x+z)/Size,b=Tau*(x-2*z)/Size,c=Tau*(3*x+2*z)/Size;
        // Analytic negative partial derivatives of the same height field,
        // scaled together. The two axes cannot disagree about ridge direction.
        double dx=.50*Math.Sin(a)+.25*Math.Sin(b)+.15*Math.Sin(c);
        double dz=.25*Math.Sin(a)-.50*Math.Sin(b)+.10*Math.Sin(c);
        int sx=128+(int)Math.Round(100*dx),sz=128+(int)Math.Round(100*dz);
        return (ushort)((sx<<8)|sz);
    }

    public static byte[] Texture()
    {
        var result=new byte[NativeBytes];int at=0;
        // GX IA8: 4x4 tiles, alpha byte followed by intensity byte.
        for(int bz=0;bz<Size;bz+=4)for(int bx=0;bx<Size;bx+=4)
            for(int z=bz;z<bz+4;z++)for(int x=bx;x<bx+4;x++){
                ushort sample=Sample(x,z);
                result[at++]=(byte)(sample>>8);result[at++]=(byte)sample;
            }
        return result;
    }
}
