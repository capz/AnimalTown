using System;
using AnimalTown;
using Dolphin.Graphics;
using Hypercube.GameCube;

// Own one per water style and share it between river, pond and sea meshes.
// Textures/effect are created outside frames; animation only changes UV matrices.
internal sealed class WaterMaterial : IDisposable
{
    private readonly Texture2D baseTexture,ripples,whitecaps,shoreFoam;
    private readonly TevMaterial tev,storm;
    private readonly Material plainSurface;
    private readonly Material? warpedSurface;
    private readonly GxEffect? warpedEffect;
    private readonly Texture2D? distortion;
    private Texture2D? sunshineTexture;
    private TevMaterial? sunshineLayer,sunshineBed;
    internal WaterRenderStyle Style=WaterRenderStyle.WindWaker;
    internal bool IsSunshine=>Style==WaterRenderStyle.Sunshine;
    internal bool IndirectWarpAvailable=>warpedSurface!=null;
    internal Material Surface=>IsSunshine&&sunshineLayer!=null?sunshineLayer.Material:warpedSurface??plainSurface;
    internal Material Bed=>sunshineBed?.Material??throw new InvalidOperationException("Sunshine bed material was not prepared.");
    internal readonly Material Storm;
    internal bool RoughOcean;
    internal Material OceanSurface=>IsSunshine?Surface:RoughOcean?Storm:Surface;
    internal readonly Material Foam=new Material{Blend=BlendMode.Alpha,DepthWrite=false,Lighting=LightingMode.Unlit,Cull=CullMode.Back};
    internal Color32 Shallow=new Color32(99,181,196),Deep=new Color32(75,154,187);
    internal Vector2 BaseSpeed=new Vector2(.012f,.025f),RippleSpeed=new Vector2(-.018f,.035f);
    private readonly ScrollingTextureLayer baseMotion=new ScrollingTextureLayer(new Vector2(.012f,.025f),new Vector2(1,1)),rippleMotion=new ScrollingTextureLayer(new Vector2(-.018f,.035f),new Vector2(1.35f,1.35f));
    internal bool FoamEnabled=true;
    internal WaterMaterial()
    {
        baseTexture=Texture2D.CreateNative(64,64,NativeTextureFormat.I8,true);baseTexture.ReplaceNative(WaterSurface.Texture(false));
        ripples=Texture2D.CreateNative(64,64,NativeTextureFormat.I8,true);ripples.ReplaceNative(WaterSurface.Texture(true));
        var program=new TevProgramBuilder();program.AddColor("Highlight",new Color32(196,228,222));
        program.AddStage(TevInput.Zero,TevInput.Raster,TevInput.Texture,TevInput.Zero);
        program.AddStage(TevInput.Previous,TevInput.Constant,TevInput.SecondaryTexture,TevInput.Zero,colorSlot:0);
        tev=new TevMaterialDefinition(program).Create(baseTexture,ripples);plainSurface=tev.Material;
        plainSurface.Blend=BlendMode.Opaque;plainSurface.DepthWrite=true;
        plainSurface.Sampler=new SamplerState(TextureAddressMode.Repeat,TextureAddressMode.Repeat,TextureFilter.Linear);
        plainSurface.SecondarySampler=plainSurface.Sampler;
        // Keep shoreline geometry archived. Alpha movement lives entirely in a
        // shared IA8 mask and its GX UV matrix, not dynamic mesh uploads.
        shoreFoam=Texture2D.CreateNative(WaterSurface.TextureSize,WaterSurface.TextureSize,NativeTextureFormat.Ia8,true);
        shoreFoam.ReplaceNative(WaterSurface.ShoreTexture());
        Foam.Texture=shoreFoam;
        Foam.Sampler=new SamplerState(TextureAddressMode.Repeat,TextureAddressMode.Repeat,TextureFilter.Linear);
        whitecaps=Texture2D.CreateNative(64,64,NativeTextureFormat.I8,true);whitecaps.ReplaceNative(WaterSurface.StormTexture());
        var waves=new TevProgramBuilder();waves.AddColor("Whitecaps",new Color32(231,247,244));
        waves.AddStage(TevInput.Zero,TevInput.Raster,TevInput.Texture,TevInput.Zero);
        waves.AddStage(TevInput.Previous,TevInput.Constant,TevInput.SecondaryTexture,TevInput.Zero,colorSlot:0);
        storm=new TevMaterialDefinition(waves).Create(baseTexture,whitecaps);Storm=storm.Material;
        Storm.Blend=BlendMode.Opaque;Storm.DepthWrite=true;Storm.Cull=CullMode.Back;
        Storm.Sampler=Storm.SecondarySampler=plainSurface.Sampler;
        // The pinned SDK's native DNEF v2 rejects v3. Probing the immutable
        // package once before the first frame is safe: keep the proven TEV v2
        // material as an exact fallback until Hypercube can use DNEF v3.
        try{
            warpedEffect=GxEffect.FromPackage(WindWakerWaterEffect.Package(196,228,222,229,245,237));
        }catch(ArgumentException){
            warpedEffect=null;
        }
        if(warpedEffect!=null){
            distortion=Texture2D.CreateNative(WaterSurface.TextureSize,WaterSurface.TextureSize,NativeTextureFormat.Rgb565,true);
            distortion.ReplaceNative(WaterSurface.WarpTexture());
            warpedSurface=new Material{
                Effect=warpedEffect,Texture=ripples,SecondaryTexture=distortion,
                Blend=BlendMode.Opaque,DepthWrite=true,Cull=CullMode.Back,Lighting=LightingMode.Unlit,
                Sampler=plainSurface.Sampler,SecondarySampler=plainSurface.Sampler
            };
        }
        Animate(0);
    }
    // The existing cliff-soil texture doubles as the seafloor. The Sunshine
    // wave texture and TEV effects are allocated only on first selection;
    // neither water style requires changing any archived terrain geometry.
    internal void PrepareSunshine(Texture2D seabedTexture)
    {
        if(sunshineLayer!=null)return;
        if(seabedTexture==null)throw new ArgumentNullException(nameof(seabedTexture));
        Texture2D? wave=null;
        TevMaterial? layer=null,bed=null;
        try{
            wave=Texture2D.FromPackage(SunshineWater.TexturePackage(),linear:true);
            var sparkle=new TevProgramBuilder();
            sparkle.AddColor("Glint",new Color32(168,244,232));
            sparkle.AddColor("Glint2",new Color32(232,255,248));
            sparkle.AddStage(TevInput.Zero,TevInput.Zero,TevInput.Zero,TevInput.Raster);
            sparkle.AddStage(TevInput.Previous,TevInput.Constant,TevInput.Texture,TevInput.Zero,colorSlot:0);
            sparkle.AddStage(TevInput.Previous,TevInput.Constant,TevInput.SecondaryTexture,TevInput.Zero,colorSlot:1);
            // Two independently scrolling taps of one authored-mip IA8 texture,
            // with translucent GX alpha and no water depth write.
            layer=new TevMaterialDefinition(sparkle).Create(wave,wave);
            layer.Material.Blend=BlendMode.Alpha;
            layer.Material.DepthWrite=false;
            layer.Material.DepthTest=true;
            layer.Material.Cull=CullMode.Back;
            layer.Material.Color=new Color32(216,255,248,242);
            layer.Material.Sampler=new SamplerState(TextureAddressMode.Repeat,TextureAddressMode.Repeat,TextureFilter.Linear);
            layer.Material.SecondarySampler=layer.Material.Sampler;

            var sand=new TevProgramBuilder();
            sand.AddColor("Sand",new Color32(239,225,189));
            // Ignore blue vertex tints; underwater sediment has its own palette.
            sand.AddStage(TevInput.Zero,TevInput.Constant,TevInput.Texture,TevInput.Zero,colorSlot:0);
            bed=new TevMaterialDefinition(sand).Create(seabedTexture);
            bed.Material.Blend=BlendMode.Opaque;
            bed.Material.DepthWrite=true;
            bed.Material.DepthTest=true;
            bed.Material.Cull=CullMode.Back;
            bed.Material.Sampler=new SamplerState(TextureAddressMode.Repeat,TextureAddressMode.Repeat,TextureFilter.Linear);

            sunshineTexture=wave;sunshineLayer=layer;sunshineBed=bed;
        }catch{
            bed?.Dispose();layer?.Dispose();wave?.Dispose();
            throw;
        }
    }
    internal Color32 Tint(float depth)=>new Color32((byte)(Shallow.R+(Deep.R-Shallow.R)*depth),(byte)(Shallow.G+(Deep.G-Shallow.G)*depth),(byte)(Shallow.B+(Deep.B-Shallow.B)*depth));
    internal void SetTerrainTint(uint rgba)
    {
        Deep=new Color32((byte)(rgba>>24),(byte)(rgba>>16),(byte)(rgba>>8));
        Shallow=new Color32((byte)Math.Min(255,Deep.R*99/75),(byte)Math.Min(255,Deep.G*181/154),(byte)Math.Min(255,Deep.B*196/187));
    }
    internal void Animate(double seconds)
    {
        baseMotion.RepeatsPerSecond=BaseSpeed;rippleMotion.RepeatsPerSecond=RippleSpeed;
        baseMotion.Apply(plainSurface,seconds);rippleMotion.Apply(plainSurface,seconds,true);
        if(warpedSurface!=null){
            rippleMotion.Apply(warpedSurface,seconds);
            baseMotion.Apply(warpedSurface,seconds,true);
        }
        Foam.Color=new Color32(220,241,235,(byte)(70+14*Math.Sin(seconds*1.2)));
        Foam.TextureTransform=new UvTransform(new Vector2(WaterSurface.Scroll(seconds,.032f),WaterSurface.Scroll(seconds,-.058f)),new Vector2(1.10f,1.10f));
        if(IsSunshine&&sunshineLayer!=null){
            // Distinct scroll vectors create Sunshine-style light interference.
            // Both GX texgens sample the same small native mipmapped texture.
            var water=sunshineLayer.Material;
            water.TextureTransform=new UvTransform(new Vector2(
                WaterSurface.Scroll(seconds,.021f),WaterSurface.Scroll(seconds,.013f)),new Vector2(1,1));
            water.SecondaryTextureTransform=new UvTransform(new Vector2(
                WaterSurface.Scroll(seconds,-.017f),WaterSurface.Scroll(seconds,.032f)),new Vector2(1.45f,.92f));
        }
        if(RoughOcean&&!IsSunshine){
            Storm.TextureTransform=new UvTransform(new Vector2(WaterSurface.Scroll(seconds,.035f),WaterSurface.Scroll(seconds,.075f)),new Vector2(1,1));
            Storm.SecondaryTextureTransform=new UvTransform(new Vector2(WaterSurface.Scroll(seconds,-.055f),WaterSurface.Scroll(seconds,.12f)),new Vector2(1.05f,.85f));
        }
    }
    public void Dispose(){
        sunshineBed?.Dispose();sunshineLayer?.Dispose();sunshineTexture?.Dispose();
        warpedEffect?.Dispose();storm.Dispose();tev.Dispose();
        distortion?.Dispose();whitecaps.Dispose();shoreFoam.Dispose();ripples.Dispose();baseTexture.Dispose();
    }
}
