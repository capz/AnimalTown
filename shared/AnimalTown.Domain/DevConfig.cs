namespace AnimalTown;

// Independent of town saves. Location actions 14/15 are never startup settings.
public sealed class DevConfig
{
    public const int Count=32,FullResolutionAaIndex=30,GroundMipBandsIndex=31,SdkPerformanceIndex=21,TreeNoiseStrength=27,TreeNoiseScale=28,RetiredTerrainCacheIndex=29;
    public const int SettingCount=Count-14;
    public readonly int[] Values=new int[Count];
    public bool SdkPerformanceOverlay {get;private set;}
    public DevConfig(int runtimeMode=4)
    {
        Values[1]=Values[4]=Values[5]=Values[6]=Values[7]=Values[22]=1;
        Values[8]=2;Values[18]=0;Values[20]=100;
        Values[TreeNoiseStrength]=10;Values[TreeNoiseScale]=3;
        Values[11]=runtimeMode&1;Values[12]=(runtimeMode>>1)&1;Values[13]=(runtimeMode>>2)&1;
    }
    // Retired fields keep their historic value ranges for old cards.
    public static bool IsSetting(int row)=>row>=0&&row<Count&&row!=1&&row!=4&&row!=5&&row!=6&&
        row!=7&&row!=8&&row!=9&&row!=10&&row!=14&&row!=15&&row!=20&&row!=TreeNoiseStrength&&row!=TreeNoiseScale&&row!=RetiredTerrainCacheIndex;
    public static bool IsSlider(int row)=>row==23;
    public static int Maximum(int row)=>row==TreeNoiseStrength?30:row==TreeNoiseScale?15:row==8?2:row==10?5:row==18?4:row==20?200:row==23?RenderDebugOptions.MaximumGroundMipDistance:row==14||row==15?0:1;
    public void Reset(int row){if(IsSlider(row))Values[row]=0;}
    public void Adjust(int row,int direction)
    {
        if(!IsSetting(row))return;
        if(row==SdkPerformanceIndex){SdkPerformanceOverlay=!SdkPerformanceOverlay;return;}
        int max=Maximum(row),next=Values[row]+(direction<0?-1:1);
        Values[row]=IsSlider(row)?Math.Max(0,Math.Min(max,next)):(next+max+1)%(max+1);
    }
    public int RuntimeMode=>Values[11]|Values[12]<<1|Values[13]<<2;
    public byte[] Encode()
    {
        var data=new byte[40];data[0]=65;data[1]=84;data[2]=68;data[3]=5;
        for(int i=0;i<Count;i++){if(i==SdkPerformanceIndex)continue;if(Values[i]<0||Values[i]>Maximum(i)||i==20&&Values[i]%5!=0)throw new InvalidOperationException("Invalid dev config");data[4+i]=(byte)Values[i];}
        uint hash=Hash(data,36);for(int i=0;i<4;i++)data[36+i]=(byte)(hash>>(i*8));return data;
    }
    public static DevConfig Decode(byte[] data)
    {
        if(data.Length<4||data[0]!=65||data[1]!=84||data[2]!=68||(data[3]!=1&&data[3]!=2&&data[3]!=3&&data[3]!=4&&data[3]!=5))throw new IOException("Invalid dev config");
        int checksum=data[3]==1?32:36,count=data[3]==1?27:data[3]==2?29:data[3]==3?30:data[3]==4?31:Count;
        if(data.Length!=checksum+4)throw new IOException("Invalid dev config length");
        for(int i=4+count;i<checksum;i++)if(data[i]!=0)throw new IOException("Invalid dev config padding");
        uint hash=0;for(int i=0;i<4;i++)hash|=(uint)data[checksum+i]<<(i*8);if(hash!=Hash(data,checksum))throw new IOException("Damaged dev config");
        var config=new DevConfig();for(int i=0;i<count;i++){if(data[4+i]>Maximum(i)||i==20&&data[4+i]%5!=0)throw new IOException("Invalid dev option");if(i!=SdkPerformanceIndex)config.Values[i]=data[4+i];}return config;
    }
    private static uint Hash(byte[] data,int count){uint hash=2166136261;for(int i=0;i<count;i++)hash=unchecked((hash^data[i])*16777619);return hash;}
}
