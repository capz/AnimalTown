using System;
using Dolphin.Graphics;
using Dolphin.Timing;
using Dolphin.Diagnostics;

// Guest-clock CPU/submission timings. GPU wait is blocking duration, not GPU time.
// Pixel telemetry can be read from unmodified real hardware output as well as Dolphin.
internal static class FrameProfile
{
    internal static bool Enabled;
    internal static int CulledObjects;
    private static readonly long[] current=new long[16];
    private static readonly int[] data=new int[32];
    private static int frames;
    private static long previous;
    internal static long Start()=>Enabled?Clock.Timestamp:0;
    internal static void Add(int slot,long microseconds){if(Enabled)current[slot]+=microseconds;}
    internal static void End(int slot,long start){if(Enabled)current[slot]+=Clock.Timestamp-start;}
    internal static void Begin(){if(!Enabled)return;for(int i=0;i<16;i++)current[i]=0;CulledObjects=0;}
    internal static void Complete(int triangles,int terrainTriangles,int rejected,int scenario)
    {
        if(!Enabled)return;long now=Clock.Timestamp;current[12]=previous==0?0:now-previous;previous=now;
        current[10]=GpuProfiler.GpuWaitMicroseconds;current[11]=GpuProfiler.RetraceWaitMicroseconds;
        data[0]=0x4154;data[1]=1;data[2]=++frames&65535;data[3]=0;
        for(int i=0;i<16;i++){if(current[i]/10>65535)data[3]|=1;data[4+i]=(int)Math.Min(65535,current[i]/10);}
        data[4]=(int)Math.Max(0,data[4]-data[5]);
        data[30]=scenario;data[31]=0;
        data[20]=triangles&65535;data[21]=(int)((uint)triangles>>16);data[22]=terrainTriangles;data[23]=rejected;data[24]=CulledObjects;
        data[25]=TextureCache.ResidentBytes/1024;data[26]=TextureCache.Hits;data[27]=TextureCache.Misses;data[28]=GpuProfiler.Snapshot().DrawSubmissions;data[29]=GpuProfiler.Snapshot().TextureBindings;
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
