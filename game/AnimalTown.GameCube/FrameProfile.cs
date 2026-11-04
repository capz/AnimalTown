using System;
using Dolphin.Graphics;
using Dolphin.Timing;
using Dolphin.Diagnostics;

// Guest-clock CPU/submission timings. GPU wait is blocking duration, not GPU time.
// Pixel telemetry can be read from unmodified real hardware output as well as Dolphin.
internal static class FrameProfile
{
    private static bool enabled;
    internal static bool Enabled {
        get=>enabled;
        set{if(enabled==value)return;enabled=value;previous=0;sampleAt=0;intervalCount=intervalHead=0;intervalSum=0;hasSample=false;for(int i=0;i<current.Length;i++)current[i]=0;for(int i=0;i<data.Length;i++)data[i]=0;}
    }
    internal static int CulledObjects;
    private static readonly long[] current=new long[16];
    private static readonly int[] data=new int[32];
    private static int frames;
    private static long previous;
    private static readonly long[] intervals=new long[120];
    private static int intervalHead,intervalCount;
    private static long intervalSum,sampleAt;
    private static bool hasSample;
    private static readonly string[] lines=new string[7];
    // Replaced by the fail-closed native build hook. Values are bytes in MEM1.
    internal static int Memory(int kind)=>throw new PlatformNotSupportedException("Performance memory requires AOT");
    private static string Ms(long us)=>(us/1000)+"."+(us%1000/100);
    private static string KiB(int bytes)=>(bytes/1024)+" KiB";
    internal static long Start()=>Enabled?Clock.Timestamp:0;
    internal static void Add(int slot,long microseconds){if(Enabled)current[slot]+=microseconds;}
    internal static void End(int slot,long start){if(Enabled&&start!=0)current[slot]+=Clock.Timestamp-start;}
    internal static void Begin(){if(!Enabled)return;for(int i=0;i<16;i++)current[i]=0;CulledObjects=0;}
    internal static void Complete(int triangles,int terrainTriangles,int rejected,int scenario)
    {
        if(!Enabled)return;long now=Clock.Timestamp;current[12]=previous==0?0:Math.Max(0,now-previous);previous=now;
        if(current[12]>0){
            if(intervalCount==intervals.Length)intervalSum-=intervals[intervalHead];else intervalCount++;
            intervals[intervalHead]=current[12];intervalSum+=current[12];intervalHead=(intervalHead+1)%intervals.Length;
        }
        current[10]=GpuProfiler.GpuWaitMicroseconds;current[11]=GpuProfiler.RetraceWaitMicroseconds;
        // 0.1 ms units support 6.55-second frames without hiding slow paths.
        current[0]=Math.Max(0,current[0]-current[1]);
        current[6]=Math.Max(0,current[6]-current[15]);
        current[7]=Math.Max(0,current[7]-current[3]);
        data[0]=0x4154;data[1]=4;data[2]=++frames&65535;data[3]=0;
        for(int i=0;i<16;i++){if(current[i]/100>65535)data[3]|=1<<i;data[4+i]=(int)Math.Min(65535,current[i]/100);}
        data[30]=scenario;data[31]=0;
        data[20]=triangles&65535;data[21]=(int)((uint)triangles>>16);data[22]=terrainTriangles;data[23]=rejected;data[24]=CulledObjects;
        var submitted=GpuProfiler.Snapshot();
        data[25]=TextureCache.ResidentBytes/1024;data[26]=GpuProfiler.DrawingApiCalls;data[27]=GpuProfiler.MaterialChanges;data[28]=submitted.DrawSubmissions;data[29]=submitted.TextureBindings;
        for(int i=0;i<31;i++)data[31]^=data[i]&65535;
        if(intervalCount>0&&(sampleAt==0||now-sampleAt>=250000)){
            sampleAt=now;long mean=intervalSum/intervalCount,min=long.MaxValue,max=0;
            for(int i=0;i<intervalCount;i++){min=Math.Min(min,intervals[i]);max=Math.Max(max,intervals[i]);}
            long fps=10000000L*intervalCount/intervalSum;
            lines[0]="FPS "+fps/10+"."+fps%10+"   Frame "+Ms(current[12])+" ms";
            lines[1]="Window ms: "+Ms(min)+" / "+Ms(mean)+" / "+Ms(max)+" min/avg/max";
            lines[2]="Polygons (tris): "+triangles+"   Draws: "+submitted.DrawSubmissions;
            lines[3]="Main RAM: "+KiB(Memory(0))+" / "+KiB(Memory(1));
            lines[4]="Textures in RAM: "+KiB(Memory(2));
            lines[5]="Draw resources: "+KiB(GraphicsDevice.Default.NativeBytes);
            lines[6]="GX wait "+Ms(current[10])+" ms   VSync "+Ms(current[11])+" ms";
            hasSample=true;
        }
    }
    internal static void Draw(Draw2D ui,UiFont font)
    {
        if(!Enabled)return;long start=Clock.Timestamp;
        ui.FillRectangle(new RectF(20,82,440,260),new Color32(16,24,24,240));
        font.Draw(ui,"PERFORMANCE   L+R+Z to hide",30,90,Color32.White,.6f);
        if(hasSample)for(int i=0;i<lines.Length;i++)font.Draw(ui,lines[i],30,112+i*19,Color32.White,.52f);
        else font.Draw(ui,"Collecting frame samples...",30,112,Color32.White,.52f);
        font.Draw(ui,"GX uses main RAM; no separate VRAM",30,248,Color32.White,.5f);
        // Bounded history: each bar is a completed frame, newest on the right.
        // 33.3 ms target line; values above 66.7 ms saturate and remain red.
        ui.FillRectangle(new RectF(30,300,420,1),new Color32(100,145,145));
        for(int i=0;i<intervalCount;i++){
            int index=(intervalHead-intervalCount+i+intervals.Length)%intervals.Length;
            float h=(float)Math.Min(40,intervals[index]*40/66667.0);
            ui.FillRectangle(new RectF(30+(120-intervalCount+i)*3.5f,320-h,3,h),intervals[index]>33334?new Color32(240,130,95):new Color32(110,215,170));
        }
        font.Draw(ui,"Frame history / 33.3 ms line / 66.7 ms top",30,325,Color32.White,.45f);
        ui.FillRectangle(new RectF(0,448,640,32),new Color32(16,16,16));
        for(int at=0;at<512;){int length=Hypercube.TelemetryRuns.Length(data,at);if(Hypercube.TelemetryRuns.Lit(data,at))ui.FillRectangle(new RectF(at%64*10,448+at/64*4,length*10,4),new Color32(240,240,240));at+=length;}
        current[14]+=Clock.Timestamp-start;
    }
}
