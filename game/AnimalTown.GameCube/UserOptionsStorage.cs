using System;
using AnimalTown;
using Dolphin.Diagnostics;
using Dolphin.Storage;

internal static class UserOptionsStorage
{
    internal const string FileName="options";
    internal static string Status="";
    internal static UserOptions Load()
    {
        try{
            using var card=MemoryCard.Mount(StorageDevice.MemoryCardSlotA,new MemoryCardIdentity("AMTE","01"));
            foreach(var entry in card.GetEntries())if(entry.Name==FileName){
                var options=UserOptions.Decode(card.ReadAllBytes(FileName));
                DebugOverlay.WriteLine("ANIMALTOWN OPTIONS LOADED "+options.Resolution+" "+(options.Widescreen?1:0));return options;
            }
        }catch(Exception){Status="Options unavailable; using defaults.";}
        return new UserOptions();
    }
    internal static bool Save(UserOptions options)
    {
        try{
            using var card=MemoryCard.Mount(StorageDevice.MemoryCardSlotA,new MemoryCardIdentity("AMTE","01"));
            byte[] bytes=options.Encode();
            card.WriteSave(FileName,bytes,new MemoryCardSaveOptions{Title="AnimalTown options",Comment="Video preferences"});
            var readback=card.ReadAllBytes(FileName);
            if(readback.Length!=bytes.Length)throw new InvalidOperationException("Options readback failed");
            for(int i=0;i<bytes.Length;i++)if(readback[i]!=bytes[i])throw new InvalidOperationException("Options readback failed");
            Status="Saved to memory card in Slot A.";
            DebugOverlay.WriteLine("ANIMALTOWN OPTIONS SAVED "+options.Resolution+" "+(options.Widescreen?1:0));return true;
        }catch(Exception){Status="Applied for this session. Could not save to Slot A.";return false;}
    }
}
