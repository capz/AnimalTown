using System;
using AnimalTown;
using Dolphin.Storage;

internal static class DevConfigStorage
{
    internal const string FileName="dev_config";
    internal static string Status="Dev config defaults";
    internal static DevConfig Load()
    {
        Status="Dev config defaults";
        if(!SaveCards.Enabled)return new DevConfig(RuntimeOptions.Mode);
        try{
            using var card=SaveCards.Mount();
            var bytes=CardSettingsStorage.Read(card,SaveCards.Device,CardSettingsStorage.DebugFile,FileName);
            if(bytes!=null){
                var config=DevConfig.Decode(bytes);Status="Dev config loaded";
                Dolphin.Diagnostics.DebugOverlay.WriteLine("ANIMALTOWN DEV CONFIG LOADED");return config;
            }
        }catch(Exception){Status="Dev config unavailable; using defaults";}
        return new DevConfig(RuntimeOptions.Mode);
    }
    internal static bool Save(DevConfig config)
    {
        if(!SaveCards.Enabled){Status="Dev config applied for this session. Saving is disabled.";return false;}
        try{
            using var card=SaveCards.Mount();
            var video=CardSettingsStorage.Read(card,SaveCards.Device,CardSettingsStorage.VideoFile,UserOptionsStorage.FileName);
            if(video==null||!UserOptions.Decode(video).HasStoredEdgeSmoothing){
                var previous=CardSettingsStorage.Read(card,SaveCards.Device,CardSettingsStorage.DebugFile,FileName);
                bool? legacy=null;
                if(previous!=null){
                    try{legacy=DevConfig.Decode(previous).LegacyEdgeSmoothing;}
                    catch(System.IO.IOException){/* An invalid old record has no trustworthy preference to migrate. */}
                }
                if(legacy.HasValue)UserOptionsStorage.PreserveLegacyEdgeSmoothing(card,SaveCards.Device,legacy.Value);
            }
            CardSettingsStorage.Save(card,SaveCards.Device,CardSettingsStorage.DebugFile,FileName,config.Encode());
            Status="Dev config saved";Dolphin.Diagnostics.DebugOverlay.WriteLine("ANIMALTOWN DEV CONFIG SAVED");return true;
        }catch(Exception){Status="Could not save dev config. Check "+SaveCards.Slot+" / free space.";return false;}
    }
    internal static void ApplyGlobal(DevConfig config)
    {
        RuntimeOptions.Apply(config.RuntimeMode);FrameProfile.SetMode(config.ProfilingMode);
        Dolphin.Diagnostics.DebugOverlay.PerformanceVisible=config.SdkPerformanceOverlay;
        UiFont.Batched=true;
    }
}
