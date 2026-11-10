using System;

namespace AnimalTown;

// DNEF v3 compatibility bridge: build a known four-stage water program without
// referencing newer SDK APIs. The pinned v2 runtime rejects this package, so
// WaterMaterial falls back to its existing v2 water. A future v3 SDK accepts it.
// The second texture supplies the indirect warp; it is not sampled as RGB.
public static class WindWakerWaterEffect
{
    public const int PackageBytes=208;
    public static byte[] Package(byte r0,byte g0,byte b0,byte r1,byte g1,byte b1,byte strength=76)
    {
        if(strength==0)throw new ArgumentOutOfRangeException("strength");
        var bytes=new byte[PackageBytes];
        bytes[0]=(byte)'D';bytes[1]=(byte)'N';bytes[2]=(byte)'E';bytes[3]=(byte)'F';
        bytes[4]=3;  // DNEF version
        bytes[8]=4;  // 3 authored stages, 1 primary-alpha epilogue
        bytes[12]=3; // both primary and secondary texture units bound
        bytes[16]=2; // two konst colors
        bytes[20]=strength; // signed indirect displacement strength
        const string name0="Foam",name1="Foam2";
        for(int i=0;i<name0.Length;i++)bytes[32+i]=(byte)name0[i];
        for(int i=0;i<name1.Length;i++)bytes[68+i]=(byte)name1[i];
        bytes[64]=r0;bytes[65]=g0;bytes[66]=b0;bytes[67]=255;
        bytes[100]=r1;bytes[101]=g1;bytes[102]=b1;bytes[103]=255;
        var stages=new byte[]{
            15,15,15,10,0,0,0,0, // prev = vertex tint
             0,14, 8,15,0,0,12,0, // mix(prev, Foam, TEX0 @ UV0), warped
             0,14, 8,15,0,0,13,2, // mix(prev, Foam2, TEX0 @ UV1), warped
            15,15,15, 0,0,0,0,0  // preserve RGB; unwarped TEX0 alpha
        };
        Array.Copy(stages,0,bytes,176,stages.Length);
        return bytes;
    }
}
