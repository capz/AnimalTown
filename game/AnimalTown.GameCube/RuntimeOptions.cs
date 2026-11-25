using System.IO;
using Dolphin.Diagnostics;
using Scheduler=Dolphin.Threading.Tasks.Scheduler;

// Productive presentation waits and completed frame-clear reuse are permanent.
// Retain the legacy profile byte for content compatibility; only pacing varies.
internal static class RuntimeOptions
{
    internal static int Mode {get;private set;}
    internal static void Load(){var bytes=File.ReadAllBytes(AnimalTown.ContentPaths.Asset("runtime.cfg"));if(bytes.Length!=1||bytes[0]>7)throw new IOException("Invalid runtime profile");Apply(bytes[0]);}
    internal static void ToggleFramePacing()=>Apply(Mode^1);
    internal static void Apply(int mode){Mode=(mode&1)|AnimalTown.DevConfig.RequiredRuntimeFlags;Scheduler.ProductiveWaits=true;GpuProfiler.ReuseFrameClear=true;DebugOverlay.WriteLine("ANIMALTOWN RUNTIME "+Mode);}
    // EndFrame already waits for retrace. Service callbacks without allocating
    // a Timer, completion source, task and delegate after every presented frame.
    internal static void Wait(){if((Mode&1)==0)NativeFrame.Service();}
}
