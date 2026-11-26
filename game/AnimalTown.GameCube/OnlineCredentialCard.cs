using System.IO;
using AnimalTown;
using Dolphin.Storage;

internal static class OnlineCredentialCard
{
    internal static OnlineCredentialStore.Snapshot? Load(GameState town)
    {
        using var card=SaveCards.Mount();
        VerifyTown(card,town);
        return new OnlineCredentialStore(new Files(card)).Load();
    }

    internal static OnlineCredentialStore.Snapshot Save(GameState town,OnlineCredentialStore.Snapshot? expected,OnlineCardCredential credential)
    {
        using var card=SaveCards.Mount();
        VerifyTown(card,town);
        return new OnlineCredentialStore(new Files(card)).Save(expected,credential);
    }

    private static void VerifyTown(MemoryCard card,GameState town)
    {
        var storage=new CardSaveStorage(card,new byte[0],new byte[0],SaveCards.Device,initializeArchive:false);
        new SaveRepository(storage).VerifyRevision(town);
    }

    private sealed class Files : ISaveStorage
    {
        private readonly MemoryCard card;
        private readonly int[] lengths=new[]{-1,-1};
        internal Files(MemoryCard card)
        {
            this.card=card;
            foreach(var entry in card.GetEntries())
            {
                if(entry.Name==OnlineCredentialStore.Bank0)lengths[0]=entry.Length;
                if(entry.Name==OnlineCredentialStore.Bank1)lengths[1]=entry.Length;
            }
        }

        private static int Bank(string name)=>name==OnlineCredentialStore.Bank0?0:1;
        public byte[] Read(string name)
        {
            int length=lengths[Bank(name)];
            if(length==-1)throw new FileNotFoundException();
            if(length!=OnlineCredentialStore.RecordBytes)throw new IOException("Invalid online credential length.");
            return card.ReadAllBytes(name);
        }
        public void Write(string name,byte[] bytes)
        {
            card.WriteSave(name,bytes,new MemoryCardSaveOptions{Title="AnimalTown",Comment="Online link"});
            lengths[Bank(name)]=bytes.Length;
        }
    }
}
