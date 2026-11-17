namespace AnimalTown;

// The selected installation is immutable. Worker reads and cache keys use the
// same prefix; device discovery never runs from a load or rendering loop.
public static class ContentPaths
{
    private static string root="dvd:/";
    private static bool selected;
    public static string Root=>root;
    public static void Select(string value)
    {
        if(selected)throw new InvalidOperationException("Content installation is already selected");
        if(value==null||value.Length>512||!value.EndsWith("/",StringComparison.Ordinal)||
           value!="dvd:/"&&(!ContentSelection.LauncherRoot(value+"default.dol",out _,out string canonical)||canonical!=value))throw new ArgumentException("Invalid content root");
        root=value;selected=true;
    }
    public static string Asset(string filename)=>root+filename;
}

public enum ContentDevice { SdSlotA=0,SdSlotB=1,SdSerialPort2=2,Disc=3,GcLoader=6 }
public interface IContentMounts
{
    bool Mount(ContentDevice device);
    // 0: absent; 1: exact build; -1: another or malformed build. The reader
    // must cap its allocation to expected.Length before reading the identity.
    int Identity(string root,string expected);
    bool Exists(string path);
    void Unmount(ContentDevice device);
}
public sealed class ContentSelection
{
    public readonly ContentDevice Device;
    public readonly string Root;
    public bool IsApp=>Device!=ContentDevice.Disc;
    private ContentSelection(ContentDevice device,string root){Device=device;Root=root;}
    public static string Identity(string commit,string version,string sourceDigest,int runtimeProfile)
        =>"ANIMALTOWN-CONTENT-1\n"+commit+"\n"+version+"\n"+sourceDigest+"\n"+runtimeProfile+"\n";
    public static bool MatchesIdentity(System.IO.Stream file,string expected)
    {
        if(expected.Length>512||file.Length!=expected.Length)return false;
        var bytes=new byte[expected.Length];int at=0;
        while(at<bytes.Length){int n=file.Read(bytes,at,bytes.Length-at);if(n<=0)return false;at+=n;}
        for(int i=0;i<bytes.Length;i++)if(bytes[i]!=expected[i])return false;
        return true;
    }
    private static string Prefix(ContentDevice device)=>device==ContentDevice.SdSlotB?"cardb:/":device==ContentDevice.SdSerialPort2?"sd:/":device==ContentDevice.GcLoader?"gcloader:/":"dvd:/";
    private static readonly string[] sentinels={"runtime.cfg","buildings.bin","wordmark.dndtex","savebanner.rgb5a3","saveicon.rgb5a3"};
    private static void Verify(IContentMounts mounts,string root,bool app)
    {
        if(app&&!mounts.Exists(root+"default.dol"))throw new InvalidOperationException("AnimalTown installation is incomplete: "+root+"default.dol");
        for(int i=0;i<sentinels.Length;i++)if(!mounts.Exists(root+sentinels[i]))throw new InvalidOperationException("AnimalTown installation is incomplete: "+root+sentinels[i]);
    }
    public static bool LauncherRoot(string path,out ContentDevice device,out string root)
    {
        device=ContentDevice.Disc;root="";
        if(path==null||path.Length==0)return false;
        int colon=path.IndexOf(":/",StringComparison.Ordinal);if(colon<1)return false;
        string prefix=path.Substring(0,colon);
        if(prefix=="carda"||prefix=="sda")throw new InvalidOperationException("Use SD2SP2 or SD Gecko in Slot B; Slot A is used for memory-card settings.");
        if(prefix=="cardb"||prefix=="sdb")device=ContentDevice.SdSlotB;
        else if(prefix=="sd"||prefix=="sd2"||prefix=="sd2sp2")device=ContentDevice.SdSerialPort2;
        else if(prefix=="gcloader"||prefix=="gcldr")device=ContentDevice.GcLoader;
        else return false;
        if(path.Length>512||!path.EndsWith(".dol",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Invalid AnimalTown launcher path");
        int start=colon+2,last=path.LastIndexOf('/');
        for(int i=start;i<=path.Length;i++){
            if(i<path.Length&&(path[i]<' '||path[i]=='\u007f'||path[i]=='\\'||path[i]==':'))throw new InvalidOperationException("Invalid AnimalTown launcher path");
            if(i==path.Length||path[i]=='/'){
                int count=i-start;
                if(count==0||count==1&&path[start]=='.'||count==2&&path[start]=='.'&&path[start+1]=='.')throw new InvalidOperationException("Invalid AnimalTown launcher path");
                start=i+1;
            }
        }
        root=Prefix(device)+path.Substring(colon+2,last-colon-1);return true;
    }
    public static ContentSelection Find(IContentMounts mounts,bool app,string executable,string expectedIdentity)
    {
        if(!app){
            if(!mounts.Mount(ContentDevice.Disc))throw new InvalidOperationException("AnimalTown game disc not found");
            if(mounts.Identity("dvd:/",expectedIdentity)!=1)throw new InvalidOperationException("The mounted disc is not this AnimalTown build");
            Verify(mounts,"dvd:/",false);return new ContentSelection(ContentDevice.Disc,"dvd:/");
        }
        if(LauncherRoot(executable,out ContentDevice supplied,out string directory)){
            if(!mounts.Mount(supplied))throw new InvalidOperationException("Cannot mount the AnimalTown app device");
            if(mounts.Identity(directory,expectedIdentity)!=1)throw new InvalidOperationException("AnimalTown app assets are missing or belong to another build: "+directory);
            Verify(mounts,directory,true);return new ContentSelection(supplied,directory);
        }
        // Fixed, bounded probes only. First valid installation wins in this
        // documented order, without scanning folders or mounting the disc.
        for(int i=0;i<3;i++){
            ContentDevice device=i==0?ContentDevice.SdSerialPort2:i==1?ContentDevice.SdSlotB:ContentDevice.GcLoader;
            if(!mounts.Mount(device))continue;
            string candidate=Prefix(device)+"apps/animaltown/";
            if(mounts.Identity(candidate,expectedIdentity)==1){Verify(mounts,candidate,true);return new ContentSelection(device,candidate);}
            mounts.Unmount(device);
        }
        throw new InvalidOperationException("AnimalTown app assets not found. Install the complete apps/animaltown folder on SD2SP2, Slot B SD Gecko or GC Loader.");
    }
}
