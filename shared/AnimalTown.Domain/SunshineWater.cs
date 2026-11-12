using System;

namespace AnimalTown;

public enum WaterRenderStyle { WindWaker, Sunshine }

// Original, fixed-function GameCube tropical-water approximation. Hand-authored
// mip *content* changes glint strength and surface opacity with distance; every
// mip is packed directly into native GX IA8 4x4 blocks. This is not copied art.
public static class SunshineWater
{
    public const int TextureSize=64,MipLevels=5,PayloadBytes=10912;
    // GX's two-reference alpha test retains sparse crests and weak clear
    // regions while discarding the intervening foggy alpha band.
    public const byte GlintLowReference=78,GlintHighReference=140;
    public const float RippleLodBias=1.1f,ReflectionLodBias=1.3f;
    public const float RiverBedDepth=.40f,SeaBedDepth=.75f;
    private const double Tau=6.283185307179586;

    public static byte Brightness(int level,int x,int y)=>BrightnessFromWave(level,Pattern(level,x,y));
    public static byte Alpha(int level,int x,int y)=>AlphaFromWave(level,Pattern(level,x,y));
    private static byte BrightnessFromWave(int level,double wave)
    {
        int baseValue=level switch{0=>8,1=>20,2=>88,3=>34,_=>12};
        int contrast=level switch{0=>95,1=>142,2=>166,3=>110,_=>72};
        return (byte)Math.Min(255,baseValue+(int)Math.Round(contrast*wave));
    }
    private static byte AlphaFromWave(int level,double wave)
    {
        int baseValue=level switch{0=>96,1=>140,2=>196,3=>158,_=>119};
        return (byte)(baseValue+(int)Math.Round(31*wave));
    }

    // The second stage must have its own wave field, not another binding of
    // TEX0. Two different directions and phase perturbations produce moving
    // interference when their UVs scroll independently on the GameCube.
    public static byte GlintBrightness(int level,int x,int y)=>GlintBrightnessFromWave(level,GlintPattern(level,x,y));
    public static byte GlintAlpha(int level,int x,int y)=>GlintAlphaFromWave(level,GlintPattern(level,x,y));
    private static byte GlintBrightnessFromWave(int level,double wave)
    {
        int pedestal=level switch{0=>4,1=>14,2=>88,3=>26,_=>4};
        int contrast=level switch{0=>194,1=>232,2=>165,3=>120,_=>54};
        return (byte)Math.Min(255,pedestal+(int)Math.Round(contrast*wave));
    }
    private static byte GlintAlphaFromWave(int level,double wave)
    {
        int pedestal=level switch{0=>4,1=>10,2=>18,3=>7,_=>4};
        int contrast=level switch{0=>242,1=>243,2=>237,3=>206,_=>126};
        return (byte)Math.Min(255,pedestal+(int)Math.Round(contrast*wave));
    }
    private static double GlintPattern(int level,int x,int y)
    {
        if((uint)level>=MipLevels)throw new ArgumentOutOfRangeException(nameof(level));
        int size=TextureSize>>level;
        double u=Mod(x,size)/(double)size,v=Mod(y,size)/(double)size;
        double phase=Tau*(5*u-2*v)+.32*Math.Sin(Tau*(u+v));
        double crest=1-Math.Abs(Math.Sin(phase));
        crest*=crest;crest*=crest;
        double glimmer=Math.Max(0,Math.Cos(Tau*(u-4*v))*Math.Sin(Tau*(3*u+2*v)));
        return Math.Min(1,.85*crest+.15*glimmer*glimmer);
    }

    private static double Pattern(int level,int x,int y)
    {
        if((uint)level>=MipLevels)throw new ArgumentOutOfRangeException(nameof(level));
        int size=TextureSize>>level;
        x=Mod(x,size);y=Mod(y,size);
        double u=x/(double)size,v=y/(double)size;
        double phase=Tau*(3*u+2*v)+.43*Math.Sin(Tau*(u-v));
        // Eight-power and squared crests with multiplications rather than
        // expensive per-texel pow() calls on the GameCube's CPU.
        double crest=1-Math.Abs(Math.Sin(phase));
        crest*=crest;crest*=crest;crest*=crest;
        double glimmer=Math.Max(0,Math.Sin(Tau*(2*u-3*v))*
            Math.Cos(Tau*(u+4*v)));
        glimmer*=glimmer;
        return Math.Min(1,.72*crest+.28*glimmer);
    }

    public static byte[] TexturePackage()=>BuildPackage(false);
    public static byte[] GlintTexturePackage()=>BuildPackage(true);
    private static byte[] BuildPackage(bool reflection)
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
                    double wave=reflection?GlintPattern(level,x,y):Pattern(level,x,y);
                    result[at++]=reflection?GlintAlphaFromWave(level,wave):AlphaFromWave(level,wave);
                    result[at++]=reflection?GlintBrightnessFromWave(level,wave):BrightnessFromWave(level,wave);
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
