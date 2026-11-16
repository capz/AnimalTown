using AnimalTown;
using Dolphin.Storage;

internal sealed class CardSaveStorage : ISaveStorage
{
    private readonly CardArchive archive;
    internal CardSaveStorage(MemoryCard card,byte[] banner,byte[] icon,int device=4){archive=new CardArchive(card,banner,icon,device);}
    internal string[] Names()=>archive.Names();
    public byte[] Read(string name)=>archive.Read(name);
    public void Write(string name,byte[] bytes)=>archive.Write(name,bytes);
}
