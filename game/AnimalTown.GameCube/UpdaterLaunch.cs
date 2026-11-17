using System.IO;
using Dolphin.Storage;
using Dolphin.IO;
using File=System.IO.File;

// The resident updater reads dvd:/version.md before releasing the source disc,
// then installs the published image in the GC Loader /games directory.
internal static class UpdaterLaunch
{
    internal static string Operation="load the updater";
    internal const string Protocol="--animaltown-update=3";
    internal static void Request()
    {
#if ANIMALTOWN_DEPOT
        throw new IOException("Update this installation through Dolphin Depot.");
#else
        if(!ContentBootstrap.DiscInstallation)throw new IOException("Update this installation through Dolphin Depot.");
        Operation="load the updater";
        string path="dvd:/animal-town-updater.dol";
        if(!File.Exists(path)){
            Operation="mount GC Loader SD for the updater fallback";
            if(!Storage.IsMounted(StorageDevice.GcLoader)&&!Storage.Mount(StorageDevice.GcLoader)){
                int code=Storage.GetMountInfo(StorageDevice.GcLoader).LastNativeError;
                throw new IOException("GC Loader SD mount failed (code "+code+").");
            }
            path="gcloader:/animal-town-updater.dol";
        }
        Operation="load the updater";
        using var updater=DolLauncher.Prepare(path);
        // No physical ISO path can be inferred from the logical dvd:/ mount.
        updater.SetArguments(path,Protocol,BuildInfo.Version);
        DolLauncher.RequestLaunch(updater);
#endif
    }
}
