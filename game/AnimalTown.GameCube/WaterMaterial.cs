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
    private Texture2D? sunshineTexture;
    private TevMaterial? sunshineLayer,sunshineBed,sunshineCloseWaves;
    internal Material CloseWaves=>sunshineCloseWaves?.Material??Surface;
    // A very thin tropical-blue body under the specular cutout, so the seabed
    // stays readable nearby while distant open water keeps its blue depth.
    // This remains a shared material; no new terrain geometry or textures.
    private readonly Material sunshineBody=new Material{
        Blend=BlendMode.Alpha,DepthTest=true,DepthWrite=false,
        Lighting=LightingMode.Unlit,Cull=CullMode.Back,
        Color=new Color32(90,189,215,48)
    };
    internal Material SunshineBody=>sunshineBody;
    internal WaterRenderStyle Style=WaterRenderStyle.WindWaker;
    internal bool IsSunshine=>Style==WaterRenderStyle.Sunshine;
    internal Material Surface=>IsSunshine&&sunshineLayer!=null?sunshineLayer.Material:water.Material;
    internal Material Bed=>sunshineBed?.Material??throw new InvalidOperationException("Sunshine bed material was not prepared.");
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
    // The existing cliff-soil texture doubles as the seafloor. The Sunshine
    // wave texture and TEV effects are allocated only on first selection;
    // neither water style requires changing any archived terrain geometry.
    internal void PrepareSunshine(Texture2D seabedTexture)
    {
        if(sunshineLayer!=null)return;
        if(seabedTexture==null)throw new ArgumentNullException(nameof(seabedTexture));
        Texture2D? wave=null;
        TevMaterial? layer=null,bed=null,close=null;
        try{
            // Read a *pre-authored* GX mip package baked from five different
            // PNGs. Generating/downsampling at runtime would erase the effect.
            var package=Dolphin.IO.File.ReadAllBytes(SunshineWater.DiscTexture);
            SunshineWater.ValidatePackage(package);
            wave=Texture2D.FromPackage(package,linear:true);
            if(wave.MipLevels!=SunshineWater.MipLevels)
                throw new InvalidOperationException("Sunshine GPU mip chain was truncated.");
            // Explicitly enable GX trilinear LOD. Material sampling overrides
            // texture sampler settings, so also configure both material slots
            // with the full 0..4 chain and the same positive hardware LOD bias.
            wave.ConfigureSampler(TextureWrapMode.Repeat,TextureWrapMode.Repeat,
                TextureMinFilter.LinearMipmapLinear,TextureMagFilter.Linear,SunshineWater.LodBias);
            var sparkle=new TevProgramBuilder();
            sparkle.AddColor("PrimaryLight",new Color32(102,150,176));
            sparkle.AddColor("ReflectedSky",new Color32(215,248,244));
            sparkle.AddStage(TevInput.Zero,TevInput.Zero,TevInput.Zero,TevInput.Raster);
            // Add both independent scrolling samples, rather than *blending*
            // two samples into a mostly unchanging base tint.
            sparkle.AddStage(TevInput.Zero,TevInput.Constant,TevInput.Texture,TevInput.Previous,colorSlot:0);
            sparkle.AddStage(TevInput.Zero,TevInput.Constant,TevInput.SecondaryTexture,TevInput.Previous,colorSlot:1);
            var glintDefinition=new TevMaterialDefinition(sparkle);
            layer=glintDefinition.Create(wave,wave);
            layer.Material.Blend=BlendMode.Alpha;
            layer.Material.AlphaCutoff=SunshineWater.GlintAlphaCutoff;
            layer.Material.DepthWrite=false;
            layer.Material.DepthTest=true;
            layer.Material.Cull=CullMode.Back;
            layer.Material.Color=new Color32(205,246,249,255);
            var mip=new SamplerState(TextureMinFilter.LinearMipmapLinear,
                TextureMagFilter.Linear,minLod:0,maxLod:4,lodBias:SunshineWater.LodBias,
                u:TextureAddressMode.Repeat,v:TextureAddressMode.Repeat);
            layer.Material.Sampler=mip;
            layer.Material.SecondarySampler=mip;
            // The original Sunshine also has a separate player-centered wave
            // overlay using its own texture. Share our authored 32px caustic
            // mip instead: a dedicated material fixes both hardware samplers
            // on level 1 even right by the camera, adding no resident image.
            close=glintDefinition.Create(wave,wave);
            close.Material.Blend=BlendMode.Alpha;
            close.Material.AlphaCutoff=.34f;
            close.Material.DepthWrite=false;
            close.Material.DepthTest=true;
            close.Material.Cull=CullMode.Back;
            close.Material.Color=new Color32(214,249,243,206);
            var closeMip=new SamplerState(TextureMinFilter.LinearMipmapNearest,
                TextureMagFilter.Linear,minLod:1,maxLod:1,lodBias:3.9f,
                u:TextureAddressMode.Repeat,v:TextureAddressMode.Repeat);
            close.Material.Sampler=closeMip;
            close.Material.SecondarySampler=closeMip;

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

            sunshineTexture=wave;sunshineLayer=layer;sunshineCloseWaves=close;sunshineBed=bed;
        }catch{
            bed?.Dispose();close?.Dispose();layer?.Dispose();wave?.Dispose();
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
        rippleMotion.Apply(water.Material,seconds);
        baseMotion.Apply(water.Material,seconds,true);
        Foam.Color=new Color32(220,241,235,(byte)(70+14*Math.Sin(seconds*1.2)));
        Foam.TextureTransform=new UvTransform(new Vector2(WaterSurface.Scroll(seconds,.032f),WaterSurface.Scroll(seconds,-.058f)),new Vector2(1.10f,1.10f));
        if(IsSunshine&&sunshineLayer!=null){
            // Distinct scroll vectors create Sunshine-style light interference.
            // Both GX texgens sample the same small native mipmapped texture.
            var water=sunshineLayer.Material;
            water.TextureTransform=new UvTransform(new Vector2(
                WaterSurface.Scroll(seconds,.017f),WaterSurface.Scroll(seconds,.044f)),new Vector2(1,1));
            // Sunshine's second copy scrolls primarily in one axis. Keeping
            // identical UV scale generates the shifting moire/caustic pattern.
            water.SecondaryTextureTransform=new UvTransform(new Vector2(
                0,WaterSurface.Scroll(seconds,-.067f)),new Vector2(1,1));
            if(sunshineCloseWaves!=null){
                var close=sunshineCloseWaves.Material;
                close.TextureTransform=water.TextureTransform;
                close.SecondaryTextureTransform=water.SecondaryTextureTransform;
            }
        }
        if(RoughOcean&&!IsSunshine&&storm!=null){
            storm.Material.TextureTransform=new UvTransform(new Vector2(WaterSurface.Scroll(seconds,.035f),WaterSurface.Scroll(seconds,.075f)),new Vector2(1,1));
            storm.Material.SecondaryTextureTransform=new UvTransform(new Vector2(WaterSurface.Scroll(seconds,-.055f),WaterSurface.Scroll(seconds,.12f)),new Vector2(1.05f,.85f));
        }
    }
    public void Dispose(){
        sunshineBed?.Dispose();sunshineCloseWaves?.Dispose();sunshineLayer?.Dispose();sunshineTexture?.Dispose();
        storm?.Dispose();water.Dispose();
        whitecaps?.Dispose();stormBase?.Dispose();distortion.Dispose();shoreFoam.Dispose();ripples.Dispose();
    }
}
