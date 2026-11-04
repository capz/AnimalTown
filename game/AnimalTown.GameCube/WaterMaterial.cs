using System;
using AnimalTown;
using Dolphin.Graphics;

// Own one per water style and share it between river, pond and sea meshes.
// Textures/effect are created outside frames; animation only changes UV matrices.
internal sealed class WaterMaterial : IDisposable
{
    private readonly Texture2D baseTexture,ripples;
    private readonly TevMaterial tev;
    internal readonly Material Surface;
    internal readonly Material Foam=new Material{Blend=BlendMode.Alpha,DepthWrite=false,Lighting=LightingMode.Unlit,Cull=CullMode.Back};
    internal Color32 Shallow=new Color32(99,181,196),Deep=new Color32(75,154,187);
    internal Vector2 BaseSpeed=new Vector2(.012f,.025f),RippleSpeed=new Vector2(-.018f,.035f);
    internal bool FoamEnabled=true;
    internal WaterMaterial()
    {
        baseTexture=Texture2D.CreateNative(64,64,NativeTextureFormat.I8,true);baseTexture.ReplaceNative(WaterSurface.Texture(false));
        ripples=Texture2D.CreateNative(64,64,NativeTextureFormat.I8,true);ripples.ReplaceNative(WaterSurface.Texture(true));
        var program=new TevProgramBuilder();program.AddColor("Highlight",new Color32(196,228,222));
        program.AddStage(TevInput.Zero,TevInput.Raster,TevInput.Texture,TevInput.Zero);
        program.AddStage(TevInput.Previous,TevInput.Constant,TevInput.SecondaryTexture,TevInput.Zero,colorSlot:0);
        tev=new TevMaterialDefinition(program).Create(baseTexture,ripples);Surface=tev.Material;
        Surface.Blend=BlendMode.Opaque;Surface.DepthWrite=true;
        Surface.Sampler=new SamplerState(TextureAddressMode.Repeat,TextureAddressMode.Repeat,TextureFilter.Linear);
        Surface.SecondarySampler=Surface.Sampler;
        Animate(0);
    }
    internal Color32 Tint(float depth)=>new Color32((byte)(Shallow.R+(Deep.R-Shallow.R)*depth),(byte)(Shallow.G+(Deep.G-Shallow.G)*depth),(byte)(Shallow.B+(Deep.B-Shallow.B)*depth));
    internal void SetTerrainTint(uint rgba)
    {
        Deep=new Color32((byte)(rgba>>24),(byte)(rgba>>16),(byte)(rgba>>8));
        Shallow=new Color32((byte)Math.Min(255,Deep.R*99/75),(byte)Math.Min(255,Deep.G*181/154),(byte)Math.Min(255,Deep.B*196/187));
    }
    internal void Animate(double seconds)
    {
        Surface.TextureTransform=new UvTransform(new Vector2(WaterSurface.Scroll(seconds,BaseSpeed.X),WaterSurface.Scroll(seconds,BaseSpeed.Y)),new Vector2(1,1));
        Surface.SecondaryTextureTransform=new UvTransform(new Vector2(WaterSurface.Scroll(seconds,RippleSpeed.X),WaterSurface.Scroll(seconds,RippleSpeed.Y)),new Vector2(1.35f,1.35f));
        Foam.Color=new Color32(202,232,222,(byte)(65+15*Math.Sin(seconds*1.2)));
    }
    public void Dispose(){tev.Dispose();ripples.Dispose();baseTexture.Dispose();}
}
