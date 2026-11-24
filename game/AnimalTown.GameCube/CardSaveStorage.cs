using AnimalTown;
using Dolphin.Storage;

internal sealed class CardSaveStorage : ISaveStorage,ISaveSnapshotStorage,ISaveVerifiedPrefixStorage,ISaveVerifiedRecordStorage,ISaveWritePreparationStorage
{
    private readonly CardArchive archive;
    internal CardSaveStorage(MemoryCard card,byte[] banner,byte[] icon,int device,GameState? textReuse=null,bool preserveVillagerLayout=false,bool initializeArchive=true)
    {archive=new CardArchive(card,banner,icon,device,textReuse,preserveVillagerLayout,initializeArchive);}
    internal string[] Names()=>archive.Names();
    internal bool UpdateVillagerFiles(GameState state)=>archive.UpdateVillagerFiles(state);
    internal void MigrateSettings()=>archive.MigrateSettings();
    internal void ReuseTextFrom(GameState state)=>archive.ReuseTextFrom(state);
    internal static void SaveTravelCheckpoint(SaveRepository repository,GameState state)
    {
        // The chooser's preview was read on an earlier mount. Verify the
        // selected town again before writing to a card that could have changed.
        var saved=repository.ReadTravelPreview();
        if(saved.TownId!=state.Town.Jobs.TownId)
            throw new System.InvalidOperationException("Town card changed. Return to title and reload.");
        repository.Save(state);
    }
    public void PrepareWrite()=>archive.PrepareWrite();
    public byte[] Read(string name)=>archive.Read(name);
    public byte[] ReadVerifiedPrefix(string name,int count,int sequence,int length,uint checksum)=>archive.ReadVerifiedPrefix(name,count,sequence,length,checksum);
    public ISaveWordReader OpenVerifiedRecord(string name,int sequence,int length,uint checksum)=>archive.OpenVerifiedRecord(name,sequence,length,checksum);
    public bool Matches(string name,byte[]? expected,int sequence,int length,uint checksum,bool ignoreSequence)=>archive.Matches(name,expected,sequence,length,checksum,ignoreSequence);
    public void Write(string name,byte[] bytes)=>archive.Write(name,bytes);
}
