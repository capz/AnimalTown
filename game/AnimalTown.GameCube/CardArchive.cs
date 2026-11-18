using System;
using System.IO;
using AnimalTown;
using Dolphin.Storage;

// One browser item per physical card; named records retain their own schemas.
// Reads never create a container. Migration publishes a verified copy before
// deleting any recognized old file. Unrelated files are never changed.
internal sealed class CardArchive
{
    private const string Container="AnimalTown",Expanded="AnimalTown2",Marker="AT-Archive";
    private readonly MemoryCard card;
    private readonly int device;
    private readonly byte[] banner,icon;
    private CardRecordStore? store;
    private int migrationPhase;
    private string container=Container;
    private bool expansionNeeded;
    private CardRecordStore? expansionTarget;
    internal CardArchive(MemoryCard card,byte[] banner,byte[] icon,int device)
    {
        this.card=card;this.banner=banner;this.icon=icon;this.device=device;
        var entries=card.GetEntries();bool old=false;
        for(int i=0;i<entries.Length;i++)if(entries[i].Name==Container)old=true;
        for(int i=0;i<entries.Length;i++)if(entries[i].Name==Expanded){
            var candidate=new CardRecordStore(new NativeCardBlocks(device,false,banner,icon,Expanded),true);
            int phase=Phase(candidate);
            if(phase>0){store=candidate;migrationPhase=phase;container=Expanded;return;}
            if(!old)throw new IOException("Unpublished expanded archive has no source; data kept");
            expansionTarget=candidate;break;
        }
        if(old){
            var blocks=new NativeCardBlocks(device,false,banner,icon);
            store=new CardRecordStore(blocks,true);migrationPhase=Phase(store);
            expansionNeeded=blocks.Blocks<NativeCardBlocks.ReservedBlocks;
        }
    }
    private static int Phase(CardRecordStore records)
    {
        foreach(string name in records.Names())if(name==Marker){var value=records.Read(Marker);if(value.Length!=1||value[0]<1||value[0]>2)throw new IOException("Invalid archive migration marker");return value[0];}
        return 0;
    }
    private static void CopyRecord(CardRecordStore target,string name,byte[] bytes)
    {
        bool same=false;
        foreach(string existing in target.Names())if(existing==name){try{Match(bytes,target.Read(name));same=true;}catch(IOException){}break;}
        if(!same)target.Write(name,bytes);Match(bytes,target.Read(name));
    }
    private void Expand()
    {
        var source=store!;
        var artBanner=banner.Length>0?banner:File.ReadAllBytes(ContentPaths.Asset("savebanner.rgb5a3"));
        var artIcon=icon.Length>0?icon:File.ReadAllBytes(ContentPaths.Asset("saveicon.rgb5a3"));
        var target=expansionTarget??new CardRecordStore(new NativeCardBlocks(device,true,artBanner,artIcon,Expanded),true);
        bool town=false;
        foreach(string name in source.Names()){
            if(name==Marker)continue;town|=SplitSaveCodec.IsFile(name);CopyRecord(target,name,source.Read(name));
        }
        // An interrupted older per-file migration still treats those files as
        // authoritative until its publication marker exists.
        if(migrationPhase==0){
            bool legacyTown=false;
            foreach(var entry in card.GetEntries())if(Known(entry.Name)){legacyTown|=SplitSaveCodec.IsFile(entry.Name);CopyRecord(target,entry.Name,card.ReadAllBytes(entry.Name));}
            if(legacyTown)Match(Snapshot(new Legacy(card)),Snapshot(new Records(target)));
        }else if(town)Match(Snapshot(new Records(source)),Snapshot(new Records(target)));
        // Publication precedes deletion. A cut during copying still loads the
        // old container; a cut after this marker loads the verified new one.
        target.Write(Marker,new byte[]{1});Match(target.Read(Marker),new byte[]{1});
        store=target;expansionTarget=null;container=Expanded;migrationPhase=1;expansionNeeded=false;
    }

