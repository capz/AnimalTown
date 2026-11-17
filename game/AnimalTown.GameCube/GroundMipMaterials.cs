using System;
using AnimalTown;
using Dolphin.Diagnostics;
using Dolphin.Graphics;
using Hypercube.GameCube;

// Prepared between frames. Draws only select a material; source art, shared
// cache leases and archived geometry are never changed by this diagnostic.
internal sealed class GroundMipMaterials : IDisposable
{
    private readonly Texture2D?[] sources=new Texture2D?[FloorStrips.MaximumMaterials];
    private readonly Texture2D?[] pages=new Texture2D?[FloorStrips.MaximumMaterials];
    private readonly Material?[] materials=new Material?[FloorStrips.MaximumMaterials];
    private readonly bool[] configured=new bool[FloorStrips.MaximumMaterials];
    private bool enabled;
    private int appliedMip=-1,appliedDistance=-1,traceState=-1,traceWidth,traceHeight,traceLevels;
    internal bool Enabled
    {
        get=>enabled;
        set{
            if(enabled==value)return;
            if(GraphicsDevice.Default.IsFrameActive)throw new InvalidOperationException("Change ground mip bands between frames");
            if(!value)Clear();
            enabled=value;
        }
    }
    internal Material Select(int slot,Material original)=>enabled&&materials[slot]?.Texture!=null?materials[slot]!:original;
    internal void Prepare(Material[] originals,int count,int pathSlot,bool mipmaps,int distance)
    {
        if(GraphicsDevice.Default.IsFrameActive)throw new InvalidOperationException("Prepare ground mip bands between frames");
        if(enabled){
            // A preview upgrade or season switch may have disposed the old
            // source. Compare identity only, then retire its diagnostic page.
            for(int entry=0;entry<sources.Length;entry++)if(sources[entry]!=null){
                bool used=originals[pathSlot].Texture==sources[entry];
                for(int slot=0;slot<count&&!used;slot++)used=originals[slot].Texture==sources[entry];
                if(!used)Retire(entry);
            }
            bool changed=appliedMip!=(mipmaps?1:0)||appliedDistance!=distance;
            Array.Clear(configured,0,configured.Length);
            for(int slot=0;slot<count;slot++)PrepareSlot(slot,originals[slot],true,mipmaps,distance,changed);
            // A resident painted-path page has its own sampler (trilinear,
            // bias zero). Its fallback aliases a floor already configured above.
            PrepareSlot(pathSlot,originals[pathSlot],false,true,0,changed);
            // Path coverage uses the borrowed secondary binding even when the
            // primary floor is replaced by its mip-level diagnostic palette.
            for(int slot=0;slot<count;slot++)if(materials[slot]!=null){
                var material=materials[slot]!;
                material.SecondaryTexture=originals[slot].SecondaryTexture!=null?materials[pathSlot]?.Texture:null;
                material.SecondarySampler=originals[slot].SecondarySampler;
            }
            appliedMip=mipmaps?1:0;appliedDistance=distance;
        }
        var source=originals[0].Texture;
        int width=source?.Width??0,height=source?.Height??0,levels=source?.MipLevels??0;
        int state=(enabled?1:0)|(mipmaps?2:0)|(distance<<2);
        if(state!=traceState||width!=traceWidth||height!=traceHeight||levels!=traceLevels){
            traceState=state;traceWidth=width;traceHeight=height;traceLevels=levels;
            DebugOverlay.WriteLine("ANIMALTOWN GROUND MIP BANDS enabled "+(enabled?1:0)+" mips "+(mipmaps?1:0)+" distance "+distance+" size "+width+"x"+height+" levels "+levels);
        }
    }
    private void PrepareSlot(int slot,Material original,bool ground,bool mipmaps,int distance,bool changed)
    {
        var source=original.Texture;
        if(source==null){if(materials[slot]!=null){materials[slot]!.Texture=null;materials[slot]!.SecondaryTexture=null;}return;}
        int entry=-1,free=-1;
        for(int i=0;i<sources.Length;i++){
            if(sources[i]==source){entry=i;break;}
            if(sources[i]==null&&free<0)free=i;
        }
        bool created=entry<0;
        if(created){
            if(free<0)throw new InvalidOperationException("Ground mip diagnostic texture budget exceeded");
            entry=free;
            var page=Texture2D.FromPackage(GroundMipPalette.Texture(source.Width,source.Height,source.MipLevels),true);
            pages[entry]=page;sources[entry]=source;
        }
        var texture=pages[entry]!;
        if(!configured[entry]&&(created||changed)){
            bool mip=mipmaps&&source.MipLevels>1;
            texture.ConfigureSampler(TextureWrapMode.Mirror,TextureWrapMode.Mirror,
                mip?TextureMinFilter.LinearMipmapLinear:TextureMinFilter.Linear,TextureMagFilter.Linear,
                mip&&ground?RenderDebugOptions.GroundMipBias(source.Width,source.Height,distance):0);
        }
        configured[entry]=true;
        var material=materials[slot];
        if(material==null)materials[slot]=material=new Material{Lighting=LightingMode.Unlit,Cull=CullMode.Back};
        material.Texture=texture;material.Sampler=original.Sampler;material.TextureTransform=original.TextureTransform;
        // Preserve vertex/environment/shadow shading, but keep seasonal ground
        // tint from obscuring the diagnostic hue associated with each mip.
        material.Color=Color32.White;
    }
    private void Clear()
    {
        for(int slot=0;slot<materials.Length;slot++)if(materials[slot]!=null){materials[slot]!.Texture=null;materials[slot]!.SecondaryTexture=null;}
        for(int entry=0;entry<pages.Length;entry++){pages[entry]?.Dispose();pages[entry]=sources[entry]=null;}
        appliedMip=appliedDistance=-1;
    }
    internal void Forget(Texture2D? source)
    {
        if(source==null)return;
        if(GraphicsDevice.Default.IsFrameActive)throw new InvalidOperationException("Retire ground mip bands between frames");
        for(int entry=0;entry<sources.Length;entry++)if(sources[entry]==source)Retire(entry);
    }
    private void Retire(int entry)
    {
        var page=pages[entry];
        for(int slot=0;slot<materials.Length;slot++)if(materials[slot]!=null){
            if(materials[slot]!.Texture==page)materials[slot]!.Texture=null;
            if(materials[slot]!.SecondaryTexture==page)materials[slot]!.SecondaryTexture=null;
        }
        pages[entry]=sources[entry]=null;page?.Dispose();
    }
    public void Dispose(){Clear();enabled=false;}
}
