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
            var archive=new CardArchive(card,new byte[0],new byte[0],SaveCards.Device);foreach(string name in archive.Names())if(name==FileName){var config=DevConfig.Decode(archive.Read(FileName));Status="Dev config loaded";Dolphin.Diagnostics.DebugOverlay.WriteLine("ANIMALTOWN DEV CONFIG LOADED");return config;}
        }catch(Exception){Status="Dev config unavailable; using defaults";}
        return new DevConfig(RuntimeOptions.Mode);
    }
    internal static bool Save(DevConfig config)
    {
        if(!SaveCards.Enabled){Status="Dev config applied for this session. Saving is disabled.";return false;}
        try{
            using var card=SaveCards.Mount();
            new CardArchive(card,new byte[0],new byte[0],SaveCards.Device).Write(FileName,config.Encode());
            Status="Dev config saved";Dolphin.Diagnostics.DebugOverlay.WriteLine("ANIMALTOWN DEV CONFIG SAVED");return true;
        }catch(Exception){Status="Could not save dev config. Check "+SaveCards.Slot+" / free space.";return false;}
    }
    internal static void ApplyGlobal(DevConfig config)
    {
        RuntimeOptions.Apply(config.RuntimeMode);FrameProfile.Enabled=config.Values[0]!=0;
        Dolphin.Diagnostics.DebugOverlay.PerformanceVisible=config.SdkPerformanceOverlay;
        UiFont.Batched=true;
    }
}
