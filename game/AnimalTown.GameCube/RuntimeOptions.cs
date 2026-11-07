using System.IO;
using System.Threading.Tasks;
using Dolphin.Diagnostics;
using Scheduler=Dolphin.Threading.Tasks.Scheduler;

// Independent A/B flags. Preserve the measured release baseline until the
// complete input/audio/save regressions justify a new default. Simulation is 30Hz.
internal static class RuntimeOptions
{
    internal static int Mode {get;private set;}
    internal static void Load(){var bytes=File.ReadAllBytes("dvd:/runtime.cfg");if(bytes.Length!=1||bytes[0]>7)throw new IOException("Invalid runtime profile");Apply(bytes[0]);}
    internal static void Cycle()=>Apply((Mode+1)%8);
    internal static void Toggle(int flag)=>Apply(Mode^flag);
    private static void Apply(int mode){Mode=mode;Scheduler.ProductiveWaits=(mode&2)!=0;GpuProfiler.ReuseFrameClear=(mode&4)!=0;DebugOverlay.WriteLine("ANIMALTOWN RUNTIME "+mode);}
    internal static void Wait(){if((Mode&1)==0)Task.Delay(1).GetAwaiter().GetResult();}
}
