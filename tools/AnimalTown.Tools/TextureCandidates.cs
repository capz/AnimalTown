using System.Text.Json;
using AnimalTown;
using DolphinDotNet.Imaging;

// Offline review artifacts; these candidates never authorize their own use.
internal static class TextureCandidates
{
    internal static byte[] Package(byte[] pixels,int w,int h,int format)
    {
        byte[] p=new byte[32+pixels.Length];int[] fields={0x58544e44,2,format,w,h,pixels.Length,1,0};
        for(int i=0;i<8;i++)for(int b=0;b<4;b++)p[i*4+b]=(byte)(fields[i]>>(b*8));pixels.CopyTo(p,32);return p;
    }
    internal static byte[] Decode(byte[] pixels,int w,int h,int format)
    {byte[] p=Package(pixels,w,h,format),rgba=new byte[w*h*4];for(int y=0;y<h;y++)for(int x=0;x<w;x++){uint c=TexturePixels.Pixel(p,x,y);int a=(y*w+x)*4;rgba[a]=(byte)(c>>24);rgba[a+1]=(byte)(c>>16);rgba[a+2]=(byte)(c>>8);rgba[a+3]=(byte)c;}return rgba;}
    internal static byte[] Encode(byte[] rgba,int w,int h,int format)
    {
        if(format!=14)return TexturePixels.Encode(rgba,w,h,format);
        byte[] result=new byte[((w+7)/8)*((h+7)/8)*32];int at=0;
        var colors=new int[16];var alpha=new bool[16];var palette=new int[4,3];
        int R(int c)=>(c>>11)*255/31;int G(int c)=>((c>>5)&63)*255/63;int B(int c)=>(c&31)*255/31;
        int Distance(int a,int b){int r=R(a)-R(b),g=G(a)-G(b),v=B(a)-B(b);return r*r+g*g+v*v;}
        for(int by=0;by<h;by+=8)for(int bx=0;bx<w;bx+=8)for(int sub=0;sub<4;sub++){
            bool transparent=false;int c0=0,c1=0,best=-1;
            for(int y=0;y<4;y++)for(int x=0;x<4;x++){int n=y*4+x,p=(Math.Min(h-1,by+sub/2*4+y)*w+Math.Min(w-1,bx+sub%2*4+x))*4;colors[n]=((rgba[p]>>3)<<11)|((rgba[p+1]>>2)<<5)|(rgba[p+2]>>3);alpha[n]=rgba[p+3]<128;transparent|=alpha[n];}
            for(int i=0;i<16;i++)if(!alpha[i])for(int j=0;j<=i;j++)if(!alpha[j]){int d=Distance(colors[i],colors[j]);if(d>best){best=d;c0=colors[i];c1=colors[j];}}
            if(transparent?c0>c1:c0<c1)(c0,c1)=(c1,c0);
            // Equal opaque endpoints use selector 0 only; transparent blocks
            // intentionally keep the three-color/transparent endpoint order.
            palette[0,0]=R(c0);palette[0,1]=G(c0);palette[0,2]=B(c0);palette[1,0]=R(c1);palette[1,1]=G(c1);palette[1,2]=B(c1);
            for(int c=0;c<3;c++){palette[2,c]=transparent?(palette[0,c]+palette[1,c])/2:(2*palette[0,c]+palette[1,c])/3;palette[3,c]=(palette[0,c]+2*palette[1,c])/3;}
            result[at]=(byte)(c0>>8);result[at+1]=(byte)c0;result[at+2]=(byte)(c1>>8);result[at+3]=(byte)c1;
            for(int y=0;y<4;y++){int bits=0;for(int x=0;x<4;x++){int n=y*4+x,pick=alpha[n]?3:0,min=int.MaxValue;if(!alpha[n])for(int k=0;k<(c0==c1?1:transparent?3:4);k++){int r=R(colors[n])-palette[k,0],g=G(colors[n])-palette[k,1],b=B(colors[n])-palette[k,2],d=r*r+g*g+b*b;if(d<min){min=d;pick=k;}}bits|=pick<<(6-x*2);}result[at+4+y]=(byte)bits;}at+=8;
        }
        return result;
    }
    internal static void Write(string root,IReadOnlyList<TextureBanks.Image> images)
    {
        string output=Path.Combine(root,"out/texture-candidates");Directory.CreateDirectory(output);var reports=new List<object>();
        for(int id=0;id<images.Count;id++){
            var image=images[id];if(image.Format!=6)continue;
            using var sheet=new RasterImage(image.Width*4,image.Height*image.Levels.Count,new Rgba32(80,80,80,255));
            for(int l=0;l<image.Levels.Count;l++){
                int w=image.Width>>l,h=image.Height>>l;byte[] source=Decode(image.Levels[l],w,h,6);
                for(int f=0;f<4;f++){
                    int format=new[]{6,4,5,14}[f];byte[] encoded=Encode(source,w,h,format),decoded=Decode(encoded,w,h,format);long error=0;int coverage=0;
                    for(int p=0;p<source.Length;p+=4){for(int c=0;c<4;c++){int delta=source[p+c]-decoded[p+c];error+=(long)delta*delta;}if((source[p+3]>=128)!=(decoded[p+3]>=128))coverage++;}
                    for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++){
                        int p=(y*h/image.Height*w+x*w/image.Width)*4,checker=((x/8+y/8)&1)==0?220:120,a=decoded[p+3];
                        sheet[x+f*image.Width,y+l*image.Height]=new Rgba32((byte)((decoded[p]*a+checker*(255-a))/255),(byte)((decoded[p+1]*a+checker*(255-a))/255),(byte)((decoded[p+2]*a+checker*(255-a))/255),255);
                    }
                    reports.Add(new{id,sourcePayloadSha256=image.Sha,level=l,width=w,height=h,format,bytes=encoded.Length,rgbaRmse=Math.Sqrt((double)error/source.Length),alphaCoverageChanges=coverage,approved=false});
                }
            }
            using var file=File.Create(Path.Combine(output,$"texture-{id}.png"));sheet.SaveAsPng(file);
        }
        File.WriteAllText(Path.Combine(output,"review.json"),JsonSerializer.Serialize(new{columns=new[]{"RGBA8","RGB565","RGB5A3","CMPR"},rows="mip level, magnified with nearest sampling; check atlas islands and alpha against native close-ups",policy="Candidates do not change shipping formats or mip safety. Record source-specific review in assets/texture-policy.json.",candidates=reports},new JsonSerializerOptions{WriteIndented=true}));
    }
}
