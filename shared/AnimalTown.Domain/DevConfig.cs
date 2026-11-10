namespace AnimalTown;

// Separate from the town commit set. Indices match the debug menu; 14/15 are
// location actions and deliberately cannot be persisted as startup settings.
public sealed class DevConfig
{
    public const int Count=27;
    public readonly int[] Values=new int[Count];
    public DevConfig(int runtimeMode=0)
    {
        Values[1]=Values[4]=Values[5]=Values[6]=Values[7]=Values[22]=1;
        Values[8]=2;Values[18]=0;Values[20]=100;
        Values[11]=runtimeMode&1;Values[12]=(runtimeMode>>1)&1;Values[13]=(runtimeMode>>2)&1;
    }
    public static bool IsSetting(int row)=>row>=0&&row<Count&&row!=14&&row!=15;
    public static int Maximum(int row)=>row==8?2:row==10?5:row==18?4:row==20?200:row==23?10:row==14||row==15?0:1;
    public void Adjust(int row,int direction)
    {
        if(!IsSetting(row))return;
        int step=row==20?5:1,max=Maximum(row),next=Values[row]+(direction<0?-step:step);
        Values[row]=row==20||row==23?Math.Max(0,Math.Min(max,next)):(next+max+1)%(max+1);
    }
    public int RuntimeMode=>Values[11]|Values[12]<<1|Values[13]<<2;
    public byte[] Encode()
    {
        var data=new byte[36];data[0]=65;data[1]=84;data[2]=68;data[3]=1;
        for(int i=0;i<Count;i++){if(Values[i]<0||Values[i]>Maximum(i)||i==20&&Values[i]%5!=0)throw new InvalidOperationException("Invalid dev config");data[4+i]=(byte)Values[i];}
        uint hash=Hash(data);for(int i=0;i<4;i++)data[32+i]=(byte)(hash>>(i*8));return data;
    }
    public static DevConfig Decode(byte[] data)
    {
        if(data.Length!=36||data[0]!=65||data[1]!=84||data[2]!=68||data[3]!=1||data[31]!=0)throw new IOException("Invalid dev config");
        uint hash=0;for(int i=0;i<4;i++)hash|=(uint)data[32+i]<<(i*8);if(hash!=Hash(data))throw new IOException("Damaged dev config");
        var config=new DevConfig();for(int i=0;i<Count;i++){if(data[4+i]>Maximum(i)||i==20&&data[4+i]%5!=0)throw new IOException("Invalid dev option");config.Values[i]=data[4+i];}return config;
    }
    private static uint Hash(byte[] data){uint hash=2166136261;for(int i=0;i<32;i++)hash=unchecked((hash^data[i])*16777619);return hash;}
}
