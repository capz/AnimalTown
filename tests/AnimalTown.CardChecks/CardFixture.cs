// Host storage fixture for the production card adapter. Models the pinned SDK's
// container/block/journal charges; this is not physical card or DMA validation.
using Dolphin.Storage;

internal static class BuildInfo {internal const string Version="0.20.0",Commit="0123456789ab";}
internal static class LoadingScreen {internal static void Pulse(){}}

namespace Dolphin.Storage
{
    internal sealed class MemoryCardSaveOptions
    {
        public string Title="",Comment="";
        public byte[]? Banner,Icon;
    }
    internal sealed class MemoryCard
    {
        internal readonly Dictionary<string,byte[]> Files=new();
        internal readonly Dictionary<string,MemoryCardSaveOptions> Metadata=new();
        private readonly Dictionary<string,int> blocks=new();
        internal int Capacity=24,Used,Peak,Writes;
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
