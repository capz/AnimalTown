namespace AnimalTown;

// One exclusive card transaction only. Never reuse across saves or mounts.
// A write invalidates BEFORE touching storage, including when it throws: its
// first verification read must always go to the physical card.
public sealed class SaveReadCache : ISaveStorage
{
    private readonly ISaveStorage storage;
    private readonly string?[] names=new string?[24];
    private readonly byte[]?[] data=new byte[]?[24];
    private int bytes,next;
    public int PhysicalReads {get;private set;}
    public int CacheHits {get;private set;}
    public SaveReadCache(ISaveStorage storage){this.storage=storage;}
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
    public void Write(string name,byte[] value)
    {
        for(int i=0;i<names.Length;i++)if(names[i]==name){bytes-=data[i]!.Length;data[i]=null;names[i]=null;}
        storage.Write(name,value);
    }
}
