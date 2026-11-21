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
    private static int Main()
    {
        bool rejection=LaunchContext.ArgumentCount>2;
        try{
            ContentBootstrap.Initialize();
            if(rejection||ContentBootstrap.DiscInstallation||Storage.IsMounted(StorageDevice.DvdDrive))return 1;
            string expected=LaunchContext.ArgumentCount>1?LaunchContext.GetArgument(1):"cardb:/apps/animaltown/";
            if(ContentPaths.Root!=expected)return 2;
            byte[] config=System.IO.File.ReadAllBytes(ContentPaths.Asset("runtime.cfg"));
            if(config.Length!=1||config[0]!=0)return 3;
            string model=TexturePolicy.ModelPath("fox",TextureResolution.Size128);
            byte[] sync=System.IO.File.ReadAllBytes(model);
            byte[] asyncBytes=Dolphin.IO.File.ReadAllBytesAsync(model).GetAwaiter().GetResult();
            if(sync.Length!=2||sync[0]!=41||sync[1]!=42||asyncBytes.Length!=2||asyncBytes[0]!=41||asyncBytes[1]!=42)return 4;
            using(var atlasFile=System.IO.File.OpenRead(ContentPaths.Asset("atlas.dndtex"))){
                var atlas=new TextureAtlasStream(atlasFile);
                if(atlas.Width!=512||atlas.Height!=512||atlas.Columns!=4||atlas.Rows!=4)return 9;
                var page=atlas.ReadPage(3,2);
                if(page.Length!=32800||TexturePixels.Width(page)!=128||TexturePixels.Height(page)!=128||page[32]!=173||page[page.Length-1]!=173)return 10;
                atlas.ReadPage(0,0);if(page[32]!=37||page[page.Length-1]!=37)return 11;
            }
            try{UpdaterLaunch.Request();return 5;}
            catch(System.IO.IOException error){if(error.Message!="Update this installation through Dolphin Depot.")return 6;}
        }catch(InvalidOperationException){if(!rejection)return 7;}
        return Storage.IsMounted(StorageDevice.DvdDrive)?8:0;
    }
}
