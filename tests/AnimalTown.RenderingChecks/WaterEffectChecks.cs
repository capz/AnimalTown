using System.IO;
using AnimalTown;
using Dolphin.Graphics;

internal static class WaterEffectChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var bed=WaterMaterial.CreateBedMaterial();
        check(bed.Texture==null&&bed.SecondaryTexture==null,"underwater bed binds no sand or other texture");
        check(bed.Color.R==18&&bed.Color.G==48&&bed.Color.B==82&&bed.Color.A==255,
            "underwater bed has a uniform opaque dark-blue tint");
        check(bed.Blend==Dolphin.Graphics.BlendMode.Opaque&&bed.DepthWrite&&bed.DepthTest&&bed.Lighting==Dolphin.Graphics.LightingMode.Unlit,
            "solid bed writes depth without lighting or translucency changing its tint");
        var indirect=WaterMaterial.WindWakerDefinition().GetPackage();
        check(indirect.Length==208&&indirect[4]==3&&indirect[8]==4&&indirect[12]==3&&indirect[20]==76,
            "Wind Waker keeps its original bounded v3 indirect material");
        check(indirect[191]==0&&indirect[199]==2&&indirect[207]==0,
            "Wind Waker still selects alternate UV then resolves base opacity");
        var sea=WaterMaterial.SunshineDefinition().GetPackage();
        var wave=WaterMaterial.SunshineWaveDefinition().GetPackage();
        check(sea[4]==5&&sea[8]==2&&sea[12]==3&&sea.Length==208,
            "Sea reproduces the two-stage program without an opacity epilogue");
        check(sea[21]==1&&sea[24]==6&&sea[25]==235&&sea[26]==3&&sea[27]==6&&sea[28]==35,
            "Sea uses GE235 XNOR GE35, including the original exclusive low boundary");
        check(sea[22]==1&&sea[29]==4&&sea[30]==1,"Sea adds source-alpha-scaled color to its background");
        check(wave[8]==2&&wave[24]==6&&wave[25]==85&&wave[26]==1&&wave[27]==3&&wave[28]==35&&
            wave[29]==4&&wave[30]==2,"Near waves have their own compare and SrcAlpha/SrcColor blend");
        var color=Rgb(sea);
        check(color[0]==203&&color[1]==200&&color[2]==248,"Sea emits wrapped signed-wide RGB, not saturated white");
        for(int first=0;first<256;first++)for(int second=0;second<256;second++){
            int product=((2*second*(first+(first>>7)))+128)>>8;
            int actual=Alpha(sea,first,second,255);
            check(actual==(product&255),"Sea alpha matches the recorded two-sample fixed-point product, including overflow");
            check(Compare(sea,actual)==(actual<35||actual>=235),"Exact original sea cutout boundaries");
            int near=Alpha(wave,first,second,255);
            check(near==Math.Min(255,product),"Near waves clamp their doubled product instead of wrapping");
        }
        check(Alpha(sea,255,128,255)==0&&Alpha(sea,255,255,255)==254,
            "Overflow cases distinguish original arithmetic from average or saturated alpha");
        foreach(int fade in new[]{0,1,32,127,255}){
            check(Alpha(wave,255,255,fade)==Math.Min(255,(2*255*(fade+(fade>>7))+128)>>8),"Near-wave raster fade participates in alpha");
        }
        foreach(bool near in new[]{false,true}){
            var material=new Material();WaterMaterial.ConfigureSunshine(material,near);
            check(material.Color.A==255&&material.AlphaCutoff==0&&material.Cull==CullMode.None,
                "Material retains the reference CullNone declaration and effect-owned comparison");
            check(material.Blend==BlendMode.Alpha&&material.DepthTest&&!material.DepthWrite,
                "Transparent classification preserves depth testing without water depth writes");
            check(material.Sampler.MinFilter==TextureMinFilter.LinearMipmapLinear&&
                material.Sampler.MinLod==0&&material.Sampler.MaxLod==4&&material.Sampler.LodBias==0&&
                material.SecondarySampler.LodBias==(near?0:-.5f),"Each sampler retains its recorded LOD bias and full authored chain");
        }
        var refract=WaterMaterial.SunshineRefractionDefinition();var rp=refract.GetPackage();
        check(refract.TextureCoordinates==TextureCoordinateMode.ScreenProjection&&rp[8]==1&&rp[12]==3&&rp[23]==1&&rp[31]==7,
            "Refraction uses projected capture coordinates plus the original signed STU matrix");
        int matrix=176+rp[8]*16;
        check(BitConverter.ToSingle(rp,matrix+16)==.001f&&BitConverter.ToSingle(rp,matrix+20)==.015f&&rp[matrix+24]==2,
            "Refraction retains both matrix coefficients and exponent two");
        CheckIdleInteraction(check);
    }
    private static void CheckIdleInteraction(Action<bool,string> check)
    {
        // Source-level contract for the native TownGame handler. The full
        // GameCube assembly is independently compiled by the same CI workflow.
        DirectoryInfo? dir=new DirectoryInfo(AppContext.BaseDirectory);
        const string filename="game/AnimalTown.GameCube/Program.cs";
        while(dir!=null&&!File.Exists(Path.Combine(dir.FullName,filename)))dir=dir.Parent;
        if(dir==null)throw new Exception("Could not locate TownGame interaction source");
        string source=File.ReadAllText(Path.Combine(dir.FullName,filename));
        int start=source.IndexOf("private void Interact()",StringComparison.Ordinal);
        int end=source.IndexOf("internal void Step(",start,StringComparison.Ordinal);
        check(start>=0&&end>start,"GameCube player interaction handler is still available");
        string action=source.Substring(start,end-start);
        check(!action.Contains("A quiet moment")&&!action.Contains("A at the doorway to leave"),
            "empty-space A press has no default outdoor or indoor chat bubble");
        check(action.Contains("if(tick%3==0&&state.Receive(15))Say")&&
              !action.Contains("No butterfly in reach"),
            "bug-net misses are silent without removing successful catches");
        check(action.Contains("Say(v.Name,state.Talk(i))")&&
              action.Contains("Say(\"Backpack\",\"Picked up!\")"),
            "genuine villager conversation and item-pickup feedback remain enabled");
    }
    // Independent GX integer oracle: A/B/C wrap, D stays wide, and scaling
    // precedes interpolation truncation. Final output keeps the low byte.
    private static int Combine(int a,int b,int c,int d,int op,int control)
    {
        a&=255;b&=255;c&=255;c+=c>>7;
        int bias=(control>>2)&3,scale=(control>>4)&3;
        d+=bias==1?128:bias==2?-128:0;
        int shift=scale==1?1:scale==2?2:0;
        int lerp=((a<<8)+(b-a)*c)<<shift;
        int term=(lerp+(scale==3?0:op==0?128:127))>>8;
        int value=(d<<shift)+(op==0?term:-term);
        if(scale==3)value>>=1;
        return (control&64)!=0?Math.Clamp(value,0,255):Math.Clamp(value,-1024,1023);
    }
    private static int Alpha(byte[] package,int first,int second,int raster)
    {
        var r=new int[4];
        for(int stage=0;stage<package[8];stage++){
            int at=176+stage*16+8,control=package[at+5];
            int Read(int v)=>v<=3?r[v]:v==4?(package[at-1]==1?second:first):v==5?raster:v==6?255:0;
            r[control&3]=Combine(Read(package[at]),Read(package[at+1]),Read(package[at+2]),Read(package[at+3]),package[at+4],control);
        }
        return r[0]&255;
    }
    private static int[] Rgb(byte[] p)
    {
        var output=new int[3];
        for(int ch=0;ch<3;ch++)for(int stage=0;stage<p[8];stage++){
            int at=176+stage*16,control=p[at+5];
            if((control&128)==0)control|=64;
            int Read(int v)=>v==0?output[ch]:v==14?p[64+(p[at+6]-12)*36+ch]:v==12?255:0;
            output[ch]=Combine(Read(p[at]),Read(p[at+1]),Read(p[at+2]),Read(p[at+3]),p[at+4],control);
        }
        for(int ch=0;ch<3;ch++)output[ch]&=255;return output;
    }
    private static bool Compare(byte[] p,int alpha)=>(alpha>=p[25])==(alpha>=p[28]);
}
