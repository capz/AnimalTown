using System;
using Dolphin.Graphics;
using Dolphin.Timing;
using Dolphin.Diagnostics;
using Hypercube;
using Hypercube.GameCube;
using AnimalTown;

// Guest-clock CPU/submission timings. GPU wait is blocking duration, not GPU time.
// Pixel telemetry can be read from unmodified real hardware output as well as Dolphin.
internal static class FrameProfile
{
    internal static readonly RenderCounters Counters=new RenderCounters();
    internal static int Mode {get;private set;}
    internal static bool Enabled {get=>Mode!=0;set=>SetMode(value?2:0);}
    internal static bool OverlayVisible=>Mode==2;
    internal static string Status="Off";
    private static ProfileCapture? capture;
    private static uint session,sequence,memorySequence,memoryWireSequence;
    private static readonly int[] identity=new int[16],memoryPacket=new int[16],memory=new int[32],memoryWire=new int[32];
    private static long memoryAt,memoryWireAt;
    private static int memoryPart,memoryHold,checkpoint,warmQueued;
    private static bool warmTerrain;
    internal static void SetMode(int mode)
    {
        if(mode==Mode)return;
        bool starting=Mode==0;
        bool releasing=capture!=null;capture=null;Mode=mode;previous=0;
        if(releasing)AnimalTown.GameCube.NativeTexturePages.Collect();
        if(starting){session=unchecked((uint)Clock.Timestamp+(session+1)*2654435761u);sequence=0;memoryAt=0;memoryPart=memoryHold=0;}
        if(mode!=0){
            SampleMemory(true);
            if(mode==3){
                long free=(long)memory[3]+memory[4],managed=(long)memory[9]-memory[7];
                if((memory[1]&1)!=0&&free>=ProfileCapture.ReservedBytes+262144&&managed>=ProfileCapture.ReservedBytes+32768){
                    try{capture=new ProfileCapture(session);}catch(OutOfMemoryException){Mode=1;}
                }else Mode=1;
            }
            if(starting)DebugOverlay.WriteLine("ANIMALTOWN PROFILE BUILD "+BuildInfo.Version+" "+BuildInfo.Commit);
        }
        Status=mode==3&&capture==null?"Capture refused: low memory; counters active":DevConfig.ProfilingLabel(Mode);
        if(mode==0){NativeFrame.Reset(0);AnimalTown.GameCube.NativeTexturePages.Collect();}
    }
    internal static void DumpCapture()
    {
        var saved=capture;if(saved==null){DebugOverlay.WriteLine("ANIMALTOWN PROFILE: select Capture first");return;}
        capture=null;Mode=1;SampleMemory(true);
        NativeFrame.Dump(saved.Words,saved.Count,memory,saved.Session,saved.Total);
        NativeFrame.DumpMetrics(saved.IntervalHistogram,saved.PhasePeaks,saved.Events,saved.EventCount,saved.DroppedEvents,saved.Session);
        previous=0;Status="Capture exported; counters active";
    }
    internal static void Checkpoint(int stage){if(!Enabled)return;checkpoint=stage;SampleMemory(true);capture?.Event(sequence,(uint)stage,(uint)Clock.Timestamp,(uint)memory[5]);}
    internal static void TerrainState(bool enabled,int queued){warmTerrain=enabled;warmQueued=queued;}
    private static void SampleMemory(bool force=false)
    {
        long now=Clock.Timestamp;if(!force&&memoryAt!=0&&now-memoryAt<250000)return;
        NativeFrame.Memory(memory);memoryAt=now;memorySequence++;
    }
    internal static int CulledObjects,AnimationUpdates;
    private static readonly long[] current=new long[16];
    private static readonly int[] data=new int[32];
    private static readonly int[] detail=new int[16],native=new int[8];
    private static readonly long[] detailTime=new long[5];
    internal static int RecycledSections;
    private static readonly int[] submissions=new int[16],currentSubmissions=new int[16];
    internal static int SubmissionStart()=>Enabled?GpuProfiler.Snapshot().DrawSubmissions:0;
    internal static void SubmissionEnd(int slot,int start){if(Enabled)currentSubmissions[slot+3]+=GpuProfiler.Snapshot().DrawSubmissions-start;}
    private static long previous;
    internal static long Start()=>Enabled?Clock.Timestamp:0;
    internal static void Add(int slot,long microseconds){if(Enabled)current[slot]+=microseconds;}
    internal static void End(int slot,long start){if(Enabled)current[slot]+=Clock.Timestamp-start;}
    internal static void Begin(){Counters.Reset();AnimationUpdates=RecycledSections=0;NativeFrame.Reset(Enabled?1:0);if(!Enabled)return;SampleMemory();for(int i=0;i<16;i++){current[i]=0;currentSubmissions[i]=0;}for(int i=0;i<5;i++)detailTime[i]=0;CulledObjects=0;}
    internal static void DetailEnd(int slot,long at){if(Enabled)detailTime[slot]+=Clock.Timestamp-at;}
    internal static void Complete(int triangles,int terrainTriangles,int rejected,int scenario,int terrainBuilds,int movement,bool groundRing,bool groundUpload,bool shadowDeferred)
    {
        if(!Enabled)return;long now=Clock.Timestamp;current[12]=previous==0?0:now-previous;previous=now;
        RenderTelemetry.CaptureCompletedFrame(Counters);
        current[10]=Counters.GpuWaitMicroseconds;current[11]=Counters.RetraceWaitMicroseconds;
        // 0.1 ms units support 6.55-second frames without hiding slow paths.
        // Simulation excludes the once-per-render animation phase.
        current[6]=Math.Max(0,current[6]-current[15]);
        current[7]=Math.Max(0,current[7]-current[3]);
        sequence++;data[0]=0x4154;data[1]=6;data[2]=(int)(sequence&65535);data[3]=0;
        for(int i=0;i<16;i++){if(current[i]/100>65535)data[3]|=1<<i;data[4+i]=(int)Math.Min(65535,current[i]/100);}
        data[30]=scenario;data[31]=0;
        data[20]=triangles&65535;data[21]=(int)((uint)triangles>>16);data[22]=terrainTriangles;data[23]=Math.Min(255,terrainBuilds)|(Math.Min(7,AnimationUpdates)<<8)|(movement>0?4096:0)|(movement==2?8192:0)|(groundRing?16384:0)|(groundUpload?32768:0)|(shadowDeferred?2048:0);data[24]=CulledObjects;
        Counters.CulledObjects=CulledObjects;
        data[25]=TextureCache.ResidentBytes/1024;data[26]=Counters.DrawingApiCalls;data[27]=Counters.MaterialChanges;data[28]=Counters.DrawSubmissions;data[29]=Counters.TextureBindings;
        for(int i=0;i<31;i++)data[31]^=data[i]&65535;
        for(int i=0;i<16;i++)submissions[i]=currentSubmissions[i];
        submissions[0]=0x4153;submissions[1]=1;submissions[2]=(int)(sequence&65535);submissions[15]=0;
        for(int i=0;i<15;i++){submissions[i]=Math.Min(65535,submissions[i]);submissions[15]^=submissions[i];}
        NativeFrame.Snapshot(native);detail[0]=0x4144;detail[1]=(int)(sequence&65535);
        for(int i=0;i<5;i++)detail[i+2]=(int)Math.Min(65535,detailTime[i]/100);
        detail[7]=Math.Min(65535,native[0]);detail[8]=Math.Min(4095,native[1])|(Math.Min(15,native[7])<<12);
        detail[9]=Math.Min(255,native[5]+native[6])|(Math.Min(255,NativeShadows.Stats(2))<<8);
        detail[10]=Math.Min(65535,native[2]);detail[11]=Math.Min(65535,native[3]);detail[12]=Math.Min(65535,native[4]);
        detail[13]=Math.Min(65535,Counters.BufferGrowths);detail[14]=Math.Min(65535,RecycledSections);detail[15]=0;
        for(int i=0;i<15;i++)detail[15]^=detail[i];
        uint interval=(uint)Math.Min(uint.MaxValue,Math.Max(0,current[12]));
        capture?.Record(sequence,interval,data,memory);
        if(memoryPart==0&&memoryHold==0){Array.Copy(memory,memoryWire,memory.Length);memoryWireSequence=memorySequence;memoryWireAt=memoryAt;}
        memoryPacket[0]=0x414d;memoryPacket[1]=1;Pair(memoryPacket,2,session);Pair(memoryPacket,4,memoryWireSequence);memoryPacket[6]=memoryPart;
        for(int i=0;i<4;i++)Pair(memoryPacket,7+i*2,unchecked((uint)memoryWire[memoryPart*4+i]));
        memoryPacket[15]=ProfileCapture.Crc(65535,memoryPacket,15);
        identity[0]=0x4149;identity[1]=1;Pair(identity,2,session);Pair(identity,4,sequence);Pair(identity,6,interval);
        identity[8]=Mode|(warmTerrain?256:0)|(checkpoint<<10);Pair(identity,9,memoryWireSequence);
        identity[11]=(int)Math.Min(65535,Math.Max(0,now-memoryWireAt)/1000);identity[12]=Math.Min(65535,warmQueued);identity[13]=VideoOptions.FullResolutionAa?1:0;identity[14]=0;
        int crc=ProfileCapture.Crc(65535,data,32);crc=ProfileCapture.Crc(crc,detail,16);crc=ProfileCapture.Crc(crc,submissions,16);crc=ProfileCapture.Crc(crc,memoryPacket,16);
        identity[15]=ProfileCapture.Crc(crc,identity,15);
        if(++memoryHold==2){memoryHold=0;memoryPart=(memoryPart+1)%8;}
        int pathIndex=AaStudy.CaptureIndex;PathProfile.Record(pathIndex,current,native);
        AaStudy.Record(current[12],current[10],current[11],current[1],Counters.DrawSubmissions,triangles);
        PathProfile.Flush(pathIndex);
    }
    internal static void Draw(Draw2D ui)
    {
        if(!OverlayVisible||AaStudy.Active)return;long start=Clock.Timestamp;
        Band(ui,identity,400);Band(ui,memoryPacket,384);
        ui.FillRectangle(new RectF(0,448,640,32),new Color32(16,16,16));
        for(int at=0;at<512;){int length=Hypercube.TelemetryRuns.Length(data,at);if(Hypercube.TelemetryRuns.Lit(data,at))ui.FillRectangle(new RectF(at%64*10,448+at/64*4,length*10,4),new Color32(240,240,240));at+=length;}
        ui.FillRectangle(new RectF(0,432,640,16),new Color32(16,16,16));
        for(int at=0;at<256;){int length=Hypercube.TelemetryRuns.Length(detail,at);if(Hypercube.TelemetryRuns.Lit(detail,at))ui.FillRectangle(new RectF(at%64*10,432+at/64*4,length*10,4),new Color32(240,240,240));at+=length;}
        ui.FillRectangle(new RectF(0,416,640,16),new Color32(16,16,16));
        for(int at=0;at<256;){int length=Hypercube.TelemetryRuns.Length(submissions,at);if(Hypercube.TelemetryRuns.Lit(submissions,at))ui.FillRectangle(new RectF(at%64*10,416+at/64*4,length*10,4),new Color32(240,240,240));at+=length;}
        current[14]+=Clock.Timestamp-start;
    }
    private static void Pair(int[] packet,int at,uint value){packet[at]=(int)(value&65535);packet[at+1]=(int)(value>>16);}
    private static void Band(Draw2D ui,int[] packet,int y)
    {
        ui.FillRectangle(new RectF(0,y,640,16),new Color32(16,16,16));
        for(int at=0;at<256;){int length=Hypercube.TelemetryRuns.Length(packet,at);if(Hypercube.TelemetryRuns.Lit(packet,at))ui.FillRectangle(new RectF(at%64*10,y+at/64*4,length*10,4),new Color32(240,240,240));at+=length;}
    }
}
