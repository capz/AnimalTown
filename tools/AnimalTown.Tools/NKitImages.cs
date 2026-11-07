using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Validate the physical compact layout, not just its reversible container.
// The homebrew apploader currently reads its DOL at the fixed 0x10000 address.
internal static class NKitImages
{
    const long Capacity=1459978240;
    record Entry(string Name,uint Offset,uint Size);
    static uint Read(byte[] a,int at)=>BinaryPrimitives.ReadUInt32BigEndian(a.AsSpan(at,4));
    static byte[] Bytes(FileStream f,long at,int size){if(at<0||size<0||at>f.Length-size)throw new InvalidDataException("Image range outside file");var a=new byte[size];f.Position=at;f.ReadExactly(a);return a;}
    static string Hash(FileStream f,long at,long size){if(at<0||size<0||at>f.Length-size)throw new InvalidDataException("Payload outside image");f.Position=at;using var h=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);var buffer=new byte[65536];while(size>0){int n=(int)Math.Min(size,buffer.Length);f.ReadExactly(buffer.AsSpan(0,n));h.AppendData(buffer,0,n);size-=n;}return Convert.ToHexString(h.GetHashAndReset()).ToLowerInvariant();}
    static string Text(byte[] a,int start,int limit){int end=Array.IndexOf(a,(byte)0,start,limit);if(end<0)throw new InvalidDataException("Unterminated image string");return Encoding.ASCII.GetString(a,start,end-start);}
    static Dictionary<string,Entry> Files(FileStream f,byte[] header)
    {
        uint at=Read(header,0x424),size=Read(header,0x428);
        if(size<12||size>1024*1024||at<0x10000)throw new InvalidDataException("Invalid compact FST range");
        var table=Bytes(f,at,(int)size);uint count=Read(table,8);
        if(Read(table,0)!=0x01000000||count<2||count>1024||count*12>size)throw new InvalidDataException("Invalid flat FST root");
        var files=new Dictionary<string,Entry>(StringComparer.Ordinal);
        for(int i=1;i<count;i++){
            uint name=Read(table,i*12),offset=Read(table,i*12+4),length=Read(table,i*12+8);
            if(name>>24!=0||name>=size-count*12||offset%4!=0||offset<(long)at+size||(long)offset+length>f.Length)throw new InvalidDataException("Invalid compact file entry");
            string label=Text(table,(int)(count*12+name),(int)(size-count*12-name));
            if(label.Length==0||label.Contains('/')||label.Contains('\\')||!files.TryAdd(label,new Entry(label,offset,length)))throw new InvalidDataException("Duplicate or invalid compact filename");
        }
        long previous=(long)at+size;
        foreach(var e in files.Values.OrderBy(e=>e.Offset)){if(e.Offset<previous)throw new InvalidDataException("Overlapping compact files");previous=(long)e.Offset+e.Size;}
        return files;
    }
    public static void Verify(string source,string compact,string report)=>VerifyCore(source,compact,report,Capacity);
    static void VerifyCore(string source,string compact,string report,long expectedSize)
    {
        using var original=File.OpenRead(source);using var packed=File.OpenRead(compact);
        if(original.Length!=expectedSize||packed.Length>=original.Length)throw new InvalidDataException("NKit must shrink a full source ISO");
        var a=Bytes(original,0,0x2460);var b=Bytes(packed,0,0x2460);
        if(Read(a,0x1c)!=0xc2339f3d||Read(b,0x1c)!=0xc2339f3d||!b.AsSpan(0x200,8).SequenceEqual("NKIT v01"u8)||Read(b,0x210)!=expectedSize)throw new InvalidDataException("Invalid NKit identity or original length");
        if(!a.AsSpan(0,0x20).SequenceEqual(b.AsSpan(0,0x20))||Text(a,0x20,0x1e0)!=Text(b,0x20,0x1e0)||!a.AsSpan(0x440,0x2000).SequenceEqual(b.AsSpan(0x440,0x2000)))throw new InvalidDataException("Disc identity, title or BI2 changed");
        uint dolAt=Read(a,0x420);
        if(dolAt!=0x10000||Read(b,0x420)!=dolAt)throw new InvalidDataException("NKit moved the fixed-address homebrew DOL");
        var dol=Bytes(original,dolAt,256);if(!dol.AsSpan().SequenceEqual(Bytes(packed,dolAt,256)))throw new InvalidDataException("DOL header changed");
        long dolSize=256;for(int i=0;i<18;i++){uint size=Read(dol,0x90+i*4),offset=Read(dol,i*4);if(size!=0){if(offset%32!=0||size%32!=0)throw new InvalidDataException("DOL DMA alignment changed");dolSize=Math.Max(dolSize,(long)offset+size);}}
        if(dolAt+dolSize>Read(b,0x424)||Hash(original,dolAt,dolSize)!=Hash(packed,dolAt,dolSize))throw new InvalidDataException("DOL load data changed");
        long appSize=32L+Read(a,0x2454)+Read(a,0x2458);
        if(appSize<33||0x2440+appSize>dolAt||Hash(original,0x2440,appSize)!=Hash(packed,0x2440,appSize))throw new InvalidDataException("Apploader changed");
        var originalFiles=Files(original,a);var compactFiles=Files(packed,b);
        if(originalFiles.Count!=compactFiles.Count)throw new InvalidDataException("NKit file count changed");
        var records=new List<object>();foreach(var e in originalFiles.Values.OrderBy(e=>e.Name,StringComparer.Ordinal)){
            if(!compactFiles.TryGetValue(e.Name,out var p)||p.Size!=e.Size)throw new InvalidDataException("Missing or resized NKit file: "+e.Name);
            string hash=Hash(original,e.Offset,e.Size);if(hash!=Hash(packed,p.Offset,p.Size))throw new InvalidDataException("Changed NKit file: "+e.Name);
            records.Add(new{file=e.Name,bytes=e.Size,sha256=hash});
        }
        File.WriteAllText(report,JsonSerializer.Serialize(new{schema=1,format="nkit.iso",sourceFile=Path.GetFileName(source),compactFile=Path.GetFileName(compact),sourceBytes=original.Length,compactBytes=packed.Length,savedPercent=100*(1-(double)packed.Length/original.Length),dolAndApploaderUnchanged=true,files=records},new JsonSerializerOptions{WriteIndented=true})+"\n");
        Console.WriteLine($"NKit verified: {records.Count} unchanged payloads and original apploader/DOL, {packed.Length:N0} compact bytes");
    }
    public static void Check()
    {
        string root=Path.Combine(Path.GetTempPath(),"animaltown-nkit-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{
            string full=Path.Combine(root,"source.iso"),small=Path.Combine(root,"small.nkit.iso"),report=Path.Combine(root,"report.json");
            var a=new byte[0x40000];void Put(byte[] v,int at,uint n)=>BinaryPrimitives.WriteUInt32BigEndian(v.AsSpan(at,4),n);
            "AMTE01"u8.CopyTo(a);"AnimalTown test\0"u8.CopyTo(a.AsSpan(0x20));Put(a,0x1c,0xc2339f3d);Put(a,0x420,0x10000);Put(a,0x424,0x20000);Put(a,0x428,48);Put(a,0x2454,32);a[0x2460]=123;
            Put(a,0x10000,256);Put(a,0x10090,32);a[0x10100]=42;
            Put(a,0x20000,0x01000000);Put(a,0x20008,2);Put(a,0x20010,0x30000);Put(a,0x20014,32);"test.bin\0"u8.CopyTo(a.AsSpan(0x20018));a[0x30000]=77;
            var b=a.AsSpan(0,0x20200).ToArray();"NKIT v01"u8.CopyTo(b.AsSpan(0x200));Put(b,0x210,(uint)a.Length);Put(b,0x20010,0x20104);a.AsSpan(0x30000,32).CopyTo(b.AsSpan(0x20104));
            File.WriteAllBytes(full,a);File.WriteAllBytes(small,b);VerifyCore(full,small,report,a.Length);
            int rejected=0;void Reject(Action<byte[]> edit){var v=(byte[])b.Clone();edit(v);File.WriteAllBytes(small,v);try{VerifyCore(full,small,report,a.Length);}catch(InvalidDataException){rejected++;return;}throw new Exception("NKit corruption accepted");}
            Reject(v=>v[0x20104]^=1);Reject(v=>v[0x2460]^=1);Reject(v=>v[0x10100]^=1);Reject(v=>Put(v,0x420,0x10020));Reject(v=>Put(v,0x210,0));Reject(v=>Put(v,0x20014,1000));Reject(v=>Put(v,0x20010,0x20105));Reject(v=>v[0x20]^=1);
            Console.WriteLine($"NKit checks: compact 4-byte FST offsets accepted; {rejected} payload/boot/identity/range corruptions rejected");
        }finally{Directory.Delete(root,true);}
    }
}
