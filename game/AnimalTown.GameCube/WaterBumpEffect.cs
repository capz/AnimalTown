using System;
using AnimalTown;
using Dolphin.Diagnostics;
using Dolphin.Graphics;
using Dolphin.Timing;

// One owner per terrain renderer. Native resources are optional, while the
// established transparent-water material remains usable under memory pressure.
internal sealed class WaterBumpEffect : IDisposable
{
    // ABI: focus Z, cylinder radius, wave0 U/V/scale, wave1 U/V/scale,
    // slope amplitude, full-resolution refraction pixels, specular, sheen.
    private readonly float[] settings=new float[12]{0,CylinderWorld.Radius,0,0,2.2f,0,0,3.1f,.14f,1.5f,.18f,.10f};
    internal const int MapBytes=16*1024,AllocationGuardBytes=1024;
    internal const long IdleGraceUs=3000000;
    internal VisualMemoryPressure? Memory;
    private bool initialized,disposed;
    private long retryAt,lastVisibleAt;
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
    internal void Prepare(float focusZ,int sunlight,int environmentRgb,bool visible,int width,int height)
    {
        if(disposed)return;
        RequireBetweenFrames();
        settings[0]=focusZ;
        long now=Clock.Timestamp;
        if(Memory?.UnderPressure==true){Reclaim();return;}
        if(!visible){
            Active=false;Features=0;
            // A short camera reversal retains storage but performs no capture
            // or native draw work. Longer absence retires both immutable maps.
            if(initialized&&now-lastVisibleAt>=IdleGraceUs)Reclaim();
            return;
        }
        lastVisibleAt=now;
        if(now<retryAt){Active=false;Features=0;return;}
        int captureBytes=CaptureBytes(width,height);
        bool matchingCapture=initialized&&NativeWater.Stats(12)==width&&NativeWater.Stats(13)==height;
        int peak=initialized?0:MapBytes;
        if(!matchingCapture)peak+=captureBytes;
        if(peak>0)peak+=AllocationGuardBytes;
        int ticket=0;
        if(Memory!=null&&!Memory.TryReserve(peak,now,false,out ticket)){Suspend();return;}
        bool started=!initialized;
        try{
            int nativeBefore=peak>0?NativeWater.Stats(0):0;
            int failuresBefore=peak>0?NativeWater.Stats(18):0;
            if(!initialized){
                byte[] slopes=WaterTextureAssets.TryLoadNativeData("surface-slopes")??WaterBumpPattern.Texture();
                initialized=NativeWater.Initialize(slopes)>0;
                if(!initialized)throw new InvalidOperationException("Water optics maps unavailable");
            }
            Features=NativeWater.Prepare(settings,1,sunlight,environmentRgb);
            if(Features==0||peak>0&&NativeWater.Stats(18)!=failuresBefore)
                throw new InvalidOperationException("Water optics allocation unavailable");
            Active=true;
            // The physical sample may predate synchronous native publication.
            // Keep new bytes charged until the next real allocator observation.
            if(peak>0&&NativeWater.Stats(0)>nativeBefore)Memory?.Complete(ticket);
            else Memory?.Cancel(ticket);
            ticket=0;
            if(started)DebugOverlay.WriteLine("ANIMALTOWN WATER OPTICS mode "+Features+" native bytes "+NativeWater.Stats(0));
        }catch(Exception error){
            // Initialize can fail after its first native texture. Native
            // shutdown also drains that partial owner before ticket release.
            NativeWater.Shutdown();initialized=false;Active=false;Features=0;
            Memory?.Cancel(ticket);retryAt=now+VisualMemoryPressure.FailureBackoffUs;
            Memory?.AllocationFailed(peak,now);
            DebugOverlay.WriteLine("ANIMALTOWN WATER OPTICS RETRY "+error.Message);
        }
    }
    private static int CaptureBytes(int width,int height)
        =>width>=8&&height>=8&&width<=640&&height<=528&&((width|height)&7)==0?width*height/2:0;
    private static void RequireBetweenFrames()
    {if(GraphicsDevice.Default.IsFrameActive)throw new InvalidOperationException("Water optics residency changes require EndFrame");}
    internal void Begin(){if(Active)NativeWater.Begin();}
    internal void Scope(int mode){if(Active)NativeWater.Scope(mode);}
    internal void Capture(){if(Active)NativeWater.Capture();}
    internal void Suspend()
    {
        RequireBetweenFrames();
        if(initialized)NativeWater.Prepare(settings,0,0,0xffffff);
        Active=false;Features=0;
    }
    internal bool Reclaim()
    {
        if(!initialized)return false;
        RequireBetweenFrames();
        NativeWater.Shutdown();initialized=false;Active=false;Features=0;return true;
    }
    public void Dispose()
    {
        if(disposed)return;
        Reclaim();Active=false;Features=0;disposed=true;
    }
}
