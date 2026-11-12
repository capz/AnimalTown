using System;
using AnimalTown;
using Dolphin.Graphics;
using Hypercube.GameCube;

// Own one per water style and share it between river, pond and sea meshes.
// Textures/effect are created outside frames; animation only changes UV matrices.
internal sealed class WaterMaterial : IDisposable
{
    private readonly Texture2D ripples,shoreFoam,distortion;
    private readonly TevMaterial water;
    private Texture2D? stormBase,whitecaps;
    private TevMaterial? storm;
    private Texture2D? sunshineTexture,sunshineGlintTexture;
    private TevMaterial? sunshineLayer;
    // Color/depth film and alpha-tested glint interference are separately
    // submitted. Neither pass modifies the stored water mesh or its tint.
    private readonly Material sunshineFilm=new Material{
        Blend=BlendMode.Alpha,DepthTest=true,DepthWrite=false,
        Lighting=LightingMode.Unlit,Cull=CullMode.Back,
        Color=new Color32(211,246,247,105)
    };
    internal WaterRenderStyle Style=WaterRenderStyle.WindWaker;
    internal bool IsSunshine=>Style==WaterRenderStyle.Sunshine;
    internal Material Surface=>IsSunshine&&sunshineLayer!=null?sunshineFilm:water.Material;
    internal Material SunshineGlints=>sunshineLayer?.Material??throw new InvalidOperationException("Sunshine glints were not prepared.");
    internal readonly Material Bed=CreateBedMaterial();
    internal static Material CreateBedMaterial()=>new Material{
        Color=new Color32(18,48,82),Blend=BlendMode.Opaque,DepthWrite=true,
        DepthTest=true,Lighting=LightingMode.Unlit,Cull=CullMode.Back};
    internal bool RoughOcean;
    internal Material OceanSurface=>IsSunshine?Surface:RoughOcean?storm!.Material:Surface;
    internal readonly Material Foam=new Material{Blend=BlendMode.Alpha,DepthWrite=false,Lighting=LightingMode.Unlit,Cull=CullMode.Back};
    internal Color32 Shallow=new Color32(99,181,196),Deep=new Color32(75,154,187);
    internal Vector2 BaseSpeed=new Vector2(.012f,.025f),RippleSpeed=new Vector2(-.018f,.035f);
    private readonly ScrollingTextureLayer baseMotion=new ScrollingTextureLayer(new Vector2(.012f,.025f),new Vector2(1,1)),rippleMotion=new ScrollingTextureLayer(new Vector2(-.018f,.035f),new Vector2(1.35f,1.35f));
    internal bool FoamEnabled=true;
    internal WaterMaterial()
    {
        ripples=Texture2D.CreateNative(64,64,NativeTextureFormat.I8,true);ripples.ReplaceNative(WaterSurface.Texture(true));
        distortion=Texture2D.CreateNative(WaterSurface.TextureSize,WaterSurface.TextureSize,NativeTextureFormat.Rgb565,true);
        distortion.ReplaceNative(WaterSurface.WarpTexture());
        water=WindWakerDefinition().Create(ripples,distortion);
        water.Material.Blend=BlendMode.Opaque;water.Material.DepthWrite=true;
        water.Material.Cull=CullMode.Back;
        water.Material.Sampler=new SamplerState(TextureAddressMode.Repeat,TextureAddressMode.Repeat,TextureFilter.Linear);
        water.Material.SecondarySampler=water.Material.Sampler;
        // Keep shoreline geometry archived. Alpha movement lives entirely in a
        // shared IA8 mask and its GX UV matrix, not dynamic mesh uploads.
        shoreFoam=Texture2D.CreateNative(WaterSurface.TextureSize,WaterSurface.TextureSize,NativeTextureFormat.Ia8,true);
        shoreFoam.ReplaceNative(WaterSurface.ShoreTexture());
        Foam.Texture=shoreFoam;
        Foam.Sampler=new SamplerState(TextureAddressMode.Repeat,TextureAddressMode.Repeat,TextureFilter.Linear);
        Animate(0);
    }
    // The public SDK preset owns the validated DNEF v3 program. The second
    // texture is an indirect UV map; calm water never changes mesh vertices.
    internal static TevMaterialDefinition WindWakerDefinition()=>TevMaterialPresets.WaterWarp(
        new Color32(196,228,222),new Color32(229,245,237));
    // Called between frames. Avoid retaining the old two-texture storm shader
    // and its 8 KiB of image data until rough weather is actually selected.
    internal void PrepareRoughOcean()
    {
        if(!RoughOcean||IsSunshine||storm!=null)return;
        Texture2D? first=null,second=null;TevMaterial? effect=null;
        try{
            first=Texture2D.CreateNative(64,64,NativeTextureFormat.I8,true);first.ReplaceNative(WaterSurface.Texture(false));
            second=Texture2D.CreateNative(64,64,NativeTextureFormat.I8,true);second.ReplaceNative(WaterSurface.StormTexture());
            var waves=new TevProgramBuilder();waves.AddColor("Whitecaps",new Color32(231,247,244));
            waves.AddStage(TevInput.Zero,TevInput.Raster,TevInput.Texture,TevInput.Zero);
            waves.AddStage(TevInput.Previous,TevInput.Constant,TevInput.SecondaryTexture,TevInput.Zero,colorSlot:0);
            effect=new TevMaterialDefinition(waves).Create(first,second);
            var material=effect.Material;material.Blend=BlendMode.Opaque;material.DepthWrite=true;material.Cull=CullMode.Back;
            material.Sampler=material.SecondarySampler=water.Material.Sampler;
        }catch{effect?.Dispose();second?.Dispose();first?.Dispose();throw;}
        stormBase=first;whitecaps=second;storm=effect;
    }
    // The dark-blue bed uses the normal untextured material path.
    internal void PrepareSunshine()
    {
        if(sunshineLayer!=null)return;
        Texture2D? wave=null,reflection=null;
        TevMaterial? layer=null;
        try{
            // TEX0 provides the sparse alpha-tested reflection mask; TEX1
            // adds a separate, smoother scrolling water-ripple pattern.
            wave=Texture2D.FromPackage(SunshineWater.GlintTexturePackage(),linear:true);
            reflection=Texture2D.FromPackage(SunshineWater.TexturePackage(),linear:true);
            // Both authored mip chains must reach GX's LOD sampler. The old
            // material selected a linear base level, losing the reflection band.
            wave.ConfigureSampler(TextureWrapMode.Repeat,TextureWrapMode.Repeat,
                TextureMinFilter.LinearMipmapLinear,TextureMagFilter.Linear,SunshineWater.ReflectionLodBias);
            reflection.ConfigureSampler(TextureWrapMode.Repeat,TextureWrapMode.Repeat,
                TextureMinFilter.LinearMipmapLinear,TextureMagFilter.Linear,SunshineWater.RippleLodBias);
            // The locked SDK supports two distinct RGB/UV stages (DNEF v2)
            // but not yet v4's independent TEX1 alpha. An alpha cutoff on
            // TEX0 masks this overlay, while the separate translucent film
            // below remains visible everywhere. It preserves visual layering
            // without an unpublished SDK dependency.
            layer=SunshineGlintDefinition().Create(wave,reflection);
            layer.Material.Blend=BlendMode.Alpha;
            layer.Material.DepthWrite=false;
            layer.Material.DepthTest=true;
            layer.Material.Cull=CullMode.Back;
            layer.Material.AlphaCutoff=SunshineWater.GlintAlphaCutoff;
            // Vertex color supplies the existing seasonal water tint. Keep
            // raster alpha opaque so the primary glint-cutout texture's alpha
            // remains the effective per-fragment cutoff input.
            layer.Material.Color=new Color32(225,246,242,255);
            layer.Material.Sampler=new SamplerState(
                TextureMinFilter.LinearMipmapLinear,TextureMagFilter.Linear,
                maxLod:SunshineWater.MipLevels-1,lodBias:SunshineWater.ReflectionLodBias,
                u:TextureAddressMode.Repeat,v:TextureAddressMode.Repeat);
            layer.Material.SecondarySampler=new SamplerState(
                TextureMinFilter.LinearMipmapLinear,TextureMagFilter.Linear,
                maxLod:SunshineWater.MipLevels-1,lodBias:SunshineWater.RippleLodBias,
                u:TextureAddressMode.Repeat,v:TextureAddressMode.Repeat);

            sunshineTexture=wave;sunshineGlintTexture=reflection;sunshineLayer=layer;
        }catch{
            layer?.Dispose();reflection?.Dispose();wave?.Dispose();
            throw;
        }
    }
    // DNEF v2 keeps the two independent animated RGB textures and a final
    // TEX0 opacity resolve. The separate color film is the translucent base.
    internal static TevMaterialDefinition SunshineGlintDefinition()
    {
        var p=new TevProgramBuilder();
        p.AddColor("PrimaryLight",new Color32(212,247,245));
        p.AddColor("SecondaryLight",new Color32(82,134,142));
        p.AddStage(TevInput.Zero,TevInput.Zero,TevInput.Zero,TevInput.Raster);
        p.AddStage(TevInput.Zero,TevInput.Constant,TevInput.Texture,TevInput.Previous,colorSlot:0);
        p.AddStage(TevInput.Zero,TevInput.Constant,TevInput.SecondaryTexture,TevInput.Previous,colorSlot:1);
        return new TevMaterialDefinition(p);
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
        rippleMotion.Apply(water.Material,seconds);
        baseMotion.Apply(water.Material,seconds,true);
        Foam.Color=new Color32(220,241,235,(byte)(70+14*Math.Sin(seconds*1.2)));
        Foam.TextureTransform=new UvTransform(new Vector2(WaterSurface.Scroll(seconds,.032f),WaterSurface.Scroll(seconds,-.058f)),new Vector2(1.10f,1.10f));
        if(IsSunshine&&sunshineLayer!=null){
            // Two independently authored images and UV transforms interfere
            // without modifying either texture or terrain vertices.
            var water=sunshineLayer.Material;
            water.TextureTransform=new UvTransform(new Vector2(
                WaterSurface.Scroll(seconds,.021f),WaterSurface.Scroll(seconds,.013f)),new Vector2(1,1));
            water.SecondaryTextureTransform=new UvTransform(new Vector2(
                WaterSurface.Scroll(seconds,-.017f),WaterSurface.Scroll(seconds,.032f)),new Vector2(1.45f,.92f));
        }
        if(RoughOcean&&!IsSunshine&&storm!=null){
            storm.Material.TextureTransform=new UvTransform(new Vector2(WaterSurface.Scroll(seconds,.035f),WaterSurface.Scroll(seconds,.075f)),new Vector2(1,1));
            storm.Material.SecondaryTextureTransform=new UvTransform(new Vector2(WaterSurface.Scroll(seconds,-.055f),WaterSurface.Scroll(seconds,.12f)),new Vector2(1.05f,.85f));
        }
    }
    public void Dispose(){
        sunshineLayer?.Dispose();
        sunshineGlintTexture?.Dispose();sunshineTexture?.Dispose();
        storm?.Dispose();water.Dispose();
        whitecaps?.Dispose();stormBase?.Dispose();distortion.Dispose();shoreFoam.Dispose();ripples.Dispose();
    }
}
