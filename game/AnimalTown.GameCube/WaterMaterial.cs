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
    private Texture2D? sunshineTexture,sunshineWaveTexture,sunshineDistortion;
    private SceneCapture? sunshineCapture;
    private TevMaterial? sunshineRefraction;
    private string preparationFailure="";
    private Viewport captureSource;
    private TevMaterial? sunshineLayer,sunshineCloseWaves;
    internal Material CloseWaves=>sunshineCloseWaves?.Material??Surface;
    // A dark translucent body under the specular cutout gives the layered
    // surface depth while keeping the seabed readable nearby.
    // This remains a shared material; no new terrain geometry or textures.
    private readonly Material sunshineBody=new Material{
        Blend=BlendMode.Alpha,DepthTest=true,DepthWrite=false,
        Lighting=LightingMode.Unlit,Cull=CullMode.Back,
        Color=new Color32(18,48,82,96)
    };
    internal Material SunshineBody=>sunshineBody;
    internal WaterRenderStyle Style=WaterRenderStyle.WindWaker;
    internal bool IsSunshine=>Style==WaterRenderStyle.Sunshine;
    internal Material Surface=>IsSunshine&&sunshineLayer!=null?sunshineLayer.Material:water.Material;
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
    // Numeric GX state from Plaza sea.bmd:_umi. Source art remains our own.
    // Two stages, including signed-wide color/alpha output and GX byte wrap.
    internal static TevMaterialDefinition SunshineDefinition()
    {
        var p=new TevProgramBuilder();
        p.AddColor("Sea0",new Color32(252,255,255));p.AddColor("Sea1",new Color32(232,228,252));
        p.AddStage(TevInput.Zero,TevInput.Zero,TevInput.Zero,TevInput.Constant,colorSlot:0);
        p.SetAlphaStage(0,TevAlphaInput.Zero,TevAlphaInput.Texture,TevAlphaInput.Raster,TevAlphaInput.Zero);
        p.AddStage(TevInput.Zero,TevInput.Constant,TevInput.Previous,TevInput.Zero,
            colorSlot:1,scale:TevScale.Two,clamp:false);
        p.SetAlphaStage(1,TevAlphaInput.Zero,TevAlphaInput.SecondaryTexture,TevAlphaInput.Previous,
            TevAlphaInput.Zero,scale:TevScale.Two,clamp:false);
        p.SetAlphaCompare(AlphaComparison.GreaterOrEqual,235,AlphaCompareOperation.Xnor,AlphaComparison.GreaterOrEqual,35);
        p.SetBlendFactors(TevBlendFactor.SourceAlpha,TevBlendFactor.One);
        return new TevMaterialDefinition(p);
    }
    // TMapObjWave::initDraw is a distinct program, not the sea's mip material.
    internal static TevMaterialDefinition SunshineWaveDefinition()
    {
        var p=new TevProgramBuilder();
        p.AddStage(TevInput.Zero,TevInput.Zero,TevInput.Zero,TevInput.Zero);
        p.SetAlphaStage(0,TevAlphaInput.Zero,TevAlphaInput.Texture,TevAlphaInput.Raster,TevAlphaInput.Zero);
        p.AddStage(TevInput.Raster,TevInput.Zero,TevInput.Zero,TevInput.Zero,scale:TevScale.Two);
        p.SetAlphaStage(1,TevAlphaInput.Zero,TevAlphaInput.SecondaryTexture,TevAlphaInput.Previous,
            TevAlphaInput.Zero,scale:TevScale.Two);
        p.SetAlphaCompare(AlphaComparison.GreaterOrEqual,85,AlphaCompareOperation.Or,AlphaComparison.LessOrEqual,35);
        p.SetBlendFactors(TevBlendFactor.SourceAlpha,TevBlendFactor.Color);
        return new TevMaterialDefinition(p);
    }
    internal static TevMaterialDefinition SunshineRefractionDefinition()
    {
        var p=new TevProgramBuilder();p.EnableIndirectWarp();
        // The original binds distortion/capture to maps 0/1. Swapping both
        // map+coordinate roles preserves the original lookup and matrix.
        p.SetIndirectMatrix(0,0,0,0,.001f,.015f,2,TevIndirectBias.STU);
        p.AddStage(TevInput.Zero,TevInput.Zero,TevInput.Zero,TevInput.Texture);
        p.SetAlphaStage(0,TevAlphaInput.Zero,TevAlphaInput.Zero,TevAlphaInput.Zero,TevAlphaInput.Texture);
        return new TevMaterialDefinition(p,LightingMode.Unlit,TextureCoordinateMode.ScreenProjection);
    }
    internal static void ConfigureSunshine(Material material,bool closeWaves)
    {
        material.Blend=BlendMode.Alpha;material.DepthTest=true;material.DepthWrite=false;
        material.Cull=CullMode.None;material.Lighting=LightingMode.Unlit;
        material.Color=closeWaves?new Color32(200,200,255,255):Color32.White;
        material.AlphaCutoff=0;
        material.Sampler=new SamplerState(TextureMinFilter.LinearMipmapLinear,TextureMagFilter.Linear,
            minLod:0,maxLod:4,lodBias:0,u:TextureAddressMode.Repeat,v:TextureAddressMode.Repeat);
        material.SecondarySampler=new SamplerState(TextureMinFilter.LinearMipmapLinear,TextureMagFilter.Linear,
            minLod:0,maxLod:4,lodBias:closeWaves?0:-.5f,u:TextureAddressMode.Repeat,v:TextureAddressMode.Repeat);
    }
    // Called between frames. Avoid retaining the old two-texture storm shader
    // and its 8 KiB of image data until rough weather is actually selected.
    internal void PrepareRoughOcean()
    {
        if(!IsSunshine)ReleaseSunshine();
        if(!RoughOcean||IsSunshine){
            storm?.Dispose();storm=null;
            whitecaps?.Dispose();whitecaps=null;stormBase?.Dispose();stormBase=null;
            return;
        }
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
        preparationFailure="";
        try{PrepareSunshineCore();}
        catch(Exception error){
            ReleaseSunshine();Style=WaterRenderStyle.WindWaker;
            preparationFailure=string.IsNullOrEmpty(error.Message)?"Water preparation failed.":error.Message;
        }
    }
    internal bool TakePreparationFailure(out string error)
    {
        error=preparationFailure;preparationFailure="";return error.Length!=0;
    }
    private void PrepareSunshineCore()
    {
        if(sunshineLayer!=null){PrepareCapture();return;}
        Texture2D? wave=null,nearWave=null,distort=null;
        TevMaterial? layer=null,close=null;
        try{
            // Read a *pre-authored* GX mip package baked from five different
            // PNGs. Generating/downsampling at runtime would erase the effect.
            var package=Dolphin.IO.File.ReadAllBytes(SunshineWater.DiscTexture);
            SunshineWater.ValidatePackage(package);
            wave=Texture2D.FromPackage(package,linear:true);
            if(wave.MipLevels!=SunshineWater.MipLevels)
                throw new InvalidOperationException("Sunshine GPU mip chain was truncated.");
            var wavePackage=Dolphin.IO.File.ReadAllBytes(SunshineWater.DiscNearWaveTexture);
            SunshineWater.ValidateNearWavePackage(wavePackage);nearWave=Texture2D.FromPackage(wavePackage,linear:true);
            var distortionPackage=Dolphin.IO.File.ReadAllBytes(SunshineWater.DiscDistortionTexture);
            SunshineWater.ValidateDistortionPackage(distortionPackage);distort=Texture2D.FromPackage(distortionPackage,linear:true);
            layer=SunshineDefinition().Create(wave,wave);ConfigureSunshine(layer.Material,false);
            close=SunshineWaveDefinition().Create(nearWave,nearWave);ConfigureSunshine(close.Material,true);

            sunshineTexture=wave;sunshineWaveTexture=nearWave;sunshineDistortion=distort;
            sunshineLayer=layer;sunshineCloseWaves=close;
        }catch{
            close?.Dispose();layer?.Dispose();distort?.Dispose();nearWave?.Dispose();wave?.Dispose();
            throw;
        }
        PrepareCapture();
    }
    private void PrepareCapture()
    {
        var device=GraphicsDevice.Default;
        if(sunshineCapture!=null&&captureSource.Width==device.Width&&captureSource.Height==device.Height)return;
        ReleaseCapture();
        var capture=new SceneCapture(device,device.Width/2,device.Height/2,NativeTextureFormat.Rgb565);
        TevMaterial? material=null;
        try{
            material=SunshineRefractionDefinition().Create(capture.Texture,sunshineDistortion);
            material.Material.Blend=BlendMode.Opaque;material.Material.DepthTest=true;material.Material.DepthWrite=false;
            material.Material.Cull=CullMode.None;
            material.Material.Sampler=new SamplerState(TextureAddressMode.Clamp,TextureAddressMode.Clamp,TextureFilter.Linear);
            material.Material.SecondarySampler=new SamplerState(TextureMinFilter.LinearMipmapLinear,TextureMagFilter.Linear,
                minLod:0,maxLod:3,u:TextureAddressMode.Repeat,v:TextureAddressMode.Repeat);
            sunshineRefraction=material;sunshineCapture=capture;
            captureSource=new Viewport(0,0,device.Width,device.Height);
        }catch{material?.Dispose();capture.Dispose();throw;}
    }
    // Once per drawable Sunshine frame, between 3D passes. No allocations.
    internal void CaptureScene()=>sunshineCapture!.Capture(captureSource);
    internal Material Refraction=>sunshineRefraction!.Material;
    // The large EFB buffer is active only outdoors in Sunshine mode. Keep the
    // small images warm so returning to the style needs no further DVD reads.
    internal void ReleaseCapture()
    {
        sunshineRefraction?.Dispose();sunshineRefraction=null;
        sunshineCapture?.Dispose();sunshineCapture=null;
    }
    private void ReleaseSunshine()
    {
        ReleaseCapture();
        sunshineCloseWaves?.Dispose();sunshineCloseWaves=null;
        sunshineLayer?.Dispose();sunshineLayer=null;
        sunshineDistortion?.Dispose();sunshineDistortion=null;
        sunshineWaveTexture?.Dispose();sunshineWaveTexture=null;
        sunshineTexture?.Dispose();sunshineTexture=null;
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
            // Authored BTK Hermite timing: 1200 frames at 30 Hz, not the
            // reference demo's deliberately accelerated 5x animation.
            float phase=SunshineWater.SurfacePhase(seconds);
            var material=sunshineLayer.Material;
            material.TextureTransform=new UvTransform(new Vector2(phase,phase+.5f),new Vector2(5,4));
            material.SecondaryTextureTransform=new UvTransform(new Vector2(-.5f,phase+.5f),new Vector2(6,4));
            if(sunshineCloseWaves!=null){
                float near=SunshineWater.NearPhase(seconds);
                sunshineCloseWaves.Material.TextureTransform=new UvTransform(new Vector2(near,0),new Vector2(.48f,.48f));
                sunshineCloseWaves.Material.SecondaryTextureTransform=new UvTransform(new Vector2(0,near),new Vector2(.48f,.6f));
            }
            if(sunshineRefraction!=null)
                sunshineRefraction.Material.SecondaryTextureTransform=new UvTransform(
                    new Vector2(0,SunshineWater.IndirectPhase(seconds)+.5f),new Vector2(5,4));
        }
        if(RoughOcean&&!IsSunshine&&storm!=null){
            storm.Material.TextureTransform=new UvTransform(new Vector2(WaterSurface.Scroll(seconds,.035f),WaterSurface.Scroll(seconds,.075f)),new Vector2(1,1));
            storm.Material.SecondaryTextureTransform=new UvTransform(new Vector2(WaterSurface.Scroll(seconds,-.055f),WaterSurface.Scroll(seconds,.12f)),new Vector2(1.05f,.85f));
        }
    }
    public void Dispose(){
        ReleaseSunshine();
        storm?.Dispose();water.Dispose();
        whitecaps?.Dispose();stormBase?.Dispose();distortion.Dispose();shoreFoam.Dispose();ripples.Dispose();
    }
}
