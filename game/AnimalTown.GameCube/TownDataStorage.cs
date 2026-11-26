using System;
using System.Collections.Generic;
using AnimalTown;
using Dolphin.Diagnostics;
using Dolphin.Storage;

internal static class TownDataStorage
{
    internal static string Status="";
    internal static bool Pending()
    {
        if(!SaveCards.Enabled)return false;
        try{using var card=SaveCards.Mount();return TownDataDeletion.Pending(new CardArchive(card,new byte[0],new byte[0],SaveCards.Device).Names());}
        catch(Exception){return false;}
    }
    internal static bool Delete()
    {
        if(!SaveCards.Enabled){Status="No memory card selected. Town data was kept.";return false;}
        try{
            using var card=SaveCards.Mount();TownDataDeletion.Erase(new Files(card));
            Status="Town data and all villagers deleted.";
            DebugOverlay.WriteLine("ANIMALTOWN TOWN DATA DELETED");return true;
        }catch(Exception){Status="Deletion incomplete. Check "+SaveCards.Slot+", then try again.";return false;}
    }
    private sealed class Files : ITownDataFiles
    {
        private readonly MemoryCard card;
        private readonly CardArchive archive;
        internal Files(MemoryCard card){this.card=card;archive=new CardArchive(card,new byte[0],new byte[0],SaveCards.Device);}
        public string[] Names()
        {
            var names=new List<string>(archive.Names());
            foreach(var entry in card.GetEntries())
                if(TownDataDeletion.IsCredentialFile(entry.Name)&&!names.Contains(entry.Name))names.Add(entry.Name);
            return names.ToArray();
        }
        public byte[] Read(string name)=>archive.Read(name);
        public void Write(string name,byte[] bytes)=>archive.Write(name,bytes);
        public void Delete(string name)
        {
            if(TownDataDeletion.IsCredentialFile(name))card.Delete(name);
            else archive.Delete(name);
        }
    }
}
