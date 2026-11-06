namespace AnimalTown;

// Small, static proxy projections. These are ground/contact shadows, not a
// realtime shadow map: receivers must connect to the caster's support surface.
public readonly struct ShadowCaster
{
    public readonly float X,Z,Scale,Height,Left,Right,Top,Bottom;
    public readonly int Profile;
    public ShadowCaster(float x,float z,float scale,int profile,float height)
    {
        X=x;Z=z;Scale=scale;Profile=profile;Height=height;
        float cx=x+scale*.24f,cz=z-scale*.2f;
        float rx=scale*(profile==3?.77f:.62f),rz=scale*(profile==3?.72f:.66f);
        Left=cx-rx;Right=cx+rx;Top=cz-rz;Bottom=cz+rz;
    }
}
public sealed class GroundShadow
{
    public const int MaximumTriangles=96;
    public readonly float[] Positions=new float[MaximumTriangles*9],Uvs=new float[MaximumTriangles*6];
    public int Vertices {get;private set;}
    public bool Overflow {get;private set;}
    private struct P
    {
        public float X,Z,Y,Depth;
        public P(float x,float z,float y,float depth){X=x;Z=z;Y=y;Depth=depth;}
    }
    private readonly P[] a=new P[12],b=new P[12];
    private Town town=null!;
    private ShadowCaster caster;
    private int row;
    public void Begin(Town source,ShadowCaster value){town=source;caster=value;row=(int)Math.Floor(value.Top);Vertices=0;Overflow=false;}
    // One fine row per job; callers own the shared frame work deadline.
    public bool Advance()
    {
        if(row>(int)Math.Floor(caster.Bottom))return true;
        int z=row++;
        for(int x=(int)Math.Floor(caster.Left);x<=Math.Floor(caster.Right);x++){
            int terrain=town.At(x,z);
            if(!town.Inside(x,z)||WaterSurface.IsWater(town,x,z)||WaterTerrain.IsCoast(terrain)||terrain==(int)Terrain.Building||terrain==(int)Terrain.Cliff)continue;
            int mode=TerrainElevation.Mode(town,x,z);
            for(int half=0;half<2;half++){
                Triangle(x,z,half,mode>=6?0:-1);
                if(mode>=6)Triangle(x,z,half,1);
            }
        }
        return false;
    }
    private P Point(int cx,int cz,float x,float z,int level)
    {
        float h=level<0?TerrainElevation.AtCell(town,cx,cz,x,z):level*2;
        var p=CylinderWorld.Surface(z,0,h+.012f);
        return new P(x,z,p.Y,p.Z);
    }
    private void Triangle(int x,int z,int half,int level)
    {
        a[0]=Point(x,z,x,z,level);
        a[1]=Point(x,z,x+1,half==0?z:z+1,level);
        a[2]=Point(x,z,half==0?x+1:x,z+1,level);
        int n=3;
        n=Clip(n,1,0,-caster.Left);n=Clip(n,-1,0,caster.Right);
        n=Clip(n,0,1,-caster.Top);n=Clip(n,0,-1,caster.Bottom);
        int mode=TerrainElevation.Mode(town,x,z);
        if(level>=0){
            float sx=mode==6||mode==8?1:-1,sz=mode==6||mode==7?1:-1;
            float c=mode==6?-2:mode==9?2:0;c-=sx*(x/2*2)+sz*(z/2*2);
            float sign=level==1?1:-1;n=Clip(n,sx*sign,sz*sign,c*sign);
        }
        if(n<3)return;
        float mx=0,mz=0;for(int i=0;i<n;i++){mx+=a[i].X;mz+=a[i].Z;}mx/=n;mz/=n;
        if(!Supported(mx,mz))return;
        for(int i=1;i<n-1;i++){
            float area=(a[i].X-a[0].X)*(a[i+1].Z-a[0].Z)-(a[i].Z-a[0].Z)*(a[i+1].X-a[0].X);
            if(area<.000001f)continue;
            if(Vertices+3>MaximumTriangles*3){Overflow=true;Vertices=0;return;}
            Emit(a[0]);Emit(a[i]);Emit(a[i+1]);
        }
    }
    private bool Supported(float x,float z)
    {
        float dx=x-caster.X,dz=z-caster.Z;int steps=1+(int)(Math.Max(Math.Abs(dx),Math.Abs(dz))/.1f);
        float previous=caster.Height;
        for(int i=1;i<=steps;i++){
            float px=caster.X+dx*i/steps,pz=caster.Z+dz*i/steps,h=TerrainElevation.At(town,px,pz);
            if(WaterSurface.IsWater(town,(int)px,(int)pz)||Math.Abs(h-previous)>.16f)return false;
            previous=h;
        }
        return true;
    }
    private int Clip(int count,float x,float z,float c)
    {
        if(count==0)return 0;int used=0;P p=a[count-1];float d=p.X*x+p.Z*z+c;
        for(int i=0;i<count;i++){
            P q=a[i];float e=q.X*x+q.Z*z+c;
            if((d>=0)!=(e>=0)){float t=d/(d-e);b[used++]=new P(p.X+(q.X-p.X)*t,p.Z+(q.Z-p.Z)*t,p.Y+(q.Y-p.Y)*t,p.Depth+(q.Depth-p.Depth)*t);}
            if(e>=0)b[used++]=q;p=q;d=e;
        }
        for(int i=0;i<used;i++)a[i]=b[i];return used;
    }
    private void Emit(P p)
    {
        int at=Vertices++;Positions[at*3]=p.X;Positions[at*3+1]=p.Y;Positions[at*3+2]=p.Depth;
        // Two-pixel transparent gutters isolate the four atlas profiles.
        Uvs[at*2]=((caster.Profile%2)*64+2+60*(p.X-caster.Left)/(caster.Right-caster.Left))/128;
        Uvs[at*2+1]=((caster.Profile/2)*64+2+60*(p.Z-caster.Top)/(caster.Bottom-caster.Top))/128;
    }
    public static byte[] Atlas()
    {
        var pixels=new byte[128*128*2];
        for(int y=0;y<128;y++)for(int x=0;x<128;x++){
            int kind=x/64+(y/64)*2;float u=(x%64-31.5f)/30,v=(y%64-31.5f)/30,d=2;
            if(kind==0){d=Math.Min(Ellipse(u+.27f,v+.06f,.63f,.72f),Math.Min(Ellipse(u-.24f,v-.06f,.62f,.72f),Ellipse(u,v+.28f,.65f,.6f)));}
            if(kind==1){d=Ellipse(u,v,.34f,.34f);for(int k=0;k<6;k++){float angle=k*6.2831853f/6,cs=(float)Math.Cos(angle),sn=(float)Math.Sin(angle);d=Math.Min(d,Ellipse(u*cs+v*sn-.35f,-u*sn+v*cs,.57f,.16f));}}
            if(kind==2)d=Math.Max(Math.Abs(u)/(.26f+.55f*(v+1)*.5f),Math.Abs(v)/.9f);
            if(kind==3)d=Math.Max(Math.Abs(u)/(.73f-.1f*v),Math.Abs(v)/.79f);
            float alpha=Math.Max(0,Math.Min(1,(1-d)/.22f));alpha=alpha*alpha*(3-2*alpha);
            if(x%64<2||x%64>61||y%64<2||y%64>61)alpha=0;
            int at=(((y/4)*32+x/4)*16+(y%4)*4+x%4)*2;
            pixels[at]=(byte)(alpha*255);pixels[at+1]=255; // GX IA8: alpha, intensity.
        }
        return pixels;
    }
    private static float Ellipse(float x,float y,float rx,float ry)=>(float)Math.Sqrt(x*x/(rx*rx)+y*y/(ry*ry));
}
