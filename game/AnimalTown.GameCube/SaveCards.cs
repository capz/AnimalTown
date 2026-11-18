using System;
using Dolphin.Storage;

// One destination for the whole session. Probing is read-only and happens only
// at boot, when opening the chooser, or on an explicit recheck, never per frame.
internal static class SaveCards
{
    internal const int RequiredBlocks=NativeCardBlocks.ReservedBlocks+2;
    internal static int Device {get;private set;}=-1;
    internal static bool Enabled=>Device>=4;
    internal static bool Chosen {get;private set;}
    internal static string Slot=>Device==5?"Slot B":"Slot A";
    internal static void Initialize()
    {
        var a=Inspect(4);var b=Inspect(5);
        Device=a.CanSave?4:b.CanSave?5:-1;Chosen=false;
    }
    internal static int DefaultChoice(SaveCardInfo a,SaveCardInfo b)
    {
        if(Chosen&&!Enabled)return 2;
        if(Device==4&&a.CanSave)return 0;
        if(Device==5&&b.CanSave)return 1;
        return a.CanSave?0:b.CanSave?1:2;
    }
    internal static bool Select(SaveCardInfo info)
    {
        if(!info.CanSave)return false;
        Device=info.Device;Chosen=true;return true;
    }
    internal static void WithoutSaving(){Device=-1;Chosen=true;}
    internal static MemoryCard Mount()
    {
        if(!Enabled)throw new InvalidOperationException("Saving is disabled for this session.");
        // Never fall back to the other slot after the town has been loaded.
        return MemoryCard.Mount((StorageDevice)Device,new MemoryCardIdentity("AMTE","01"));
    }
    internal static SaveCardInfo Inspect(int device)
    {
        if(device!=4&&device!=5)throw new ArgumentOutOfRangeException(nameof(device));
        try{
            using var manager=MemoryCardManager.Open((StorageDevice)device);
            if(!manager.IsReady){
                int result=manager.MountResult;
                return new SaveCardInfo(device,result==-3||result==-2?"No memory card":result==-6?"Card needs checking":"Card unavailable");
            }
            int free=manager.FreeBlocks,entries=manager.FreeEntries;bool reserved=false;
            // A current archive already owns its copy-on-write/recovery space.
            // A legacy/smaller archive needs room for a verified replacement.
            for(int start=0;start<manager.Count;start+=16){
                foreach(var save in manager.ReadPage(start,16))if(save.GameCode=="AMTE"&&save.CompanyCode=="01"&&
                    (save.Filename=="AnimalTown"||save.Filename=="AnimalTown2")&&save.Blocks>=RequiredBlocks)reserved=true;
            }
            bool canSave=reserved||free>=RequiredBlocks&&entries>0;
            string status=canSave?"Ready / "+free+" blocks free":free<RequiredBlocks?"Needs "+RequiredBlocks+" free blocks":"No free save entries";
            return new SaveCardInfo(device,status,canSave,free);
        }catch(Exception error){
            string detail=error.Message??"";
            return new SaveCardInfo(device,detail.StartsWith("No memory card")||detail.Contains("(-2)")?"No memory card":"Card unavailable");
        }
    }
}

internal sealed class SaveCardInfo
{
    internal readonly int Device,FreeBlocks;
    internal readonly string Status;
    internal readonly bool CanSave;
    internal SaveCardInfo(int device,string status,bool canSave=false,int freeBlocks=0)
    {Device=device;Status=status;CanSave=canSave;FreeBlocks=freeBlocks;}
}
