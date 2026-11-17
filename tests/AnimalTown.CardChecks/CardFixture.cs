// Host storage fixture for the production card adapter. Models the pinned SDK's
// container/block/journal charges; this is not physical card or DMA validation.
using Dolphin.Storage;

internal static class BuildInfo {internal const string Version="0.20.0",Commit="0123456789ab";}
internal static class LoadingScreen {internal static void Pulse(){}}

namespace Dolphin.Storage
{
    internal sealed class MemoryCardEntry {public string Name="";}
    internal sealed class MemoryCardSaveOptions
    {
        public string Title="",Comment="";
        public byte[]? Banner,Icon;
    }
    internal sealed class MemoryCard
    {
        internal static MemoryCard Current=null!;
        internal MemoryCard(){Current=this;}
        internal readonly Dictionary<string,byte[][]> RecordPages=new();
        internal readonly Dictionary<string,int> RecordIdentities=new();
        internal int FailBlockWrite=-1,FailDelete=-1;
        internal readonly Dictionary<string,byte[]> Files=new();
        internal readonly Dictionary<string,MemoryCardSaveOptions> Metadata=new();
        private readonly Dictionary<string,int> blocks=new();
        internal int Capacity=80,Used,Peak,Writes;
        public MemoryCardEntry[] GetEntries()=>Files.Keys.Select(name=>new MemoryCardEntry{Name=name}).ToArray();
        public void Delete(string name){if(FailDelete--==0)throw new IOException("Card removed during cleanup");Files.Remove(name);Metadata.Remove(name);RecordPages.Remove(name);RecordIdentities.Remove(name);if(blocks.Remove(name,out int n))Used-=n;}
        internal int CreateRecords(string name,int count,int identity,byte[] header)
        {
            if(Files.ContainsKey(name))return -17;
            if(Used+count+2>Capacity)return -28;
            RecordIdentities[name]=identity;var pages=new byte[count][];for(int i=0;i<count;i++)pages[i]=new byte[8192];RecordPages[name]=pages;
            Files[name]=Array.Empty<byte>();blocks[name]=count+2;Used+=count+2;Peak=Math.Max(Peak,Used);
            var banner=new byte[6144];var icon=new byte[2048];Array.Copy(header,96,banner,0,6144);Array.Copy(header,6240,icon,0,2048);
            Metadata[name]=new MemoryCardSaveOptions{Title="AnimalTown",Comment="Town, villagers and options",Banner=banner,Icon=icon};return 0;
        }
        public byte[] ReadAllBytes(string name)=>Files.TryGetValue(name,out var bytes)?(byte[])bytes.Clone():throw new FileNotFoundException(name);
        public void WriteSave(string name,byte[] data,MemoryCardSaveOptions options)
        {
            if(options.Title.Length>31||options.Comment.Length>31||options.Banner!=null&&options.Banner.Length!=6144||options.Icon!=null&&options.Icon.Length!=2048)
                throw new InvalidOperationException("Invalid SDK save metadata");
            int size=(data.Length+96+(options.Banner?.Length??0)+(options.Icon?.Length??0)+8191)/8192;
            int next=Used-(blocks.TryGetValue(name,out int old)?old:0)+size;
            int journal=size+1; // Padded container plus the 64-byte journal header.
            int peak=Math.Max(Used,next)+journal;
            if(peak>Capacity)throw new IOException("Memory card full; journal and previous save preserved");
            Peak=Math.Max(Peak,peak);Used=next;blocks[name]=size;Writes++;
            Files[name]=(byte[])data.Clone();Metadata[name]=options;
        }
    }
}

namespace Dolphin.Timing {internal static class Clock {internal static long Timestamp=>1234;}}
internal static class CardBlockBridge
{
    internal static int Io(int device,string name,int op,int block,int identity,byte[] bytes)
    {
        if(device!=4)throw new InvalidOperationException("Host fixture uses Slot A");
        var card=MemoryCard.Current;
        if(op==0)return card.RecordPages.TryGetValue(name,out var existing)?existing.Length:0;
        if(op==3)return card.CreateRecords(name,block,identity,bytes);
        if(op==4)return card.RecordIdentities[name];
        if(!card.RecordPages.TryGetValue(name,out var pages)||identity!=card.RecordIdentities[name])return -19;
        if(op==1){Array.Copy(pages[block],bytes,8192);return 0;}
        card.Writes++;if(card.FailBlockWrite--==0){Array.Copy(bytes,pages[block],4096);throw new IOException("Interrupted block write");}
        Array.Copy(bytes,pages[block],8192);return 0;
    }
}
