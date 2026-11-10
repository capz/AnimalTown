using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AnimalTown;

// Versioned, game-owned external texture references. Legacy DNMDs stay intact.
internal static class TextureBanks
{
    internal const int Magic=0x58545441,RecordBytes=256,ModelBytes=128,Quantum=32768;
    internal sealed class Image
    {
        internal int Width,Height,Minimum,SafeLevels,Format=6;
        internal uint Hash;
        internal string Sha="";
        internal readonly List<byte[]> Levels=new();
        internal readonly List<(int Bank,int Offset,int Length,uint Crc,int Block,int Blocks)> Ranges=new();
    }
    internal static uint Crc(ReadOnlySpan<byte> data)
    {uint c=uint.MaxValue;foreach(byte b in data){c^=b;for(int i=0;i<8;i++)c=(c>>1)^((c&1)!=0?0xedb88320u:0);}return ~c;}
    private static int Be(byte[] b,int p)=>checked((int)BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(p,4)));
    private static void PutBe(byte[] b,int p,int n)=>BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(p,4),n);
    private static void Put(byte[] b,int p,int n)=>BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(p,4),n);
    private static byte[] Package(byte[] p,int w,int h)
    {var b=new byte[p.Length+32];Put(b,0,0x58544e44);Put(b,4,2);Put(b,8,6);Put(b,12,w);Put(b,16,h);Put(b,20,p.Length);Put(b,24,1);p.CopyTo(b,32);return b;}
    private static byte[] Half(byte[] pixels,int w,int h)
    {
        var source=Package(pixels,w,h);var rgba=new byte[w/2*(h/2)*4];TexturePixels.Resample(source,0,0,w,h,rgba,w/2,h/2);return TexturePixels.Encode(rgba,w/2,h/2,6);
    }
    internal static void Build(string root)
    {
        string disc=Path.Combine(root,"out/disc");
        var names=new List<string>{"fox","bunny","giraffe","cat","hamster","spaniel"};
        var buildings=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(root,"out/building-assets.json")))!;
        names.AddRange(buildings.Keys.Order().Select(n=>n+"-tex128"));
        var images=new List<Image>();var byHash=new Dictionary<string,int>();var outputs=new Dictionary<string,byte[]>();long removed=0;
        foreach(string name in names){
            byte[] source=File.ReadAllBytes(Path.Combine(disc,name+".dndmodel"));if(Be(source,0)!=0x444e444d||Be(source,4)!=2||Be(source,8)!=source.Length)throw new InvalidDataException("Bank input must be a complete v2 model");
            int start=Be(source,68),length=Be(source,72),count=Be(source,start),at=start+4;
            using var refs=new MemoryStream();using(var writer=new BinaryWriter(refs,Encoding.UTF8,true))writer.Write(BinaryPrimitives.ReverseEndianness(count));
            for(int t=0;t<count;t++){
                int w=Be(source,at),h=Be(source,at+4),levels=Be(source,at+8),size=Be(source,at+24);if(w<4||h<4||levels<1||levels>9||at+28+size>start+length)throw new InvalidDataException("Invalid texture range");
                byte[] pixels=source.AsSpan(at+28,size).ToArray();string sha=Convert.ToHexString(SHA256.HashData(pixels));string key=$"{w}:{h}:{levels}:{sha}";
                if(!byHash.TryGetValue(key,out int id)){
                    id=images.Count;var image=new Image{Width=w,Height=h,Minimum=w>128||h>128?1:0,SafeLevels=levels,Hash=TexturePixels.Hash(pixels),Sha=sha};
                    int position=0;for(int l=0;l<levels;l++){int bytes=(w>>l)*(h>>l)*4;image.Levels.Add(pixels.AsSpan(position,bytes).ToArray());position+=bytes;}if(position!=size)throw new InvalidDataException("Invalid texture mip lengths");
                    int mw=w>>(levels-1),mh=h>>(levels-1);while(mw>=8&&mh>=8){image.Levels.Add(Half(image.Levels[^1],mw,mh));mw/=2;mh/=2;}
                    // The existing two levels are the conservative atlas-safe baseline.
                    // Reviewed policies below may permit a longer chain or a smaller base.
                    images.Add(image);byHash.Add(key,id);
                }
                byte[] record=source.AsSpan(at,28).ToArray();PutBe(record,24,id);refs.Write(record);var identity=new byte[4];PutBe(identity,0,unchecked((int)images[id].Hash));refs.Write(identity);removed+=size;at+=28+size;
            }
            if(at!=start+length)throw new InvalidDataException("Texture section trailing bytes");
            var sections=new byte[6][];for(int s=0;s<6;s++)sections[s]=s==3?refs.ToArray():source.AsSpan(Be(source,20+s*16),Be(source,24+s*16)).ToArray();
            int total=160;foreach(var section in sections)total=((total+31)&~31)+section.Length;
            var output=new byte[total];PutBe(output,0,0x444e444d);PutBe(output,4,3);PutBe(output,8,total);PutBe(output,12,6);int dest=160;
            for(int s=0;s<6;s++){dest=(dest+31)&~31;PutBe(output,16+s*16,s+1);PutBe(output,20+s*16,dest);PutBe(output,24+s*16,sections[s].Length);PutBe(output,28+s*16,unchecked((int)TexturePixels.Hash(sections[s])));sections[s].CopyTo(output,dest);dest+=sections[s].Length;}
            outputs.Add("dvd:/"+name+".dndmodel",output);
        }
        if(images.Count>512||outputs.Count>128)throw new InvalidDataException("External texture catalog capacity");
        string policyPath=Path.Combine(root,"assets/texture-policy.json");
        var policies=File.Exists(policyPath)?JsonSerializer.Deserialize<Dictionary<string,Policy>>(File.ReadAllText(policyPath),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!:new();
        foreach(var image in images)if(policies.TryGetValue(image.Sha,out var policy)){
            if(!policy.Reviewed)continue;
            if(policy.MinimumEdge!=64&&policy.MinimumEdge!=128)throw new InvalidDataException("Reviewed minimum edge must be 64 or 128");
            image.Minimum=0;while(image.Minimum+1<image.Levels.Count&&Math.Max(image.Width>>image.Minimum,image.Height>>image.Minimum)>policy.MinimumEdge)image.Minimum++;
            image.SafeLevels=Math.Clamp(policy.SafeLevels,image.Minimum+1,image.Levels.Count);
            if(policy.Format!=6)throw new InvalidDataException("Reviewed compressed formats must be cooked through the candidate pipeline");
        }
        using var baseBank=new MemoryStream();using var detailBank=new MemoryStream();var checksums=new List<uint>();
        foreach(var image in images)for(int l=0;l<image.Levels.Count;l++){
            var bank=l>=image.Minimum?baseBank:detailBank;while((bank.Position&31)!=0)bank.WriteByte(0);int offset=checked((int)bank.Position),first=checksums.Count;var pixels=image.Levels[l];
            for(int p=0;p<pixels.Length;p+=Quantum)checksums.Add(Crc(pixels.AsSpan(p,Math.Min(Quantum,pixels.Length-p))));
            image.Ranges.Add((l>=image.Minimum?0:1,offset,pixels.Length,Crc(pixels),first,checksums.Count-first));bank.Write(pixels);
        }
        int modelOffset=32+images.Count*RecordBytes,blockOffset=modelOffset+outputs.Count*ModelBytes;
        var index=new byte[blockOffset+checksums.Count*4];int[] header={Magic,1,images.Count,outputs.Count,checksums.Count,32,modelOffset,blockOffset};for(int i=0;i<header.Length;i++)Put(index,i*4,header[i]);
        for(int i=0;i<images.Count;i++){
            var image=images[i];int p=32+i*RecordBytes;int[] values={image.Width,image.Height,image.Format,image.Minimum,image.SafeLevels,image.Levels.Count,unchecked((int)image.Hash),0};for(int k=0;k<values.Length;k++)Put(index,p+k*4,values[k]);
            for(int l=0;l<image.Ranges.Count;l++){var r=image.Ranges[l];int q=p+32+l*24;int[] fields={r.Bank,r.Offset,r.Length,unchecked((int)r.Crc),r.Block,r.Blocks};for(int k=0;k<fields.Length;k++)Put(index,q+k*4,fields[k]);}
        }
        int m=0;foreach(var pair in outputs){byte[] path=Encoding.UTF8.GetBytes(pair.Key);if(path.Length>=ModelBytes)throw new InvalidDataException("Model bank path too long");path.CopyTo(index,modelOffset+m++*ModelBytes);string name=Path.GetFileNameWithoutExtension(pair.Key);File.WriteAllBytes(Path.Combine(disc,name+"-stream.dndmodel"),pair.Value);}
        for(int i=0;i<checksums.Count;i++)Put(index,blockOffset+i*4,unchecked((int)checksums[i]));
        File.WriteAllBytes(Path.Combine(disc,"textures.idx"),index);File.WriteAllBytes(Path.Combine(disc,"textures-base.bank"),baseBank.ToArray());File.WriteAllBytes(Path.Combine(disc,"textures-detail.bank"),detailBank.ToArray());
        var report=new{schema=1,externalModels=outputs.Count,uniqueTextures=images.Count,removedEmbeddedBytes=removed,baseBankBytes=baseBank.Length,detailBankBytes=detailBank.Length,textures=images.Select((image,id)=>new{id,image.Width,image.Height,image.Format,image.Minimum,image.SafeLevels,generatedLevels=image.Levels.Count,sourcePayloadSha256=image.Sha,levels=image.Ranges.Select(r=>new{r.Bank,r.Offset,r.Length,crc32=r.Crc})})};
        File.WriteAllText(Path.Combine(root,"out/texture-banks.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"Texture banks: {outputs.Count} external models, {images.Count} shared images, {removed} embedded bytes removed");
    }
    internal static void Check()
    {
        if(Crc("123456789"u8)!=0xcbf43926u)throw new Exception("Bank CRC32 oracle");
        string root=Path.Combine(Path.GetTempPath(),"animaltown-banks-"+Guid.NewGuid().ToString("N"));
        try{
            Directory.CreateDirectory(Path.Combine(root,"out/disc"));File.WriteAllText(Path.Combine(root,"out/building-assets.json"),"{}");
            var rgba=new byte[8*8*4];for(int p=0;p<rgba.Length;p+=4){rgba[p]=123;rgba[p+1]=87;rgba[p+2]=66;rgba[p+3]=255;}
            byte[] first=TexturePixels.Encode(rgba,8,8,6),second=Half(first,8,8);using var texture=new MemoryStream();
            var count=new byte[4];PutBe(count,0,2);texture.Write(count);
            for(int t=0;t<2;t++){var record=new byte[28];int[] fields={8,8,2,0,0,1,first.Length+second.Length};for(int k=0;k<fields.Length;k++)PutBe(record,k*4,fields[k]);texture.Write(record);texture.Write(first);texture.Write(second);}
            var sections=Enumerable.Range(0,6).Select(i=>i==3?texture.ToArray():new byte[4]).ToArray();int size=160;foreach(var part in sections)size=((size+31)&~31)+part.Length;
            var model=new byte[size];PutBe(model,0,0x444e444d);PutBe(model,4,2);PutBe(model,8,size);PutBe(model,12,6);int at=160;
            for(int i=0;i<6;i++){at=(at+31)&~31;PutBe(model,16+i*16,i+1);PutBe(model,20+i*16,at);PutBe(model,24+i*16,sections[i].Length);PutBe(model,28+i*16,unchecked((int)TexturePixels.Hash(sections[i])));sections[i].CopyTo(model,at);at+=sections[i].Length;}
            string[] names={"fox","bunny","giraffe","cat","hamster","spaniel"};foreach(var name in names)File.WriteAllBytes(Path.Combine(root,"out/disc",name+".dndmodel"),model);
            Build(root);byte[] before=File.ReadAllBytes(Path.Combine(root,"out/disc/textures.idx"));Build(root);byte[] after=File.ReadAllBytes(Path.Combine(root,"out/disc/textures.idx"));
            if(!before.SequenceEqual(after)||TexturePixels.Int(after,8)!=1||TexturePixels.Int(after,12)!=6)throw new Exception("Deterministic texture deduplication");
            foreach(var name in names){if(!File.ReadAllBytes(Path.Combine(root,"out/disc",name+".dndmodel")).SequenceEqual(model))throw new Exception("Bank cooker changed original model");byte[] external=File.ReadAllBytes(Path.Combine(root,"out/disc",name+"-stream.dndmodel"));if(Be(external,4)!=3||external.Length>=model.Length)throw new Exception("External image extraction");for(int i=0;i<6;i++){int a=Be(external,20+i*16),n=Be(external,24+i*16);if(BinaryPrimitives.ReadUInt32BigEndian(external.AsSpan(28+i*16,4))!=TexturePixels.Hash(external.AsSpan(a,n).ToArray()))throw new Exception("External section checksum");}}
            Console.WriteLine("Texture banks: CRC32 oracle, exact mip bytes, shared identities, deterministic output, versioned references and original preservation passed");
        }finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    internal sealed class Policy {public bool Reviewed{get;set;}public int MinimumEdge{get;set;}=128;public int SafeLevels{get;set;}=2;public int Format{get;set;}=6;}
}
