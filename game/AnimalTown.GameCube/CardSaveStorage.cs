using AnimalTown;
using Dolphin.Storage;

internal sealed class CardSaveStorage : ISaveStorage
{
    private readonly MemoryCard card;
    private readonly byte[] banner,icon;
    internal CardSaveStorage(MemoryCard card,byte[] banner,byte[] icon){this.card=card;this.banner=banner;this.icon=icon;}
    public byte[] Read(string name)=>card.ReadAllBytes(name);
    public void Write(string name,byte[] bytes)
    {card.WriteSave(name,bytes,new MemoryCardSaveOptions{Title="AnimalTown",Comment=BuildInfo.Commit+" / "+name,Banner=banner,Icon=icon});}
}
