using System;
using System.IO;
using Dolphin.Storage;
using Dolphin.IO;
using File=System.IO.File;

// Resolve and verify the explicitly selected installation only on Update.
// Direct boot uses its installation record; loaders may also supply a path.
internal static class UpdaterLaunch
{
    internal static string Operation="read launch path";
    internal const string Protocol="--animaltown-update=1";
    internal static string VerifiedImage()
    {
        Operation="read launch path";
        var mounted=Storage.GetMountInfo(StorageDevice.DvdDrive).LastSuccessfulConfiguration;
        string source=mounted!=null&&mounted.Kind==MountKind.DiscImage?mounted.ImagePath:"";
        // Direct GC Loader boot does not expose its selected filename. Use
        // explicit installer metadata as another source; never infer a name.
        Operation="mount GC Loader SD";
        if(!Storage.IsMounted(StorageDevice.GcLoader)&&!Storage.Mount(StorageDevice.GcLoader)){
            int code=Storage.GetMountInfo(StorageDevice.GcLoader).LastNativeError;
            throw new IOException("GC Loader SD mount failed (code "+code+").");
        }
        Operation="read the installed image mapping";
        const string record="gcloader:/animaltown-updater/image-path.txt";string installed="";
        if(File.Exists(record)){
            using var mapping=File.OpenRead(record);
            if(mapping.Length<1||mapping.Length>1024)throw new IOException("Invalid image mapping. Run provision.py with --image.");
            var bytes=new byte[(int)mapping.Length];int used=0;
            while(used<bytes.Length){int n=mapping.Read(bytes,used,bytes.Length-used);if(n==0)throw new IOException("Image mapping could not be read.");used+=n;}
            installed=Dolphin.Text.Encoding.UTF8.GetString(bytes);
            int length=installed.Length;
            if(length>0&&installed[length-1]=='\n'){length--;if(length>0&&installed[length-1]=='\r')length--;}
            if(length!=installed.Length)installed=installed.Substring(0,length);
            if(installed.Length==0)throw new IOException("Empty image mapping. Run provision.py with --image.");
        }
        string image=UpdateImagePath.Find(NativeLaunchArguments.Read(),source,installed);
        // Filename provenance is explicit; verify the intended installation
        // matches the currently running game before preparing its updater.
        Operation="read the selected game image";
        using(var input=File.OpenRead(image)){
            var header=new byte[0x440];int used=0;
            while(used<header.Length){int n=input.Read(header,used,header.Length-used);if(n==0)throw new IOException("The selected game image is truncated.");used+=n;}
            UpdateImagePath.VerifyHeader(header,BuildInfo.Version);
        }
        return image;
    }
    internal static void Request()
    {
        string image=VerifiedImage();
        Operation="load the updater";
        string path="dvd:/animal-town-updater.dol";
        if(!File.Exists(path))path="gcloader:/animal-town-updater.dol";
        using var updater=DolLauncher.Prepare(path);
        // Preserve each argument as one argv element, including spaced paths.
        updater.SetArguments(path,Protocol,image,BuildInfo.Version);
        DolLauncher.RequestLaunch(updater);
    }
}
