namespace AnimalTown.Jobs;

// Small lazy pages avoid a 74 KiB contiguous allocation while decoding a town
// beside another full town and its encoded checkpoint. Reads never allocate.
internal sealed class JobMailSlots
{
    internal const int PageSize=16;
    private static readonly JobMailRecord[] missing=new JobMailRecord[1];
    private readonly JobMailRecord[]?[] pages;
    private ulong shared;
    internal int Length {get;}
    internal JobMailSlots(int length){Length=length;pages=new JobMailRecord[(length+PageSize-1)/PageSize][];}
    internal JobMailSlots Share()
    {
        var copy=new JobMailSlots(Length);
        for(int i=0;i<pages.Length;i++)if(pages[i]!=null){copy.pages[i]=pages[i];ulong bit=1UL<<i;shared|=bit;copy.shared|=bit;}
        return copy;
    }
    internal ref readonly JobMailRecord this[int index]
    {
        get{
            if((uint)index>=Length)throw new ArgumentOutOfRangeException(nameof(index));
            var page=pages[index/PageSize];if(page==null)return ref missing[0];return ref page[index%PageSize];
        }
    }
    internal void Set(int index,JobMailRecord value)
    {
        if((uint)index>=Length)throw new ArgumentOutOfRangeException(nameof(index));int at=index/PageSize;var page=pages[at];
        if(page==null){if(value.State==JobMailState.Empty)return;page=new JobMailRecord[PageSize];pages[at]=page;}
        else if((shared&(1UL<<at))!=0){page=(JobMailRecord[])page.Clone();pages[at]=page;shared&=~(1UL<<at);}
        page[index%PageSize]=value;
        if(value.State==JobMailState.Empty){for(int i=0;i<page.Length;i++)if(page[i].State!=JobMailState.Empty)return;pages[at]=null;}
    }
    internal void ReserveAll(){for(int i=0;i<pages.Length;i++)if(pages[i]==null)pages[i]=new JobMailRecord[PageSize];}
}
