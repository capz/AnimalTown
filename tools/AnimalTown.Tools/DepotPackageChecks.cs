using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AnimalTown;

internal static class DepotPackageChecks
{
    internal static void Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"animaltown-depot-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        int checks=0;
        void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
        void Reject(Action action,string message){bool refused=false;try{action();}catch(InvalidDataException){refused=true;}Check(refused,message);}
        try{
            const string commit="0123456789abcdef0123456789abcdef01234567",version="1.2.3";
            string digest=new('A',64),disc=Path.Combine(root,"out/disc"),native=Path.Combine(root,"out/native-depot"),notices=Path.Combine(root,"out/depot-notices");
            Directory.CreateDirectory(disc);Directory.CreateDirectory(Path.Combine(native,"source"));Directory.CreateDirectory(notices);
            DepotPackage.Prepare(root,commit,version,digest,3,commit.Substring(0,12));
            File.WriteAllText(Path.Combine(root,"out/version.json"),JsonSerializer.Serialize(new{commit,version}));
            File.WriteAllText(Path.Combine(root,"out/build-revisions.txt"),$"Version: {version}\nGame source: {commit}\nSource digest: {digest}\nRuntime profile: 3\nDirty: False\n");
            var dol=new byte[288];void Big(int at,uint value)=>BinaryPrimitives.WriteUInt32BigEndian(dol.AsSpan(at),value);
            Big(0,256);Big(0x48,0x80003100);Big(0x90,32);Big(0xe0,0x80003100);File.WriteAllBytes(Path.Combine(native,"Game.dol"),dol);
            File.WriteAllText(Path.Combine(native,"Makefile"),"CFLAGS += -DDND_CAPTURE_LAUNCH_ARGUMENTS\n");File.WriteAllText(Path.Combine(native,"source/generated_program.c"),"dnd_launch_count();");
            DepotPackage.Receipt(root);
            using(var output=File.Create(Path.Combine(disc,"buildings.bin")))using(var writer=new BinaryWriter(output)){
                void Text(string s){writer.Write(s.Length);writer.Write(Encoding.ASCII.GetBytes(s));}
                writer.Write(0x424c4447);writer.Write(2);writer.Write(1);Text("cottage_small");writer.Write(4);
                foreach(int n in new[]{973,3092,1536,3584,666,717,2867,922,1229})writer.Write(n);
                writer.Write(1);writer.Write(2048);writer.Write(2048);writer.Write(0);
                foreach(string part in new[]{"exterior","door","floor","north","east","south","west"})Text("cottage-"+part);
            }
            var floor=new byte[FloorStrips.Records];void Little(int at,int n)=>BinaryPrimitives.WriteInt32LittleEndian(floor.AsSpan(at),n);
            Little(0,0x32525441);Little(4,1);Little(8,TerrainTiles.SurfaceCount);Little(12,1);Little(16,FloorTexture.Maximum);
            for(int i=0;i<TerrainTiles.SurfaceCount;i++){Little(20+i*20,1);Little(28+i*20,-1);Little(32+i*20,-1);Little(36+i*20,-1);}
            foreach(string season in new[]{"spring","summer","fall","winter"})File.WriteAllBytes(Path.Combine(disc,"floor-"+season+".bin"),floor);
            foreach(string name in DepotPackage.Payload(disc)){
                if(name is "default.dol" or "content.id" or "version.json"||name=="buildings.bin"||name.EndsWith(".bin",StringComparison.Ordinal))continue;
                string directory=name.EndsWith("-LICENSE.txt",StringComparison.Ordinal)||name is "THIRD-PARTY-NOTICES.txt" or "DEPOT-README.txt"?notices:disc;
                byte[] bytes=name=="opening.bnr"?new byte[6496]:name=="savebanner.rgb5a3"?new byte[6144]:name=="saveicon.rgb5a3"?new byte[2048]:Encoding.ASCII.GetBytes(name=="version.md"?version:name);
                if(name=="opening.bnr")bytes=DiscBannerFile.Create(new byte[6144],version);if(name=="runtime.cfg")bytes=new byte[]{3};
                File.WriteAllBytes(Path.Combine(directory,name),bytes);
            }
            foreach(string unwanted in new[]{"animal-town-updater.dol","roots.pem","entropy.bin","sunshine-water.dndtex","source.glb","other.DOL"})File.WriteAllText(Path.Combine(disc,unwanted),"must not ship");
            string zip=Path.Combine(root,"AnimalTown-1.2.3-depot.zip");DepotPackage.Build(root,zip);
            using(var archive=ZipFile.OpenRead(zip)){
                Check(archive.Entries.All(e=>e.FullName.StartsWith("apps/animaltown/",StringComparison.Ordinal)),"exact installed app directory");
                Check(archive.Entries.Count(e=>e.FullName.EndsWith(".dol",StringComparison.OrdinalIgnoreCase))==1,"exactly one playable executable");
                Check(!archive.Entries.Any(e=>e.FullName.Contains("updater",StringComparison.Ordinal)||e.FullName.Contains("sunshine",StringComparison.Ordinal)||e.FullName.EndsWith(".pem",StringComparison.Ordinal)),"credentials, updater and retired assets are excluded");
            }
            void Mutated(string entry,byte[]? bytes=null,int? attributes=null){
                string changed=Path.Combine(root,"bad.zip");File.Copy(zip,changed,true);
                using(var archive=ZipFile.Open(changed,ZipArchiveMode.Update)){
                    archive.GetEntry(entry)?.Delete();var item=archive.CreateEntry(entry);if(attributes.HasValue)item.ExternalAttributes=attributes.Value;
                    using var output=item.Open();output.Write(bytes??new byte[]{1});
                }
                Reject(()=>DepotPackage.Verify(root,changed),"reject malformed package: "+entry);
            }
            Mutated("../outside");Mutated("/apps/animaltown/default.dol");Mutated("Apps/animaltown/extra.txt");Mutated("apps/animaltown/DEFAULT.DOL");
            Mutated("apps/animaltown/other.dol");Mutated("apps/animaltown/wordmark.dndtex");Mutated("apps/animaltown/version.md",Encoding.ASCII.GetBytes("1.2.4"));
            Mutated("apps/animaltown/link",attributes:unchecked((int)0xa1ff0000));
            Mutated("apps/animaltown/opening.bnr",new byte[6496]);Mutated("apps/animaltown/depot-package.json",Encoding.ASCII.GetBytes("{}"));
            string changedDol=Path.Combine(native,"Game.dol");dol[256]=1;File.WriteAllBytes(changedDol,dol);
            Reject(()=>DepotPackage.Verify(root,zip),"stale app build receipt rejects another executable");
            Console.WriteLine($"{checks} Depot package checks passed: clean extraction, strict paths/case, closed payload, source identity and corruption rejection");
        }finally{Directory.Delete(root,true);}
    }
}
