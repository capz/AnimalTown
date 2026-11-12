using System;

namespace AnimalTown;

public enum WaterRenderStyle { WindWaker, Sunshine }

// The five surface levels are independently authored; the near-wave and
// indirect-displacement textures have separate, ordinary mip chains. All use
// GX I4, whose decoded intensity supplies both texture color and texture alpha.
public static class SunshineWater
{
    public const int TextureSize=128,MipLevels=5,PayloadBytes=10912,Cells=16;
    public const int NearWaveWidth=128,NearWaveHeight=256,NearWaveMipLevels=5,NearWavePayloadBytes=21824;
    public const int DistortionSize=64,DistortionMipLevels=4,DistortionPayloadBytes=2720;
    public const float RiverBedDepth=.40f,SeaBedDepth=.75f;
    public const string DiscTexture="dvd:/sunshine-water.dndtex";
    public const string DiscNearWaveTexture="dvd:/sunshine-wave.dndtex";
    public const string DiscDistortionTexture="dvd:/sunshine-distortion.dndtex";
    public const float LodBias=0,SecondaryLodBias=-.5f;
    // Surface: GEQUAL235 XNOR GEQUAL35, hence alpha<35 OR alpha>=235.
    // The separate near-wave pass uses GEQUAL85 OR LEQUAL35.
    public const byte GlintLowReference=35,GlintHighReference=235;
    public const byte NearLowReference=35,NearHighReference=85;

    public static int Edge(int level)
    {
        if((uint)level>=MipLevels)throw new ArgumentOutOfRangeException(nameof(level));
        return TextureSize>>level;
    }

    public static int SourceEdge(int level)=>Edge(level)/2;

    public static byte QuantizeLuma(byte value)=>(byte)(((value+8)/17)*17);

    // I4 expands a four-bit sample to eight bits by repeating its nibble.
    // There is no separately tuned opacity curve or stored alpha channel.
    public static byte AlphaForLuma(int level,byte value)
    {
        if((uint)level>=MipLevels)throw new ArgumentOutOfRangeException(nameof(level));
        return QuantizeLuma(value);
    }

    // Input retains the original 64/32/16/8/4 PNG dimensions. Each independent
    // source is expanded exactly 2x with nearest sampling, then quantized to I4.
    public static byte[] PackLevels(byte[][] levelPixels)
    {
        if(levelPixels==null||levelPixels.Length!=MipLevels)throw new ArgumentException("Expected five authored mip levels.");
        var nativeLevels=new byte[MipLevels][];
        for(int level=0;level<MipLevels;level++){
            int sourceSize=SourceEdge(level);
            var pixels=levelPixels[level];
            if(pixels==null||pixels.Length!=sourceSize*sourceSize)
                throw new ArgumentException("Incorrect authored mip dimensions: "+level);
            nativeLevels[level]=Resize(pixels,sourceSize,sourceSize,Edge(level),Edge(level));
        }
        return PackI4(nativeLevels,TextureSize,TextureSize,PayloadBytes);
    }

    public static void ValidatePackage(byte[] data)
    {
        ValidateI4(data,TextureSize,TextureSize,MipLevels,PayloadBytes);
        int at=32;int[] mean=new int[MipLevels];
        for(int level=0;level<MipLevels;level++){
            int size=Edge(level),total=0,min=255,max=0;
            for(int pixel=0;pixel<size*size;pixel+=2){
                int packed=data[at++],a=(packed>>4)*17,b=(packed&15)*17;
                total+=a+b;min=Math.Min(min,Math.Min(a,b));max=Math.Max(max,Math.Max(a,b));
            }
            mean[level]=total/(size*size);
            if((level==0||level==4)&&max!=0)
                throw new InvalidOperationException("Near/far Sunshine surface levels must be fully clear.");
            if((level==1||level==2)&&max-min<34)
                throw new InvalidOperationException("Sunshine caustic/reflection source needs visible contrast.");
        }
        if(at!=data.Length||!(mean[0]==0&&mean[1]>50&&mean[2]>mean[1]+15&&
           mean[2]>mean[3]*2&&mean[4]==0))
            throw new InvalidOperationException("Sunshine mip levels are not independently authored.");
    }

    // These two textures deliberately do not inherit the surface's clear
    // base/end levels. Their independently resized L1 art remains visible near
    // the camera, and conventional mip reduction is appropriate for each.
    public static byte[] PackNearWave(byte[] source32)=>PackOrdinary(source32,NearWaveWidth,NearWaveHeight,NearWaveMipLevels,NearWavePayloadBytes);
    public static byte[] PackDistortion(byte[] source32)=>PackOrdinary(source32,DistortionSize,DistortionSize,DistortionMipLevels,DistortionPayloadBytes);
    public static void ValidateNearWavePackage(byte[] data)=>ValidateI4(data,NearWaveWidth,NearWaveHeight,NearWaveMipLevels,NearWavePayloadBytes);
    public static void ValidateDistortionPackage(byte[] data)=>ValidateI4(data,DistortionSize,DistortionSize,DistortionMipLevels,DistortionPayloadBytes);

