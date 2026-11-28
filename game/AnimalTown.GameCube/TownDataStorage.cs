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
    internal static bool RequiresOnlineReview()
    {
        if(!SaveCards.Enabled||Pending())return false;
        try
        {
            using var card=SaveCards.Mount();
            foreach(var entry in card.GetEntries())if(TownDataDeletion.IsOnlineFile(entry.Name))return true;
            return false;
        }
        catch(Exception){return true;}
    }
    internal static void DeleteOnline(DevConfig config)
    {
#if ANIMALTOWN_ONLINE_PLAY
        var session=TownSession.Load();
        if(session.Loaded&&!session.Damaged&&OnlinePlay.CloseLastResident(config,session,true)){Delete(true);return;}
        Status="Town kept. Finish online returns and closure before erasing.";
#else
        Status="Use an online-enabled build to close this town before erasing.";
#endif
    }
    internal static bool Delete(bool reviewed=false)
    {
        if(!SaveCards.Enabled){Status="No memory card selected. Town data was kept.";return false;}
        if(!reviewed&&RequiresOnlineReview()){Status="Close the linked online town before erasing its card.";return false;}
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
                if(TownDataDeletion.IsOnlineFile(entry.Name)&&!names.Contains(entry.Name))names.Add(entry.Name);
            return names.ToArray();
        }
        public byte[] Read(string name)=>archive.Read(name);
        public void Write(string name,byte[] bytes)=>archive.Write(name,bytes);
        public void Delete(string name)
        {
            if(TownDataDeletion.IsOnlineFile(name))card.Delete(name);
            else archive.Delete(name);
        }
    }
}
