using System;
using AnimalTown;
using Dolphin.Diagnostics;
using Dolphin.Storage;

internal static class TownDataStorage
{
    internal static string Status="";
    internal static bool Pending()
    {
        try{using var card=Mount();return TownDataDeletion.Pending(Names(card));}
        catch(Exception){return false;}
    }
    internal static bool Delete()
    {
        try{
            using var card=Mount();TownDataDeletion.Erase(new Files(card));
            Status="Town data and all villagers deleted.";
            DebugOverlay.WriteLine("ANIMALTOWN TOWN DATA DELETED");return true;
        }catch(Exception){Status="Deletion incomplete. Check Slot A, then try again.";return false;}
    }
    private static MemoryCard Mount()=>MemoryCard.Mount(StorageDevice.MemoryCardSlotA,new MemoryCardIdentity("AMTE","01"));
    private static string[] Names(MemoryCard card)
    {var entries=card.GetEntries();var names=new string[entries.Length];for(int i=0;i<names.Length;i++)names[i]=entries[i].Name;return names;}
    private sealed class Files : ITownDataFiles
    {
        private readonly MemoryCard card;
        internal Files(MemoryCard card){this.card=card;}
        public string[] Names()=>TownDataStorage.Names(card);
        public byte[] Read(string name)=>card.ReadAllBytes(name);
        public void Write(string name,byte[] bytes)=>card.WriteSave(name,bytes,new MemoryCardSaveOptions{Title="AnimalTown",Comment="Town deletion in progress"});
        public void Delete(string name)=>card.Delete(name);
    }
}
