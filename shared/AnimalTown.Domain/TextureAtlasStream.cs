using System.IO;

namespace AnimalTown;

// Read GX-tiled atlas pages without retaining the full source package. The
// caller owns the stream; ReadPage reuses one bounded native-format packet.
public sealed class TextureAtlasStream
{
    public const int Edge=128;
    private readonly Stream stream;
    private readonly byte[] page;
    private readonly int blockWidth,blockHeight,blockBytes,sourceColumns,pageColumns,pageRows;
    public int Width {get;}
    public int Height {get;}
    public int Columns=>(Width+Edge-1)/Edge;
    public int Rows=>(Height+Edge-1)/Edge;
    public uint SourceHash {get;}
    public TextureAtlasStream(Stream source)
    {
        stream=source;var header=new byte[32];ReadExact(header,0,32);
        TexturePixels.ValidateHeader(header,stream.Length);
        Width=TexturePixels.Width(header);Height=TexturePixels.Height(header);
        int format=TexturePixels.Int(header,8);
        blockWidth=format<=2||format==14?8:4;blockHeight=format==0||format==14?8:4;blockBytes=format==6?64:32;
        sourceColumns=(Width+blockWidth-1)/blockWidth;
        pageColumns=(Math.Min(Edge,Width)+blockWidth-1)/blockWidth;
        pageRows=(Math.Min(Edge,Height)+blockHeight-1)/blockHeight;
        page=new byte[32+pageColumns*pageRows*blockBytes];Array.Copy(header,page,32);
        Number(4,1);Number(12,Math.Min(Edge,Width));Number(16,Math.Min(Edge,Height));Number(20,page.Length-32);Number(24,0);
        // Preserve the original-package identity, including any discarded mips.
        uint hash=2166136261;for(int i=0;i<header.Length;i++)hash=(hash^header[i])*16777619;
        int left=TexturePixels.Int(header,20);var scratch=new byte[8192];
        while(left>0){int count=Math.Min(left,scratch.Length);ReadExact(scratch,0,count);for(int i=0;i<count;i++)hash=(hash^scratch[i])*16777619;left-=count;}
        SourceHash=hash;
    }
    private void Number(int at,int value){for(int i=0;i<4;i++)page[at+i]=(byte)((uint)value>>(i*8));}
    private void ReadExact(byte[] bytes,int offset,int count)
    {
        while(count>0){int read=stream.Read(bytes,offset,count);if(read<=0)throw new IOException("Truncated atlas page");offset+=read;count-=read;}
    }
    public byte[] ReadPage(int x,int y)
    {
        if((uint)x>=Columns||(uint)y>=Rows)throw new ArgumentOutOfRangeException("atlas page");
        int rowBytes=pageColumns*blockBytes;
        for(int row=0;row<pageRows;row++){
            long offset=32L+((long)(y*Edge/blockHeight+row)*sourceColumns+x*Edge/blockWidth)*blockBytes;
            if(stream.Seek(offset,SeekOrigin.Begin)!=offset)throw new IOException("Could not seek atlas page");
            ReadExact(page,32+row*rowBytes,rowBytes);
        }
        return page;
    }
}
