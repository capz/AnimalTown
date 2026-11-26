using System.IO;

namespace AnimalTown;

public sealed class OnlineCardCredential
{
    public readonly string Origin, Town, AccountId, DeviceId, RefreshToken, RotationId;
    public readonly int Generation;

    public OnlineCardCredential(string origin,string town,string accountId,string deviceId,string refreshToken,int generation,string rotationId="")
    {
        if(!ValidOrigin(origin)||!Hex(town,0,16)||town=="0000000000000000"||
            !Identifier(accountId)||!Identifier(deviceId)||!refreshToken.StartsWith("r2_")||!Hex(refreshToken,3,64)||
            generation<1||rotationId.Length!=0&&!Identifier(rotationId))
            throw new FormatException("Invalid online credential record.");
        Origin=origin;Town=town;AccountId=accountId;DeviceId=deviceId;
        RefreshToken=refreshToken;Generation=generation;RotationId=rotationId;
    }

    public OnlineCardCredential WithRotation(string id)=>new OnlineCardCredential(Origin,Town,AccountId,DeviceId,RefreshToken,Generation,id);

    public static bool ValidOrigin(string value)
    {
        const string scheme="https://";
        if(value.Length<=scheme.Length||value.Length>71)return false;
        for(int i=0;i<scheme.Length;i++)if(value[i]!=scheme[i])return false;
        int labelLength=0;
        for(int i=scheme.Length;i<value.Length;i++)
        {
            char c=value[i];
            if(c=='.'){
                if(labelLength==0||value[i-1]=='-')return false;
                labelLength=0;
                continue;
            }
            if(!(c>='a'&&c<='z'||c>='0'&&c<='9'||c=='-'&&labelLength>0))return false;
            labelLength++;
        }
        return labelLength>0&&value[value.Length-1]!='-';
    }

    public static bool Hex(string value,int offset,int length)
    {
        if(value.Length!=offset+length)return false;
        for(int i=offset;i<value.Length;i++)if(!HexDigit(value[i]))return false;
        return true;
    }

    private static bool HexDigit(char c)=>c>='0'&&c<='9'||c>='a'&&c<='f'||c>='A'&&c<='F';

    public static bool Identifier(string value)
    {
        if(value.Length!=36)return false;
        for(int i=0;i<value.Length;i++){
            bool separator=i==8||i==13||i==18||i==23;
            if(separator?value[i]!='-':!HexDigit(value[i]))return false;
        }
        return true;
    }
}

// Each bank is independently committed. Verify the inactive bank before it
// may replace the last readable record. A pending rotation contains the
// old refresh token and the exact ID the server needs to recover its reply.
public sealed class OnlineCredentialStore
{
    public const string Bank0="AnimalTown-Link0",Bank1="AnimalTown-Link1";
    public const int RecordBytes=12+6+71+16+36+36+67+36+4;
    private const int Magic=0x314B4E4C;
    private readonly ISaveStorage storage;

    public OnlineCredentialStore(ISaveStorage storage){this.storage=storage;}

    public sealed class Snapshot
    {
        internal readonly int Bank, Sequence;
        internal readonly uint Checksum;
        public readonly OnlineCardCredential Credential;
        internal Snapshot(int bank,int sequence,uint checksum,OnlineCardCredential credential)
        {Bank=bank;Sequence=sequence;Checksum=checksum;Credential=credential;}
    }

    public Snapshot? Load()
    {
        var first=ReadBank(0);
        var second=ReadBank(1);
        if(first==null)return second;
        if(second==null)return first;
        if(first.Sequence==second.Sequence&&first.Checksum!=second.Checksum)
            throw new IOException("Online credential banks have conflicting revisions.");
        return first.Sequence>=second.Sequence?first:second;
    }

    public Snapshot Save(Snapshot? expected,OnlineCardCredential credential)
    {
        var current=Load();
        if(current?.Sequence!=expected?.Sequence||current?.Checksum!=expected?.Checksum)
            throw new IOException("Online credential record changed.");
        if(current?.Sequence==int.MaxValue)throw new IOException("Online credential bank sequence exhausted.");
        int bank=current==null?0:1-current.Bank;
        int sequence=(current?.Sequence??0)+1;
        byte[] bytes=Encode(sequence,credential);
        try{
            storage.Write(Name(bank),bytes);
            byte[] actual=storage.Read(Name(bank));
            try{
                if(actual.Length!=bytes.Length)throw new IOException("Online credential readback failed.");
                for(int i=0;i<bytes.Length;i++)if(actual[i]!=bytes[i])throw new IOException("Online credential readback failed.");
                return Decode(bank,actual);
            }finally{Array.Clear(actual,0,actual.Length);}
        }finally{Array.Clear(bytes,0,bytes.Length);}
    }

    private Snapshot? ReadBank(int bank)
    {
        byte[]? bytes=null;
        try{
            bytes=storage.Read(Name(bank));
            return Decode(bank,bytes);
        }catch(IOException){return null;}
        catch(FormatException){return null;}
        finally{if(bytes!=null)Array.Clear(bytes,0,bytes.Length);}
    }

    private static string Name(int bank)=>bank==0?Bank0:Bank1;

    private static byte[] Encode(int sequence,OnlineCardCredential credential)
    {
        var bytes=new byte[RecordBytes];
        Put(bytes,0,Magic);Put(bytes,4,sequence);Put(bytes,8,credential.Generation);
        int at=12;
        WriteText(bytes,ref at,credential.Origin,71);
        WriteText(bytes,ref at,credential.Town,16);
        WriteText(bytes,ref at,credential.AccountId,36);
        WriteText(bytes,ref at,credential.DeviceId,36);
        WriteText(bytes,ref at,credential.RefreshToken,67);
        WriteText(bytes,ref at,credential.RotationId,36);
        Put(bytes,at,(int)SaveCodec.Crc(bytes,0,at));
        return bytes;
    }

    private static Snapshot Decode(int bank,byte[] bytes)
    {
        if(bytes.Length!=RecordBytes||Get(bytes,0)!=Magic||Get(bytes,4)<1||
            (uint)Get(bytes,RecordBytes-4)!=SaveCodec.Crc(bytes,0,RecordBytes-4))
            throw new FormatException("Invalid online credential bank.");
        int at=12;
        string origin=ReadText(bytes,ref at,71);
        string town=ReadText(bytes,ref at,16);
        string account=ReadText(bytes,ref at,36);
        string device=ReadText(bytes,ref at,36);
        string refresh=ReadText(bytes,ref at,67);
        string rotation=ReadText(bytes,ref at,36);
        return new Snapshot(bank,Get(bytes,4),(uint)Get(bytes,at),
            new OnlineCardCredential(origin,town,account,device,refresh,Get(bytes,8),rotation));
    }

    private static void WriteText(byte[] bytes,ref int at,string text,int width)
    {
        bytes[at++]=(byte)text.Length;
        for(int i=0;i<text.Length;i++)bytes[at+i]=(byte)text[i];
        at+=width;
    }

    private static string ReadText(byte[] bytes,ref int at,int width)
    {
        int length=bytes[at++];
        if(length>width)throw new FormatException("Invalid online credential field.");
        var chars=new char[length];
        for(int i=0;i<length;i++)chars[i]=(char)bytes[at+i];
        at+=width;
        return new string(chars);
    }

    private static void Put(byte[] bytes,int at,int value)
    {for(int i=0;i<4;i++)bytes[at+i]=(byte)((uint)value>>(i*8));}
    private static int Get(byte[] bytes,int at)
    {int value=0;for(int i=0;i<4;i++)value|=bytes[at+i]<<(i*8);return value;}
}
