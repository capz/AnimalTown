using System;
using System.IO;
using System.Threading.Tasks;
using Dolphin.Timing;
using AnimalTown;

internal static class NativeTextures
{
    internal static int Init(byte[] catalog)=>throw new PlatformNotSupportedException("Native texture catalog required");
    internal static int Begin(int startup)=>throw new PlatformNotSupportedException("Native texture scheduler required");
    internal static int Step(int allowRead)=>throw new PlatformNotSupportedException("Native texture scheduler required");
    internal static int Read()=>throw new PlatformNotSupportedException("Native texture I/O required");
    internal static int Complete(int count)=>throw new PlatformNotSupportedException("Native texture I/O required");
    internal static int Stats(int kind)=>throw new PlatformNotSupportedException("Native texture accounting required");
    internal static int Flags(int flags)=>throw new PlatformNotSupportedException("Native texture policy required");
    internal static int HasModel(string path)=>throw new PlatformNotSupportedException("Native model catalog required");
    internal static int PoseRegister(int actor,int model,int expectedBytes)=>throw new PlatformNotSupportedException("Native pose registration required");
    internal static int PoseActor(int actor)=>throw new PlatformNotSupportedException("Native pose grouping required");
    internal static int PoseBatch(int begin)=>throw new PlatformNotSupportedException("Native pose grouping required");
    internal static int GeometryScope(int enabled)=>throw new PlatformNotSupportedException("Native geometry cache required");
    internal static int Demand(int slot,int pixels,int priority)=>throw new PlatformNotSupportedException("Native texture demand required");
}
internal static class TextureStreaming
{
    private static Task<int>? read;
    internal static int Features=127;
    internal static long WorstMicroseconds;
    internal static int LongestReadFrames;private static int frame,readStarted;
    internal static bool Reading=>read!=null;
    internal static bool BasesReady=>NativeTextures.Stats(0)==1;
    internal static void Initialize()
    {if(NativeTextures.Init(File.ReadAllBytes("dvd:/textures.idx"))!=1)throw new InvalidOperationException("Invalid texture catalog or native budget");}
    internal static void Begin(bool startup)
    {
        frame++;long at=FrameProfile.Start();Poll();FrameProfile.End(2,at);if(NativeTextures.Begin(startup?1:0)!=1)throw new InvalidOperationException("Texture frame ownership");
    }
    private static void Poll()
    {
        if(read==null||!read.IsCompleted)return;
        int count;try{count=read.GetAwaiter().GetResult();}catch{count=-1;}
        if(NativeTextures.Complete(count)<0)throw new IOException("Native texture lease could not be restored");
        int elapsed=frame-readStarted;if(elapsed>LongestReadFrames)LongestReadFrames=elapsed;read=null;
    }
    private static int ReadNative()=>NativeTextures.Read();
    internal static void Advance(bool allowRead)
    {
        Poll();long start=Clock.Timestamp,deadline=start+1000;
        if(read!=null){if(NativeTextures.Step(0)<0)throw new IOException("Required texture restore failed");RecordWork(start);return;}
        do{
            int action=NativeTextures.Step(allowRead?1:0);
            if(action<0)throw new IOException("Required texture could not be restored");
            if(action==0)break;
            if(action==1){readStarted=frame;read=Task.Run(ReadNative);break;}
        }while(Clock.Timestamp<deadline);
        RecordWork(start);
    }
    private static void RecordWork(long start){long elapsed=Clock.Timestamp-start;if(elapsed>WorstMicroseconds)WorstMicroseconds=elapsed;}
    internal static string ModelPath(string name,TextureResolution resolution=TexturePolicy.Default)
    {
        string original=TexturePolicy.ModelPath(name,resolution);
        return NativeTextures.HasModel(original)==1?original.Substring(0,original.Length-9)+"-stream.dndmodel":original;
    }
    internal static void Request(int slot,int pixels,int priority=2)
    {if(NativeTextures.Demand(slot,pixels,priority)!=1)throw new InvalidOperationException("Texture demand bounds");}
    internal static void Toggle(int bit){Features^=bit;if(NativeTextures.Flags(Features)<0)throw new InvalidOperationException("Texture policy outside frame");}
    internal static bool Enabled(int bit)=>(Features&bit)!=0;
}
