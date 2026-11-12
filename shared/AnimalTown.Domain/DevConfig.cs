namespace AnimalTown;

// Independent of town saves. Location actions 14/15 are never startup settings.
public sealed class DevConfig
{
    public const int Count=29,TreeNoiseStrength=27,TreeNoiseScale=28;
    public readonly int[] Values=new int[Count];
    public DevConfig(int runtimeMode=0)
    {
        Values[1]=Values[4]=Values[5]=Values[6]=Values[7]=Values[22]=1;
        Values[8]=2;Values[18]=0;Values[20]=100;
        Values[TreeNoiseStrength]=10;Values[TreeNoiseScale]=3;
        Values[11]=runtimeMode&1;Values[12]=(runtimeMode>>1)&1;Values[13]=(runtimeMode>>2)&1;
    }
    public int TreeNoiseStrengthPercent=>Values[TreeNoiseStrength]*10;
    public int TreeNoiseScalePercent=>(Values[TreeNoiseScale]+1)*25;
    public static bool IsSetting(int row)=>row>=0&&row<Count&&row!=14&&row!=15;
    public static bool IsSlider(int row)=>row==20||row==23||row==TreeNoiseStrength||row==TreeNoiseScale;
    public static int Maximum(int row)=>row==TreeNoiseStrength?30:row==TreeNoiseScale?15:row==8?2:row==10?5:row==18?4:row==20?200:row==23?10:row==14||row==15?0:1;
    public void Reset(int row){if(IsSlider(row))Values[row]=row==20?100:row==TreeNoiseStrength?10:row==TreeNoiseScale?3:0;}
    public void Adjust(int row,int direction)
    {
        if(!IsSetting(row))return;
        int step=row==20?5:1,max=Maximum(row),next=Values[row]+(direction<0?-step:step);
        Values[row]=IsSlider(row)?Math.Max(0,Math.Min(max,next)):(next+max+1)%(max+1);
    }
    public int RuntimeMode=>Values[11]|Values[12]<<1|Values[13]<<2;
    public byte[] Encode()
    {
        var data=new byte[40];data[0]=65;data[1]=84;data[2]=68;data[3]=2;
        for(int i=0;i<Count;i++){if(Values[i]<0||Values[i]>Maximum(i)||i==20&&Values[i]%5!=0)throw new InvalidOperationException("Invalid dev config");data[4+i]=(byte)Values[i];}
        uint hash=Hash(data,36);for(int i=0;i<4;i++)data[36+i]=(byte)(hash>>(i*8));return data;
    }
    public static DevConfig Decode(byte[] data)
    {
        if(data.Length<4||data[0]!=65||data[1]!=84||data[2]!=68||(data[3]!=1&&data[3]!=2))throw new IOException("Invalid dev config");
        bool legacy=data[3]==1;int checksum=legacy?32:36,count=legacy?27:Count;
        if(data.Length!=checksum+4)throw new IOException("Invalid dev config length");
        for(int i=4+count;i<checksum;i++)if(data[i]!=0)throw new IOException("Invalid dev config padding");
        uint hash=0;for(int i=0;i<4;i++)hash|=(uint)data[checksum+i]<<(i*8);if(hash!=Hash(data,checksum))throw new IOException("Damaged dev config");
        var config=new DevConfig();for(int i=0;i<count;i++){if(data[4+i]>Maximum(i)||i==20&&data[4+i]%5!=0)throw new IOException("Invalid dev option");config.Values[i]=data[4+i];}return config;
    }
    private static uint Hash(byte[] data,int count){uint hash=2166136261;for(int i=0;i<count;i++)hash=unchecked((hash^data[i])*16777619);return hash;}
}
