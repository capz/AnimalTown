namespace AnimalTown;

// A 6m receiver covers a 2m movement bucket plus overlap. Only its mask moves
// every frame; the same immutable triangles remain until the next bucket.
public readonly struct VillagerShadowPatch
{
    public const float Radius=.55f,OffsetX=.12f,OffsetZ=.1f,UvScale=1/(Radius*2);
    public readonly float X,Z,Height;
    public readonly int Space,Size,Left,Top,Version,Side;
    public readonly bool EntryMat;
    public int Span=>Space==0?6:4;
    public float UOffset=>.5f-(X+OffsetX)*UvScale;
    public float VOffset=>.5f-(Z+OffsetZ)*UvScale;
    public ShadowCaster Receiver=>new ShadowCaster(X,Z,Height,Left,Top,Left+Span,Top+Span);
    public VillagerShadowPatch(Town town,int space,float x,float z,int size=0,bool entryMat=false)
    {
        X=x;Z=z;Space=space;Size=size;EntryMat=entryMat;
        int pad=space==0?2:1;Left=(int)Math.Floor(x/2)*2-pad;Top=(int)Math.Floor(z/2)*2-pad;
        Version=town.TerrainVersion;Height=space==0?TerrainElevation.At(town,x,z):0;
        Side=space==0&&TerrainElevation.Mode(town,(int)x,(int)z)>=6?1+(int)Height:0;
    }
    public bool SameRegion(VillagerShadowPatch p)=>Space==p.Space&&Size==p.Size&&EntryMat==p.EntryMat&&Left==p.Left&&Top==p.Top&&Version==p.Version&&Side==p.Side;
    public bool Covers(Town town,VillagerShadowPatch current)
    {
        if(Space!=current.Space||Size!=current.Size||EntryMat!=current.EntryMat||Version!=current.Version)return false;
        float x=current.X+OffsetX,z=current.Z+OffsetZ;
        float l=x-Radius,r=x+Radius,t=z-Radius,b=z+Radius;
        if(Space!=0){l=Math.Max(0,l);t=Math.Max(0,t);r=Math.Min(Size,r);b=Math.Min(Size,b);}
        return l>=Left&&r<=Left+Span&&t>=Top&&b<=Top+Span&&(Space!=0||GroundShadow.Connected(town,X,Z,Height,current.X,current.Z));
    }
    public static byte[] Texture()
    {
        var data=new byte[32*32*2];
        for(int y=0;y<32;y++)for(int x=0;x<32;x++){
            float u=(x+.5f)/16-1,v=(y+.5f)/16-1;
            float a=Math.Max(0,Math.Min(1,(1-(float)Math.Sqrt(u*u+v*v))/.28f));a=a*a*(3-2*a);
            if(x==0||x==31||y==0||y==31)a=0;
            int at=(((y/4)*8+x/4)*16+y%4*4+x%4)*2;
            data[at]=GroundShadow.Opacity(a);data[at+1]=255;
        }
        return data;
    }
}
