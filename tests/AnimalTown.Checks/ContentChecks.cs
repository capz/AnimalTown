using AnimalTown;
using System.Text;

internal static class ContentChecks
{
public static void Run(Action<bool,string> Check)
{
void Reject(Action action,string message){bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}Check(rejected,message);}
const string identity="ANIMALTOWN-CONTENT-1\n0123456789abcdef\n1.2.3\nsource\n0\n";
var disc=new Mounts();disc.Add(ContentDevice.Disc,"dvd:/",identity);
var selected=ContentSelection.Find(disc,false,"sdb:/other/default.dol",identity);
Check(selected.Root=="dvd:/"&&!selected.IsApp&&disc.Attempts.SequenceEqual(new[]{ContentDevice.Disc}),"disc profile uses only the identified disc");
var sd=new Mounts();sd.Add(ContentDevice.Disc,"dvd:/",identity);sd.Add(ContentDevice.SdSlotB,"cardb:/apps/animaltown/",identity);
selected=ContentSelection.Find(sd,true,"",identity);
Check(selected.Root=="cardb:/apps/animaltown/"&&selected.IsApp&&sd.Attempts.SequenceEqual(new[]{ContentDevice.SdSerialPort2,ContentDevice.SdSlotB}),"no-argument app boots from Slot B without mounting even a matching disc");
var both=new Mounts();both.Add(ContentDevice.SdSerialPort2,"sd:/apps/animaltown/",identity);both.Add(ContentDevice.SdSlotB,"cardb:/apps/animaltown/",identity);
selected=ContentSelection.Find(both,true,"host:/unusable/default.dol",identity);
Check(selected.Device==ContentDevice.SdSerialPort2&&both.Attempts.Count==1,"multiple installs use a fixed priority without extra device searches");
var relocated=new Mounts();relocated.Add(ContentDevice.SdSlotB,"cardb:/games/My Town/",identity);
selected=ContentSelection.Find(relocated,true,"sdb:/games/My Town/default.dol",identity);
Check(selected.Root=="cardb:/games/My Town/"&&relocated.Attempts.SequenceEqual(new[]{ContentDevice.SdSlotB}),"launcher path determines the device and directory instead of the working directory");
var mismatch=new Mounts();mismatch.Add(ContentDevice.SdSlotB,"cardb:/games/old/","other");mismatch.Add(ContentDevice.SdSerialPort2,"sd:/apps/animaltown/",identity);
Reject(()=>ContentSelection.Find(mismatch,true,"sdb:/games/old/default.dol",identity),"explicit mismatched launcher installation never falls back to a different device");
Check(mismatch.Attempts.SequenceEqual(new[]{ContentDevice.SdSlotB}),"failed explicit selection probes no other mount");
var partial=new Mounts();partial.Add(ContentDevice.SdSerialPort2,"sd:/apps/animaltown/",identity,false);partial.Add(ContentDevice.SdSlotB,"cardb:/apps/animaltown/",identity);
Reject(()=>ContentSelection.Find(partial,true,"",identity),"a matching but incomplete package fails without borrowing another copy's assets");
Check(partial.Attempts.Count==1,"partial install does not trigger a second-device fallback");
var stale=new Mounts();stale.Add(ContentDevice.SdSerialPort2,"sd:/apps/animaltown/","old");stale.Add(ContentDevice.SdSlotB,"cardb:/apps/animaltown/",identity);
Check(ContentSelection.Find(stale,true,"",identity).Device==ContentDevice.SdSlotB&&stale.Unmounted.SequenceEqual(new[]{ContentDevice.SdSerialPort2}),"fixed discovery skips another build and frees its mount cache");
var absent=new Mounts();Reject(()=>ContentSelection.Find(absent,true,"",identity),"missing app reports a bounded startup failure");
Check(absent.Attempts.Count==3&&!absent.Attempts.Contains(ContentDevice.Disc)&&!absent.Attempts.Contains(ContentDevice.SdSlotA),"absent-device search is fixed and leaves Slot A and the disc untouched");
foreach(string path in new[]{"sda:/apps/animaltown/default.dol","sdb:/../default.dol","sd2:/apps//default.dol","sd2:/apps/a\0/default.dol","sd2:/apps/default.txt"})
    Reject(()=>ContentSelection.LauncherRoot(path,out _,out _),"unsafe or incompatible launcher path is rejected: "+path.Replace('\0','?'));
foreach(var pair in new[]{("sd2:/apps/a/default.dol","sd:/apps/a/"),("cardb:/apps/a/default.dol","cardb:/apps/a/"),("gcldr:/apps/a/default.dol","gcloader:/apps/a/")})
    Check(ContentSelection.LauncherRoot(pair.Item1,out _,out string root)&&root==pair.Item2,"launcher aliases normalize to SDK mount names");
using(var stream=new MemoryStream(Encoding.ASCII.GetBytes(identity)))Check(ContentSelection.MatchesIdentity(stream,identity),"exact package identity is accepted");
using(var stream=new MemoryStream(Encoding.ASCII.GetBytes(identity+"extra")))Check(!ContentSelection.MatchesIdentity(stream,identity),"extra identity bytes are rejected");
using(var huge=new HugeStream())Check(!ContentSelection.MatchesIdentity(huge,identity)&&huge.Reads==0,"oversized identity is rejected before reading or allocating a file-sized buffer");
Check(ContentPaths.Asset("wordmark.dndtex")=="dvd:/wordmark.dndtex","legacy host/default path remains disc compatible");
ContentPaths.Select("cardb:/apps/animaltown/");
Check(ContentPaths.Asset("wordmark.dndtex")=="cardb:/apps/animaltown/wordmark.dndtex"&&TexturePolicy.ModelPath("fox",TextureResolution.Size128)=="cardb:/apps/animaltown/fox-tex128.dndmodel","models and ordinary assets share the selected installation root");
Reject(()=>ContentPaths.Select("sd:/apps/animaltown/"),"content root cannot change after initialization");
}

sealed class Mounts : IContentMounts
{
    public readonly List<ContentDevice> Attempts=new(),Unmounted=new();
    private readonly HashSet<ContentDevice> devices=new();
    private readonly Dictionary<string,string> ids=new();
    private readonly HashSet<string> files=new();
    public void Add(ContentDevice device,string root,string id,bool complete=true){devices.Add(device);ids[root]=id;foreach(string name in new[]{"default.dol","runtime.cfg","buildings.bin","wordmark.dndtex","savebanner.rgb5a3","saveicon.rgb5a3"})if(complete||name!="saveicon.rgb5a3")files.Add(root+name);}
    public bool Mount(ContentDevice device){Attempts.Add(device);return devices.Contains(device);}
    public int Identity(string root,string expected)=>ids.TryGetValue(root,out string? id)?id==expected?1:-1:0;
    public bool Exists(string path)=>files.Contains(path);
    public void Unmount(ContentDevice device)=>Unmounted.Add(device);
}
sealed class HugeStream : Stream
{
    public int Reads;
    public override long Length=>long.MaxValue;
    public override int Read(byte[] buffer,int offset,int count){Reads++;throw new Exception("Must not read an oversized identity");}
    public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;
    public override long Position{get=>0;set=>throw new NotSupportedException();}
    public override void Flush()=>throw new NotSupportedException();
    public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
    public override void SetLength(long value)=>throw new NotSupportedException();
    public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
}
}
