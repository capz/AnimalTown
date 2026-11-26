using System;
using System.IO;
using AnimalTown;
using AnimalTown.Online;
using Dolphin.Storage;

internal static class OnlineTravelCard
{
    internal static OnlineTravelStore.Snapshot? Load(GameState home)
    {
        using var card=SaveCards.Mount();Verify(card,home);
        return ReadMounted(card);
    }
    internal static OnlineTravelStore.Snapshot? ReadMounted(MemoryCard card)=>new OnlineTravelStore(new Files(card)).Load();
    internal static OnlineTravelStore.Snapshot Save(GameState home,OnlineTravelStore.Snapshot? expected,OnlineTravelRecord record)
    {
        using var card=SaveCards.Mount();Verify(card,home);
        return new OnlineTravelStore(new Files(card)).Save(expected,record);
    }
    internal static OnlineTravelStore.Snapshot Rebind(GameState home,OnlineTravelStore.Snapshot expected,string origin)
    {
        using var card=SaveCards.Mount();Verify(card,home);return new OnlineTravelStore(new Files(card)).RebindOrigin(expected,origin);
    }
    static void Verify(MemoryCard card,GameState home)
    {
        if(home.IsOnlineProjection||home.Visiting)throw new IOException("Select the home card before online recovery.");
        var storage=new CardSaveStorage(card,new byte[0],new byte[0],SaveCards.Device,initializeArchive:false);
        new SaveRepository(storage).VerifyRevision(home);
    }
    sealed class Files:ISaveStorage
    {
        readonly MemoryCard card;readonly int[] lengths={-1,-1};
        internal Files(MemoryCard card)
        {
            this.card=card;
            foreach(var entry in card.GetEntries())
            {if(entry.Name==OnlineTravelStore.Bank0)lengths[0]=entry.Length;if(entry.Name==OnlineTravelStore.Bank1)lengths[1]=entry.Length;}
        }
        static int Bank(string name)=>name==OnlineTravelStore.Bank0?0:1;
        public byte[] Read(string name)
        {
            int length=lengths[Bank(name)];if(length<0)throw new FileNotFoundException();
            if(length!=OnlineTravelStore.RecordBytes)throw new IOException("Unreadable online recovery record. Keep this card.");
            return card.ReadAllBytes(name);
        }
        public void Write(string name,byte[] bytes)
        {card.WriteSave(name,bytes,new MemoryCardSaveOptions{Title="AnimalTown",Comment="Online travel recovery"});lengths[Bank(name)]=bytes.Length;}
    }
}
