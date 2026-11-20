using AnimalTown;
using Dolphin.IO;
using Dolphin.Storage;

internal static class BuildInfo
{
    internal const string Version="1.2.3";
    internal const string ContentIdentity="ANIMALTOWN-CONTENT-1\n0123456789abcdef0123456789abcdef01234567\n1.2.3\nAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\n0\n";
}
internal static class Program
{
    private static void Check(bool value,string message)
    {if(!value)throw new Exception("Content startup assertion: "+message);}
    private static int Main()
    {
        string cwd=Dolphin.IO.Directory.GetCurrentDirectory();
        // The native host checks the expected outcome, mount attempts and final
        // ownership independently. A fixture must never accept an assertion
        // failure as an expected startup rejection.
        try{ContentBootstrap.Initialize();}
        catch(InvalidOperationException error){Console.WriteLine(error.Message);return 10;}
        catch(System.IO.IOException error){Console.WriteLine(error.Message);return 10;}
#if ANIMALTOWN_DEPOT
        Check(!ContentBootstrap.DiscInstallation&&!UpdaterLaunch.Available,"Depot remains independent of the disc updater");
        Check(!Storage.IsMounted(StorageDevice.DvdDrive),"Depot never mounts even a matching current disc");
#else
        Check(ContentBootstrap.DiscInstallation&&UpdaterLaunch.Available,"disc profile retains its updater policy");
        Check(ContentPaths.Root=="dvd:/","disc build ignores DOL-relative launcher metadata");
#endif
        Check(Dolphin.IO.Directory.GetCurrentDirectory()==cwd,"bootstrap preserves the working directory");
        MountedFileSystem? selected=null;
        foreach(var info in Storage.GetMounts())if(info.IsMounted){
            Check(selected==null,"startup retains exactly its selected content filesystem");
            Check(Storage.TryGetMountedFileSystem(info.Device,out selected),"selected filesystem remains mounted after Initialize returns");
        }
        Check(selected!=null,"content owner survives startup");
#if ANIMALTOWN_DEPOT
        string expected=LaunchContext.ArgumentCount>1?LaunchContext.GetArgument(1):selected!.RootPath+"apps/animaltown/";
        Check(ContentPaths.Root==expected,"resolved installation directory matches launcher metadata or fixed fallback");
#endif
        try{RuntimeOptions.Load();}
        catch(System.IO.IOException error){Console.WriteLine(error.Message);return 20;}
        Check(RuntimeOptions.Mode==0,"production runtime loader reads the selected installation's profile");
        string root=ContentPaths.Root;
        long generation=selected!.MountId;
        string model=TexturePolicy.ModelPath("fox",TextureResolution.Size128);
        Check(model==root+"fox-tex128.dndmodel","model variants share the ordinary asset root");
        // Streaming begins after the bootstrap stack frame has unwound. Native
        // GC-stress mode collects during the following allocations while an
        // async read can still be pending, as during play.
        var pending=Dolphin.IO.File.ReadAllBytesAsync(model);
        byte[] sync=System.IO.File.ReadAllBytes(model);
        byte[] asyncBytes=pending.GetAwaiter().GetResult();
        Check(sync.Length==2&&sync[0]==41&&sync[1]==42&&asyncBytes.Length==2&&asyncBytes[0]==41&&asyncBytes[1]==42,"late sync/async streaming reads actual installation bytes");
        using(var atlasFile=System.IO.File.OpenRead(ContentPaths.Asset("atlas.dndtex"))){
            var atlas=new TextureAtlasStream(atlasFile);
            Check(atlas.Width==512&&atlas.Height==512&&atlas.Columns==4&&atlas.Rows==4,"atlas metadata survives selected content root");
            var page=atlas.ReadPage(3,2);
            Check(page.Length==32800&&TexturePixels.Width(page)==128&&TexturePixels.Height(page)==128&&page[32]==173&&page[page.Length-1]==173,"atlas page streams from selected filesystem");
            atlas.ReadPage(0,0);
            Check(page[32]==37&&page[page.Length-1]==37,"atlas reuses page buffer for subsequent reads");
        }
        Check(selected.IsValid&&Storage.GetMountInfo(selected.Device).MountId==generation&&ContentPaths.Root==root,"streaming does not replace the content mount or root");
#if ANIMALTOWN_DEPOT
        bool refused=false;
        try{UpdaterLaunch.Request();}
        catch(System.IO.IOException error){refused=error.Message=="Update this installation through Dolphin Depot.";}
        Check(refused,"Depot cannot start the disc updater");
#endif
        return 0;
    }
}
