using Dolphin.Graphics;

// Recording lifetime double for tools/native-water-optics.inc. This models
// ownership/admission transitions, not GX behavior or physical MEM1 headroom.
internal static class NativeWater
{
    private const int MapBytes=8192;
    private static bool slopes,lookup,active;
    private static int captureBytes,sourceWidth,sourceHeight,attemptWidth,attemptHeight,scope;
    internal static int Width=640,Height=480;
    internal static int Bytes=>(slopes?MapBytes:0)+(lookup?MapBytes:0)+captureBytes;
    internal static int Allocations {get;private set;}
    internal static int Failures {get;private set;}
    internal static int FailAtAllocation;
    internal static int InitializeCalls,PrepareCalls,ShutdownCalls,BeginCalls,ScopeCalls,CaptureCalls;
    internal static Action<int>? BeforeAllocation;

    internal static void Reset()
    {
        ClearOwners();Width=640;Height=480;Allocations=Failures=FailAtAllocation=0;
        InitializeCalls=PrepareCalls=ShutdownCalls=BeginCalls=ScopeCalls=CaptureCalls=0;
        BeforeAllocation=null;
    }
    private static bool Allocate(int bytes)
    {
        Allocations++;BeforeAllocation?.Invoke(bytes);
        if(Allocations!=FailAtAllocation)return true;
        Failures++;return false;
    }
    private static void RetireCapture()
    {
        captureBytes=sourceWidth=sourceHeight=attemptWidth=attemptHeight=0;
    }
    private static void ClearOwners()
    {
        active=false;scope=0;RetireCapture();slopes=lookup=false;
    }
    internal static int Initialize(byte[]? data)
    {
        InitializeCalls++;
        if(GraphicsDevice.Default.IsFrameActive||data==null||data.Length!=MapBytes)return 0;
        ClearOwners();
        if(!Allocate(MapBytes)){ClearOwners();return 0;}
        slopes=true;
        if(!Allocate(MapBytes)){ClearOwners();return 0;}
        lookup=true;return 1;
    }
    internal static int Prepare(float[]? settings,int enabled,int sunlight,int environmentRgb)
    {
        PrepareCalls++;
        if(GraphicsDevice.Default.IsFrameActive)return 0;
        scope=0;
        if(enabled==0){active=false;RetireCapture();return 0;}
        if(settings==null||settings.Length!=12||!slopes||!lookup){active=false;return 0;}
        for(int i=0;i<settings.Length;i++)if(!float.IsFinite(settings[i])){active=false;return 0;}
        if(settings[1]<1||settings[4]<=0||settings[7]<=0){active=false;return 0;}
        active=true;
        if(captureBytes!=0&&(Width!=sourceWidth||Height!=sourceHeight))RetireCapture();
        if(captureBytes==0&&(Width!=attemptWidth||Height!=attemptHeight)){
            attemptWidth=Width;attemptHeight=Height;
            if(Width>=8&&Height>=8&&Width<=640&&Height<=528&&((Width|Height)&7)==0){
                int bytes=Width*Height/2;
                if(Allocate(bytes)){captureBytes=bytes;sourceWidth=Width;sourceHeight=Height;}
            }
        }
        return captureBytes!=0?3:1;
    }
    internal static int Begin(){BeginCalls++;scope=0;return active?1:0;}
    internal static int Scope(int mode){ScopeCalls++;int previous=scope;scope=mode>=0&&mode<=2?mode:0;return previous;}
    internal static int Capture()
    {
        CaptureCalls++;
        return active&&captureBytes!=0&&GraphicsDevice.Default.IsFrameActive&&Width==sourceWidth&&Height==sourceHeight?1:0;
    }
    internal static int Shutdown(){ShutdownCalls++;ClearOwners();return 1;}
    internal static int Stats(int kind)=>kind switch{
        0=>Bytes,
        8=>captureBytes!=0?sourceWidth/2:0,
        9=>captureBytes!=0?sourceHeight/2:0,
        10=>slopes&&lookup?1:0,
        11=>scope,
        12=>sourceWidth,
        13=>sourceHeight,
        18=>Failures,
        _=>0
    };
}

internal static class WaterTextureAssets
{
    internal static int Reads;
    internal static byte[]? TryLoadNativeData(string name){Reads++;return null;}
    internal static void ResetReads(){Reads=0;}
}
