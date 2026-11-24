using System;
using AnimalTown;
using Dolphin.Graphics;
using Hypercube.GameCube;

// The inland floor and rocks are opaque; their water surface is drawn later
// with depth testing and no depth writes. Only UV matrices change in a frame.
internal sealed class WaterMaterial : IDisposable
{
    private readonly Texture2D ripples,shoreFoam,distortion,bedColor,caustics,oceanRipples,oceanDistortion;
    private readonly TevMaterial water,ocean,bed,rocks;
    private Texture2D? stormBase,whitecaps;
    private TevMaterial? storm;
    internal Material Surface=>water.Material;
    internal Material BedMaterial=>bed.Material;
    internal Material RockMaterial=>rocks.Material;
    internal bool RoughOcean;
    internal Material OceanSurface=>RoughOcean&&storm!=null?storm.Material:ocean.Material;
    internal readonly Material Foam=new Material{Blend=BlendMode.Alpha,DepthTest=true,DepthWrite=false,Lighting=LightingMode.Unlit,Cull=CullMode.Back};
    internal Color32 Shallow=new Color32(99,181,196),Deep=new Color32(75,154,187);
    internal Vector2 BaseSpeed=new Vector2(.012f,.025f),RippleSpeed=new Vector2(-.018f,.035f);
    private readonly ScrollingTextureLayer baseMotion=new ScrollingTextureLayer(new Vector2(.012f,.025f),new Vector2(1,1)),
        rippleMotion=new ScrollingTextureLayer(new Vector2(-.018f,.035f),new Vector2(1.35f,1.35f));
    internal bool FoamEnabled=true;

    internal WaterMaterial()
    {
        try{
            ripples=WaterTextureAssets.TryLoad("surface-ripples")??Native(64,NativeTextureFormat.I8,ShallowWaterPattern.RippleTexture());
            distortion=WaterTextureAssets.TryLoad("surface-distortion")??Native(64,NativeTextureFormat.Rgb565,ShallowWaterPattern.DistortionTexture());
            water=ShallowDefinition().Create(ripples,distortion);
            Configure(water.Material,true);

            bedColor=WaterTextureAssets.TryLoad("riverbed-color")??Native(128,NativeTextureFormat.Rgb565,ShallowWaterPattern.BedTexture());
            caustics=WaterTextureAssets.TryLoad("bed-caustics")??Native(64,NativeTextureFormat.I8,ShallowWaterPattern.CausticTexture());
            bed=BedDefinition().Create(bedColor,caustics);Configure(bed.Material,false);
            rocks=RockDefinition().Create(caustics);Configure(rocks.Material,false);

            // The open sea still has no finite floor. Retain its opaque legacy
            // appearance and maps, including the existing rough-weather option.
            oceanRipples=Native(64,NativeTextureFormat.I8,WaterSurface.Texture(true));
            oceanDistortion=Native(64,NativeTextureFormat.Rgb565,WaterSurface.WarpTexture());
            ocean=WindWakerDefinition().Create(oceanRipples,oceanDistortion);Configure(ocean.Material,false);

            // Keep shoreline geometry archived. Its IA8 mask moves on the GPU.
            shoreFoam=WaterTextureAssets.TryLoad("shoreline-foam")??Native(64,NativeTextureFormat.Ia8,WaterSurface.ShoreTexture());
            Foam.Texture=shoreFoam;Foam.Sampler=water.Material.Sampler;
            Animate(0);
        }catch{Dispose();throw;}
    }
    private static Texture2D Native(int size,NativeTextureFormat format,byte[] pixels)
    {
        var texture=Texture2D.CreateNative(size,size,format,true);
        try{texture.ReplaceNative(pixels);return texture;}catch{texture.Dispose();throw;}
    }
    private static void Configure(Material material,bool transparent)
    {
        material.Blend=transparent?BlendMode.Alpha:BlendMode.Opaque;
        material.DepthTest=true;material.DepthWrite=!transparent;material.Cull=CullMode.Back;
        material.Sampler=new SamplerState(TextureAddressMode.Repeat,TextureAddressMode.Repeat,TextureFilter.Linear);
        material.SecondarySampler=material.Sampler;
    }

