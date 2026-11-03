namespace AnimalTown;

// Bounded, allocation-free GX decoder. Destination allocation is at most 128² RGBA.
// Package v1/v2 base levels; callers release the source after upload. Source atlas
// crops use the same decoder, so no >128 native GPU texture is ever allocated.
public static class TexturePixels
{
    public const int Maximum=128,PolicyVersion=1;
    public static int Int(byte[] b,int at)=>b[at]|b[at+1]<<8|b[at+2]<<16|b[at+3]<<24;
    public static int Width(byte[] b)=>Int(b,12);
    public static int Height(byte[] b)=>Int(b,16);
    public static void Validate(byte[] b)
    {
        if(b.Length<32||Int(b,0)!=0x58544e44||(Int(b,4)!=1&&Int(b,4)!=2))throw new InvalidOperationException("Invalid texture package");
        int w=Width(b),h=Height(b),f=Int(b,8);
        if(w<1||h<1||w>1024||h>1024||(w&(w-1))!=0||(h&(h-1))!=0||
          (f!=0&&f!=1&&f!=2&&f!=3&&f!=4&&f!=5&&f!=6&&f!=14)||Int(b,20)!=b.Length-32||Int(b,28)!=0)
            throw new InvalidOperationException("Unsupported texture dimensions/format");
        int bw=f==0||f==14?8:f==1||f==2?8:4,bh=f==0||f==14?8:4;
        int bytes=(w+bw-1)/bw*((h+bh-1)/bh)*(f==6?64:32);
        if(b.Length-32<bytes)throw new InvalidOperationException("Truncated texture base level");
    }
    public static uint Hash(byte[] b)
    {uint h=2166136261;for(int i=0;i<b.Length;i++)h=(h^b[i])*16777619;return h;}
    public static int FitWidth(int w,int h)=>Math.Max(1,w/Scale(w,h));
    public static int FitHeight(int w,int h)=>Math.Max(1,h/Scale(w,h));
    private static int Scale(int w,int h){int s=1;while(w/s>Maximum||h/s>Maximum)s*=2;return s;}
    public static void Resample(byte[] source,int x,int y,int width,int height,byte[] rgba,int outWidth,int outHeight)
    {
        if(x<0||y<0||width<1||height<1||x+width>Width(source)||y+height>Height(source)||outWidth<1||outHeight<1||outWidth>Maximum||outHeight>Maximum||rgba.Length<outWidth*outHeight*4)
            throw new InvalidOperationException("Texture resample range exceeds policy");
        for(int oy=0;oy<outHeight;oy++)for(int ox=0;ox<outWidth;ox++)
        {
            int left=x+ox*width/outWidth,right=x+(ox+1)*width/outWidth,top=y+oy*height/outHeight,bottom=y+(oy+1)*height/outHeight;
            if(right<=left)right=left+1;if(bottom<=top)bottom=top+1;
            long r=0,g=0,b=0,a=0;int count=0;
            for(int sy=top;sy<bottom;sy++)for(int sx=left;sx<right;sx++){
                uint p=Pixel(source,sx,sy);int alpha=(int)(p&255);a+=alpha;r+=(p>>24)*alpha;g+=((p>>16)&255)*alpha;b+=((p>>8)&255)*alpha;count++;
            }
            int at=(oy*outWidth+ox)*4;rgba[at]=(byte)(a==0?0:r/a);rgba[at+1]=(byte)(a==0?0:g/a);rgba[at+2]=(byte)(a==0?0:b/a);rgba[at+3]=(byte)(a/count);
        }
    }
    private static uint Rgba(int r,int g,int b,int a)=>((uint)r<<24)|((uint)g<<16)|((uint)b<<8)|(uint)a;
    public static byte[] Encode(byte[] rgba,int width,int height,int format)
    {
        if(width<1||height<1||width>Maximum||height>Maximum||rgba.Length<width*height*4||(format!=3&&format!=4&&format!=5&&format!=6))throw new InvalidOperationException("Invalid bounded native texture output");
        var result=new byte[((width+3)/4)*((height+3)/4)*(format==6?64:32)];
        for(int by=0;by<height;by+=4)for(int bx=0;bx<width;bx+=4)for(int dy=0;dy<4;dy++)for(int dx=0;dx<4;dx++){
            int src=(Math.Min(by+dy,height-1)*width+Math.Min(bx+dx,width-1))*4,r=rgba[src],g=rgba[src+1],b=rgba[src+2],a=rgba[src+3];
            int at=((by/4)*((width+3)/4)+bx/4)*(format==6?64:32)+(dy*4+dx)*2;
            if(format==6){result[at]=(byte)a;result[at+1]=(byte)r;result[at+32]=(byte)g;result[at+33]=(byte)b;}
            else if(format==3){result[at]=(byte)a;result[at+1]=(byte)r;}
            else{int value=format==4?((r>>3)<<11)|((g>>2)<<5)|(b>>3):a>=224?0x8000|((r>>3)<<10)|((g>>3)<<5)|(b>>3):((a>>5)<<12)|((r>>4)<<8)|((g>>4)<<4)|(b>>4);result[at]=(byte)(value>>8);result[at+1]=(byte)value;}
        }return result;
    }
    private static uint Rgb565(int p)=>Rgba(((p>>11)&31)*255/31,((p>>5)&63)*255/63,(p&31)*255/31,255);
    public static uint Pixel(byte[] b,int x,int y)
    {
        int w=Width(b),f=Int(b,8),at,p;
        if(f==0){at=32+((y/8)*((w+7)/8)+x/8)*32+y%8*4+x%8/2;p=b[at];int i=((x&1)==0?p>>4:p&15)*17;return Rgba(i,i,i,255);}
        if(f==1||f==2){at=32+((y/4)*((w+7)/8)+x/8)*32+y%4*8+x%8;p=b[at];return f==1?Rgba(p,p,p,255):Rgba((p&15)*17,(p&15)*17,(p&15)*17,(p>>4)*17);}
        if(f==14){
            at=32+((y/8)*((w+7)/8)+x/8)*32+(y%8/4*2+x%8/4)*8;
            int c0=b[at]<<8|b[at+1],c1=b[at+2]<<8|b[at+3],choice=(b[at+4+y%4]>>(6-x%4*2))&3;
            if(choice==0)return Rgb565(c0);if(choice==1)return Rgb565(c1);if(c0<=c1&&choice==3)return 0;
            uint u=Rgb565(c0),v=Rgb565(c1);int a=c0>c1?(choice==2?2:1):1,d=c0>c1?3:2;
            return Rgba((int)(((u>>24)*a+(v>>24)*(d-a))/d),(int)((((u>>16)&255)*a+((v>>16)&255)*(d-a))/d),(int)((((u>>8)&255)*a+((v>>8)&255)*(d-a))/d),255);
        }
        int tile=(y/4)*((w+3)/4)+x/4,index=(y%4)*4+x%4;
        if(f==6){at=32+tile*64+index*2;return Rgba(b[at+1],b[at+32],b[at+33],b[at]);}
        at=32+tile*32+index*2;
        if(f==3)return Rgba(b[at+1],b[at+1],b[at+1],b[at]);
        p=b[at]<<8|b[at+1];if(f==4)return Rgb565(p);
        return (p&0x8000)!=0?Rgba(((p>>10)&31)*255/31,((p>>5)&31)*255/31,(p&31)*255/31,255):Rgba(((p>>8)&15)*17,((p>>4)&15)*17,(p&15)*17,((p>>12)&7)*255/7);
    }
}
