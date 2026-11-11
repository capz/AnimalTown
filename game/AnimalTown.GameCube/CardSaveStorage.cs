using AnimalTown;
using Dolphin.Storage;

internal sealed class CardSaveStorage : ISaveStorage
{
    private readonly MemoryCard card;
    private readonly byte[] banner,icon;
    // The directory already identifies each record. Appending its name can
    // exceed the card's 31-byte ASCII comment limit as version counters grow.
    private static readonly string saveComment=BuildInfo.Version.Length<=31?BuildInfo.Version:BuildInfo.Commit;
    internal CardSaveStorage(MemoryCard card,byte[] banner,byte[] icon){this.card=card;this.banner=banner;this.icon=icon;}
    public byte[] Read(string name){LoadingScreen.Pulse();return card.ReadAllBytes(name);}
    public void Write(string name,byte[] bytes)
    {card.WriteSave(name,bytes,new MemoryCardSaveOptions{Title="AnimalTown",Comment=saveComment,Banner=banner,Icon=icon});}
}
