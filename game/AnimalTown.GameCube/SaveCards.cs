using System;
using AnimalTown;
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
    private enum TownPresence { Unknown, Absent, Present }
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
    internal static bool TryAutoSelect(SaveCardInfo a,SaveCardInfo b)
    {
        if(!a.Present)return Select(b);
        if(!b.Present)return Select(a);
        var townA=InspectTown(a);var townB=InspectTown(b);
        if(townA==TownPresence.Present&&townB==TownPresence.Absent)return Select(a);
        if(townB==TownPresence.Present&&townA==TownPresence.Absent)return Select(b);
        return false;
    }
    private static TownPresence InspectTown(SaveCardInfo info)
    {
        if(!info.Readable)return TownPresence.Unknown;
        if(!info.HasArchive)return info.HasLooseTown?TownPresence.Present:TownPresence.Absent;
        // A consolidated archive may contain console preferences alone.
        try{
            using var card=MemoryCard.Mount((StorageDevice)info.Device,new MemoryCardIdentity("AMTE","01"));
            var storage=new CardSaveStorage(card,new byte[0],new byte[0],info.Device);
            foreach(string name in storage.Names())if(IsTownRecord(name))return TownPresence.Present;
            return TownPresence.Absent;
        }catch(Exception){return TownPresence.Unknown;}
    }
    private static bool IsTownRecord(string name)=>SplitSaveCodec.IsFile(name)||name==TownDataDeletion.Marker;
    internal static void SelectVerifiedDevice(int device){if(device!=4&&device!=5)throw new ArgumentOutOfRangeException(nameof(device));Device=device;Chosen=true;}
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
                bool missing=result==-3||result==-2;
                return new SaveCardInfo(device,missing?"No memory card":result==-6?"Card needs checking":"Card unavailable",present:!missing);
            }
            int free=manager.FreeBlocks,entries=manager.FreeEntries;bool reserved=false,archive=false,looseTown=false;
            // A current archive already owns its copy-on-write/recovery space.
            // A legacy/smaller archive needs room for a verified replacement.
            for(int start=0;start<manager.Count;start+=16){
                foreach(var save in manager.ReadPage(start,16)){
                    if(save.GameCode!="AMTE"||save.CompanyCode!="01")continue;
                    if(save.Filename=="AnimalTown"||save.Filename=="AnimalTown2"){
                        archive=true;
                        if(save.Blocks>=RequiredBlocks)reserved=true;
                    }else if(IsTownRecord(save.Filename))looseTown=true;
                }
            }
            bool canSave=reserved||free>=RequiredBlocks&&entries>0;
            string status=canSave?"Ready / "+free+" blocks free":free<RequiredBlocks?"Needs "+RequiredBlocks+" free blocks":"No free save entries";
            return new SaveCardInfo(device,status,canSave,free,present:true,readable:true,archive:archive,looseTown:looseTown);
        }catch(Exception error){
            string detail=error.Message??"";
            bool missing=detail.StartsWith("No memory card")||detail.Contains("(-2)");
            return new SaveCardInfo(device,missing?"No memory card":"Card unavailable",present:!missing);
        }
    }
}

internal sealed class SaveCardInfo
{
    internal readonly int Device,FreeBlocks;
    internal readonly string Status;
    internal readonly bool CanSave,Present,Readable,HasArchive,HasLooseTown;
    internal SaveCardInfo(int device,string status,bool canSave=false,int freeBlocks=0,bool present=false,bool readable=false,bool archive=false,bool looseTown=false)
    {Device=device;Status=status;CanSave=canSave;FreeBlocks=freeBlocks;Present=present;Readable=readable;HasArchive=archive;HasLooseTown=looseTown;}
}
