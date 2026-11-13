using System.IO;
using Dolphin.Storage;
using Dolphin.IO;
using File=System.IO.File;

// Stage the standalone updater before it releases dvd:/ and scans GC Loader.
// Image discovery and verified installation belong to the updater executable.
internal static class UpdaterLaunch
{
    internal static string Operation="load the updater";
    internal const string Protocol="--animaltown-update=2";
    internal static void Request()
    {
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
    }
}