    // Four RGB stages leave room for the native environment/shadow footer.
    // K0/K1 are ours; K2/K3 remain available to that footer. RGB register 0
    // holds the weak lower ripple, leaving register 1 for the footer's lamps.
    internal static TevMaterialDefinition ShallowDefinition()
    {
        var program=new TevProgramBuilder();
        program.AddColor("LowerTint",new Color32(54,127,145));
        program.AddColor("Glint",new Color32(211,242,236));
        program.EnableIndirectWarp(24);
        // GX fetches .abg. RGB565 has constant alpha: select blue/green instead
        // of the preset's alpha/blue axes, and bias both around signed zero.
        program.SetIndirectMatrix(0,.035f,0,0,0,.035f,-1,TevIndirectBias.TU);
        program.AddStage(TevInput.Zero,TevInput.Zero,TevInput.Zero,TevInput.Raster);
        program.AddStage(TevInput.Zero,TevInput.Texture,TevInput.Half,TevInput.Zero,TevRegister.Color0);
        program.AddStage(TevInput.Previous,TevInput.Constant,TevInput.Color0,TevInput.Zero,colorSlot:0);
        program.AddStage(TevInput.Previous,TevInput.Constant,TevInput.Texture,TevInput.Zero,colorSlot:1,alternateUv:true);
        RasterAlpha(program,false);
        return new TevMaterialDefinition(program,LightingMode.Unlit);
    }
    internal static TevMaterialDefinition BedDefinition()
    {
        var program=new TevProgramBuilder();program.AddColor("CausticLight",new Color32(24,33,28));
        program.AddStage(TevInput.Zero,TevInput.Texture,TevInput.Raster,TevInput.Zero);
        program.AddStage(TevInput.Zero,TevInput.SecondaryTexture,TevInput.Constant,TevInput.Zero,TevRegister.Color0,colorSlot:0);
        program.AddStage(TevInput.Zero,TevInput.Color0,TevInput.Raster,TevInput.Previous);
        RasterAlpha(program,false);
        return new TevMaterialDefinition(program,LightingMode.Unlit);
    }
    internal static TevMaterialDefinition RockDefinition()
    {
        var program=new TevProgramBuilder();program.AddColor("CausticLight",new Color32(24,33,28));
        program.AddStage(TevInput.Zero,TevInput.Zero,TevInput.Zero,TevInput.Raster);
        program.AddStage(TevInput.Zero,TevInput.Texture,TevInput.Constant,TevInput.Zero,TevRegister.Color0,colorSlot:0);
        program.AddStage(TevInput.Zero,TevInput.Color0,TevInput.Raster,TevInput.Zero,TevRegister.Color0);
        // Rock vertex alpha is a waterline mask, not framebuffer opacity.
        program.AddStage(TevInput.Zero,TevInput.Color0,TevInput.RasterAlpha,TevInput.Previous);
        RasterAlpha(program,true);
        return new TevMaterialDefinition(program,LightingMode.Unlit);
    }
    private static void RasterAlpha(TevProgramBuilder program,bool opaque)
    {
        // GX I8 replicates intensity into alpha too. Explicit raster opacity
        // prevents dark/empty ripple texels from making holes in the water.
        for(int i=0;i<program.StageCount;i++)
            program.SetAlphaStage(i,TevAlphaInput.Zero,TevAlphaInput.Zero,TevAlphaInput.Zero,opaque?TevAlphaInput.One:TevAlphaInput.Raster);
    }
    internal static TevMaterialDefinition WindWakerDefinition()=>TevMaterialPresets.WaterWarp(
        new Color32(196,228,222),new Color32(229,245,237));

