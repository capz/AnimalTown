using System;
using System.IO;
using AnimalTown;
using Dolphin.IO;
using Dolphin.Storage;
using Dolphin.Diagnostics;

internal static class ContentBootstrap
{
    internal static bool DiscInstallation {get;private set;}
    private sealed class Mounts : IContentMounts
    {
        public bool Mount(ContentDevice device)=>device==ContentDevice.Disc?Storage.MountDisc():Storage.IsMounted((StorageDevice)device)||Storage.Mount((StorageDevice)device);
        public bool Exists(string path)=>System.IO.File.Exists(path);
        public void Unmount(ContentDevice device)=>Storage.Unmount((StorageDevice)device);
        public int Identity(string root,string expected)
        {
            string path=root+"content.id";if(!System.IO.File.Exists(path))return 0;
            using var file=System.IO.File.OpenRead(path);
            return ContentSelection.MatchesIdentity(file,expected)?1:-1;
        }
    }
    internal static void Initialize()
    {
#if ANIMALTOWN_DEPOT
        const bool app=true;
        string executable=LaunchContext.ExecutablePath;
#else
        const bool app=false;
        string executable="";
#endif
        var selected=ContentSelection.Find(new Mounts(),app,executable,BuildInfo.ContentIdentity);
        ContentPaths.Select(selected.Root);DiscInstallation=!selected.IsApp;
        DebugOverlay.WriteLine("ANIMALTOWN CONTENT "+(app?"APP ":"DISC ")+selected.Root);
    }
}
