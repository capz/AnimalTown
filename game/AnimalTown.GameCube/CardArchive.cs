using System;
using System.IO;
using AnimalTown;
using Dolphin.Storage;

// Two alternating backing names permit verified growth without replacing the
// sole authoritative file. Epoch publication precedes deletion of the old file.
// Normal towns still reserve 32 blocks; mail expands only when actually needed.
internal sealed class CardArchive
{
    private const string Container="AnimalTown",Expanded="AnimalTown2",Marker="AT-Archive";
    private readonly MemoryCard card;
    private readonly int device;
    private readonly byte[] banner,icon;
    private TownCardRecords? store;
    private string container=Container;
    private int phase,epoch,growBlocks;
    internal CardArchive(MemoryCard card,byte[] banner,byte[] icon,int device)
    {
        this.card=card;this.banner=banner;this.icon=icon;this.device=device;
        var entries=card.GetEntries();
        // A source growth intent also permits recovery of a target whose
        // initial zero filling was interrupted before it had a checkpoint.
        for(int pass=0;pass<2;pass++)for(int i=0;i<entries.Length;i++){
            string name=entries[i].Name;if(name!=Container&&name!=Expanded)continue;
            try{
                var candidate=new TownCardRecords(new NativeCardBlocks(device,false,banner,icon,name),true);
                ReadMarker(candidate,out int p,out int e,out int growth);
                if(p>0&&(store==null||phase==0||e>epoch||e==0&&epoch==0&&name==Expanded)){store=candidate;container=name;phase=p;epoch=e;growBlocks=growth;}
                else if(store==null&&name==Container){store=candidate;container=name;}
            }catch(IOException){if(pass==1&&(store==null||phase!=3||name==container))throw;}
        }
        if(store==null)foreach(var entry in entries)if(entry.Name==Expanded)throw new IOException("Unpublished expanded archive has no source; data kept");
    }
    private static int Number(byte[] bytes,int at)
    {int n=0;for(int i=0;i<4;i++)n|=bytes[at+i]<<(i*8);return n;}
    private static byte[] MarkerBytes(int p,int e,int growth=0)
    {var bytes=new byte[growth>0?9:5];bytes[0]=(byte)p;for(int i=0;i<4;i++)bytes[1+i]=(byte)((uint)e>>(i*8));if(growth>0)for(int i=0;i<4;i++)bytes[5+i]=(byte)((uint)growth>>(i*8));return bytes;}
    private static void ReadMarker(TownCardRecords records,out int p,out int e,out int growth)
    {
        if(records.ArchiveMarker(out p,out e,out growth)){if(p<1||p>3||e<0||p==3&&(growth<32||growth>2048))throw new IOException("Invalid archive root publication");return;}
        p=e=growth=0;foreach(string name in records.Names())if(name==Marker){var b=records.Read(name);if(b.Length!=1&&b.Length!=5&&b.Length!=9||b[0]<1||b[0]>3)throw new IOException("Invalid archive publication marker");p=b[0];e=b.Length>=5?Number(b,1):0;growth=b.Length==9?Number(b,5):0;if(e<0||p==3&&(growth<32||growth>2048))throw new IOException("Invalid archive growth intent");return;}
    }
    private void PublishMarker(int value,int generation,int growth=0)
    {store!.SetArchiveMarker(value,generation,growth);ReadMarker(store,out int readPhase,out int readEpoch,out int readGrowth);if(readPhase!=value||readEpoch!=generation||readGrowth!=growth)throw new IOException("Archive root publication differs");phase=value;epoch=generation;growBlocks=growth;}
    private static bool Known(string name)=>SplitSaveCodec.IsFile(name)||name==TownDataDeletion.Marker||name=="options"||name=="dev_config";
    private bool Published=>phase>0;
    internal string[] Names()
    {if(Published)return store!.Names();var entries=card.GetEntries();var names=new string[entries.Length];for(int i=0;i<entries.Length;i++)names[i]=entries[i].Name;return names;}
    internal byte[] Read(string name)=>Published?store!.Read(name):card.ReadAllBytes(name);
    internal void Write(string name,byte[] bytes)
    {
        EnsureWritable();
        try{store!.Write(name,bytes);}
        catch(IOException e){if(e.Message!="Card container has insufficient reserved space")throw;Grow(GrowthSize(bytes.Length));EnsureWritable();store!.Write(name,bytes);}
    }
    internal void Delete(string name){EnsureWritable();store!.Delete(name);}
    private static void Match(byte[] a,byte[] b)
    {if(a.Length!=b.Length)throw new IOException("Archive readback differs");for(int i=0;i<a.Length;i++)if(a[i]!=b[i])throw new IOException("Archive readback differs");}
    private static void CopyRecord(TownCardRecords target,string name,byte[] bytes)
    {
        bool same=false;foreach(string existing in target.Names())if(existing==name){try{Match(bytes,target.Read(name));same=true;}catch(IOException){}break;}
        if(!same)target.Write(name,bytes);Match(bytes,target.Read(name));
    }
    private int RecordLength(string name)=>store!.Read(name).Length;
    private int GrowthSize(int incoming)
    {
        long total=16L*8192+2L*incoming;foreach(string name in store!.Names())total+=RecordLength(name);
        int required=Math.Max(store.CapacityBlocks*2,(int)((total+8191)/8192)),rounded=32;
        while(rounded<required&&rounded<2048)rounded*=2;
        if(required>2048)throw new IOException("The town needs more card space. Existing saves are kept.");return rounded;
    }
    private void Grow(int blocks)
    {
        if(epoch==int.MaxValue)throw new IOException("Archive generation exhausted");
        var source=store!;string targetName=container==Container?Expanded:Container;
        // Written before target creation, this proves which file is temporary.
        if(phase!=3)PublishMarker(3,epoch,blocks);else blocks=growBlocks;
        var artBanner=banner.Length>0?banner:File.ReadAllBytes(ContentPaths.Asset("savebanner.rgb5a3"));
        var artIcon=icon.Length>0?icon:File.ReadAllBytes(ContentPaths.Asset("saveicon.rgb5a3"));
        TownCardRecords target;
        try{
            var backing=new NativeCardBlocks(device,false,artBanner,artIcon,targetName);
            target=new TownCardRecords(backing,true);ReadMarker(target,out int published,out _,out _);
            if(published!=0||backing.Blocks!=blocks)throw new InvalidOperationException("Growth target needs recovery; both archives were kept");
        }catch(FileNotFoundException){target=new TownCardRecords(new NativeCardBlocks(device,true,artBanner,artIcon,targetName,blocks),true);}
        catch(IOException){
            // The durable source intent authorizes recreating only this
            // alternate temporary file; the complete source is still intact.
            bool exists=false;foreach(var entry in card.GetEntries())if(entry.Name==targetName)exists=true;
            if(exists)card.Delete(targetName);
            target=new TownCardRecords(new NativeCardBlocks(device,true,artBanner,artIcon,targetName,blocks),true);
        }
        foreach(string name in source.Names())if(name!=Marker)CopyRecord(target,name,source.Read(name));
        target.SetArchiveMarker(1,epoch+1,0);ReadMarker(target,out int readPhase,out int readEpoch,out _);if(readPhase!=1||readEpoch!=epoch+1)throw new IOException("Expanded archive publication differs");
        store=target;container=targetName;phase=1;epoch++;growBlocks=0;Cleanup();
    }
    private sealed class Legacy : ISaveStorage
    {
        private readonly MemoryCard card;internal Legacy(MemoryCard card){this.card=card;}
        public byte[] Read(string name)=>card.ReadAllBytes(name);
        public void Write(string name,byte[] bytes)=>throw new InvalidOperationException("Read-only");
    }
    private void EnsureWritable()
    {
        if(phase==3){Grow(growBlocks);return;}
        if(Published&&store!.CapacityBlocks<NativeCardBlocks.ReservedBlocks){Grow(NativeCardBlocks.ReservedBlocks);return;}
        if(Published){if(phase==1)Cleanup();return;}
        var entries=card.GetEntries();bool town=false,pending=false;
        foreach(var entry in entries){town|=SplitSaveCodec.IsFile(entry.Name);pending|=entry.Name==TownDataDeletion.Marker;}
        if(town&&!pending&&new SaveRepository(new Legacy(card)).Load()==null)throw new IOException("Existing save is incomplete; original files kept");
        if(store==null){var b=banner.Length>0?banner:File.ReadAllBytes(ContentPaths.Asset("savebanner.rgb5a3"));var i=icon.Length>0?icon:File.ReadAllBytes(ContentPaths.Asset("saveicon.rgb5a3"));store=new TownCardRecords(new NativeCardBlocks(device,true,b,i),true);}
        foreach(var entry in entries)if(Known(entry.Name))CopyRecord(store,entry.Name,card.ReadAllBytes(entry.Name));
        PublishMarker(1,epoch);Cleanup();
    }
    private void Cleanup()
    {
        foreach(var entry in card.GetEntries())if(Known(entry.Name)){Match(card.ReadAllBytes(entry.Name),store!.Read(entry.Name));card.Delete(entry.Name);}
        string other=container==Container?Expanded:Container;
        foreach(var entry in card.GetEntries())if(entry.Name==other){
            var old=new TownCardRecords(new NativeCardBlocks(device,false,banner,icon,other),true);ReadMarker(old,out _,out int oldEpoch,out _);
            if(oldEpoch>=epoch)throw new IOException("Conflicting archive generations kept");
            foreach(string name in old.Names())if(name!=Marker)Match(old.Read(name),store!.Read(name));card.Delete(other);
        }
        PublishMarker(2,epoch);
    }
}
