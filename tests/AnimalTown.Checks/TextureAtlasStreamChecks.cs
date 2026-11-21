using AnimalTown;

internal static class TextureAtlasStreamChecks
{
    private sealed class ShortReads : MemoryStream
    {
        internal int LargestRequest;
        internal ShortReads(byte[] bytes):base(bytes,false){}
        public override int Read(byte[] buffer,int offset,int count)
        {LargestRequest=Math.Max(LargestRequest,count);return base.Read(buffer,offset,Math.Min(count,37));}
    }
    internal static void Run(Action<bool,string> check)
    {
        foreach(int format in new[]{0,1,2,3,4,5,6,14})foreach(var size in new[]{(512,512),(256,128),(64,16),(1,1)}){
            int w=size.Item1,h=size.Item2,bw=format<=2||format==14?8:4,bh=format==0||format==14?8:4;
            int payload=(w+bw-1)/bw*((h+bh-1)/bh)*(format==6?64:32);
            var original=new byte[32+payload+64];int[] header={0x58544e44,2,format,w,h,original.Length-32,2,0};
            for(int i=0;i<8;i++)for(int b=0;b<4;b++)original[i*4+b]=(byte)((uint)header[i]>>(b*8));
            for(int i=32;i<original.Length;i++)original[i]=(byte)(i*13+(i>>12)*71);
            using var stream=new ShortReads(original);long before=GC.GetAllocatedBytesForCurrentThread();var atlas=new TextureAtlasStream(stream);
            check(GC.GetAllocatedBytesForCurrentThread()-before<80*1024,"atlas source never allocates its half-MiB package");
            check(atlas.SourceHash==TexturePixels.Hash(original)&&atlas.Width==w&&atlas.Height==h,"streamed atlas retains exact original identity, including mips");
            byte[]? first=null;
            for(int y=0;y<atlas.Rows;y++)for(int x=0;x<atlas.Columns;x++){
                var page=atlas.ReadPage(x,y);TexturePixels.Validate(page);first??=page;
                check(ReferenceEquals(page,first)&&page.Length<=32+128*128*4,"atlas pages reuse one bounded packet");
                for(int py=0;py<TexturePixels.Height(page);py++)for(int px=0;px<TexturePixels.Width(page);px++)
                    check(TexturePixels.Pixel(page,px,py)==TexturePixels.Pixel(original,x*128+px,y*128+py),"every streamed texel matches the original GX decoder");
            }
            check(stream.LargestRequest<=8192,"atlas reads remain bounded during hashing and row extraction");
            bool rejected=false;try{atlas.ReadPage(atlas.Columns,0);}catch(ArgumentOutOfRangeException){rejected=true;}check(rejected,"atlas refuses an out-of-range page");
            original[20]^=1;rejected=false;try{new TextureAtlasStream(new MemoryStream(original));}catch(InvalidOperationException){rejected=true;}check(rejected,"atlas refuses a truncated or mismatched payload before upload");
        }
        Console.WriteLine("Streamed atlases: all GX formats, exact per-pixel pages, original hashes, short reads, bounded scratch and malformed input passed");
    }
}
