using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Numerics;

static class Program
{
    static uint Read(byte[] b,int p)=>BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(p,4));
    static void Put(byte[] b,int p,uint n)=>BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(p,4),n);
    static long Align(long n,int by=32)=>(n+by-1)/by*by;
    static void WriteAt(FileStream f,long at,byte[] data){f.Position=at;f.Write(data);}
    static string Root="";
    static int Main(string[] args)
    {
        try
        {
            if(args.Length<2)throw new ArgumentException("repack-textures ROOT INPUT.glb OUTPUT.glb --size128|--size256|--size512|--size1024|--sizeOriginal [--max-rmse N]; or repack-check ROOT");
            Root=Path.GetFullPath(args[1]);
            if(args[0]=="buildings-generate")BuildingAssets.Generate(Root);
            else if(args[0]=="shop-generate")BuildingAssets.GenerateShop(Root);
            else if(args[0]=="buildings")BuildingAssets.Cook(Root);
            else if(args[0]=="building-native"||args[0]=="model-native")NativeModelWinding.ConvertFile(args[2]);
            else if(args[0]=="building-check")BuildingAssets.Check(Root);
            else if(args[0]=="repack-textures")TextureRepacker.Command(args);
            else if(args[0]=="town-preview")TownGenerationPreview.Run(args[2],int.Parse(args[3]),args.Length>4?int.Parse(args[4]):1);
            else if(args[0]=="terrain-primitives")TerrainPrimitiveAssets.Build(Root,args.Length>2?int.Parse(args[2]):128);
            else if(args[0]=="repack-check")TextureRepackerChecks.Run(Root);
            else if(args[0]=="audit")Audit();
            else if(args[0]=="prepare")Prepare(args[2],args[3],args.Length>4&&args[4]=="--villager-runtime");
            else if(args[0]=="prune-model-variants")ModelVariants.Prune(Root);
            else if(args[0]=="art")Art();
            else if(args[0]=="brand-check"){BrandTextureAssets.Check(Root);DiscBannerFile.Check();}
            else if(args[0]=="dropped-item-import")DroppedItemAssets.Import(Root);
            else if(args[0]=="dropped-item-check")DroppedItemAssets.Check(Root);
            else if(args[0]=="splash-check")SplashAssets.Check(Root);
            else if(args[0]=="splash-import-engine")SplashAssets.ImportEngine(Root,args[2]);
            else if(args[0]=="path-import")PathAssets.Import(Root,args[2]);
            else if(args[0]=="path-assets"||args[0]=="path-check")PathAssets.Build(Root);
            else if(args[0]=="terrain"){RepeatFloorAssets.Build(Root);PathAssets.Build(Root);}
            else if(args[0]=="terrain-check")TerrainChecks.Run(Root);
            else if(args[0]=="tree-audit")TreeRenderAudit.Run(Root,args[2]);
            else if(args[0]=="iso")Disc(args[2],args[3],args.Length>4?args[4]:null);
            else if(args[0]=="banner")ReleaseMetadata(args[2]);
            else if(args[0]=="content-identity")DepotPackage.Prepare(Root,args[2],args[3],args[4],int.Parse(args[5]),args[6]);
            else if(args[0]=="depot-receipt")DepotPackage.Receipt(Root);
            else if(args[0]=="depot")DepotPackage.Build(Root,args[2]);
            else if(args[0]=="verify-depot")DepotPackage.Verify(Root,args[2]);
            else if(args[0]=="depot-check")DepotPackage.Check();
            else if(args[0]=="verify")Verify(args[2]);
            else if(args[0]=="verify-nkit"){Verify(args[2]);NKitImages.Verify(args[2],args[3],args[4]);}
            else if(args[0]=="verify-nkit-output")NKitImages.VerifyOutput(args[2],args[3]);
            else if(args[0]=="nkit-check"){SdkApploader.Check();DiscBannerFile.Check();NKitImages.Check();}
            else throw new ArgumentException("repack-textures|repack-check|audit|art|iso|verify|tree-audit ROOT [PATH] [COMMIT]");
            return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}
    }
    static void Audit()
    {
        var entries=new List<object>();
        foreach(var path in Directory.GetFiles(Path.Combine(Root,"assets"),"*.glb",SearchOption.AllDirectories).Order())
        {
            byte[] b=File.ReadAllBytes(path);int length=BitConverter.ToInt32(b,12);using var doc=JsonDocument.Parse(b.AsMemory(20,length));var j=doc.RootElement;
            int vertices=0,triangles=0;var acc=j.GetProperty("accessors");
            foreach(var mesh in j.GetProperty("meshes").EnumerateArray())foreach(var p in mesh.GetProperty("primitives").EnumerateArray())
            { int v=acc[p.GetProperty("attributes").GetProperty("POSITION").GetInt32()].GetProperty("count").GetInt32();vertices+=v;triangles+=(p.TryGetProperty("indices",out var index)?acc[index.GetInt32()].GetProperty("count").GetInt32():v)/3; }
            var clips=new List<string>();if(j.TryGetProperty("animations",out var animations))foreach(var a in animations.EnumerateArray())clips.Add(a.TryGetProperty("name",out var name)?name.GetString()!:"unnamed");
            var joints=new List<string>();if(j.TryGetProperty("skins",out var skins))foreach(var s in skins.EnumerateArray())foreach(var joint in s.GetProperty("joints").EnumerateArray())joints.Add(j.GetProperty("nodes")[joint.GetInt32()].TryGetProperty("name",out var name)?name.GetString()!:joint.ToString());
            entries.Add(new {path=Path.GetRelativePath(Root,path),bytes=b.Length,vertices,triangles,clips,joints,sha256=Convert.ToHexString(SHA256.HashData(b))});
        }
        Directory.CreateDirectory(Path.Combine(Root,"out"));File.WriteAllText(Path.Combine(Root,"out/asset-audit.json"),JsonSerializer.Serialize(entries,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("Audited "+entries.Count+" GLBs");
    }
    static void Prepare(string input,string output,bool villagerRuntime)
    {
        byte[] b=File.ReadAllBytes(input);int len=BitConverter.ToInt32(b,12);
        var j=JsonNode.Parse(b.AsSpan(20,len))!;
        // The console only plays these two clips. Keep the imported runtime
        // package small enough to admit all of the rigs in an outdoor town.
        if(villagerRuntime&&j["animations"] is JsonArray animations){
            for(int i=animations.Count-1;i>=0;i--)
                if((string?)animations[i]?["name"] is not ("Running" or "Walking"))animations.RemoveAt(i);
            CompactConstantTracks(j,animations,b);
        }
        // Tangent-space normal mapping is not used by the chosen GX material path.
        foreach(var mesh in j["meshes"]!.AsArray())foreach(var p in mesh!["primitives"]!.AsArray())p!["attributes"]!.AsObject().Remove("TANGENT");
        foreach(var m in j["materials"]!.AsArray())m!.AsObject().Remove("normalTexture");
        if(villagerRuntime)KeepVillagerTextures(j);
        // Normalize the actual node-transformed bind-pose bounds, preserving all
        // selected animation tracks and skin inverse binds under a scene root.
        var nodes=j["nodes"]!.AsArray();var minimum=new Vector3(float.PositiveInfinity);var maximum=new Vector3(float.NegativeInfinity);
        float[] Values(JsonNode? n,float[] fallback)=>n==null?fallback:n.AsArray().Select(v=>(float)v!).ToArray();
        Matrix4x4 Local(JsonNode n)
        {
            if(n["matrix"] is JsonArray a){var v=a.Select(v=>(float)v!).ToArray();return new Matrix4x4(v[0],v[1],v[2],v[3],v[4],v[5],v[6],v[7],v[8],v[9],v[10],v[11],v[12],v[13],v[14],v[15]);}
            var s=Values(n["scale"],[1,1,1]);var q=Values(n["rotation"],[0,0,0,1]);var t=Values(n["translation"],[0,0,0]);return Matrix4x4.CreateScale(s[0],s[1],s[2])*Matrix4x4.CreateFromQuaternion(new Quaternion(q[0],q[1],q[2],q[3]))*Matrix4x4.CreateTranslation(t[0],t[1],t[2]);
        }
        void Visit(int index,Matrix4x4 parent)
        {
            var n=nodes[index]!;var world=Local(n)*parent;
            if(n["mesh"]!=null)foreach(var p in j["meshes"]![(int)n["mesh"]!]!["primitives"]!.AsArray())
            {
                var accessor=j["accessors"]![(int)p!["attributes"]!["POSITION"]!]!;var lo=Values(accessor["min"],[0,0,0]);var hi=Values(accessor["max"],[1,1,1]);
                // Skin palettes already include global-joint * inverse-bind;
                // applying the mesh node again double-counts its author scale.
                var boundsTransform=n["skin"]==null?world:Matrix4x4.Identity;
                for(int mask=0;mask<8;mask++){var v=Vector3.Transform(new Vector3((mask&1)==0?lo[0]:hi[0],(mask&2)==0?lo[1]:hi[1],(mask&4)==0?lo[2]:hi[2]),boundsTransform);minimum=Vector3.Min(minimum,v);maximum=Vector3.Max(maximum,v);}
            }
            if(n["children"] is JsonArray children)foreach(var c in children)Visit((int)c!,world);
        }
        var scene=j["scenes"]![(int?)j["scene"]??0]!;var roots=scene["nodes"]!.AsArray();foreach(var r in roots)Visit((int)r!,Matrix4x4.Identity);
        float height=maximum.Y-minimum.Y;if(!float.IsFinite(height)||height<.00001f)throw new InvalidDataException("Invalid model bounds");float scale=1/height;
        var wrapper=new JsonObject{["name"]="AnimalTown normalized root",["scale"]=new JsonArray(scale,scale,scale),["translation"]=new JsonArray(-(minimum.X+maximum.X)*.5f*scale,-minimum.Y*scale,-(minimum.Z+maximum.Z)*.5f*scale),["children"]=roots.DeepClone()};
        int wrapperIndex=nodes.Count;nodes.Add(wrapper);scene["nodes"]=new JsonArray(wrapperIndex);
        byte[] text=Encoding.UTF8.GetBytes(j.ToJsonString());int padded=(int)Align(text.Length,4);int rest=20+len;
        byte[] result=new byte[20+padded+b.Length-rest];Array.Copy(b,result,12);BitConverter.GetBytes(result.Length).CopyTo(result,8);BitConverter.GetBytes(padded).CopyTo(result,12);BitConverter.GetBytes(0x4e4f534a).CopyTo(result,16);
        Array.Fill(result,(byte)32,20,padded);text.CopyTo(result,20);Array.Copy(b,rest,result,20+padded,b.Length-rest);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);File.WriteAllBytes(output,result);
    }
    static void CompactConstantTracks(JsonNode model,JsonArray animations,byte[] glb)
    {
        if(animations.Count==0)return;
        int binaryChunk=20+BinaryPrimitives.ReadInt32LittleEndian(glb.AsSpan(12,4));
        var binary=glb.AsSpan(binaryChunk+8,BinaryPrimitives.ReadInt32LittleEndian(glb.AsSpan(binaryChunk,4)));
        var accessors=model["accessors"]!.AsArray();
        var views=model["bufferViews"]!.AsArray();
        foreach(var animation in animations)
        foreach(var entry in animation!["samplers"]!.AsArray()){
            var sampler=entry!;
            if((string?)sampler["interpolation"] is not (null or "LINEAR" or "STEP"))continue;
            var times=accessors[(int)sampler["input"]!]!;
            var values=accessors[(int)sampler["output"]!]!;
            int keys=(int)times["count"]!;
            int valueSize=(string?)values["type"] switch{"VEC3"=>12,"VEC4"=>16,_=>0};
            if(keys<2||valueSize==0||(int)values["count"]! != keys||
                !AnimationData(times,views,4,out int timeOffset,out int timeStride)||
                !AnimationData(values,views,valueSize,out int valueOffset,out int valueStride))continue;
            var first=binary.Slice(valueOffset,valueSize);
            int key=1;
            while(key<keys&&first.SequenceEqual(binary.Slice(valueOffset+key*valueStride,valueSize)))key++;
            if(key!=keys)continue;
            // A single constant sample at the original last time preserves
            // both the pose and clip duration, even before the first key.
            var time=times.DeepClone();
            var value=values.DeepClone();
            int lastOffset=(keys-1)*timeStride;
            float last=BinaryPrimitives.ReadSingleLittleEndian(binary.Slice(timeOffset+lastOffset,4));
            time["count"]=1;
            value["count"]=1;
            time["byteOffset"]=((int?)time["byteOffset"]??0)+lastOffset;
            time["min"]=new JsonArray(last);
            time["max"]=new JsonArray(last);
            sampler["input"]=accessors.Count;accessors.Add(time);
            sampler["output"]=accessors.Count;accessors.Add(value);
        }
    }
    static bool AnimationData(JsonNode accessor,JsonArray views,int size,out int offset,out int stride)
    {
        offset=0;stride=size;
        if((int?)accessor["componentType"]!=5126||accessor["sparse"]!=null||accessor["bufferView"]==null)return false;
        var view=views[(int)accessor["bufferView"]!]!;
        if((int?)view["buffer"]!=0)return false;
        offset=((int?)view["byteOffset"]??0)+((int?)accessor["byteOffset"]??0);
        stride=(int?)view["byteStride"]??size;
        return true;
    }
    static void KeepVillagerTextures(JsonNode model)
    {
        if(model["textures"] is not JsonArray textures)return;
        var packed=new JsonArray();
        var remap=new Dictionary<int,int>();
        void Keep(JsonNode? reference)
        {
            if(reference==null)return;
            int index=(int)reference["index"]!;
            if(!remap.TryGetValue(index,out int packedIndex)){
                packedIndex=packed.Count;
                packed.Add(textures[index]!.DeepClone());
                remap.Add(index,packedIndex);
            }
            reference["index"]=packedIndex;
        }
        foreach(var material in model["materials"]!.AsArray()){
            var pbr=material!["pbrMetallicRoughness"] as JsonObject;
            pbr?.Remove("metallicRoughnessTexture");
            Keep(pbr?["baseColorTexture"]);
            Keep(material["occlusionTexture"]);
            Keep(material["emissiveTexture"]);
        }
        // The SDK writes every texture table entry, even if no GX material
        // samples it. Retain only the textures used by the cooked materials.
        model["textures"]=packed;
    }
    static byte[] Banner(int width)=>BrandAssets.Banner(Root,width);
    static void Text(byte[] b,int offset,int max,string text)
    { byte[] t=Encoding.Latin1.GetBytes(text);if(t.Length>=max)throw new ArgumentException("Disc metadata too long");t.CopyTo(b,offset); }
    static void Art()
    {
        string dir=Path.Combine(Root,"out/disc");Directory.CreateDirectory(dir);
        ModelVariants.Prune(Root);
        // Reused/SkipAssets output must not carry retired textures into a disc.
        foreach(string retired in new[]{"sunshine-water.dndtex","sunshine-wave.dndtex","sunshine-distortion.dndtex"})File.Delete(Path.Combine(dir,retired));
        BrandAssets.Build(Root);RepeatFloorAssets.Build(Root);PathAssets.Build(Root);TerrainCliffAssets.CookTexture(Root);DroppedItemAssets.Build(Root);
        byte[] image=Banner(96);File.WriteAllBytes(Path.Combine(dir,"savebanner.rgb5a3"),image);File.WriteAllBytes(Path.Combine(dir,"saveicon.rgb5a3"),Banner(32));
    }
    static void ReleaseMetadata(string commit)
    {
        string disc=Path.Combine(Root,"out/disc");Directory.CreateDirectory(disc);Directory.CreateDirectory(Path.Combine(Root,"out/brand"));
        byte[] banner=Banner(96);
        // The current disc uses US BI2 metadata, so ship BNR1. Keep a versioned
        // six-language BNR2 alternate outside the disc for future PAL packaging.
        File.WriteAllBytes(Path.Combine(disc,"opening.bnr"),DiscBannerFile.Create(banner,commit));
        File.WriteAllBytes(Path.Combine(Root,"out/brand/opening-pal.bnr"),DiscBannerFile.Create(banner,commit,pal:true));
        File.WriteAllText(Path.Combine(disc,"BUILD.txt"),DiscBannerFile.GameTitle+"\nBuild version: "+commit+"\n"+File.ReadAllText(Path.Combine(Root,"out/build-revisions.txt")));
        DiscVersionFile.Write(disc,commit);
    }
    static void Disc(string dolPath,string commit,string? output=null)
    {
        string disc=Path.Combine(Root,"out/disc");BuildingAssets.Cook(Root);Art();TextureAudit.Run(Root);ReleaseMetadata(commit);
        byte[] dol=File.ReadAllBytes(dolPath),app=File.ReadAllBytes(Path.Combine(Root,"out/native/apploader.img"));ValidateDol(dol);
        SdkApploader.Validate(app);
        if(!File.Exists(Path.Combine(disc,"sdk-apploader-LICENSE.txt")))throw new InvalidDataException("SDK apploader license missing; run tools/build-game.ps1");
        var files=Directory.GetFiles(disc,"*",SearchOption.AllDirectories).Where(p=>!p.EndsWith(".deps.json",StringComparison.Ordinal)).Select(p=>(path:p,name:Path.GetRelativePath(disc,p).Replace('\\','/'))).OrderBy(p=>p.name,StringComparer.Ordinal).ToArray();
        if(files.Any(p=>p.name.Contains('/')))throw new InvalidDataException("Disc assets must use flat filenames");
        long fstAt=Align(0x10000+dol.Length,32768);var names=new MemoryStream();names.WriteByte(0);var nameOffsets=new List<int>();
        foreach(var file in files){nameOffsets.Add((int)names.Length);names.Write(Encoding.ASCII.GetBytes(file.name));names.WriteByte(0);}
        byte[] fst=new byte[(files.Length+1)*12+names.Length];Put(fst,0,0x01000000);Put(fst,8,(uint)files.Length+1);names.ToArray().CopyTo(fst,(files.Length+1)*12);
        long cursor=Align(fstAt+fst.Length,32768);
        var offsets=new List<long>();for(int i=0;i<files.Length;i++){offsets.Add(cursor);Put(fst,(i+1)*12,(uint)nameOffsets[i]);Put(fst,(i+1)*12+4,(uint)cursor);Put(fst,(i+1)*12+8,(uint)new FileInfo(files[i].path).Length);cursor=Align(cursor+new FileInfo(files[i].path).Length,32768);}
        const long capacity=1459978240;if(cursor>capacity)throw new InvalidDataException("Disc full");
        output=Path.GetFullPath(output??Path.Combine(Root,"out/AnimalTown-"+commit+".iso"));
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using(var f=new FileStream(output,FileMode.Create,FileAccess.ReadWrite))
        {
            // Fixed release identity at 0x20 remains before NKit's 0x200 area.
            f.SetLength(capacity);var boot=new byte[0x2460];Text(boot,0,7,"AMTE01");Put(boot,0x1c,0xc2339f3d);Text(boot,0x20,0x1e0,DiscBannerFile.BootTitle(commit));
            Put(boot,0x420,0x10000);Put(boot,0x424,(uint)fstAt);Put(boot,0x428,(uint)fst.Length);Put(boot,0x42c,(uint)fst.Length);Put(boot,0x458,0xffffffff);
            // BI2: conservative retail-compatible fields, US region, 24 MiB memory.
            Put(boot,0x440+4,0x1800000);Put(boot,0x440+0x18,1);Put(boot,0x440+0x1c,1);
            WriteAt(f,0,boot);WriteAt(f,SdkApploader.DiscOffset,app);WriteAt(f,0x10000,dol);WriteAt(f,fstAt,fst);
            for(int i=0;i<files.Length;i++){f.Position=offsets[i];using var input=File.OpenRead(files[i].path);input.CopyTo(f);}
        }
        Verify(output,commit);Console.WriteLine(output);
    }
    internal static void ValidateDol(byte[] dol)
    {
        if(dol.Length<256)throw new InvalidDataException("DOL header missing");
        uint entry=Read(dol,0xe0);bool found=false;
        var ranges=new List<(uint start,uint end)>();
        for(int i=0;i<18;i++)
        {
            uint size=Read(dol,0x90+4*i);if(size==0)continue;uint offset=Read(dol,4*i),address=Read(dol,0x48+4*i);
            if(offset<256||(long)offset+size>dol.Length||address<0x80003100||(long)address+size>0x81200000)throw new InvalidDataException("Invalid or apploader-overlapping DOL section");
            if(ranges.Any(r=>address<r.end&&address+size>r.start))throw new InvalidDataException("Overlapping DOL sections");ranges.Add((address,address+size));
            if(i<7&&entry>=address&&entry<address+size)found=true;
        }
        if(!found)throw new InvalidDataException("DOL entry is not executable");
        uint bss=Read(dol,0xd8),length=Read(dol,0xdc);if(length>0&&(bss<0x80003100||(long)bss+length>0x81200000))throw new InvalidDataException("Invalid BSS");
    }
    static void Verify(string path,string? expectedVersion=null)
    {
        using var f=File.OpenRead(path);byte[] h=new byte[0x2460];f.ReadExactly(h);
        if(f.Length!=1459978240||Read(h,0x1c)!=0xc2339f3d||Read(h,0x2450)!=0x81200000||Read(h,0x2454)==0)throw new InvalidDataException("Boot header invalid");
        if(expectedVersion!=null){
            int end=Array.IndexOf(h,(byte)0,0x20,0x1e0);
            if(end<0||Encoding.ASCII.GetString(h,0x20,end-0x20)!=DiscBannerFile.BootTitle(expectedVersion))
                throw new InvalidDataException("Embedded release version differs from the build identity");
        }
        int titleEnd=Array.IndexOf(h,(byte)0,0x20,0x1e0);
        if(titleEnd<0)throw new InvalidDataException("Disc title is not terminated");
        string title=Encoding.ASCII.GetString(h,0x20,titleEnd-0x20);
        const string titlePrefix=DiscBannerFile.BootTitlePrefix;
        if(!title.StartsWith(titlePrefix,StringComparison.Ordinal))throw new InvalidDataException("Disc release identity missing");
        string discVersion=expectedVersion??title.Substring(titlePrefix.Length);
        int dolAt=(int)Read(h,0x420),fstAt=(int)Read(h,0x424),length=(int)Read(h,0x428);if(length<12||length>1024*1024||fstAt<=dolAt)throw new InvalidDataException("FST location invalid");
        byte[] dol=new byte[fstAt-dolAt];f.Position=dolAt;f.ReadExactly(dol);ValidateDol(dol);
        byte[] fst=new byte[length];f.Position=fstAt;f.ReadExactly(fst);int count=(int)Read(fst,8);if(count<2||count>1024||count*12>length)throw new InvalidDataException("FST root invalid");
        var seen=new HashSet<string>();long previous=Align(fstAt+length,32768);
        for(int i=1;i<count;i++)
        {
            int name=count*12+(int)Read(fst,i*12);if(name<count*12||name>=length)throw new InvalidDataException("FST name invalid");int end=Array.IndexOf(fst,(byte)0,name);if(end<0)throw new InvalidDataException("FST name not terminated");
            string n=Encoding.ASCII.GetString(fst,name,end-name);uint offset=Read(fst,i*12+4),size=Read(fst,i*12+8);
            if(!seen.Add(n)||offset%32768!=0||offset<previous||(long)offset+size>f.Length)throw new InvalidDataException("FST range invalid");previous=offset+size;
            if(n=="buildings.bin"){byte[] descriptor=new byte[size];f.Position=offset;f.ReadExactly(descriptor);AnimalTown.BuildingCatalog.Configure(descriptor);}
            if(n=="opening.bnr"){byte[] b=new byte[size];f.Position=offset;f.ReadExactly(b);DiscBannerFile.Verify(b,discVersion);}
            if(n=="version.md"){
                if(size>64)throw new InvalidDataException("ISO version.md exceeds its 64-byte limit");
                byte[] version=new byte[size];f.Position=offset;f.ReadExactly(version);DiscVersionFile.Verify(version,discVersion);
            }
        }
        foreach(string n in new[]{"opening.bnr","BUILD.txt","version.md","runtime.cfg","savebanner.rgb5a3","saveicon.rgb5a3","gorditas-bold.dndtex","gorditas-bold.metrics","wordmark.dndtex","item-icons.dndtex","dropped-item.dndtex","dropped-money.dndtex","dropped-tool.dndtex","dropped-clothing.dndtex","dropped-quest.dndtex","dropped-present.dndtex","cat.dndmodel","fox.dndmodel","bunny.dndmodel","giraffe.dndmodel","hamster.dndmodel","spaniel.dndmodel","building.dndmodel"})if(!seen.Contains(n))throw new InvalidDataException("Missing "+n);
        foreach(string model in new[]{"fox","bunny","giraffe","cat","hamster","spaniel"}){
            if(!seen.Contains(model+"-tex128.dndmodel"))throw new InvalidDataException("Missing gameplay villager model "+model);
            foreach(int edge in new[]{512,1024})if(seen.Contains(model+"-tex"+edge+".dndmodel"))throw new InvalidDataException("Oversized villager model must not ship: "+model);
        }
        if(seen.Contains("deciduous.dndmodel")||seen.Contains("palm.dndmodel")||seen.Contains("pine.dndmodel"))throw new InvalidDataException("Retired high-poly trees must not ship");
        if(!seen.Contains("tent.dndmodel"))throw new InvalidDataException("Missing supplied tent model");
        foreach(string season in new[]{"spring","summer","fall","winter"})foreach(string extension in new[]{"bin","dndtex"})if(!seen.Contains(extension=="bin"?"floor-"+season+".bin":"floor-"+season+(seen.Contains("floor-outdoor-neutral.dndtex")?"-1.dndtex":"-0.dndtex")))throw new InvalidDataException("Missing seasonal terrain asset");
        if(!seen.Contains("terrain.cfg"))throw new InvalidDataException("Missing terrain configuration");
        if(!seen.Contains("buildings.bin"))throw new InvalidDataException("Missing building catalog");
        for(int type=0;type<AnimalTown.BuildingCatalog.Count;type++){
            var d=AnimalTown.BuildingCatalog.Get(type);foreach(string model in new[]{d.Exterior,d.Door,d.Floor,d.North,d.East,d.South,d.West})
                if(!seen.Contains(model+"-tex128.dndmodel"))throw new InvalidDataException("Missing building model "+model);
        }
        if(!seen.Contains(PathAssets.DiscName))throw new InvalidDataException("Missing painted path texture");
        if(!seen.Contains("cliff-soil.dndtex"))throw new InvalidDataException("Missing shared cliff soil texture");
        foreach(string retired in new[]{"sunshine-water.dndtex","sunshine-wave.dndtex","sunshine-distortion.dndtex"})
            if(seen.Contains(retired))throw new InvalidDataException("Retired water texture must not ship: "+retired);
        Console.WriteLine("Verified full-size bootable disc structure, "+(count-1)+" files");
    }
}
