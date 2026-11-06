using System;
using Dolphin.Graphics;
using Hypercube.GameCube;

// Game weather policy; the engine owns the shared mask, effect and UV motion.
internal sealed class CloudShadows : IDisposable
{
    private readonly CloudShadowLayer layer=new CloudShadowLayer(2,128,new Vector2(.18f,.09f));
    internal float Cover {get=>layer.Cover;set=>layer.Cover=value;}
    internal void Animate(double seconds)=>layer.Animate(seconds);
    internal void Apply(Material material,Color32 tint,bool outdoors)=>layer.Apply(material,tint,outdoors);
    public void Dispose()=>layer.Dispose();
}