    private static bool Known(string name)=>SplitSaveCodec.IsFile(name)||name==TownDataDeletion.Marker||name=="options"||name=="dev_config";
    private bool Published()=>migrationPhase>0;
    internal string[] Names()
    {if(Published())return store!.Names();var entries=card.GetEntries();var names=new string[entries.Length];for(int i=0;i<entries.Length;i++)names[i]=entries[i].Name;return names;}
    internal byte[] Read(string name)=>Published()?store!.Read(name):card.ReadAllBytes(name);
    internal void Write(string name,byte[] bytes){EnsureWritable();store!.Write(name,bytes);}
    internal void Delete(string name){EnsureWritable();store!.Delete(name);}
    private static void Match(byte[] a,byte[] b){if(a.Length!=b.Length)throw new IOException("Archive migration readback differs");for(int i=0;i<a.Length;i++)if(a[i]!=b[i])throw new IOException("Archive migration readback differs");}
    private sealed class Records : ISaveStorage
    {
        private readonly CardRecordStore records;
        internal Records(CardRecordStore records){this.records=records;}
        public byte[] Read(string name)=>records.Read(name);
        public void Write(string name,byte[] bytes)=>throw new InvalidOperationException("Read-only validation");
    }
    private sealed class Legacy : ISaveStorage
    {
        private readonly MemoryCard card;
        internal Legacy(MemoryCard card){this.card=card;}
        public byte[] Read(string name)=>card.ReadAllBytes(name);
        public void Write(string name,byte[] bytes)=>throw new InvalidOperationException("Read-only validation");
    }
    private static byte[] Snapshot(ISaveStorage source)
    {var state=new SaveRepository(source).Load()??throw new IOException("Existing save is incomplete; migration retained all original files");return SaveCodec.Encode(state);}
    private void EnsureWritable()
    {
        if(expansionNeeded)Expand();
        bool published=Published();
        if(published&&migrationPhase==2)return;
        var entries=card.GetEntries();bool town=false,pending=false;
        for(int i=0;i<entries.Length;i++){if(SplitSaveCodec.IsFile(entries[i].Name))town=true;if(entries[i].Name==TownDataDeletion.Marker)pending=true;}
        byte[]? snapshot=!published&&town&&!pending?Snapshot(new Legacy(card)):null;
        if(store==null){
            var artBanner=banner.Length>0?banner:File.ReadAllBytes(AnimalTown.ContentPaths.Asset("savebanner.rgb5a3"));
            var artIcon=icon.Length>0?icon:File.ReadAllBytes(AnimalTown.ContentPaths.Asset("saveicon.rgb5a3"));
            store=new CardRecordStore(new NativeCardBlocks(device,true,artBanner,artIcon),true);
        }
        if(!published){
            foreach(var entry in entries)if(Known(entry.Name)){
                var bytes=card.ReadAllBytes(entry.Name);bool same=false;
                foreach(string existing in store.Names())if(existing==entry.Name){Match(store.Read(existing),bytes);same=true;break;}
                if(!same)store.Write(entry.Name,bytes);Match(store.Read(entry.Name),bytes);
            }
            if(snapshot!=null)Match(snapshot,Snapshot(new Records(store)));
            store.Write(Marker,new byte[]{1});Match(store.Read(Marker),new byte[]{1});migrationPhase=1;
        }
        // A cut during cleanup resumes against exactly the migrated bytes.
        foreach(var entry in entries)if(Known(entry.Name)){Match(card.ReadAllBytes(entry.Name),store.Read(entry.Name));card.Delete(entry.Name);}
        if(container==Expanded)foreach(var entry in entries)if(entry.Name==Container){
            var original=new CardRecordStore(new NativeCardBlocks(device,false,banner,icon),true);
            foreach(string name in original.Names())if(name!=Marker)Match(original.Read(name),store!.Read(name));
            card.Delete(Container);
        }
        store!.Write(Marker,new byte[]{2});Match(store.Read(Marker),new byte[]{2});migrationPhase=2;
    }
}
