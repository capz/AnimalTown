using AnimalTown;
using Dolphin.Graphics;

// Shared ground-only effect: four physical TEV stages including the SDK's
// opacity stage. No extra draws, vertices, per-frame uploads or mesh rebuilds.
internal sealed class CloudShadows
{
    private readonly Texture2D mask;
    private readonly GxEffect effect;
    private float previousCover=-1;
    internal float Cover=.5f;
    private UvTransform transform;
    internal CloudShadows()
    {
        mask=Texture2D.CreateNative(64,64,NativeTextureFormat.I8,true);mask.ReplaceNative(CloudShadowPattern.Texture());
        var p=new TevProgramBuilder();p.AddColor("CloudShade",Color32.White);
        p.AddStage(TevInput.Zero,TevInput.Raster,TevInput.Texture,TevInput.Zero);
        p.AddStage(TevInput.One,TevInput.Constant,TevInput.SecondaryTexture,TevInput.Zero,TevRegister.Color0,colorSlot:0);
        p.AddStage(TevInput.Zero,TevInput.Previous,TevInput.Color0,TevInput.Zero);
        effect=p.CreateEffect();
    }
    internal void Animate(double seconds)
    {
        if(previousCover!=Cover){byte s=CloudShadowPattern.Shade(Cover);effect.SetColor(0,new Color32(s,s,s));previousCover=Cover;}
        // Ground UV0 is logical world XZ * .5, including cliff tops/lips.
        transform=new UvTransform(new Vector2(CloudShadowPattern.Offset(seconds,CloudShadowPattern.SpeedX),CloudShadowPattern.Offset(seconds,CloudShadowPattern.SpeedZ)),new Vector2(2/CloudShadowPattern.WorldPeriod,2/CloudShadowPattern.WorldPeriod));
    }
    internal void Apply(Material material,Color32 tint,bool outdoors)
    {
        bool patches=outdoors&&CloudShadowPattern.Shade(Cover)<255;
        material.Effect=patches?effect:null;material.SecondaryTexture=patches?mask:null;
        material.SecondaryTextureTransform=patches?transform:UvTransform.Identity;
        byte ambient=outdoors?CloudShadowPattern.Ambient(Cover):(byte)255;
        material.Color=new Color32((byte)(tint.R*ambient/255),(byte)(tint.G*ambient/255),(byte)(tint.B*ambient/255),tint.A);
    }
}
