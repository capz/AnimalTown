using System;
using AnimalTown;
using Dolphin.IO;
using Dolphin.Storage;
using Dolphin.Diagnostics;

internal static class ContentBootstrap
{
#if ANIMALTOWN_DEPOT
    internal static bool DiscInstallation=>false;
#else
    internal static bool DiscInstallation=>true;
#endif
    // The title, later towns and asynchronous streaming all share this mount.
    // Do not dispose it when Initialize returns or when a town is unloaded.
    private static LaunchContent? content;
    private static readonly string[] requiredFiles={"runtime.cfg","buildings.bin","wordmark.dndtex","savebanner.rgb5a3","saveicon.rgb5a3"};

    internal static void Initialize()
    {
        if(content!=null)throw new InvalidOperationException("Content installation is already selected");
        var options=new LaunchContentOptions{Validate=Validate};
#if ANIMALTOWN_DEPOT
        // Swiss calls GC Loader paths dvd:/. A standalone app must never
        // probe or borrow the currently inserted physical/ODE disc instead.
        options.DvdSource=LaunchContentDvdSource.GcLoader;
        options.Fallbacks=new[]{
            new LaunchContentLocation(MountConfiguration.BlockVolume(StorageDevice.SdSerialPort2),"apps/animaltown"),
            new LaunchContentLocation(MountConfiguration.BlockVolume(StorageDevice.SdSlotB),"apps/animaltown"),
            new LaunchContentLocation(MountConfiguration.BlockVolume(StorageDevice.GcLoader),"apps/animaltown")
        };
        var result=LaunchContent.Mount(options);
#else
        // A disc executable reads its FST root, not a Swiss ISO filename or
        // the directory containing that image on an adapter.
        options.Fallbacks=new[]{new LaunchContentLocation(MountConfiguration.Disc())};
        var result=LaunchContent.Mount("",options);
#endif
        if(!result.Succeeded){
            foreach(var attempt in result.Attempts)
                DebugOverlay.WriteLine("ANIMALTOWN CONTENT REJECTED "+attempt.RootPath+" failure="+(int)attempt.Failure+" mount="+(int)attempt.MountFailure);
            throw new InvalidOperationException(DiscInstallation
                ?"The mounted disc is not a complete copy of this AnimalTown build."
                :"AnimalTown app assets not found or rejected (failure "+(int)result.Failure+"). Keep the complete app beside its DOL; without a launcher path install apps/animaltown on SD2SP2, Slot B SD Gecko or GC Loader.");
        }
        var selected=result.Content!;
        try{ContentPaths.Select(selected.RootPath);}
        catch{selected.Dispose();throw;}
        content=selected;
        DebugOverlay.WriteLine("ANIMALTOWN CONTENT "+(DiscInstallation?"DISC ":"APP ")+content.RootPath+(content.IsFallback?" (fallback)":" (launch-relative)"));
    }

    private static bool Validate(LaunchContent candidate)
    {
#if ANIMALTOWN_DEPOT
        if(!candidate.IsFallback&&!candidate.SourcePath.EndsWith(".dol",StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("AnimalTown app launcher path must name a DOL.");
#endif
        if(!Dolphin.IO.File.Exists(candidate.GetPath("content.id")))return false;
        using(var file=candidate.OpenRead("content.id")){
            string expected=BuildInfo.ContentIdentity;
            if(expected.Length>ContentIdentity.MaximumLength||file.Length!=expected.Length)return false;
            var bytes=new byte[expected.Length];int at=0;
            while(at<bytes.Length){int read=file.Read(bytes,at,bytes.Length-at);if(read<=0)return false;at+=read;}
            if(!ContentIdentity.Matches(bytes,expected))return false;
        }
        // A different build may be skipped during explicit fallback discovery.
        // An exact build with a partial payload must fail, never borrow assets.
#if ANIMALTOWN_DEPOT
        Require(candidate,"default.dol");
#endif
        for(int i=0;i<requiredFiles.Length;i++)Require(candidate,requiredFiles[i]);
        return true;
    }

    private static void Require(LaunchContent candidate,string filename)
    {
        string path=candidate.GetPath(filename);
        if(!Dolphin.IO.File.Exists(path))throw new InvalidOperationException("AnimalTown installation is incomplete: "+path);
    }
}
