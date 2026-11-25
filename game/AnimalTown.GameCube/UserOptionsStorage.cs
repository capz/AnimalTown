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
            var options=bytes==null?new UserOptions():UserOptions.Decode(bytes);
            if(bytes==null||!options.HasStoredEdgeSmoothing){
                // Reading preferences must stay read-only. The next options
                // save, or a legacy debug rewrite, durably publishes the import.
                try{
                    var debug=CardSettingsStorage.Read(card,SaveCards.Device,CardSettingsStorage.DebugFile,DevConfigStorage.FileName);
                    bool? legacy=debug==null?null:DevConfig.Decode(debug).LegacyEdgeSmoothing;
                    if(legacy.HasValue)options.EdgeSmoothing=legacy.Value;
                }catch(Exception){/* An unavailable debug record cannot discard valid video options. */}
            }
            DebugOverlay.WriteLine("ANIMALTOWN OPTIONS LOADED "+options.Resolution+" "+(options.Widescreen?1:0)+" "+(options.EdgeSmoothing?1:0));Current=options;return options;
        }catch(Exception){Status="Options unavailable; using defaults.";}
        Current=new UserOptions();return Current;
    }
    internal static void PreserveLegacyEdgeSmoothing(MemoryCard card,int device,bool enabled)
    {
        var bytes=CardSettingsStorage.Read(card,device,CardSettingsStorage.VideoFile,FileName);
        var options=bytes==null?new UserOptions():UserOptions.Decode(bytes);
        if(bytes!=null&&options.HasStoredEdgeSmoothing)return;
        options.EdgeSmoothing=enabled;
        // Verify the new video record before the caller retires the only
        // persisted copy of the old AA choice in its debug record.
        CardSettingsStorage.Save(card,device,CardSettingsStorage.VideoFile,FileName,options.Encode());
        DebugOverlay.WriteLine("ANIMALTOWN EDGE SMOOTHING MIGRATED");
    }
    internal static bool Save(UserOptions options)
    {
        Current=options;
        if(!SaveCards.Enabled){Status="Applied for this session. Saving is disabled.";return false;}
        try{
            using var card=SaveCards.Mount();
            CardSettingsStorage.Save(card,SaveCards.Device,CardSettingsStorage.VideoFile,FileName,options.Encode());
            Status="Saved to memory card in "+SaveCards.Slot+".";
            DebugOverlay.WriteLine("ANIMALTOWN OPTIONS SAVED "+options.Resolution+" "+(options.Widescreen?1:0)+" "+(options.EdgeSmoothing?1:0));return true;
        }catch(Exception){Status="Applied for this session. Could not save to "+SaveCards.Slot+".";return false;}
    }
}