    // Original BTK timing is 1200 frames at 30fps. Endpoint tangents of 1/2400
    // per frame produce this Hermite curve, rather than a constant UV speed.
    public static float SurfacePhase(double seconds)
    {
        double u=Repeat(seconds,40)/40;
        return (float)(-u*u*u+1.5*u*u+.5*u);
    }
    public static float IndirectPhase(double seconds)=>(float)(3*Repeat(seconds,40)/40);
    public static float NearPhase(double seconds)=>(float)Repeat(seconds*.045,1);

    public static float MaxAxisEdge(int x,int z)
    {
        if((uint)x>Cells||(uint)z>Cells)return 0;
        return 1-Math.Max(Math.Abs(x-Cells/2),Math.Abs(z-Cells/2))/(Cells/2f);
    }
    public static byte VertexFade(int x,int z)=>(byte)(255*MaxAxisEdge(x,z));

    // Sunshine's separate small near-field wave overlay uses just two sine
    // components and fades at its edges. The GPU handles all fine caustics.
    public static float LocalWaveHeight(int x,int z,float worldX,float worldZ,double seconds)
    {
        float fade=MaxAxisEdge(x,z);
        double tA=Math.IEEERemainder(seconds*1.13,2*Math.PI);
        double tB=Math.IEEERemainder(seconds*.86,2*Math.PI);
        return .047f+fade*(.024f*(float)Math.Sin(worldX*1.75+tA)+
                             .015f*(float)Math.Sin(worldZ*1.42+tB));
    }

    // Uniform scale of the curved water mesh around its cylinder axis:
    // Pbed = scale*Psurface + (scale-1)*Radius along local Y. As the mesh
    // already sits on Radius + WaterSurface.Height, this produces a constant
    // physical inset for the merged bed vertices baked into the terrain archive.
    public static float BedScale(float depth)
    {
        float radius=CylinderWorld.Radius+WaterSurface.Height;
        if(!float.IsFinite(depth)||depth<=0||depth>=radius)
            throw new ArgumentOutOfRangeException(nameof(depth));
        return (radius-depth)/radius;
    }
    public static float BedAxisOffset(float depth)=>(BedScale(depth)-1)*CylinderWorld.Radius;

    private static double Repeat(double seconds,double period)
    {
        if(!double.IsFinite(seconds))throw new ArgumentOutOfRangeException(nameof(seconds));
        double value=seconds%period;return value<0?value+period:value;
    }

    private static byte[] PackOrdinary(byte[] source,int width,int height,int levels,int payload)
    {
        if(source==null||source.Length!=32*32)throw new ArgumentException("Expected the original 32x32 caustic source.");
        var chain=new byte[levels][];
        chain[0]=Resize(source,32,32,width,height);
        for(int level=1;level<levels;level++)
            chain[level]=Resize(chain[level-1],width>>(level-1),height>>(level-1),width>>level,height>>level);
        return PackI4(chain,width,height,payload);
    }

    private static byte[] Resize(byte[] source,int sw,int sh,int width,int height)
    {
        var result=new byte[width*height];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++){
            int left=x*sw/width,right=Math.Max(left+1,(x+1)*sw/width);
            int top=y*sh/height,bottom=Math.Max(top+1,(y+1)*sh/height),sum=0,count=0;
            for(int sy=top;sy<bottom;sy++)for(int sx=left;sx<right;sx++){sum+=source[sy*sw+sx];count++;}
            result[y*width+x]=(byte)((sum+count/2)/count);
        }
        return result;
    }

    private static byte[] PackI4(byte[][] levels,int width,int height,int payload)
    {
        var data=new byte[32+payload];int at=32;
        Put(data,0,0x58544e44);Put(data,4,2);Put(data,8,0);
        Put(data,12,width);Put(data,16,height);Put(data,20,payload);Put(data,24,levels.Length);
        for(int level=0;level<levels.Length;level++){
            int w=width>>level,h=height>>level;
            for(int by=0;by<h;by+=8)for(int bx=0;bx<w;bx+=8)
                for(int y=by;y<by+8;y++)for(int x=bx;x<bx+8;x+=2)
                    data[at++]=(byte)((QuantizeLuma(levels[level][y*w+x])/17<<4)|QuantizeLuma(levels[level][y*w+x+1])/17);
        }
        if(at!=data.Length)throw new InvalidOperationException("Invalid Sunshine I4 mip payload size.");
        return data;
    }

    private static void ValidateI4(byte[] data,int width,int height,int levels,int payload)
    {
        if(data==null||data.Length!=32+payload||TexturePixels.Int(data,0)!=0x58544e44||
           TexturePixels.Int(data,4)!=2||TexturePixels.Int(data,8)!=0||
           TexturePixels.Int(data,12)!=width||TexturePixels.Int(data,16)!=height||
           TexturePixels.Int(data,20)!=payload||TexturePixels.Int(data,24)!=levels||TexturePixels.Int(data,28)!=0)
            throw new InvalidOperationException("Invalid Sunshine GX I4 mip package.");
        int expected=0;
        for(int level=0;level<levels;level++)expected+=((width>>level)+7)/8*(((height>>level)+7)/8)*32;
        if(expected!=payload)throw new InvalidOperationException("Invalid Sunshine I4 tile dimensions.");
    }

    private static void Put(byte[] data,int at,int value)
    {for(int i=0;i<4;i++)data[at+i]=(byte)(value>>(i*8));}
}
