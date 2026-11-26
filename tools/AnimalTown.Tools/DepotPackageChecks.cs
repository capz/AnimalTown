using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
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
            // Startup admission is part of the real runtime payload. Generate
            // its catalog and coarse models from valid, checksum-bearing DNMD
            // fixtures rather than supplying a placeholder .bin or weakening
            // package validation for these new assets.
            foreach(string species in ModelLodCooker.Species){
                byte[] fixture=ModelLodChecks.Grid();
                File.WriteAllBytes(Path.Combine(disc,species+"-tex128.dndmodel"),fixture);
                File.WriteAllBytes(Path.Combine(disc,species+".dndmodel"),fixture);
            }
            ModelLodCooker.CookAll(root);ModelMemoryCooker.Cook(root);ModelMemoryCooker.Validate(root);
            using(var image=WaterTextureArtChecks.Fixture(1))File.WriteAllBytes(Path.Combine(disc,WaterTextureCatalog.DiscName(1)),WaterTextureArt.Encode(image,1));
            Check(DepotPackage.Payload(disc).Contains(WaterTextureCatalog.DiscName(1)),"valid optional water artwork enters the closed payload");
            foreach(string unwanted in new[]{"animal-town-updater.dol","roots.pem","entropy.bin","sunshine-water.dndtex","water-unknown.dndtex","source.glb","other.DOL"})File.WriteAllText(Path.Combine(disc,unwanted),"must not ship");
            foreach(string species in new[]{"fox","bunny","giraffe","cat","hamster","spaniel"})foreach(int edge in new[]{512,1024}){
                string name=species+"-tex"+edge+".dndmodel";
                File.WriteAllText(Path.Combine(disc,name),"stale oversized model");File.WriteAllText(Path.Combine(disc,name+".deps.json"),"stale cooking receipt");
                Check(!DepotPackage.Payload(disc).Contains(name),"oversized villagers are outside the runtime whitelist: "+name);
            }
            string otherVariant=Path.Combine(disc,"building-tex1024.dndmodel"),otherHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(otherVariant)));
            Check(ModelVariants.Prune(root)==12&&ModelVariants.Prune(root)==0,"incremental/SkipAssets staging removes all twelve oversized villager packages and is idempotent");
            Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(otherVariant)))==otherHash,"villager pruning preserves other model variants byte-for-byte");
            Check(File.Exists(Path.Combine(disc,"source.glb")),"pruning never deletes artist source files");
            foreach(string species in new[]{"fox","bunny","giraffe","cat","hamster","spaniel"})foreach(int edge in new[]{512,1024})
                Check(!File.Exists(Path.Combine(disc,species+"-tex"+edge+".dndmodel.deps.json")),"obsolete villager cooking receipts retire with their generated models");
            string zip=Path.Combine(root,"AnimalTown-1.2.3-depot.zip");DepotPackage.Build(root,zip);
            using(var archive=ZipFile.OpenRead(zip)){
                Check(archive.Entries.All(e=>e.FullName.StartsWith("apps/animaltown/",StringComparison.Ordinal)),"exact installed app directory");
                Check(archive.Entries.Count(e=>e.FullName.EndsWith(".dol",StringComparison.OrdinalIgnoreCase))==1,"exactly one playable executable");
                Check(archive.GetEntry("apps/animaltown/mbedTLS-LICENSE.txt")==null&&
                      archive.GetEntry("apps/animaltown/Jitterentropy-LICENSE.txt")==null,"standard packages omit the optional networking notices");
                Check(!archive.Entries.Any(e=>e.FullName.Contains("updater",StringComparison.Ordinal)||e.FullName.Contains("sunshine",StringComparison.Ordinal)||e.FullName.EndsWith(".pem",StringComparison.Ordinal)),"credentials, updater and retired assets are excluded");
                Check(archive.GetEntry("apps/animaltown/"+WaterTextureCatalog.DiscName(1))!=null&&archive.GetEntry("apps/animaltown/water-unknown.dndtex")==null,"only declared optional water maps ship in the real Depot archive");
                foreach(string species in new[]{"fox","bunny","giraffe","cat","hamster","spaniel"}){
                    Check(archive.GetEntry("apps/animaltown/"+species+"-tex128.dndmodel")!=null,"gameplay villager package is included: "+species);
                    Check(archive.GetEntry("apps/animaltown/"+species+"-lod1-tex128.dndmodel")!=null,"coarse villager package is included: "+species);
                    Check(!archive.Entries.Any(e=>e.FullName=="apps/animaltown/"+species+"-tex512.dndmodel"||e.FullName=="apps/animaltown/"+species+"-tex1024.dndmodel"),"oversized villager packages are excluded: "+species);
                }
                var memoryEntry=archive.GetEntry("apps/animaltown/"+ModelMemoryCooker.DiscName);Check(memoryEntry!=null&&memoryEntry.Length==160,"package-bound startup memory catalog is included");
            }
            File.WriteAllText(Path.Combine(disc,"mbedTLS-LICENSE.txt"),"mbedTLS fixture");
            Reject(()=>DepotPackage.Payload(disc),"reject a partial set of networking notices");
            File.WriteAllText(Path.Combine(disc,"Jitterentropy-LICENSE.txt"),"Jitterentropy fixture");
            var networkingNotices=new[]{"mbedTLS-LICENSE.txt","Jitterentropy-LICENSE.txt"};
            foreach(string name in networkingNotices)File.Copy(Path.Combine(disc,name),Path.Combine(notices,name));
            string pairingZip=Path.Combine(root,"pairing-depot.zip");
            DepotPackage.Build(root,pairingZip);
            using(var archive=ZipFile.OpenRead(pairingZip)){
                Check(networkingNotices.All(name=>archive.GetEntry("apps/animaltown/"+name)!=null),"pairing packages include both native networking notices");
            }
            Reject(()=>DepotPackage.Verify(root,zip),"pairing disc requires pairing notices in its Depot package");
            foreach(string name in networkingNotices)File.Delete(Path.Combine(disc,name));
            // Fresh PowerShell processes can serialize hashtable properties in
            // another order. Equal snapshots remain equal; new fields do not.
            string versionFile=Path.Combine(root,"out/version.json");
            File.WriteAllText(versionFile,JsonSerializer.Serialize(new{version,commit}));
            DepotPackage.Verify(root,zip);Check(true,"version metadata object order does not change the source snapshot");
            File.WriteAllText(versionFile,JsonSerializer.Serialize(new{commit,version,mainCommit="another snapshot"}));
            Reject(()=>DepotPackage.Verify(root,zip),"version snapshot differences are still rejected");
            File.WriteAllText(versionFile,JsonSerializer.Serialize(new{commit,version}));
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
            Mutated("apps/animaltown/"+ModelMemoryCooker.DiscName);
            foreach(string species in ModelLodCooker.Species)Mutated("apps/animaltown/"+species+"-lod1-tex128.dndmodel");
            Mutated("apps/animaltown/"+WaterTextureCatalog.DiscName(1));Mutated("apps/animaltown/water-unknown.dndtex");
            // Even a self-consistent manifest must retain the same optional art
            // as the ISO; removing it cannot silently select a different look.
            string withoutWater=Path.Combine(root,"without-water.zip");File.Copy(zip,withoutWater);
            using(var archive=ZipFile.Open(withoutWater,ZipArchiveMode.Update)){
                string name=WaterTextureCatalog.DiscName(1);archive.GetEntry("apps/animaltown/"+name)!.Delete();
                var entry=archive.GetEntry("apps/animaltown/depot-package.json")!;
                System.Text.Json.Nodes.JsonNode manifest;
                using(var input=entry.Open())manifest=System.Text.Json.Nodes.JsonNode.Parse(input)!;
                manifest["Files"]!.AsObject().Remove(name);entry.Delete();
                using var output=archive.CreateEntry("apps/animaltown/depot-package.json").Open();output.Write(Encoding.UTF8.GetBytes(manifest.ToJsonString()));
            }
            Reject(()=>DepotPackage.Verify(root,withoutWater),"manifested missing optional art still rejects ISO/Depot mismatch");
            foreach(string species in new[]{"fox","bunny","giraffe","cat","hamster","spaniel"})foreach(int edge in new[]{512,1024})Mutated("apps/animaltown/"+species+"-tex"+edge+".dndmodel");
            Mutated("apps/animaltown/link",attributes:unchecked((int)0xa1ff0000));
            Mutated("apps/animaltown/opening.bnr",new byte[6496]);Mutated("apps/animaltown/depot-package.json",Encoding.ASCII.GetBytes("{}"));
            string runtimeZip=zip;zip=Path.Combine(root,"with-reports.zip");File.Copy(runtimeZip,zip);
            var reportFiles=new Dictionary<string,byte[]>{
                {"BUILD.txt",Encoding.UTF8.GetBytes("release build report")},
                {"terrain-materials-mips-auto.png",new byte[]{137,80,78,71,13,10,26,10}},
                {"terrain-materials-water-river-legacy-frozen-b.png",new byte[]{137,80,78,71,13,10,26,10,1}},
                {"terrain-materials-report.json",JsonSerializer.SerializeToUtf8Bytes(new{scenario="terrain-materials",passed=true})},
                {"terrain-materials-build.json",JsonSerializer.SerializeToUtf8Bytes(new{commit,version})}
            };
            reportFiles.Add("daylight-build.json",JsonSerializer.SerializeToUtf8Bytes(new{commit,version}));
            foreach(string name in new[]{"build","smoothing-aa-option","options-title-options","options-title-options-load","unsupported-title-options-unsupported"})
                reportFiles.Add("video-options-"+name+".json",JsonSerializer.SerializeToUtf8Bytes(new{commit,version,passed=true}));
            foreach(string name in new[]{"smoothing-aa-initial-home","smoothing-aa-menu-on","smoothing-aa-menu-off","smoothing-aa-menu-restored","smoothing-aa-576p-rejected","options-options-restored","options-options-delete-default","unsupported-options-unsupported"})
                reportFiles.Add("video-options-"+name+".png",new byte[]{137,80,78,71,13,10,26,10});
            reportFiles.Add("scenarios/jobs/jobs-delivery-result.png",new byte[]{137,80,78,71,13,10,26,10});
            reportFiles.Add("scenarios/jobs/jobs-delivery-state.json",JsonSerializer.SerializeToUtf8Bytes(new{mode="delivery",passed=true}));
            foreach(string phase in new[]{"noon","dawn","dusk","night"}){
                reportFiles.Add("daylight-"+phase+"-report.json",JsonSerializer.SerializeToUtf8Bytes(new{passed=true}));
                foreach(string shot in new[]{"a","b"})reportFiles.Add("daylight-"+phase+"-"+shot+".png",new byte[]{137,80,78,71,13,10,26,10});
            }
            var reportHashes=new SortedDictionary<string,string>(StringComparer.Ordinal);
            foreach(var pair in reportFiles)reportHashes.Add(pair.Key,Convert.ToHexString(SHA256.HashData(pair.Value)).ToLowerInvariant());
            var reportManifest=new{schema=1,commit,version,files=reportHashes};
            using(var archive=ZipFile.Open(zip,ZipArchiveMode.Update)){
                foreach(var pair in reportFiles){using var output=archive.CreateEntry("release/"+pair.Key).Open();output.Write(pair.Value);}
                using(var output=archive.CreateEntry("release/manifest.json").Open())output.Write(JsonSerializer.SerializeToUtf8Bytes(reportManifest));
            }
            DepotPackage.Verify(root,zip);Check(true,"terrain PNGs and exact build/report JSON names pass the release report whitelist");
            using(var original=ZipFile.OpenRead(runtimeZip))using(var bundled=ZipFile.OpenRead(zip)){
                Check(original.Entries.Select(e=>e.FullName).Order(StringComparer.Ordinal).SequenceEqual(bundled.Entries.Where(e=>e.FullName.StartsWith("apps/animaltown/",StringComparison.Ordinal)).Select(e=>e.FullName).Order(StringComparer.Ordinal)),"release evidence does not add installed app files");
                foreach(var entry in original.Entries){
                    using var before=entry.Open();using var after=bundled.GetEntry(entry.FullName)!.Open();
                    Check(SHA256.HashData(before).SequenceEqual(SHA256.HashData(after)),"release evidence preserves runtime entry bytes: "+entry.FullName);
                }
            }
            // Supply a correct hash and manifest entry, so these failures test
            // the report allowlist rather than merely an untracked ZIP entry.
            void UnsupportedReport(string name){
                string changed=Path.Combine(root,"bad-report.zip");File.Copy(zip,changed,true);
                byte[] bytes=Encoding.UTF8.GetBytes("not authorized supplementary content");
                var hashes=new SortedDictionary<string,string>(reportHashes,StringComparer.Ordinal){[name]=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()};
                using(var archive=ZipFile.Open(changed,ZipArchiveMode.Update)){
                    using(var output=archive.CreateEntry("release/"+name).Open())output.Write(bytes);
                    archive.GetEntry("release/manifest.json")!.Delete();
                    using var manifestOutput=archive.CreateEntry("release/manifest.json").Open();manifestOutput.Write(JsonSerializer.SerializeToUtf8Bytes(new{schema=1,commit,version,files=hashes}));
                }
                Reject(()=>DepotPackage.Verify(root,changed),"reject manifested unsupported report: "+name);
            }
            foreach(string name in new[]{"terrain-materials.png","terrain-materials-Frame.png","terrain-materials-frame.PNG","terrain-materials-unlisted.json","terrain-materials-source.cs","terrain-materials-frame.png/source.py","scenarios/terrain-materials/frame.png"})UnsupportedReport(name);
            foreach(string name in new[]{"daylight-noon.py","daylight-noon-a.PNG","daylight-Night-a.png","daylight-midnight-a.png","daylight-night-extra.json","daylight-night-extra.png","daylight-build.json/source.py","daylight/night-a.png"})UnsupportedReport(name);
            foreach(string name in new[]{"video-options-extra.json","video-options-smoothing-extra.json","video-options-build.py","video-options-smoothing-source.cs","video-options-smoothing-aa-menu-on.PNG","video-options-smoothing-aa-menu-on.png.cs","video-options-unknown-frame.png","video-options-build.json/source.py","video-options/smoothing/frame.png"})UnsupportedReport(name);
            Mutated("release/BUILD.txt",Encoding.UTF8.GetBytes("corrupt report"));
            Mutated("release/terrain-materials-mips-auto.png",Encoding.UTF8.GetBytes("corrupt capture"));
            Mutated("release/daylight-night-a.png",Encoding.UTF8.GetBytes("corrupt night capture"));
            Mutated("release/video-options-smoothing-aa-menu-on.png",Encoding.UTF8.GetBytes("corrupt video capture"));
            Mutated("release/animal-town-updater.dol");Mutated("release/source.cs");Mutated("release/scenarios/performance/source.py");
            Mutated("release/scenarios/performance/../bad.txt");Mutated("release/scenarios/performance/link.txt",attributes:unchecked((int)0xa1ff0000));
            Mutated("release/manifest.json",JsonSerializer.SerializeToUtf8Bytes(new{schema=1,commit=new string('b',40),version,files=reportManifest.files}));
            Mutated("release/manifest.json",JsonSerializer.SerializeToUtf8Bytes(new{schema=1,commit,version="1.2.4",files=reportManifest.files}));
            Mutated("release/manifest.json",new byte[1024*1024+1]);
            zip=runtimeZip;
            string changedDol=Path.Combine(native,"Game.dol");dol[256]=1;File.WriteAllBytes(changedDol,dol);
            Reject(()=>DepotPackage.Verify(root,zip),"stale app build receipt rejects another executable");
            Console.WriteLine($"{checks} Depot package checks passed: clean extraction, strict paths/case, closed payload, source identity and corruption rejection");
        }finally{Directory.Delete(root,true);}
    }
}
