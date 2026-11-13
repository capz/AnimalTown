namespace AnimalTown;

// Deterministic code-generated meshes, authorized as the temporary tree set.
// XYZ triangle streams use GX clockwise winding; parts are trunk/dark/mid/light.
public sealed class LowPolyTree
{
    public const int TriangleBudget=96;
    public readonly float[] Positions=new float[TriangleBudget*9];
    public readonly float[] Uvs=new float[TriangleBudget*6];
    public readonly byte[] Parts=new byte[TriangleBudget];
    public int Triangles;
    private readonly struct P
    {
        public readonly float X,Y,Z;
        public P(float x,float y,float z){X=x;Y=y;Z=z;}
    }
    public static LowPolyTree Build(int kind)
    {
        if(kind<0||kind>2)throw new ArgumentOutOfRangeException("kind");
        var t=new LowPolyTree();t.Trunk(kind==1?.83f:.46f,kind==1?.055f:.075f);
        if(kind==0){t.Crown(-.16f,.66f,0,.27f,.25f,.27f);t.Crown(.16f,.70f,.03f,.27f,.25f,.27f);t.Crown(0,.81f,-.04f,.26f,.20f,.26f);}
        else if(kind==2){t.Cone(.22f,.73f,.34f);t.Cone(.43f,.89f,.27f);t.Cone(.65f,1,.19f);}
        else{
            t.Crown(0,.85f,0,.14f,.12f,.14f);
            for(int i=0;i<6;i++){
                float a=i*6.2831853f/6,dx=(float)Math.Cos(a),dz=(float)Math.Sin(a);
                var p0=new P(-dz*.08f,.86f,dx*.08f);var p1=new P(dz*.08f,.86f,-dx*.08f);
                var tip=new P(dx*.62f,.69f,dz*.62f);var ridge=new P(dx*.27f,.99f,dz*.27f);
                var center=new P(dx*.2225f,.85f,dz*.2225f);
                t.Face(p0,p1,tip,center,false);t.Face(p0,ridge,p1,center,false);t.Face(p0,tip,ridge,center,false);t.Face(p1,ridge,tip,center,false);
            }
        }
        return t;
    }
    private void Trunk(float height,float radius)
    {
        for(int i=0;i<6;i++){
            float a=i*6.2831853f/6,b=(i+1)*6.2831853f/6;
            var p=new P((float)Math.Cos(a)*radius,0,(float)Math.Sin(a)*radius);var q=new P((float)Math.Cos(b)*radius,0,(float)Math.Sin(b)*radius);
            var u=new P(p.X,height,p.Z);var v=new P(q.X,height,q.Z);var center=new P(0,height*.5f,0);
            Face(p,u,v,center,true);Face(p,v,q,center,true);Face(new P(0,0,0),p,q,center,true);Face(new P(0,height,0),v,u,center,true);
        }
    }
    private void Cone(float bottom,float top,float radius)
    {
        var center=new P(0,(bottom+top)*.5f,0);
        for(int i=0;i<6;i++){
            float a=i*6.2831853f/6,b=(i+1)*6.2831853f/6;
            var p=new P((float)Math.Cos(a)*radius,bottom,(float)Math.Sin(a)*radius);var q=new P((float)Math.Cos(b)*radius,bottom,(float)Math.Sin(b)*radius);
            Face(p,new P(0,top,0),q,center,false);Face(new P(0,bottom,0),p,q,center,false);
        }
    }
    private void Crown(float x,float y,float z,float sx,float sy,float sz)
    {
        const float a=.5257311f,b=.8506508f;
        float[] p={-a,b,0,a,b,0,-a,-b,0,a,-b,0,0,-a,b,0,a,b,0,-a,-b,0,a,-b,b,0,-a,b,0,a,-b,0,-a,-b,0,a};
        int[] f={0,11,5,0,5,1,0,1,7,0,7,10,0,10,11,1,5,9,5,11,4,11,10,2,10,7,6,7,1,8,3,9,4,3,4,2,3,2,6,3,6,8,3,8,9,4,9,5,2,4,11,6,2,10,8,6,7,9,8,1};
        for(int i=0;i<f.Length;i+=3){int u=f[i]*3,v=f[i+1]*3,w=f[i+2]*3;Face(new P(x+p[u]*sx,y+p[u+1]*sy,z+p[u+2]*sz),new P(x+p[v]*sx,y+p[v+1]*sy,z+p[v+2]*sz),new P(x+p[w]*sx,y+p[w+1]*sy,z+p[w+2]*sz),new P(x,y,z),false);}
    }
    private void Face(P a,P b,P c,P center,bool trunk)
    {
        float nx=(b.Y-a.Y)*(c.Z-a.Z)-(b.Z-a.Z)*(c.Y-a.Y),ny=(b.Z-a.Z)*(c.X-a.X)-(b.X-a.X)*(c.Z-a.Z),nz=(b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
        if(nx*((a.X+b.X+c.X)/3-center.X)+ny*((a.Y+b.Y+c.Y)/3-center.Y)+nz*((a.Z+b.Z+c.Z)/3-center.Z)<0){var swap=b;b=c;c=swap;nx=-nx;ny=-ny;nz=-nz;}
        float shade=nx*.4f+ny*.8f+nz*.2f;
        if(Triangles>=TriangleBudget)throw new InvalidOperationException("Generated tree budget exceeded");
        Parts[Triangles]=(byte)(trunk?0:shade>.01f?3:shade< -.01f?1:2);
        int triangle=Triangles++,at=triangle*9;Put(at,a);Put(at+3,c);Put(at+6,b);
        // Dominant face projection avoids collapsed UVs on vertical trunks
        // and horizontal foliage. Coordinates stay attached to each tree.
        int axis=Math.Abs(nx)>Math.Abs(ny)&&Math.Abs(nx)>Math.Abs(nz)?0:Math.Abs(ny)>Math.Abs(nz)?1:2;
        TexturePoint(triangle*6,a,axis);TexturePoint(triangle*6+2,c,axis);TexturePoint(triangle*6+4,b,axis);
    }
    private void TexturePoint(int at,P p,int axis)
    {Uvs[at]=(axis==0?p.Z:p.X)*2;Uvs[at+1]=(axis==1?p.Z:p.Y)*2;}

    public const int DetailEdge=32,DetailBytes=1440;
    // One shared opaque GX I8 texture with a complete box-filtered mip chain.
    // A fixed toroidal grid supplies soft mottling, plus restrained fine grain.
    // UI fills opt into independent uniform samples instead of the coarse grid.
    // At the 30% UI default, darkening spans 0..29/255 (about 11%).
    public static byte[] DetailTexture(int strengthPercent=100,bool uniform=false)
    {
        if(strengthPercent<0||strengthPercent>300)throw new ArgumentOutOfRangeException("strengthPercent");
        var result=new byte[32+DetailBytes];
        Write(result,0,0x58544e44);Write(result,4,2);Write(result,8,1);
        Write(result,12,DetailEdge);Write(result,16,DetailEdge);Write(result,20,DetailBytes);Write(result,24,6);
        var level=new byte[DetailEdge*DetailEdge];
        int uniformRange=Math.Min(255,(95*strengthPercent+50)/100)+1;
        for(int y=0;y<DetailEdge;y++)for(int x=0;x<DetailEdge;x++){
            if(uniform){
                level[y*DetailEdge+x]=(byte)(255-GrainHash(x+17,y+31)%(uint)uniformRange);
                continue;
            }
            int gx=x/8,gy=y/8,fx=x%8,fy=y%8;
            int top=Grain(gx,gy)*(8-fx)+Grain((gx+1)%4,gy)*fx;
            int bottom=Grain(gx,(gy+1)%4)*(8-fx)+Grain((gx+1)%4,(gy+1)%4)*fx;
            int coarse=(top*(8-fy)+bottom*fy)/64;
            int intensity=208+(coarse*3+Grain(x+17,y+31))/4;
            level[y*DetailEdge+x]=(byte)(255-((255-intensity)*strengthPercent+50)/100);
        }
        int offset=32;
        for(int edge=DetailEdge;edge>=1;edge/=2){
            for(int by=0;by<edge;by+=4)for(int bx=0;bx<edge;bx+=8)for(int y=0;y<4;y++)for(int x=0;x<8;x++)
                result[offset++]=level[Math.Min(edge-1,by+y)*edge+Math.Min(edge-1,bx+x)];
            if(edge==1)break;
            int next=edge/2;var filtered=new byte[next*next];
            for(int y=0;y<next;y++)for(int x=0;x<next;x++){
                int at=y*2*edge+x*2;filtered[y*next+x]=(byte)((level[at]+level[at+1]+level[at+edge]+level[at+edge+1]+2)/4);
            }
            level=filtered;
        }
        return result;
    }
    private static int Grain(int x,int y)
    {return (int)(GrainHash(x,y)%48);}
    private static uint GrainHash(int x,int y)
    {uint v=unchecked((uint)(x*374761393+y*668265263));v=(v^(v>>13))*1274126177u;return v^(v>>16);}
    private static void Write(byte[] b,int at,int n){for(int i=0;i<4;i++)b[at+i]=(byte)(n>>(i*8));}
    private void Put(int at,P p){Positions[at]=p.X;Positions[at+1]=p.Y;Positions[at+2]=p.Z;}
}
