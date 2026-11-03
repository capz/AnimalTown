namespace AnimalTown;

// Load-time opaque floor policy: default <=256px, GX-native mipmaps, CMPR when its
// measured RGB error is small; RGB565 fallback for high-contrast artwork.
public static class FloorTexture
{
    public const int Maximum=256,MaximumMipLevels=2;
    public static byte[] Prepare(byte[] source,TextureResolution resolution=TexturePolicy.Default)
    {
        TexturePixels.Validate(source);int sw=TexturePixels.Width(source),sh=TexturePixels.Height(source),w=sw,h=sh;
        int limit=TexturePolicy.Edge(resolution);
        while(w>limit||h>limit){w=Math.Max(1,w/2);h=Math.Max(1,h/2);}
        if(w<4||h<4)throw new InvalidOperationException("Floor textures need at least four pixels on each axis");
        var rgba=new byte[w*h*4];TexturePixels.Resample(source,0,0,sw,sh,rgba,w,h);
        for(int i=3;i<rgba.Length;i+=4)if(rgba[i]!=255)throw new InvalidOperationException("Repeating floors must be opaque");
        var cmpr=Compress(rgba,w,h);var test=Package(cmpr,w,h,14,1);long error=0;
        for(int y=0;y<h;y++)for(int x=0;x<w;x++){uint p=TexturePixels.Pixel(test,x,y);int at=(y*w+x)*4;long r=(int)(p>>24)-rgba[at],g=(int)((p>>16)&255)-rgba[at+1],b=(int)((p>>8)&255)-rgba[at+2];error+=r*r+g*g+b*b;}
        int format=cmpr.Length<w*h*2&&error<=w*h*3*256L?14:4;
        int bytes=0,levels=0;
        for(int mw=w,mh=h;mw>=4&&mh>=4&&levels<MaximumMipLevels;mw/=2,mh/=2){bytes+=Size(mw,mh,format);levels++;}
        var payload=new byte[bytes];int write=0;
        for(int level=0,mw=w,mh=h;level<levels;level++,mw/=2,mh/=2){
            var encoded=format==14?(level==0?cmpr:Compress(rgba,mw,mh)):TexturePixels.Encode(rgba,mw,mh,4);Array.Copy(encoded,0,payload,write,encoded.Length);write+=encoded.Length;
            if(level+1<levels){var next=new byte[mw/2*(mh/2)*4];for(int y=0;y<mh/2;y++)for(int x=0;x<mw/2;x++)for(int c=0;c<4;c++){int a=(y*2*mw+x*2)*4+c;next[(y*(mw/2)+x)*4+c]=(byte)((rgba[a]+rgba[a+4]+rgba[a+mw*4]+rgba[a+mw*4+4])/4);}rgba=next;}
        }
        return Package(payload,w,h,format,levels);
    }
    private static int Size(int w,int h,int f)=>f==14?((w+7)/8)*((h+7)/8)*32:((w+3)/4)*((h+3)/4)*32;
    private static byte[] Package(byte[] payload,int w,int h,int f,int levels)
    {
        var bytes=new byte[32+payload.Length];int[] header={0x58544e44,2,f,w,h,payload.Length,levels,0};
        for(int i=0;i<8;i++)for(int b=0;b<4;b++)bytes[i*4+b]=(byte)(header[i]>>(b*8));Array.Copy(payload,0,bytes,32,payload.Length);return bytes;
    }
    private static int Color(byte[] rgba,int at)=>((rgba[at]>>3)<<11)|((rgba[at+1]>>2)<<5)|(rgba[at+2]>>3);
    private static int R(int p)=>((p>>11)&31)*255/31;
    private static int G(int p)=>((p>>5)&63)*255/63;
    private static int B(int p)=>(p&31)*255/31;
    private static int Distance(int r,int g,int b,int x,int y,int z){r-=x;g-=y;b-=z;return r*r+g*g+b*b;}
    private static byte[] Compress(byte[] rgba,int width,int height)
    {
        var bytes=new byte[Size(width,height,14)];var colors=new int[16];var red=new int[4];var green=new int[4];var blue=new int[4];int at=0;
        for(int by=0;by<height;by+=8)for(int bx=0;bx<width;bx+=8)for(int sub=0;sub<4;sub++){
            for(int y=0;y<4;y++)for(int x=0;x<4;x++)colors[y*4+x]=Color(rgba,(Math.Min(height-1,by+sub/2*4+y)*width+Math.Min(width-1,bx+sub%2*4+x))*4);
            int c0=colors[0],c1=c0,best=-1;
            for(int i=0;i<16;i++)for(int j=0;j<i;j++){int d=Distance(R(colors[i]),G(colors[i]),B(colors[i]),R(colors[j]),G(colors[j]),B(colors[j]));if(d>best){best=d;c0=colors[i];c1=colors[j];}}
            if(c0<c1){int swap=c0;c0=c1;c1=swap;}
            red[0]=R(c0);green[0]=G(c0);blue[0]=B(c0);red[1]=R(c1);green[1]=G(c1);blue[1]=B(c1);
            red[2]=(red[0]*2+red[1])/3;green[2]=(green[0]*2+green[1])/3;blue[2]=(blue[0]*2+blue[1])/3;
            red[3]=(red[0]+red[1]*2)/3;green[3]=(green[0]+green[1]*2)/3;blue[3]=(blue[0]+blue[1]*2)/3;
            bytes[at]=(byte)(c0>>8);bytes[at+1]=(byte)c0;bytes[at+2]=(byte)(c1>>8);bytes[at+3]=(byte)c1;
            for(int y=0;y<4;y++){int selectors=0;for(int x=0;x<4;x++){int color=colors[y*4+x],pick=0,min=int.MaxValue;for(int k=0;k<(c0==c1?1:4);k++){int d=Distance(R(color),G(color),B(color),red[k],green[k],blue[k]);if(d<min){min=d;pick=k;}}selectors|=pick<<(6-x*2);}bytes[at+4+y]=(byte)selectors;}at+=8;
        }
        return bytes;
    }
}
