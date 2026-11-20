using AnimalTown;
using Dolphin.Storage;

internal sealed class CardSaveStorage : ISaveStorage,ISaveSnapshotStorage
{
    private readonly CardArchive archive;
    internal CardSaveStorage(MemoryCard card,byte[] banner,byte[] icon,int device,GameState? textReuse=null){archive=new CardArchive(card,banner,icon,device,textReuse);}
    internal string[] Names()=>archive.Names();
    public byte[] Read(string name)=>archive.Read(name);
    public bool Matches(string name,byte[]? expected,int sequence,int length,uint checksum,bool ignoreSequence)=>archive.Matches(name,expected,sequence,length,checksum,ignoreSequence);
    public void Write(string name,byte[] bytes)=>archive.Write(name,bytes);
}
