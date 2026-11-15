namespace AnimalTown;

// Console preferences are independent of the town and debug configuration.
public sealed class UserOptions
{
    // Zero preserves boot output until a progressive mode is explicitly chosen.
    public int Resolution;
    public bool Widescreen;
    public bool ShowDebugMenus;
    public byte[] Encode()
    {
        if(Resolution<0||Resolution>2)throw new InvalidOperationException("Invalid video resolution");
        var bytes=new byte[16];bytes[0]=65;bytes[1]=84;bytes[2]=79;bytes[3]=2;
        bytes[4]=(byte)Resolution;bytes[5]=(byte)(Widescreen?1:0);bytes[6]=(byte)(ShowDebugMenus?1:0);
        uint hash=Hash(bytes);for(int i=0;i<4;i++)bytes[12+i]=(byte)(hash>>(8*i));return bytes;
    }
    public static UserOptions Decode(byte[] bytes)
    {
        if(bytes.Length!=16||bytes[0]!=65||bytes[1]!=84||bytes[2]!=79||(bytes[3]!=1&&bytes[3]!=2)||bytes[4]>2||bytes[5]>1||bytes[3]==2&&bytes[6]>1)throw new IOException("Invalid options");
        for(int i=bytes[3]==1?6:7;i<12;i++)if(bytes[i]!=0)throw new IOException("Invalid options padding");
        uint expected=0;for(int i=0;i<4;i++)expected|=(uint)bytes[12+i]<<(8*i);
        if(expected!=Hash(bytes))throw new IOException("Damaged options");
        return new UserOptions{Resolution=bytes[4],Widescreen=bytes[5]!=0,ShowDebugMenus=bytes[3]==2&&bytes[6]!=0};
    }
    private static uint Hash(byte[] bytes)
    {uint hash=2166136261;for(int i=0;i<12;i++)hash=unchecked((hash^bytes[i])*16777619);return hash;}
}
