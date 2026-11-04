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
            if(args[0]=="repack-textures")TextureRepacker.Command(args);
            else if(args[0]=="town-preview")TownGenerationPreview.Run(args[2],int.Parse(args[3]),args.Length>4?int.Parse(args[4]):1);
            else if(args[0]=="repack-check")TextureRepackerChecks.Run(Root);
            else if(args[0]=="audit")Audit();
            else if(args[0]=="prepare")Prepare(args[2],args[3]);
            else if(args[0]=="art")Art();
            else if(args[0]=="terrain")RepeatFloorAssets.Build(Root);
            else if(args[0]=="terrain-check")TerrainChecks.Run(Root);
            else if(args[0]=="tree-audit")TreeRenderAudit.Run(Root,args[2]);
            else if(args[0]=="iso")Disc(args[2],args[3]);
            else if(args[0]=="verify")Verify(args[2]);
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
    static void Prepare(string input,string output)
    {
        byte[] b=File.ReadAllBytes(input);int len=BitConverter.ToInt32(b,12);
        var j=JsonNode.Parse(b.AsSpan(20,len))!;
        // Tangent-space normal mapping is not used by the chosen GX material path.
        foreach(var mesh in j["meshes"]!.AsArray())foreach(var p in mesh!["primitives"]!.AsArray())p!["attributes"]!.AsObject().Remove("TANGENT");
        foreach(var m in j["materials"]!.AsArray())m!.AsObject().Remove("normalTexture");
        // Normalize the actual node-transformed bind-pose bounds, preserving all
        // existing animation tracks and skin inverse binds under a scene root.
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
    static byte[] Banner(int width)=>BrandAssets.Banner(Root,width);
    static void Text(byte[] b,int offset,int max,string text)
    { byte[] t=Encoding.Latin1.GetBytes(text);if(t.Length>=max)throw new ArgumentException("Disc metadata too long");t.CopyTo(b,offset); }
    static void Art()
    {
        string dir=Path.Combine(Root,"out/disc");Directory.CreateDirectory(dir);
        BrandAssets.Build(Root);RepeatFloorAssets.Build(Root);
        byte[] image=Banner(96);File.WriteAllBytes(Path.Combine(dir,"savebanner.rgb5a3"),image);File.WriteAllBytes(Path.Combine(dir,"saveicon.rgb5a3"),Banner(32));
    }
    static void Disc(string dolPath,string commit)
    {
        string disc=Path.Combine(Root,"out/disc");Art();TextureAudit.Run(Root);
        var bnr=new byte[6496];Encoding.ASCII.GetBytes("BNR1").CopyTo(bnr,0);Banner(96).CopyTo(bnr,32);
        Text(bnr,0x1820,32,"AnimalTown");Text(bnr,0x1840,32,"AnimalTown contributors");Text(bnr,0x1860,64,"AnimalTown / "+commit);Text(bnr,0x18a0,64,"AnimalTown contributors");Text(bnr,0x18e0,128,"A rolling village life game. Commit "+commit);
        File.WriteAllBytes(Path.Combine(disc,"opening.bnr"),bnr);
        File.WriteAllText(Path.Combine(disc,"BUILD.txt"),"AnimalTown\nGame commit: "+commit+"\n"+File.ReadAllText(Path.Combine(Root,"out/build-revisions.txt")));
        byte[] dol=File.ReadAllBytes(dolPath),app=File.ReadAllBytes(Path.Combine(Root,"out/apploader.bin"));ValidateDol(dol);
        if(app.Length>0x10000-0x2460)throw new InvalidDataException("Apploader too large");
        var files=Directory.GetFiles(disc,"*",SearchOption.AllDirectories).Where(p=>!p.EndsWith(".deps.json",StringComparison.Ordinal)).Select(p=>(path:p,name:Path.GetRelativePath(disc,p).Replace('\\','/'))).OrderBy(p=>p.name,StringComparer.Ordinal).ToArray();
        if(files.Any(p=>p.name.Contains('/')))throw new InvalidDataException("Disc assets must use flat filenames");
        long fstAt=Align(0x10000+dol.Length,32768);var names=new MemoryStream();names.WriteByte(0);var nameOffsets=new List<int>();
        foreach(var file in files){nameOffsets.Add((int)names.Length);names.Write(Encoding.ASCII.GetBytes(file.name));names.WriteByte(0);}
        byte[] fst=new byte[(files.Length+1)*12+names.Length];Put(fst,0,0x01000000);Put(fst,8,(uint)files.Length+1);names.ToArray().CopyTo(fst,(files.Length+1)*12);
        long cursor=Align(fstAt+fst.Length,32768);
        var offsets=new List<long>();for(int i=0;i<files.Length;i++){offsets.Add(cursor);Put(fst,(i+1)*12,(uint)nameOffsets[i]);Put(fst,(i+1)*12+4,(uint)cursor);Put(fst,(i+1)*12+8,(uint)new FileInfo(files[i].path).Length);cursor=Align(cursor+new FileInfo(files[i].path).Length,32768);}
        const long capacity=1459978240;if(cursor>capacity)throw new InvalidDataException("Disc full");
        string output=Path.Combine(Root,"out/AnimalTown-"+commit+".iso");using(var f=new FileStream(output,FileMode.Create,FileAccess.ReadWrite))
        {
            f.SetLength(capacity);var boot=new byte[0x2460];Text(boot,0,7,"AMTE01");Put(boot,0x1c,0xc2339f3d);Text(boot,0x20,0x3e0,"AnimalTown / "+commit);
            Put(boot,0x420,0x10000);Put(boot,0x424,(uint)fstAt);Put(boot,0x428,(uint)fst.Length);Put(boot,0x42c,(uint)fst.Length);Put(boot,0x458,0xffffffff);
            // BI2: conservative retail-compatible fields, US region, 24 MiB memory.
            Put(boot,0x440+4,0x1800000);Put(boot,0x440+0x18,1);Put(boot,0x440+0x1c,1);
            Text(boot,0x2440,16,"2026/11/02");Put(boot,0x2450,0x81200000);Put(boot,0x2454,(uint)Align(app.Length));Put(boot,0x2458,0);
            WriteAt(f,0,boot);WriteAt(f,0x2460,app);WriteAt(f,0x10000,dol);WriteAt(f,fstAt,fst);
            for(int i=0;i<files.Length;i++){f.Position=offsets[i];using var input=File.OpenRead(files[i].path);input.CopyTo(f);}
        }
        Verify(output);Console.WriteLine(output);
    }
    static void ValidateDol(byte[] dol)
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
    static void Verify(string path)
    {
        using var f=File.OpenRead(path);byte[] h=new byte[0x2460];f.ReadExactly(h);
        if(f.Length!=1459978240||Read(h,0x1c)!=0xc2339f3d||Read(h,0x2450)!=0x81200000||Read(h,0x2454)==0)throw new InvalidDataException("Boot header invalid");
        int dolAt=(int)Read(h,0x420),fstAt=(int)Read(h,0x424),length=(int)Read(h,0x428);if(length<12||length>1024*1024||fstAt<=dolAt)throw new InvalidDataException("FST location invalid");
        byte[] dol=new byte[fstAt-dolAt];f.Position=dolAt;f.ReadExactly(dol);ValidateDol(dol);
        byte[] fst=new byte[length];f.Position=fstAt;f.ReadExactly(fst);int count=(int)Read(fst,8);if(count<2||count>1024||count*12>length)throw new InvalidDataException("FST root invalid");
        var seen=new HashSet<string>();long previous=Align(fstAt+length,32768);
        for(int i=1;i<count;i++)
        {
            int name=count*12+(int)Read(fst,i*12);if(name<count*12||name>=length)throw new InvalidDataException("FST name invalid");int end=Array.IndexOf(fst,(byte)0,name);if(end<0)throw new InvalidDataException("FST name not terminated");
            string n=Encoding.ASCII.GetString(fst,name,end-name);uint offset=Read(fst,i*12+4),size=Read(fst,i*12+8);
            if(!seen.Add(n)||offset%32768!=0||offset<previous||(long)offset+size>f.Length)throw new InvalidDataException("FST range invalid");previous=offset+size;
            if(n=="opening.bnr"){byte[] b=new byte[size];f.Position=offset;f.ReadExactly(b);if(b.Length!=6496||Encoding.ASCII.GetString(b,0,4)!="BNR1")throw new InvalidDataException("Banner invalid");}
        }
        foreach(string n in new[]{"opening.bnr","BUILD.txt","savebanner.rgb5a3","saveicon.rgb5a3","gorditas-bold.dndtex","gorditas-bold.metrics","wordmark.dndtex","item-icons.dndtex","cat.dndmodel","fox.dndmodel","bunny.dndmodel","giraffe.dndmodel","hamster.dndmodel","spaniel.dndmodel","building.dndmodel"})if(!seen.Contains(n))throw new InvalidDataException("Missing "+n);
        if(seen.Contains("deciduous.dndmodel")||seen.Contains("palm.dndmodel")||seen.Contains("pine.dndmodel"))throw new InvalidDataException("Retired high-poly trees must not ship");
        if(!seen.Contains("tent.dndmodel"))throw new InvalidDataException("Missing supplied tent model");
        foreach(string season in new[]{"spring","summer","fall","winter"})foreach(string extension in new[]{"bin","dndtex"})if(!seen.Contains(extension=="bin"?"floor-"+season+".bin":"floor-"+season+"-0.dndtex"))throw new InvalidDataException("Missing seasonal terrain asset");
        if(!seen.Contains("terrain.cfg"))throw new InvalidDataException("Missing terrain configuration");
        Console.WriteLine("Verified full-size bootable disc structure, "+(count-1)+" files");
    }
}
