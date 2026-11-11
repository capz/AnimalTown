using System;

namespace AnimalTown;

public enum WaterRenderStyle { WindWaker, Sunshine }

// Original, fixed-function GameCube tropical-water approximation. Hand-authored
// mip *content* changes glint strength and surface opacity with distance; every
// mip is packed directly into native GX IA8 4x4 blocks. This is not copied art.
public static class SunshineWater
{
    public const int TextureSize=64,MipLevels=5,PayloadBytes=10912;
    public const float RiverBedDepth=.40f,SeaBedDepth=.75f;
    private const double Tau=6.283185307179586;

    public static byte Brightness(int level,int x,int y)
    {
        double wave=Pattern(level,x,y);
        int baseValue=level switch{0=>8,1=>20,2=>88,3=>34,_=>12};
        int contrast=level switch{0=>95,1=>142,2=>166,3=>110,_=>72};
        return (byte)Math.Min(255,baseValue+(int)Math.Round(contrast*wave));
    }

    public static byte Alpha(int level,int x,int y)
    {
        double wave=Pattern(level,x,y);
        int baseValue=level switch{0=>96,1=>140,2=>196,3=>158,_=>119};
        return (byte)(baseValue+(int)Math.Round(31*wave));
    }

    private static double Pattern(int level,int x,int y)
    {
        if((uint)level>=MipLevels)throw new ArgumentOutOfRangeException(nameof(level));
        int size=TextureSize>>level;
        x=Mod(x,size);y=Mod(y,size);
        double u=x/(double)size,v=y/(double)size;
        double phase=Tau*(3*u+2*v)+.43*Math.Sin(Tau*(u-v));
        double crest=Math.Pow(1-Math.Abs(Math.Sin(phase)),8);
        double glimmer=Math.Pow(Math.Max(0,Math.Sin(Tau*(2*u-3*v))*
            Math.Cos(Tau*(u+4*v))),2);
        return Math.Min(1,.72*crest+.28*glimmer);
    }

    public static byte[] TexturePackage()
    {
        var result=new byte[32+PayloadBytes];int at=32;
        Put(result,0,0x58544e44); // DNTX
        Put(result,4,2);          // Native package with explicit mip levels
        Put(result,8,3);          // GX IA8: alpha followed by luminance
        Put(result,12,TextureSize);Put(result,16,TextureSize);
        Put(result,20,PayloadBytes);Put(result,24,MipLevels);
        for(int level=0;level<MipLevels;level++){
            int size=TextureSize>>level;
            for(int by=0;by<size;by+=4)for(int bx=0;bx<size;bx+=4)
                for(int y=by;y<by+4;y++)for(int x=bx;x<bx+4;x++){
                    result[at++]=Alpha(level,x,y);
                    result[at++]=Brightness(level,x,y);
                }
        }
        if(at!=result.Length)throw new InvalidOperationException("Invalid Sunshine mip payload size.");
        return result;
    }

    // Uniform scale of the curved water mesh around its cylinder axis:
    // Pbed = scale*Psurface + (scale-1)*Radius along local Y. As the mesh
    // already sits on Radius + WaterSurface.Height, this produces a constant
    // physical inset without storing a second geometry archive in ARAM.
    public static float BedScale(float depth)
    {
        float radius=CylinderWorld.Radius+WaterSurface.Height;
        if(!float.IsFinite(depth)||depth<=0||depth>=radius)
            throw new ArgumentOutOfRangeException(nameof(depth));
        return (radius-depth)/radius;
    }

    public static float BedAxisOffset(float depth)=>(BedScale(depth)-1)*CylinderWorld.Radius;

    private static int Mod(int value,int size)
    {
        value%=size;return value<0?value+size:value;
    }
    private static void Put(byte[] data,int at,int value)
    {
        for(int i=0;i<4;i++)data[at+i]=(byte)(value>>(i*8));
    }
}
