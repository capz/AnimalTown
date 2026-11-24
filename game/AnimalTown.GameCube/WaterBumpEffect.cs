using System;
using AnimalTown;
using Dolphin.Diagnostics;

// One owner per terrain renderer. Native resources are optional, while the
// established transparent-water material remains usable under memory pressure.
internal sealed class WaterBumpEffect : IDisposable
{
    // ABI: focus Z, cylinder radius, wave0 U/V/scale, wave1 U/V/scale,
    // slope amplitude, full-resolution refraction pixels, specular, sheen.
    private readonly float[] settings=new float[12]{0,CylinderWorld.Radius,0,0,2.2f,0,0,3.1f,.14f,1.5f,.18f,.10f};
    private bool attempted,initialized,disposed;
    internal bool Active {get;private set;}
    internal int Features {get;private set;}
    internal bool CaptureReady=>(Features&2)!=0;

    internal void Animate(double seconds)
    {
        settings[2]=WaterSurface.Scroll(seconds,.016f);
        settings[3]=WaterSurface.Scroll(seconds,.021f);
        settings[5]=WaterSurface.Scroll(seconds,-.011f);
        settings[6]=WaterSurface.Scroll(seconds,.014f);
    }
    internal void Prepare(float focusZ,int sunlight,int environmentRgb,bool visible)
    {
        if(disposed)return;
        settings[0]=focusZ;
        bool started=false;
        if(visible&&!attempted){
            attempted=true;
            byte[] slopes=WaterTextureAssets.TryLoadNativeData("surface-slopes")??WaterBumpPattern.Texture();
            initialized=NativeWater.Initialize(slopes)>0;
            started=initialized;
            if(!initialized)DebugOverlay.WriteLine("ANIMALTOWN WATER OPTICS unavailable; keeping transparent water");
        }
        Features=initialized?NativeWater.Prepare(settings,visible?1:0,sunlight,environmentRgb):0;
        Active=visible&&Features>0;
        if(started)DebugOverlay.WriteLine("ANIMALTOWN WATER OPTICS mode "+Features+" native bytes "+NativeWater.Stats(0));
    }
    internal void Begin(){if(initialized)NativeWater.Begin();}
    internal void Scope(int mode){if(initialized)NativeWater.Scope(mode);}
    internal void Capture(){if(Active)NativeWater.Capture();}
    internal void Suspend()
    {
        if(initialized)NativeWater.Prepare(settings,0,0,0xffffff);
        Active=false;Features=0;
    }
    public void Dispose()
    {
        if(disposed)return;
        if(initialized)NativeWater.Shutdown();
        initialized=false;Active=false;Features=0;disposed=true;
    }
}
