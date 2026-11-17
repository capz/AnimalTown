using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AnimalTown;

// A closed runtime payload, not a copy of the disc staging directory. In
// particular, updater credentials, additional DOLs and authoring files never
// enter a Depot installation even if a previous build left them in out/disc.
internal static class DepotPackage
{
    private const string Prefix="apps/animaltown/",Manifest="depot-package.json";
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    private sealed record Identity(string Commit,string Version,string SourceDigest,int RuntimeProfile)
    {
        public string Text=>ContentSelection.Identity(Commit,Version,SourceDigest,RuntimeProfile);
    }
    private sealed record BuildReceipt(string Profile,string ContentIdentity,string DolSha256);
    private sealed record PackageManifest(int Schema,string Profile,string Commit,string Version,string SourceDigest,int RuntimeProfile,SortedDictionary<string,string> Files);
    private static string Hash(string path){using var file=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();}
    private static string At(string root,string path)=>Path.Combine(root,path);
    private static string ReadSmall(string path,int limit)
    {
        if(new FileInfo(path).Length>limit)throw new InvalidDataException("Oversized package metadata: "+path);
        return File.ReadAllText(path);
    }
    private static void Validate(Identity id)
    {
        if(!Regex.IsMatch(id.Commit,@"\A[0-9a-f]{40}\z")||!Regex.IsMatch(id.SourceDigest,@"\A[0-9a-fA-F]{64}\z")||id.RuntimeProfile<0||id.RuntimeProfile>7)
            throw new InvalidDataException("Invalid content build identity");
        DiscVersionFile.Verify(Encoding.ASCII.GetBytes(id.Version),id.Version);
    }
    private static Identity ReadIdentity(string directory)
    {
        string text=ReadSmall(At(directory,"content.id"),512);string[] lines=text.Split('\n');
        if(lines.Length!=6||lines[0]!="ANIMALTOWN-CONTENT-1"||lines[5]!=""||!int.TryParse(lines[4],out int profile))throw new InvalidDataException("Invalid content identity file");
        var id=new Identity(lines[1],lines[2],lines[3],profile);Validate(id);
        if(text!=id.Text)throw new InvalidDataException("Noncanonical content identity");return id;
    }
    internal static void Prepare(string root,string commit,string version,string digest,int profile,string displayCommit)
    {
        var id=new Identity(commit,version,digest,profile);Validate(id);
        string disc=At(root,"out/disc");Directory.CreateDirectory(disc);
        File.WriteAllText(At(disc,"content.id"),id.Text,Encoding.ASCII);
        File.WriteAllText(At(root,"out/BuildInfo.cs"),"internal static class BuildInfo { internal const string Commit="+JsonSerializer.Serialize(displayCommit)+"; internal const string Version="+JsonSerializer.Serialize(version)+"; internal const string ContentIdentity="+JsonSerializer.Serialize(id.Text)+"; }\n");
    }
    internal static void Receipt(string root)
    {
        var id=ReadIdentity(At(root,"out/disc"));
        string native=At(root,"out/native-depot");
        if(!File.ReadAllText(At(native,"Makefile")).Contains("-DDND_CAPTURE_LAUNCH_ARGUMENTS",StringComparison.Ordinal))throw new InvalidDataException("App build must include SDK launch context");
        // Reachability must retain the app bootstrap and its launch-context
        // call; a renamed ISO-dependent Game.dol is not an app build.
        string generated=File.ReadAllText(At(native,"source/generated_program.c"));
        if(!generated.Contains("dnd_launch_count(",StringComparison.Ordinal))throw new InvalidDataException("App build has no incoming launch-context call");
        Program.ValidateDol(File.ReadAllBytes(At(native,"Game.dol")));
        File.WriteAllText(At(root,"out/depot-build.json"),JsonSerializer.Serialize(new BuildReceipt("sd-app",id.Text,Hash(At(native,"Game.dol"))),Json)+"\n");
    }
    internal static string[] Payload(string directory)
    {
        var files=new SortedSet<string>(StringComparer.Ordinal){
            "default.dol","opening.bnr","BUILD.txt","version.md","version.json","content.id","runtime.cfg","terrain.cfg","buildings.bin",
            "savebanner.rgb5a3","saveicon.rgb5a3","gorditas-bold.dndtex","gorditas-bold.metrics","Gorditas-OFL.txt","wordmark.dndtex","item-icons.dndtex",
            "splash-dolphindotnet.dndtex","splash-hypercube-engine.dndtex","cliff-soil.dndtex",PathAssets.DiscName,
            "THIRD-PARTY-NOTICES.txt","DolphinDotNet-LICENSE.txt","Hypercube-LICENSE.txt","Ryu-LICENSE.txt","ufbx-LICENSE.txt","DEPOT-README.txt"
        };
        foreach(string name in DroppedItemAssets.Names)files.Add(name+".dndtex");
        var models=new SortedSet<string>(new[]{"fox","bunny","giraffe","cat","hamster","spaniel","building","tent"},StringComparer.Ordinal);
        foreach(string name in new[]{"fox","bunny","giraffe","cat","hamster","spaniel"})files.Add("picker-"+name+".dndtex");
        BuildingCatalog.Configure(File.ReadAllBytes(At(directory,"buildings.bin")));
        for(int i=0;i<BuildingCatalog.Count;i++){
            var d=BuildingCatalog.Get(i);
            foreach(string name in new[]{d.Exterior,d.Exterior+"-closed",d.Door,d.Floor,d.North,d.East,d.South,d.West})models.Add(name);
        }
        foreach(string model in models)foreach(string suffix in new[]{"-tex128","","-tex512","-tex1024"})files.Add(model+suffix+".dndmodel");
        foreach(string season in new[]{"spring","summer","fall","winter"}){
            string name="floor-"+season;files.Add(name+".bin");byte[] data=File.ReadAllBytes(At(directory,name+".bin"));FloorStrips.Validate(data);
            bool shared=FloorStrips.Read(data,4)==2;
            for(int slot=0;slot<FloorStrips.Read(data,12);slot++){
                int source=FloorStrips.TextureSlot(data,slot);
                if(shared&&source==0){files.Add(NeutralFloorAssets.FullTexture);files.Add(NeutralFloorAssets.PreviewTexture);}
                else files.Add(name+"-"+source+".dndtex");
            }
        }
        foreach(string name in files)if(name.IndexOfAny(new[]{'/','\\',':'})>=0||name is "." or "..")throw new InvalidDataException("Unsafe runtime asset name");
        return files.ToArray();
    }
    private static void VerifyBuild(string root,Identity id)
    {
        using var version=JsonDocument.Parse(ReadSmall(At(root,"out/version.json"),65536));var v=version.RootElement;
        if(v.GetProperty("commit").GetString()!=id.Commit||v.GetProperty("version").GetString()!=id.Version)throw new InvalidDataException("App version/source differs from release");
        string revisions=ReadSmall(At(root,"out/build-revisions.txt"),65536);
        foreach(string line in new[]{"Version: "+id.Version,"Game source: "+id.Commit,"Source digest: "+id.SourceDigest,"Runtime profile: "+id.RuntimeProfile,"Dirty: False"})
            if(!revisions.Replace("\r","").Split('\n').Contains(line))throw new InvalidDataException("App build revisions differ: "+line);
        var receipt=JsonSerializer.Deserialize<BuildReceipt>(ReadSmall(At(root,"out/depot-build.json"),4096))??throw new InvalidDataException("Missing app build receipt");
        if(receipt.Profile!="sd-app"||receipt.ContentIdentity!=id.Text||receipt.DolSha256!=Hash(At(root,"out/native-depot/Game.dol")))throw new InvalidDataException("App executable differs from its build receipt");
    }
    internal static void Build(string root,string output)
    {
        string disc=At(root,"out/disc");var id=ReadIdentity(disc);VerifyBuild(root,id);
        output=Path.GetFullPath(output);Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        string temporary=output+".tmp-"+Guid.NewGuid().ToString("N");
        string stage=At(root,"out/depot-work/"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
        try{
            // First copy only metadata needed to derive the closed whitelist.
            foreach(string name in new[]{"buildings.bin","floor-spring.bin","floor-summer.bin","floor-fall.bin","floor-winter.bin"})File.Copy(At(disc,name),At(stage,name));
            var files=Payload(stage);
            foreach(string name in files){
                string source=name=="default.dol"?At(root,"out/native-depot/Game.dol"):name=="version.json"?At(root,"out/version.json"):name.EndsWith("-LICENSE.txt",StringComparison.Ordinal)||name is "THIRD-PARTY-NOTICES.txt" or "DEPOT-README.txt"?At(root,"out/depot-notices/"+name):At(disc,name);
                File.Copy(source,At(stage,name),true);
            }
            var hashes=new SortedDictionary<string,string>(StringComparer.Ordinal);foreach(string name in files)hashes.Add(name,Hash(At(stage,name)));
            var manifest=new PackageManifest(1,"sd-app",id.Commit,id.Version,id.SourceDigest,id.RuntimeProfile,hashes);
            File.WriteAllText(At(stage,Manifest),JsonSerializer.Serialize(manifest,Json)+"\n");
            using(var archive=ZipFile.Open(temporary,ZipArchiveMode.Create)){
                foreach(string name in files.Append(Manifest)){
                    var entry=archive.CreateEntry(Prefix+name,CompressionLevel.Optimal);
                    entry.LastWriteTime=new DateTimeOffset(2000,1,1,0,0,0,TimeSpan.Zero);entry.ExternalAttributes=0;
                    using var input=File.OpenRead(At(stage,name));using var destination=entry.Open();input.CopyTo(destination);
                }
            }
            Verify(root,temporary);File.Move(temporary,output,true);Console.WriteLine("Dolphin Depot runtime package: "+output);
        }finally{if(File.Exists(temporary))File.Delete(temporary);Directory.Delete(stage,true);}
    }
    internal static void Verify(string root,string archive)
    {
        string stage=At(root,"out/depot-verify/"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
        try{
            using(var zip=ZipFile.OpenRead(archive)){
                var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;
                if(zip.Entries.Count<2||zip.Entries.Count>2048)throw new InvalidDataException("Invalid Depot file count");
                foreach(var entry in zip.Entries){
                    string name=entry.FullName;
                    if(!name.StartsWith(Prefix,StringComparison.Ordinal)||name.Length==Prefix.Length||name.Substring(Prefix.Length).IndexOfAny(new[]{'/','\\',':'})>=0||name.Substring(Prefix.Length) is "." or ".."||name.Any(c=>c<' '||c>126)||!names.Add(name)||((entry.ExternalAttributes>>16)&0xf000)==0xa000)
                        throw new InvalidDataException("Unsafe, duplicate or incorrectly cased Depot archive path: "+name);
                    total=checked(total+entry.Length);if(total>512L*1024*1024)throw new InvalidDataException("Depot payload exceeds budget");
                }
                zip.ExtractToDirectory(stage);
            }
            string app=At(stage,Prefix);var id=ReadIdentity(app);VerifyBuild(root,id);
            var manifest=JsonSerializer.Deserialize<PackageManifest>(ReadSmall(At(app,Manifest),1024*1024))??throw new InvalidDataException("Missing package manifest");
            if(manifest.Schema!=1||manifest.Profile!="sd-app"||manifest.Commit!=id.Commit||manifest.Version!=id.Version||manifest.SourceDigest!=id.SourceDigest||manifest.RuntimeProfile!=id.RuntimeProfile||manifest.Files==null)throw new InvalidDataException("Depot manifest identity mismatch");
            string[] expected=Payload(app);
            if(!expected.SequenceEqual(manifest.Files.Keys.Order(StringComparer.Ordinal))||!expected.Append(Manifest).Order(StringComparer.Ordinal).SequenceEqual(Directory.GetFiles(app).Select(Path.GetFileName).Order(StringComparer.Ordinal)))throw new InvalidDataException("Depot archive differs from the runtime whitelist");
            foreach(string name in expected)if(Hash(At(app,name))!=manifest.Files[name])throw new InvalidDataException("Depot payload checksum mismatch: "+name);
            if(Hash(At(app,"default.dol"))!=Hash(At(root,"out/native-depot/Game.dol")))throw new InvalidDataException("Depot executable is not this app build");
            Program.ValidateDol(File.ReadAllBytes(At(app,"default.dol")));
            byte[] banner=File.ReadAllBytes(At(app,"opening.bnr"));
            DiscBannerFile.Verify(banner,id.Version);
            if(Hash(At(app,"opening.bnr"))!=Hash(At(root,"out/disc/opening.bnr")))throw new InvalidDataException("Depot banner differs from the disc build");
            if(new FileInfo(At(app,"savebanner.rgb5a3")).Length!=96*32*2||new FileInfo(At(app,"saveicon.rgb5a3")).Length!=32*32*2)throw new InvalidDataException("Depot save artwork is invalid");
            DiscVersionFile.Verify(File.ReadAllBytes(At(app,"version.md")),id.Version);
            if(!File.ReadAllBytes(At(app,"runtime.cfg")).SequenceEqual(new[]{(byte)id.RuntimeProfile}))throw new InvalidDataException("Depot runtime profile differs from build");
            if(ReadSmall(At(app,"BUILD.txt"),65536)!=File.ReadAllText(At(root,"out/disc/BUILD.txt")))throw new InvalidDataException("Depot BUILD.txt differs from console build");
            if(ReadSmall(At(app,"version.json"),65536)!=File.ReadAllText(At(root,"out/version.json")))throw new InvalidDataException("Depot version metadata differs from release");
            Console.WriteLine("Verified clean Depot extraction, closed payload, exact source/version, DOL, BNR1, save artwork and per-file SHA-256");
        }finally{Directory.Delete(stage,true);}
    }
    internal static void Check()=>DepotPackageChecks.Run();
}
