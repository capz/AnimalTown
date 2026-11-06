using System;
using Dolphin.Graphics;
using Dolphin.Timing;
using Dolphin.Diagnostics;
using Hypercube;
using Hypercube.GameCube;

// Guest-clock CPU/submission timings. GPU wait is blocking duration, not GPU time.
// Pixel telemetry can be read from unmodified real hardware output as well as Dolphin.
internal static class FrameProfile
{
    internal static readonly RenderCounters Counters=new RenderCounters();
    internal static bool Enabled;
    internal static int CulledObjects,AnimationUpdates;
    private static readonly long[] current=new long[16];
    private static readonly int[] data=new int[32];
    private static int frames;
    private static long previous;
    internal static long Start()=>Enabled?Clock.Timestamp:0;
    internal static void Add(int slot,long microseconds){if(Enabled)current[slot]+=microseconds;}
    internal static void End(int slot,long start){if(Enabled)current[slot]+=Clock.Timestamp-start;}
    internal static void Begin(){Counters.Reset();AnimationUpdates=0;if(!Enabled)return;for(int i=0;i<16;i++)current[i]=0;CulledObjects=0;}
    internal static void Complete(int triangles,int terrainTriangles,int rejected,int scenario,int terrainBuilds,int movement,bool groundRing,bool groundUpload)
    {
        if(!Enabled)return;long now=Clock.Timestamp;current[12]=previous==0?0:now-previous;previous=now;
        RenderTelemetry.CaptureCompletedFrame(Counters);
        current[10]=Counters.GpuWaitMicroseconds;current[11]=Counters.RetraceWaitMicroseconds;
        // 0.1 ms units support 6.55-second frames without hiding slow paths.
        // Simulation excludes the once-per-render animation phase.
        current[6]=Math.Max(0,current[6]-current[15]);
        current[7]=Math.Max(0,current[7]-current[3]);
        data[0]=0x4154;data[1]=5;data[2]=++frames&65535;data[3]=0;
        for(int i=0;i<16;i++){if(current[i]/100>65535)data[3]|=1<<i;data[4+i]=(int)Math.Min(65535,current[i]/100);}
        data[30]=scenario;data[31]=0;
        data[20]=triangles&65535;data[21]=(int)((uint)triangles>>16);data[22]=terrainTriangles;data[23]=Math.Min(255,terrainBuilds)|(Math.Min(7,AnimationUpdates)<<8)|(movement>0?4096:0)|(movement==2?8192:0)|(groundRing?16384:0)|(groundUpload?32768:0);data[24]=CulledObjects;
        Counters.CulledObjects=CulledObjects;
        data[25]=TextureCache.ResidentBytes/1024;data[26]=Counters.DrawingApiCalls;data[27]=Counters.MaterialChanges;data[28]=Counters.DrawSubmissions;data[29]=Counters.TextureBindings;
        for(int i=0;i<31;i++)data[31]^=data[i]&65535;
    }
    internal static void Draw(Draw2D ui)
    {
        if(!Enabled)return;long start=Clock.Timestamp;
        ui.FillRectangle(new RectF(0,448,640,32),new Color32(16,16,16));
        for(int at=0;at<512;){int length=Hypercube.TelemetryRuns.Length(data,at);if(Hypercube.TelemetryRuns.Lit(data,at))ui.FillRectangle(new RectF(at%64*10,448+at/64*4,length*10,4),new Color32(240,240,240));at+=length;}
        current[14]+=Clock.Timestamp-start;
    }
}
