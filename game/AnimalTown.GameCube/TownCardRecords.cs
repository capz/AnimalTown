// Derived from the pinned DolphinDotNet CardRecordStore (GPL-2.0-only).
// Same wire format; AnimalTown extends the bounded backing-file ceiling for mail.
using System;
using System.IO;
using Dolphin.Storage;

/// <summary>One directory item, independently replaceable named records. Two
/// checkpoints and copy-on-write data retain the previous committed version.
/// The backing file must be exclusively owned for this instance's lifetime.</summary>
internal sealed class TownCardRecords : AnimalTown.ISaveStorage
{
    public const int BlockBytes=8192,MaximumRecords=64;
    private const int Header=32,Stride=48,Magic=0x31525444;
    private readonly ICardBlockStorage storage;
    private byte[] root,previous;
    private readonly byte[] scratch=new byte[BlockBytes];
    private int bank;
    private bool faulted;
    byte[] AnimalTown.ISaveStorage.Read(string name)=>Read(name);
    void AnimalTown.ISaveStorage.Write(string name,byte[] bytes)=>Write(name,bytes);
    // Keep the large read buffer in this one call frame. A buffer evaluated as
    // an argument in the caller's loop can remain a conservative GC root while
    // the next mirrored economy is read, even though the first copy is done.
    internal void CopyFrom(AnimalTown.ISaveStorage source,string name)
    {
        AnimalTown.Jobs.JobMemory.ReclaimTemporaryText?.Invoke();
        var bytes=source.Read(name);bool same=false;
        foreach(string existing in Names())if(existing==name){try{same=Matches(name,bytes,-1,bytes.Length,0,false);}catch(IOException){}break;}
        if(!same)Write(name,bytes);
        if(!Matches(name,bytes,-1,bytes.Length,0,false))throw new IOException("Archive readback differs");
    }
    internal void VerifyFrom(AnimalTown.ISaveStorage source,string name)
    {
        AnimalTown.Jobs.JobMemory.ReclaimTemporaryText?.Invoke();
        var bytes=source.Read(name);
        if(!Matches(name,bytes,-1,bytes.Length,0,false))throw new IOException("Archive readback differs");
    }
    private static int Get(byte[] b,int p)=>(int)((uint)b[p]|(uint)b[p+1]<<8|(uint)b[p+2]<<16|(uint)b[p+3]<<24);
    private static void Put(byte[] b,int p,int v){for(int i=0;i<4;i++)b[p+i]=(byte)((uint)v>>(8*i));}
    private static uint Hash(byte[] b,int offset,int count,uint h=2166136261){for(int i=0;i<count;i++)h=unchecked((h^b[offset+i])*16777619);return h;}
    private static string Name(byte[] b,int at){int n=0;while(n<32&&b[at+n]!=0)n++;var c=new char[n];for(int i=0;i<n;i++)c[i]=(char)b[at+i];return new string(c);}
    private static void Key(string key){if(key==null||key.Length<1||key.Length>31)throw new ArgumentException("Record name must be 1..31 ASCII characters");for(int i=0;i<key.Length;i++)if(key[i]<32||key[i]>126)throw new ArgumentException("Record name must be ASCII");}
    private static int At(int i)=>Header+i*Stride;
    private bool Valid(byte[] b)
    {
        if(Get(b,0)!=Magic||Get(b,4)!=1||Get(b,8)<1||Get(b,12)!=storage.Blocks||Get(b,BlockBytes-4)!=(int)Hash(b,0,BlockBytes-4))return false;
        for(int i=0;i<MaximumRecords;i++){
            int at=At(i);if(b[at]==0)continue;
            string name=Name(b,at);try{Key(name);}catch(ArgumentException){return false;}
            for(int j=0;j<i;j++)if(Name(b,At(j))==name)return false;
            int page=Get(b,at+32),offset=Get(b,at+36),length=Get(b,at+40);
            if(page<2||page>=storage.Blocks||offset<0||offset>=BlockBytes||length<0||length>(storage.Blocks-page)*BlockBytes-offset)return false;
            int pages=(offset+length+BlockBytes-1)/BlockBytes;
            if(pages==0)pages=1;
            for(int j=0;j<i;j++)if(b[At(j)]!=0){
                int start=Get(b,At(j)+32)*BlockBytes+Get(b,At(j)+36),size=Get(b,At(j)+40);
                int current=page*BlockBytes+offset;
                if(current<start+Math.Max(1,size)&&start<current+Math.Max(1,length))return false;
            }
            uint crc=2166136261;int done=0;
            for(int p=0;p<pages;p++){
                storage.Read(page+p,scratch);int begin=p==0?offset:0,count=Math.Min(length-done,BlockBytes-begin);
                crc=Hash(scratch,begin,count,crc);done+=count;
            }
            if((int)crc!=Get(b,at+44))return false;
        }
        return true;
    }
    public TownCardRecords(ICardBlockStorage storage,bool initialize=false)
    {
        this.storage=storage??throw new ArgumentNullException("storage");
        if(storage.Blocks<6||storage.Blocks>2048)throw new ArgumentOutOfRangeException("storage");
        root=new byte[BlockBytes];previous=new byte[BlockBytes];storage.Read(0,root);storage.Read(1,previous);
        bool a=Valid(root),b=Valid(previous);
        if(!a&&!b){
            if(!initialize)throw new IOException("No verified card checkpoint; existing data retained");
            var empty=new byte[BlockBytes];Put(empty,0,Magic);Put(empty,4,1);Put(empty,8,1);Put(empty,12,storage.Blocks);Seal(empty);
            // A cut while writing the very first empty checkpoint is retryable.
            // No data write can precede that checkpoint's successful readback.
            bool zeroTail=false;
            for(int i=0;i<BlockBytes;i++){
                if(root[i]!=empty[i])zeroTail=true;
                if((zeroTail&&root[i]!=0)||previous[i]!=0)throw new IOException("Refusing to initialize existing card data");
            }
            for(int page=2;page<storage.Blocks;page++){storage.Read(page,scratch);for(int i=0;i<BlockBytes;i++)if(scratch[i]!=0)throw new IOException("Refusing to initialize unverified existing data");}
            Put(root,0,Magic);Put(root,4,1);Put(root,8,1);Put(root,12,storage.Blocks);Seal(root);
            storage.Write(0,root);storage.Read(0,scratch);Match(root,scratch);bank=0;return;
        }
        if(b&&(!a||Get(previous,8)>Get(root,8))){var swap=root;root=previous;previous=swap;bank=1;}
        if(!a||!b)Array.Clear(previous,0,previous.Length);
    }
    private static void Seal(byte[] b)=>Put(b,BlockBytes-4,(int)Hash(b,0,BlockBytes-4));
    private static void Match(byte[] a,byte[] b){for(int i=0;i<a.Length;i++)if(a[i]!=b[i])throw new IOException("Card block readback failed");}
    private void Alive(){if(faulted)throw new IOException("Reopen the card after an interrupted operation");}
    internal int CapacityBlocks=>storage.Blocks;
    // The original format reserves bytes 16..31 in each CRC-protected root.
    // Publishing here needs no free data page, so even a full archive can
    // durably announce growth. Old one-byte AT-Archive records remain readable.
    internal bool ArchiveMarker(out int phase,out int epoch,out int growth)
    {phase=epoch=growth=0;if(Get(root,16)!=0x32435241)return false;epoch=Get(root,20);growth=Get(root,24);phase=Get(root,28);return true;}
    internal void SetArchiveMarker(int phase,int epoch,int growth)
    {Alive();var next=(byte[])root.Clone();Put(next,16,0x32435241);Put(next,20,epoch);Put(next,24,growth);Put(next,28,phase);try{Publish(next);}catch{faulted=true;throw;}}
    public int AvailableBlocks
    {get{Alive();var used=new bool[storage.Blocks];used[0]=used[1]=true;Protect(root,used);Protect(previous,used);int n=0;for(int i=2;i<used.Length;i++)if(!used[i])n++;return n;}}
    private int Find(string name){for(int i=0;i<MaximumRecords;i++)if(Name(root,At(i))==name)return i;return -1;}
    public string[] Names()
    {Alive();int count=0;for(int i=0;i<MaximumRecords;i++)if(root[At(i)]!=0)count++;var names=new string[count];count=0;for(int i=0;i<MaximumRecords;i++)if(root[At(i)]!=0)names[count++]=Name(root,At(i));return names;}
    public byte[] Read(string name)
    {
        Alive();Key(name);int i=Find(name);if(i<0)throw new FileNotFoundException("Card record not found");
        int at=At(i),page=Get(root,at+32),offset=Get(root,at+36),length=Get(root,at+40),done=0;
        var bytes=new byte[length];while(done<length){storage.Read(page++,scratch);int n=Math.Min(length-done,BlockBytes-offset);Array.Copy(scratch,offset,bytes,done,n);done+=n;offset=0;}
        if((int)Hash(bytes,0,length)!=Get(root,at+44))throw new IOException("Card record checksum failed");return bytes;
    }
    private static readonly uint[] crcNibbles={0,0x1db71064,0x3b6e20c8,0x26d930ac,0x76dc4190,0x6b6b51f4,0x4db26158,0x5005713c,0xedb88320,0xf00f9344,0xd6d6a3e8,0xcb61b38c,0x9b64c2b0,0x86d3d2d4,0xa00ae278,0xbdbdf21c};
    internal int Length(string name)
    {Alive();Key(name);int index=Find(name);if(index<0)throw new FileNotFoundException(name);return Get(root,At(index)+40);}
    internal byte[] ReadVerifiedPrefix(string name,int count,int sequence,int length,uint checksum)
    {
        if(count<0||count>8192||count>length)throw new IOException("Invalid card prefix size");var prefix=new byte[count];
        if(!Matches(name,null,sequence,length,checksum,false,prefix))throw new IOException("Card prefix reference differs");return prefix;
    }
    internal bool Matches(string name,byte[]? expected,int sequence,int length,uint checksum,bool ignoreSequence,byte[]? prefix=null)
    {
        Alive();Key(name);int index=Find(name);if(index<0)return false;int record=At(index),actualLength=Get(root,record+40);if(actualLength!=length||expected!=null&&expected.Length!=length)return false;
        int page=Get(root,record+32),offset=Get(root,record+36),done=0,actualSequence=0;uint hash=2166136261,crc=0xffffffff,storedCrc=0;bool same=true;
        while(done<length){storage.Read(page++,scratch);int count=Math.Min(length-done,BlockBytes-offset);
            for(int i=0;i<count;i++){int pos=done+i;byte b=scratch[offset+i];hash=unchecked((hash^b)*16777619u);
                if(prefix!=null&&pos<prefix.Length)prefix[pos]=b;
                if(pos<length-4){crc^=b;crc=(crc>>4)^crcNibbles[crc&15];crc=(crc>>4)^crcNibbles[crc&15];}else storedCrc|=(uint)b<<((pos-(length-4))*8);
                if(pos>=16&&pos<20)actualSequence|=b<<((pos-16)*8);
                if(expected!=null&&!(ignoreSequence&&(pos>=16&&pos<20||pos>=length-4))&&b!=expected[pos])same=false;
            }done+=count;offset=0;
        }
        if(hash!=(uint)Get(root,record+44))throw new IOException("Card record checksum failed");
        if(sequence>=0&&(actualSequence!=sequence||~crc!=checksum||storedCrc!=checksum))return false;
        return same;
    }
    private void Protect(byte[] index,bool[] used)
    {for(int i=0;i<MaximumRecords;i++){int at=At(i);if(index[at]==0)continue;int page=Get(index,at+32),n=Math.Max(1,(Get(index,at+36)+Get(index,at+40)+BlockBytes-1)/BlockBytes);for(int p=0;p<n;p++)used[page+p]=true;}}
    private static int FreeRun(bool[] used,int pages)
    {int run=0;for(int p=2;p<used.Length;p++){run=used[p]?0:run+1;if(run==pages)return p-pages+1;}return -1;}
    private bool Relocation(bool[] used,int needed,out int source,out int target,out int pages)
    {
        source=target=pages=0;var after=new bool[used.Length];
        for(int i=0;i<MaximumRecords;i++){
            int at=At(i);if(root[at]==0)continue;int start=Get(root,at+32),count=1;bool valid=true;
            for(int j=0;j<MaximumRecords;j++)if(root[At(j)]!=0){int p=Get(root,At(j)+32),n=Math.Max(1,(Get(root,At(j)+36)+Get(root,At(j)+40)+BlockBytes-1)/BlockBytes);if(p==start){if(j<i){valid=false;break;}count=Math.Max(count,n);}}
            if(!valid)continue;
            // Packed neighbours with the same first page move together. Refuse
            // unusual externally authored extents sharing only a trailing page.
            for(int j=0;j<MaximumRecords&&valid;j++)if(root[At(j)]!=0){int p=Get(root,At(j)+32),n=Math.Max(1,(Get(root,At(j)+36)+Get(root,At(j)+40)+BlockBytes-1)/BlockBytes);if(p!=start&&p<start+count&&start<p+n)valid=false;}
            if(!valid)continue;
            for(int gap=2;gap<used.Length;){
                if(used[gap]){gap++;continue;}int end=gap;while(end<used.Length&&!used[end])end++;
                if(end-gap>=count)for(int side=0;side<2;side++){
                    int destination=side==0?gap:end-count;Array.Copy(used,after,used.Length);
                    for(int p=0;p<count;p++){after[start+p]=false;after[destination+p]=true;}
                    if(FreeRun(after,needed)>=0){source=start;target=destination;pages=count;return true;}
                }
                gap=end;
            }
        }
        return false;
    }
    private void Relocate(int source,int target,int pages)
    {
        var verified=new byte[BlockBytes];
        for(int p=0;p<pages;p++){storage.Read(source+p,scratch);storage.Write(target+p,scratch);storage.Read(target+p,verified);Match(scratch,verified);}
        var next=(byte[])root.Clone();for(int i=0;i<MaximumRecords;i++)if(next[At(i)]!=0&&Get(next,At(i)+32)==source)Put(next,At(i)+32,target);
        Publish(next);Publish((byte[])root.Clone());
    }
    private void Publish(byte[] next)
    {
        if(Get(root,8)==int.MaxValue)throw new IOException("Card checkpoint sequence exhausted");
        Put(next,8,Get(root,8)+1);Seal(next);storage.Write(1-bank,next);storage.Read(1-bank,scratch);Match(next,scratch);
        previous=root;root=next;bank=1-bank;
    }
    public void Delete(string name)
    {Alive();Key(name);int i=Find(name);if(i<0)return;var next=(byte[])root.Clone();Array.Clear(next,At(i),Stride);try{Publish(next);}catch{faulted=true;throw;}}
    public void Write(string name,byte[] bytes)
    {
        Alive();Key(name);if(bytes==null)throw new ArgumentNullException("bytes");
        if(bytes.Length>(storage.Blocks-4)*BlockBytes)throw new IOException("Card container has insufficient reserved space");
        int index=Find(name);if(index<0)for(int i=0;i<MaximumRecords;i++)if(root[At(i)]==0){index=i;break;}
        if(index<0)throw new IOException("Card record directory is full");
        var used=new bool[storage.Blocks];used[0]=used[1]=true;Protect(root,used);Protect(previous,used);
        int pages=Math.Max(1,(bytes.Length+BlockBytes-1)/BlockBytes),page=FreeRun(used,pages);
        if(page<0){
            // Publish the verified current directory unchanged before retiring
            // pages referenced only by its older checkpoint. Both durable roots
            // then describe the same complete records; none of their live pages
            // can be reused. This avoids unnecessary growth after large rewrites.
            Array.Clear(used,0,used.Length);used[0]=used[1]=true;Protect(root,used);page=FreeRun(used,pages);
            bool move=false;int source=0,target=0,count=0;if(page<0)move=Relocation(used,pages,out source,out target,out count);
            if(page>=0||move)try{
                Publish((byte[])root.Clone());
                if(move){Relocate(source,target,count);Array.Clear(used,0,used.Length);used[0]=used[1]=true;Protect(root,used);page=FreeRun(used,pages);}
            }catch{faulted=true;throw;}
        }
        if(page<0)throw new IOException("Card container has insufficient reserved space");
        var next=(byte[])root.Clone();
        int group=-1;
        // Pack small records into one new page, copying every live neighbor.
        // Both old checkpoints stay untouched until the new root is verified.
        if(pages==1){
            for(int i=0;i<MaximumRecords;i++)if(i!=index&&root[At(i)]!=0){
                int candidate=Get(root,At(i)+32),size=Math.Max(1,bytes.Length);
                for(int j=0;j<MaximumRecords;j++)if(j!=index&&root[At(j)]!=0&&Get(root,At(j)+32)==candidate){
                    int n=Get(root,At(j)+40);if(Get(root,At(j)+36)+n>BlockBytes){size=BlockBytes+1;break;}size+=Math.Max(1,n);
                }
                if(size<=BlockBytes){group=candidate;break;}
            }
        }
        try{
            int offset=0;
            if(group>=0){
                var packed=new byte[BlockBytes];storage.Read(group,scratch);
                for(int j=0;j<MaximumRecords;j++)if(j!=index&&root[At(j)]!=0&&Get(root,At(j)+32)==group){
                    int at=At(j),n=Get(root,at+40);Array.Copy(scratch,Get(root,at+36),packed,offset,n);
                    Put(next,at+32,page);Put(next,at+36,offset);offset+=Math.Max(1,n);
                }
                Array.Copy(bytes,0,packed,offset,bytes.Length);storage.Write(page,packed);storage.Read(page,scratch);Match(packed,scratch);
            }else{
                int done=0;for(int p=0;p<pages;p++){
                    Array.Clear(scratch,0,BlockBytes);int n=Math.Min(bytes.Length-done,BlockBytes);Array.Copy(bytes,done,scratch,0,n);
                    storage.Write(page+p,scratch);var verified=new byte[BlockBytes];storage.Read(page+p,verified);Match(scratch,verified);done+=n;
                }
            }
            int target=At(index);Array.Clear(next,target,Stride);for(int i=0;i<name.Length;i++)next[target+i]=(byte)name[i];
            Put(next,target+32,page);Put(next,target+36,offset);Put(next,target+40,bytes.Length);Put(next,target+44,(int)Hash(bytes,0,bytes.Length));Publish(next);
        }catch{faulted=true;throw;}
    }
}
