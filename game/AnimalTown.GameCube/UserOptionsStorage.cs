using System;
using AnimalTown;
using Dolphin.Diagnostics;
using Dolphin.Storage;

internal static class UserOptionsStorage
{
    internal const string FileName="options";
    internal static string Status="";
    internal static UserOptions Current {get;private set;}=new UserOptions();
    internal static UserOptions Load()
    {
        if(!SaveCards.Enabled){Current=new UserOptions();return Current;}
        try{
            using var card=SaveCards.Mount();
            var bytes=CardSettingsStorage.Read(card,SaveCards.Device,CardSettingsStorage.VideoFile,FileName);
            if(bytes!=null){
                var options=UserOptions.Decode(bytes);
                DebugOverlay.WriteLine("ANIMALTOWN OPTIONS LOADED "+options.Resolution+" "+(options.Widescreen?1:0));Current=options;return options;
            }
        }catch(Exception){Status="Options unavailable; using defaults.";}
        Current=new UserOptions();return Current;
    }
    internal static bool Save(UserOptions options)
    {
        if(!SaveCards.Enabled){Status="Applied for this session. Saving is disabled.";return false;}
        try{
            using var card=SaveCards.Mount();
            CardSettingsStorage.Save(card,SaveCards.Device,CardSettingsStorage.VideoFile,FileName,options.Encode());
            Status="Saved to memory card in "+SaveCards.Slot+".";
            DebugOverlay.WriteLine("ANIMALTOWN OPTIONS SAVED "+options.Resolution+" "+(options.Widescreen?1:0));return true;
        }catch(Exception){Status="Applied for this session. Could not save to "+SaveCards.Slot+".";return false;}
    }
}
