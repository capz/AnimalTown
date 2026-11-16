using System.Buffers.Binary;
using AnimalTown;
using SkiaSharp;

// Offline GX RGB5A3 cooker. All dropped world sprites are 64x64 with a
// seven-level alpha-weighted mip chain, including an in-art ground shadow.
internal static class DroppedItemAssets
{
    internal const int Edge=64,Levels=7,PixelBytes=10976;
    internal static readonly string[] Names={
        "dropped-item","dropped-money","dropped-tool",
        "dropped-clothing","dropped-quest","dropped-present"
    };
    private const string SourceDir="assets/textures/items";
    // The original fallback retains a committed native package for compatibility.
    // Additional icons are reproducibly cooked directly into out/disc.
    internal static void Import(string root)
    {
        File.WriteAllBytes(Path.Combine(root,SourceDir,Names[0]+".dndtex"),Cook(root,Names[0]));
        Build(root);
    }
    private static byte[] Cook(string root,string name)
    {
        using var source=SKBitmap.Decode(Path.Combine(root,SourceDir,name+".png"))??
            throw new InvalidDataException("Dropped-item source missing: "+name);
        if(source.Width!=Edge||source.Height!=Edge)
            throw new InvalidDataException("Dropped-item sprite must be exactly 64x64: "+name);
        var rgba=new byte[Edge*Edge*4];
        for(int y=0;y<Edge;y++)for(int x=0;x<Edge;x++){
            var p=source.GetPixel(x,y);int at=(y*Edge+x)*4;
            rgba[at]=p.Red;rgba[at+1]=p.Green;rgba[at+2]=p.Blue;rgba[at+3]=p.Alpha;
            if((x<2||x>=Edge-2||y<2||y>=Edge-2)&&p.Alpha!=0)
                throw new InvalidDataException("Dropped-item sprite needs two transparent padding pixels: "+name);
        }
        var result=new byte[32+PixelBytes];int[] header={0x58544e44,2,5,Edge,Edge,PixelBytes,Levels,0};
        for(int i=0;i<header.Length;i++)BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(i*4),header[i]);
        int offset=32;
        for(int edge=Edge;edge>=1;edge/=2){
            // Bleed opaque RGB into zero-alpha neighbours, avoiding dark fringes.
            var bled=(byte[])rgba.Clone();Bleed(bled,edge);
            var native=TexturePixels.Encode(bled,edge,edge,5);
            native.CopyTo(result,offset);offset+=native.Length;
            if(edge==1)break;
            int next=edge/2;var filtered=new byte[next*next*4];
            for(int y=0;y<next;y++)for(int x=0;x<next;x++){
                int at=(y*2*edge+x*2)*4,dest=(y*next+x)*4,a=0,r=0,g=0,b=0;
                for(int j=0;j<2;j++)for(int i=0;i<2;i++){
                    int p=at+(j*edge+i)*4,v=rgba[p+3];
                    a+=v;r+=rgba[p]*v;g+=rgba[p+1]*v;b+=rgba[p+2]*v;
                }
                filtered[dest]=(byte)(a==0?0:(r+a/2)/a);
                filtered[dest+1]=(byte)(a==0?0:(g+a/2)/a);
                filtered[dest+2]=(byte)(a==0?0:(b+a/2)/a);
                filtered[dest+3]=(byte)((a+2)/4);
            }
            rgba=filtered;
        }
        if(offset!=result.Length)throw new InvalidDataException("Dropped-item mip budget mismatch: "+name);
        return result;
    }
    private static void Bleed(byte[] rgba,int edge)
    {
        var visited=new bool[edge*edge];var queue=new int[edge*edge];int read=0,write=0;
        for(int p=0;p<visited.Length;p++)if(rgba[p*4+3]>0){visited[p]=true;queue[write++]=p;}
        while(read<write){
            int p=queue[read++],x=p%edge,y=p/edge;
            if(x>0)Fill(p,p-1);if(x+1<edge)Fill(p,p+1);
            if(y>0)Fill(p,p-edge);if(y+1<edge)Fill(p,p+edge);
        }
        void Fill(int from,int to){
            if(visited[to])return;visited[to]=true;
            for(int c=0;c<3;c++)rgba[to*4+c]=rgba[from*4+c];
            queue[write++]=to;
        }
    }
    internal static void Build(string root)
    {
        string dir=Path.Combine(root,"out/disc");Directory.CreateDirectory(dir);
        for(int i=0;i<Names.Length;i++){
            string name=Names[i];byte[] cooked=Cook(root,name);
            if(i==0){
                byte[] committed=File.ReadAllBytes(Path.Combine(root,SourceDir,name+".dndtex"));
                if(!cooked.SequenceEqual(committed))
                    throw new InvalidDataException("Dropped-item fallback native asset is stale; run dropped-item-import");
                cooked=committed;
            }
            TexturePixels.Validate(cooked);
            File.WriteAllBytes(Path.Combine(dir,name+".dndtex"),cooked);
        }
        Console.WriteLine("Dropped-item sprites: "+Names.Length+" x 64x64 RGB5A3, 7 alpha-weighted mips, "+PixelBytes+" native bytes each");
    }
    internal static void Check(string root)
    {
        Build(root);
        foreach(string name in Names){
            byte[] package=File.ReadAllBytes(Path.Combine(root,"out/disc",name+".dndtex"));
            using var source=SKBitmap.Decode(Path.Combine(root,SourceDir,name+".png"))!;
            int transparent=0,opaque=0;long alpha=0;
            for(int y=0;y<Edge;y++)for(int x=0;x<Edge;x++){
                var p=source.GetPixel(x,y);uint decoded=TexturePixels.Pixel(package,x,y);
                int a=(int)(decoded&255);alpha+=p.Alpha;
                if(Math.Abs(a-p.Alpha)>31)throw new InvalidDataException(name+" sprite alpha quantization exceeds RGB5A3");
                if(p.Alpha>0&&(Math.Abs((int)(decoded>>24)-p.Red)>15||
                    Math.Abs((int)((decoded>>16)&255)-p.Green)>15||
                    Math.Abs((int)((decoded>>8)&255)-p.Blue)>15))
                    throw new InvalidDataException(name+" sprite RGB round trip exceeds RGB5A3");
                if(a==0)transparent++;if(a==255)opaque++;
            }
            if(transparent<500||opaque<(name==Names[0]?1200:400))
                throw new InvalidDataException(name+" sprite lost its transparent background or solid subject");
            int offset=32;
            for(int edge=Edge;edge>=1;edge/=2){
                int bytes=((edge+3)/4)*((edge+3)/4)*32;var level=new byte[32+bytes];
                int[] header={0x58544e44,1,5,edge,edge,bytes,0,0};
                for(int i=0;i<8;i++)BinaryPrimitives.WriteInt32LittleEndian(level.AsSpan(i*4),header[i]);
                Array.Copy(package,offset,level,32,bytes);TexturePixels.Validate(level);
                long actual=0;
                for(int y=0;y<edge;y++)for(int x=0;x<edge;x++)actual+=TexturePixels.Pixel(level,x,y)&255;
                if(Math.Abs((double)actual/(edge*edge)-(double)alpha/(Edge*Edge))>32)
                    throw new InvalidDataException(name+" sprite mip lost alpha coverage");
                offset+=bytes;
            }
            if(offset!=package.Length||(TexturePixels.Pixel(package,0,0)&255)!=0||
                (TexturePixels.Pixel(package,0,0)>>24)==0)
                throw new InvalidDataException(name+" sprite padding must retain bleed RGB with zero alpha");
        }
        Console.WriteLine("Dropped-item asset checks passed: six sources, native RGB5A3, all GX mips, alpha coverage and filtering");
    }
}
