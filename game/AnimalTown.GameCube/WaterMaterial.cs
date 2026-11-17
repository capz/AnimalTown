using System;
using AnimalTown;
using Dolphin.Graphics;
using Hypercube.GameCube;

// Shared Wind Waker water materials for river, pond and sea meshes.
// Textures/effect are created outside frames; animation only changes UV matrices.
internal sealed class WaterMaterial : IDisposable
{
    private readonly Texture2D ripples,shoreFoam,distortion;
    private readonly TevMaterial water;
    private Texture2D? stormBase,whitecaps;
    private TevMaterial? storm;
    internal Material Surface=>water.Material;
    internal bool RoughOcean;
    internal Material OceanSurface=>RoughOcean?storm!.Material:Surface;
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
        if(!RoughOcean){
            storm?.Dispose();storm=null;
            whitecaps?.Dispose();whitecaps=null;stormBase?.Dispose();stormBase=null;
            return;
        }
        if(storm!=null)return;
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
        if(RoughOcean&&storm!=null){
            storm.Material.TextureTransform=new UvTransform(new Vector2(WaterSurface.Scroll(seconds,.035f),WaterSurface.Scroll(seconds,.075f)),new Vector2(1,1));
            storm.Material.SecondaryTextureTransform=new UvTransform(new Vector2(WaterSurface.Scroll(seconds,-.055f),WaterSurface.Scroll(seconds,.12f)),new Vector2(1.05f,.85f));
        }
    }
    public void Dispose(){
        storm?.Dispose();water.Dispose();
        whitecaps?.Dispose();stormBase?.Dispose();distortion.Dispose();shoreFoam.Dispose();ripples.Dispose();
    }
}
