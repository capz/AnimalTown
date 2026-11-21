using System;
using AnimalTown;
using Dolphin.Diagnostics;
using Dolphin.Storage;

internal static class CardSettingsStorage
{
    internal const string DebugFile="AnimalTown-Debug",VideoFile="AnimalTown-Video";

    private static bool Exists(MemoryCard card,string name)
    {
        foreach(var entry in card.GetEntries())if(entry.Name==name)return true;
        return false;
    }

    internal static byte[]? Read(MemoryCard card,int device,string file,string legacyName)
    {
        if(Exists(card,file))return card.ReadAllBytes(file);
        var archive=new CardArchive(card,new byte[0],new byte[0],device);
        foreach(string name in archive.Names())if(name==legacyName)return archive.Read(name);
        return Exists(card,legacyName)?card.ReadAllBytes(legacyName):null;
    }

    internal static void Save(MemoryCard card,int device,string file,string legacyName,byte[] bytes)
    {
        Write(card,file,bytes);
        // The new file is already authoritative if archive cleanup is interrupted.
        try{
            var archive=new CardArchive(card,new byte[0],new byte[0],device);
            archive.RemoveArchivedSetting(legacyName);
            if(Exists(card,legacyName))card.Delete(legacyName);
        }catch(Exception error){DebugOverlay.WriteLine("SETTINGS CARD CLEANUP: "+error.Message);}
    }

    internal static void TryMigrate(MemoryCard card,CardArchive archive,string file,string legacyName)
    {
        try{
            var archived=archive.ReadArchivedSetting(legacyName);
            if(archived==null)return;
            // A newer settings save can survive interrupted archive cleanup.
            // Validate the authoritative payload before retiring the older copy.
            bool exists=Exists(card,file);
            var bytes=exists?card.ReadAllBytes(file):archived;
            if(file==VideoFile)UserOptions.Decode(bytes);
            else DevConfig.Decode(bytes);
            if(!exists)Write(card,file,bytes);
            archive.RemoveArchivedSetting(legacyName);
        }catch(Exception error){DebugOverlay.WriteLine("SETTINGS CARD MIGRATION: "+error.Message);}
    }

    private static void Write(MemoryCard card,string file,byte[] bytes)
    {
        string comment=file==VideoFile?"Video configuration":"Debug configuration";
        card.WriteSave(file,bytes,new MemoryCardSaveOptions{Title="AnimalTown",Comment=comment});
        CardArchive.Match(bytes,card.ReadAllBytes(file));
    }
}
