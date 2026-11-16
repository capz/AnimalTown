using System;
using AnimalTown;
using Dolphin.Diagnostics;
using Dolphin.Storage;

internal static class TownDataStorage
{
    internal static string Status="";
    internal static bool Pending()
    {
        try{using var card=Mount();return TownDataDeletion.Pending(new CardArchive(card,new byte[0],new byte[0]).Names());}
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
    private sealed class Files : ITownDataFiles
    {
        private readonly CardArchive archive;
        internal Files(MemoryCard card){archive=new CardArchive(card,new byte[0],new byte[0]);}
        public string[] Names()=>archive.Names();
        public byte[] Read(string name)=>archive.Read(name);
        public void Write(string name,byte[] bytes)=>archive.Write(name,bytes);
        public void Delete(string name)=>archive.Delete(name);
    }
}