    // Called between frames; rough weather's additional 8 KiB is lazy.
    internal void PrepareRoughOcean()
    {
        if(!RoughOcean){
            storm?.Dispose();storm=null;
            whitecaps?.Dispose();whitecaps=null;stormBase?.Dispose();stormBase=null;
            return;
        }
        if(storm!=null)return;
        Texture2D? first=null,second=null;TevMaterial? effect=null;
        try{
            first=Native(64,NativeTextureFormat.I8,WaterSurface.Texture(false));
            second=Native(64,NativeTextureFormat.I8,WaterSurface.StormTexture());
            var waves=new TevProgramBuilder();waves.AddColor("Whitecaps",new Color32(231,247,244));
            waves.AddStage(TevInput.Zero,TevInput.Raster,TevInput.Texture,TevInput.Zero);
            waves.AddStage(TevInput.Previous,TevInput.Constant,TevInput.SecondaryTexture,TevInput.Zero,colorSlot:0);
            effect=new TevMaterialDefinition(waves).Create(first,second);Configure(effect.Material,false);
        }catch{effect?.Dispose();second?.Dispose();first?.Dispose();throw;}
        stormBase=first;whitecaps=second;storm=effect;
    }
    internal Color32 Tint(float depth)=>SurfaceTint(depth);
    internal Color32 SurfaceTint(float depth)
    {
        Color32 color=OceanTint(depth);
        return new Color32(color.R,color.G,color.B,ShallowWaterPattern.Opacity(depth));
    }
    internal Color32 OceanTint(float depth)
    {
        depth=Math.Clamp(depth,0,1);
        return new Color32((byte)(Shallow.R+(Deep.R-Shallow.R)*depth),(byte)(Shallow.G+(Deep.G-Shallow.G)*depth),(byte)(Shallow.B+(Deep.B-Shallow.B)*depth));
    }
    internal void SetTerrainTint(uint rgba)
    {
        Deep=new Color32((byte)(rgba>>24),(byte)(rgba>>16),(byte)(rgba>>8));
        Shallow=new Color32((byte)Math.Min(255,Deep.R*99/75),(byte)Math.Min(255,Deep.G*181/154),(byte)Math.Min(255,Deep.B*196/187));
    }
    internal static UvTransform CausticTransform(double seconds)=>new UvTransform(
        new Vector2(WaterSurface.Scroll(seconds,.011f),WaterSurface.Scroll(seconds,-.008f)),new Vector2(1.6f,1.6f));
    internal void Animate(double seconds)
    {
        // At WaterSurface.UvScale=.25 these repeat every 1.7/1.4 metres;
        // the short arcs inside each tile are much smaller than the old foam.
        water.Material.TextureTransform=new UvTransform(new Vector2(WaterSurface.Scroll(seconds,BaseSpeed.X),WaterSurface.Scroll(seconds,BaseSpeed.Y)),new Vector2(2.4f,2.4f));
        water.Material.SecondaryTextureTransform=new UvTransform(new Vector2(WaterSurface.Scroll(seconds,RippleSpeed.X),WaterSurface.Scroll(seconds,RippleSpeed.Y)),new Vector2(2.8f,2.8f));
        UvTransform light=CausticTransform(seconds);
        bed.Material.SecondaryTextureTransform=light;rocks.Material.TextureTransform=light;
        baseMotion.RepeatsPerSecond=BaseSpeed;rippleMotion.RepeatsPerSecond=RippleSpeed;
        rippleMotion.Apply(ocean.Material,seconds);baseMotion.Apply(ocean.Material,seconds,true);
        Foam.Color=new Color32(220,241,235,(byte)(70+14*Math.Sin(seconds*1.2)));
        Foam.TextureTransform=new UvTransform(new Vector2(WaterSurface.Scroll(seconds,.032f),WaterSurface.Scroll(seconds,-.058f)),new Vector2(1.10f,1.10f));
        if(RoughOcean&&storm!=null){
            storm.Material.TextureTransform=new UvTransform(new Vector2(WaterSurface.Scroll(seconds,.035f),WaterSurface.Scroll(seconds,.075f)),new Vector2(1,1));
            storm.Material.SecondaryTextureTransform=new UvTransform(new Vector2(WaterSurface.Scroll(seconds,-.055f),WaterSurface.Scroll(seconds,.12f)),new Vector2(1.05f,.85f));
        }
    }
    public void Dispose()
    {
        // Null-safe also handles an authored-package/native allocation failure
        // partway through construction. Effects never own their input textures.
        storm?.Dispose();water?.Dispose();ocean?.Dispose();bed?.Dispose();rocks?.Dispose();
        whitecaps?.Dispose();stormBase?.Dispose();distortion?.Dispose();shoreFoam?.Dispose();ripples?.Dispose();
        bedColor?.Dispose();caustics?.Dispose();oceanRipples?.Dispose();oceanDistortion?.Dispose();
    }
}
