using AnimalTown;
using Dolphin.Storage;

internal sealed class CardSaveStorage : ISaveStorage,ISaveSnapshotStorage,ISaveVerifiedPrefixStorage,ISaveWritePreparationStorage
{
    private readonly CardArchive archive;
    internal CardSaveStorage(MemoryCard card,byte[] banner,byte[] icon,int device,GameState? textReuse=null){archive=new CardArchive(card,banner,icon,device,textReuse);}
    internal string[] Names()=>archive.Names();
    internal bool UpdateVillagerFiles(GameState state)=>archive.UpdateVillagerFiles(state);
    internal void MigrateSettings()=>archive.MigrateSettings();
    internal void ReuseTextFrom(GameState state)=>archive.ReuseTextFrom(state);
    public void PrepareWrite()=>archive.PrepareWrite();
    public byte[] Read(string name)=>archive.Read(name);
    public byte[] ReadVerifiedPrefix(string name,int count,int sequence,int length,uint checksum)=>archive.ReadVerifiedPrefix(name,count,sequence,length,checksum);
    public bool Matches(string name,byte[]? expected,int sequence,int length,uint checksum,bool ignoreSequence)=>archive.Matches(name,expected,sequence,length,checksum,ignoreSequence);
    public void Write(string name,byte[] bytes)=>archive.Write(name,bytes);
}
