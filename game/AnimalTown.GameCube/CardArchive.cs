using System;
using System.IO;
using AnimalTown;
using Dolphin.Storage;

// One browser item per physical card; named records retain their own schemas.
// Reads never create a container. Migration publishes a verified copy before
// deleting any recognized old file. Unrelated files are never changed.
internal sealed class CardArchive
{
    private const string Container="AnimalTown",Marker="AT-Archive";
    private readonly MemoryCard card;
    private readonly int device;
    private readonly byte[] banner,icon;
    private CardRecordStore? store;
    private int migrationPhase;
    internal CardArchive(MemoryCard card,byte[] banner,byte[] icon,int device=4)
    {
        this.card=card;this.banner=banner;this.icon=icon;this.device=device;
        foreach(var entry in card.GetEntries())if(entry.Name==Container){
            store=new CardRecordStore(new NativeCardBlocks(device,false,banner,icon),true);
            foreach(string name in store.Names())if(name==Marker){var marker=store.Read(Marker);if(marker.Length!=1||marker[0]<1||marker[0]>2)throw new IOException("Invalid archive migration marker");migrationPhase=marker[0];break;}
            break;
        }
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
        store!.Write(Marker,new byte[]{2});Match(store.Read(Marker),new byte[]{2});migrationPhase=2;
    }
}
