using System;
using Dolphin.Graphics;
using Hypercube.GameCube;

// Game weather policy; the engine owns the shared mask, effect and UV motion.
internal sealed class CloudShadows : IDisposable
{
    private CloudShadowLayer? layer;
    private float cover=.5f;
    private double seconds;
    private bool prepared;
    internal float Cover {get=>cover;set{if(cover!=value){cover=value;prepared=false;}}}
    internal void Animate(double time){seconds=time;prepared=false;}
    internal void Apply(Material material,Color32 tint,bool outdoors)
    {
        if(cover==0){
            material.Effect=null;material.SecondaryTexture=null;
            material.SecondaryTextureTransform=UvTransform.Identity;material.Color=tint;return;
        }
        layer??=new CloudShadowLayer(2,128,new Vector2(.18f,.09f));
        if(!prepared){layer.Cover=cover;layer.Animate(seconds);prepared=true;}
        layer.Apply(material,tint,outdoors);
    }
    // Call after detaching every borrowing material, outside BeginFrame.
    internal void ReleaseUnused(Material[] materials)
    {
        if(cover!=0||layer==null)return;
        foreach(var material in materials)if(material!=null){
            material.Effect=null;material.SecondaryTexture=null;material.SecondaryTextureTransform=UvTransform.Identity;
        }
        Dispose();
    }
    public void Dispose(){layer?.Dispose();layer=null;prepared=false;}
}
