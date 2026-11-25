namespace AnimalTown;

// Independent of town saves. Location actions 14/15 are never startup settings.
public sealed class DevConfig
{
    public const int Count=34,RetiredFullResolutionAaIndex=30,GroundMipBandsIndex=31,RetiredWarmTerrainPrefetchIndex=32,ProfilingModeIndex=33,SdkPerformanceIndex=21,TreeNoiseStrength=27,TreeNoiseScale=28,RetiredTerrainCacheIndex=29;
    public const int SettingCount=Count-16;
    public readonly int[] Values=new int[Count];
    // Migration input only. New debug records never store the player's video
    // preference; UserOptions owns it after the old record has been imported.
    public bool? LegacyEdgeSmoothing {get;private set;}
    public bool SdkPerformanceOverlay {get;private set;}
    private int profilingMode=-1;
    public int ProfilingMode=>profilingMode<0?(Values[0]==0?0:2):profilingMode;
    public void SelectProfilingMode(int mode){if(mode<0||mode>3)throw new ArgumentOutOfRangeException(nameof(mode));profilingMode=mode;}
    public static string ProfilingLabel(int mode)=>mode==0?"Off":mode==1?"Counters":mode==2?"Overlay":"Capture";
    public static bool SessionOnly(int row)=>row==SdkPerformanceIndex||row==ProfilingModeIndex;
    public DevConfig(int runtimeMode=4)
    {
        Values[1]=Values[4]=Values[5]=Values[6]=Values[7]=Values[22]=1;
        Values[8]=2;Values[18]=0;Values[20]=100;
        Values[TreeNoiseStrength]=10;Values[TreeNoiseScale]=3;
        Values[11]=runtimeMode&1;Values[12]=(runtimeMode>>1)&1;Values[13]=(runtimeMode>>2)&1;
    }
    // Retired fields keep their historic value ranges for old cards.
    public static bool IsSetting(int row)=>row>=0&&row<Count&&row!=1&&row!=4&&row!=5&&row!=6&&
        row!=7&&row!=8&&row!=9&&row!=10&&row!=14&&row!=15&&row!=20&&row!=TreeNoiseStrength&&row!=TreeNoiseScale&&row!=RetiredTerrainCacheIndex&&row!=RetiredFullResolutionAaIndex&&row!=RetiredWarmTerrainPrefetchIndex;
    public static bool IsSlider(int row)=>row==23;
    public static int Maximum(int row)=>row==ProfilingModeIndex?3:row==TreeNoiseStrength?30:row==TreeNoiseScale?15:row==8?2:row==10?5:row==18?4:row==20?200:row==23?RenderDebugOptions.MaximumGroundMipDistance:row==14||row==15?0:1;
    public void Reset(int row){if(IsSlider(row))Values[row]=0;}
    public void Adjust(int row,int direction)
    {
        if(!IsSetting(row))return;
        if(row==SdkPerformanceIndex){SdkPerformanceOverlay=!SdkPerformanceOverlay;return;}
        if(row==ProfilingModeIndex){profilingMode=(ProfilingMode+(direction<0?3:1))%4;return;}
        if(row==0)profilingMode=-1;
        int max=Maximum(row),next=Values[row]+(direction<0?-1:1);
        Values[row]=IsSlider(row)?Math.Max(0,Math.Min(max,next)):(next+max+1)%(max+1);
    }
    public int RuntimeMode=>Values[11]|Values[12]<<1|Values[13]<<2;
    public byte[] Encode()
    {
        var data=new byte[44];data[0]=65;data[1]=84;data[2]=68;data[3]=7;
        for(int i=0;i<Count;i++){if(SessionOnly(i)||i==RetiredFullResolutionAaIndex)continue;if(Values[i]<0||Values[i]>Maximum(i)||i==20&&Values[i]%5!=0)throw new InvalidOperationException("Invalid dev config");data[4+i]=(byte)Values[i];}
        uint hash=Hash(data,40);for(int i=0;i<4;i++)data[40+i]=(byte)(hash>>(i*8));return data;
    }
    public static DevConfig Decode(byte[] data)
    {
        if(data.Length<4||data[0]!=65||data[1]!=84||data[2]!=68||data[3]<1||data[3]>7)throw new IOException("Invalid dev config");
        int checksum=data[3]==1?32:data[3]<6?36:40,count=data[3]==1?27:data[3]==2?29:data[3]==3?30:data[3]==4?31:data[3]==5?32:Count;
        if(data.Length!=checksum+4)throw new IOException("Invalid dev config length");
        for(int i=4+count;i<checksum;i++)if(data[i]!=0)throw new IOException("Invalid dev config padding");
        uint hash=0;for(int i=0;i<4;i++)hash|=(uint)data[checksum+i]<<(i*8);if(hash!=Hash(data,checksum))throw new IOException("Damaged dev config");
        var config=new DevConfig();for(int i=0;i<count;i++){
            if(data[4+i]>Maximum(i)||i==20&&data[4+i]%5!=0||data[3]>=7&&i==RetiredFullResolutionAaIndex&&data[4+i]!=0)throw new IOException("Invalid dev option");
            if(i==RetiredFullResolutionAaIndex){if(data[3]<7)config.LegacyEdgeSmoothing=data[4+i]!=0;}
            else if(!SessionOnly(i))config.Values[i]=data[4+i];
        }return config;
    }
    private static uint Hash(byte[] data,int count){uint hash=2166136261;for(int i=0;i<count;i++)hash=unchecked((hash^data[i])*16777619);return hash;}
}
