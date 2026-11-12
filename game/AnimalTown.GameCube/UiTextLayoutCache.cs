using System;
using System.IO;

// Glyph advances and line heights are integral font units. Keep the existing
// direct-mapped cache, but store exact offsets in two bytes instead of four.
// Unsupported offsets use the existing uncached drawing path.
internal sealed class UiTextLayoutCache
{
    internal const int Slots=64,Characters=128;
    internal const int CoordinatePayloadBytes=Slots*Characters*2*sizeof(ushort);
    private readonly string?[] texts=new string?[Slots];
    private readonly ushort[] x=new ushort[Slots*Characters],y=new ushort[Slots*Characters];
    private readonly float[] widths=new float[Slots];
    internal float X(int slot,int character)=>x[slot*Characters+character];
    internal float Y(int slot,int character)=>y[slot*Characters+character];
    internal float Width(int slot)=>widths[slot];
    internal int Find(string text,float[] advances,float lineHeight,bool firmware,out bool changed)
    {
        changed=false;
        if(text.Length>Characters)return -1;
        uint hash=2166136261;for(int i=0;i<text.Length;i++)hash=unchecked((hash^text[i])*16777619);
        int slot=(int)(hash&(Slots-1));
        if(texts[slot]==text)return slot;
        // A failed replacement must not leave a cache hit pointing at offsets
        // partially overwritten by the rejected text.
        texts[slot]=null;changed=true;
        float pen=0,line=0,max=0;int start=slot*Characters;
        for(int i=0;i<text.Length;i++){
            if(!Fits(pen)||!Fits(line))return -1;
            x[start+i]=Offset(pen);y[start+i]=Offset(line);char c=text[i];
            if(c=='\r')continue;
            if(c=='\n'){max=Math.Max(max,pen);pen=0;line+=lineHeight;continue;}
            int glyph=c>=32&&c<128?c-32:'?'-32;
            pen+=firmware&&c=='\t'?advances[0]*4:advances[glyph];
        }
        widths[slot]=Math.Max(max,pen);texts[slot]=text;return slot;
    }
    // Keep the integer return boundary explicit for the pinned AOT compiler.
    private static ushort Offset(float value)=>(ushort)value;
    private static bool Fits(float value)=>value>=0&&value<=ushort.MaxValue&&(float)(ushort)value==value;

    // Heading glyphs can span atlas pages. Reserve their actual maximum slice
    // count instead of eight slices for every glyph; IPL always uses one.
    internal static int GlyphRegionStride(byte[] metrics)
    {
        if(metrics.Length!=784)throw new IOException("Invalid font metrics");
        int stride=1;
        for(int i=0;i<96;i++){
            int at=16+i*8,x=Read(metrics,at),y=Read(metrics,at+2),width=Read(metrics,at+4);
            int slices=((x+width+127)/128-x/128)*((y+64+127)/128-y/128);
            if(slices>8)throw new IOException("Glyph exceeds bounded page layout");
            stride=Math.Max(stride,slices);
        }
        return stride;
    }
    private static int Read(byte[] data,int at)=>data[at]|data[at+1]<<8;
}
