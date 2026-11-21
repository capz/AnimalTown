namespace AnimalTown;

// One exclusive card transaction only. Never reuse across saves or mounts.
// A write invalidates BEFORE touching storage, including when it throws: its
// first verification read must always go to the physical card.
public sealed class SaveReadCache : ISaveStorage,ISaveSnapshotStorage,ISaveVerifiedPrefixStorage,ISaveWritePreparationStorage
{
    private readonly ISaveStorage storage;
    private readonly string?[] names=new string?[24];
    private readonly byte[]?[] data=new byte[]?[24];
    private int bytes,next;
    public int PhysicalReads {get;private set;}
    public int CacheHits {get;private set;}
    public SaveReadCache(ISaveStorage storage){this.storage=storage;}
    public void PrepareWrite(){if(storage is ISaveWritePreparationStorage preparation)preparation.PrepareWrite();}
    public byte[] Read(string name)
    {
        for(int i=0;i<names.Length;i++)if(names[i]==name){CacheHits++;return data[i]!;}
        PhysicalReads++;var value=storage.Read(name);
        if(value.Length<=8192){
            while(bytes+value.Length>65536||data[next]!=null){
                bytes-=data[next]?.Length??0;data[next]=null;names[next]=null;
                next=(next+1)%names.Length;
            }
            names[next]=name;data[next]=value;bytes+=value.Length;next=(next+1)%names.Length;
        }
        return value;
    }
    public bool Matches(string name,byte[]? expected,int sequence,int length,uint checksum,bool ignoreSequence)
    {if(storage is ISaveSnapshotStorage verified){PhysicalReads++;return verified.Matches(name,expected,sequence,length,checksum,ignoreSequence);}var actual=Read(name);if(actual.Length!=length)return false;if(sequence>=0){int seq=0;for(int i=0;i<4;i++)seq|=actual[16+i]<<(i*8);if(seq!=sequence||SaveCodec.Crc(actual,0,actual.Length-4)!=checksum)return false;}if(expected!=null)for(int i=0;i<actual.Length;i++)if(!(ignoreSequence&&(i>=16&&i<20||i>=actual.Length-4))&&actual[i]!=expected[i])return false;return true;}
    public byte[] ReadVerifiedPrefix(string name,int count,int sequence,int length,uint checksum)
    {
        if(storage is ISaveVerifiedPrefixStorage verified){PhysicalReads++;return verified.ReadVerifiedPrefix(name,count,sequence,length,checksum);}
        var bytes=Read(name);if(count<0||count>length||bytes.Length!=length||length<32)throw new InvalidOperationException("Invalid save prefix length");
        int seq=0,crc=0;for(int i=0;i<4;i++){seq|=bytes[16+i]<<(i*8);crc|=bytes[length-4+i]<<(i*8);}
        if(seq!=sequence||(uint)crc!=checksum||SaveCodec.Crc(bytes,0,length-4)!=checksum)throw new InvalidOperationException("Invalid save prefix reference");
        var prefix=new byte[count];Array.Copy(bytes,prefix,count);return prefix;
    }
    public void Write(string name,byte[] value)
    {
        for(int i=0;i<names.Length;i++)if(names[i]==name){bytes-=data[i]!.Length;data[i]=null;names[i]=null;}
        storage.Write(name,value);
    }
}
