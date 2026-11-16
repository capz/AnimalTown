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
            var archive=new CardArchive(card,new byte[0],new byte[0]);
            foreach(string name in archive.Names())if(name==FileName){
                var options=UserOptions.Decode(archive.Read(FileName));
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
            var archive=new CardArchive(card,new byte[0],new byte[0]);archive.Write(FileName,bytes);
            var readback=archive.Read(FileName);
            if(readback.Length!=bytes.Length)throw new InvalidOperationException("Options readback failed");
            for(int i=0;i<bytes.Length;i++)if(readback[i]!=bytes[i])throw new InvalidOperationException("Options readback failed");
            Status="Saved to memory card in Slot A.";
            DebugOverlay.WriteLine("ANIMALTOWN OPTIONS SAVED "+options.Resolution+" "+(options.Widescreen?1:0));return true;
        }catch(Exception){Status="Applied for this session. Could not save to Slot A.";return false;}
    }
}
