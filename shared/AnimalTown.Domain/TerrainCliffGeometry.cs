namespace AnimalTown;

// Source GLB and live inspection use this same bounded template geometry.
// Clockwise X/Z polygon outlines; exported triangles have outward glTF winding.
public sealed class TerrainCliffGeometry
{
    public const int Up=0,Down=1,Left=2,Right=3,UpLeft=4,UpRight=5,DownLeft=6,DownRight=7;
    public const int Top=0,Soil=1,Lip=2,Low=3;
    public const float Size=2,Height=2,LipDepth=.06f;
    public readonly float[] Positions=new float[108],Normals=new float[108],Uvs=new float[72];
    public readonly int[] Indices=new int[54],Parts=new int[18];
    public int VertexCount,IndexCount;
    public int Triangles=>IndexCount/3;
    private static readonly TerrainCliffGeometry square=Build(-1);
    private static readonly TerrainCliffGeometry[] corners={Build(0),Build(1),Build(2),Build(3)};
    public static TerrainCliffGeometry For(int orientation)
    {if(orientation<0||orientation>7)throw new ArgumentOutOfRangeException("orientation");return orientation<4?square:corners[orientation-4];}
    public static string Name(int orientation)=>orientation==0?"up":orientation==1?"down":orientation==2?"left":orientation==3?"right":orientation==4?"up_left":orientation==5?"up_right":orientation==6?"down_left":"down_right";
    public static bool Raised(int orientation,float x,float z)=>orientation<4|| (orientation==UpLeft?x+z>=2:orientation==UpRight?x<=z:orientation==DownLeft?z<=x:x+z<=2);
    private int Vertex(float x,float y,float z,float nx,float ny,float nz,float u,float v)
    {int i=VertexCount++;Positions[i*3]=x;Positions[i*3+1]=y;Positions[i*3+2]=z;Normals[i*3]=nx;Normals[i*3+1]=ny;Normals[i*3+2]=nz;Uvs[i*2]=u;Uvs[i*2+1]=v;return i;}
    private void Triangle(int part,int a,int b,int c)
    {Parts[IndexCount/3]=part;Indices[IndexCount++]=a;Indices[IndexCount++]=b;Indices[IndexCount++]=c;}
    private void Cap(float[] polygon,float height,int part)
    {int start=VertexCount,n=polygon.Length/2;for(int i=0;i<n;i++)Vertex(polygon[i*2],height,polygon[i*2+1],0,1,0,polygon[i*2]*.5f,polygon[i*2+1]*.5f);for(int i=1;i<n-1;i++)Triangle(part,start,start+i+1,start+i);}
    private void Wall(float ax,float az,float bx,float bz,float high,float low,int part)
    {
        float dx=bx-ax,dz=bz-az,length=(float)Math.Sqrt(dx*dx+dz*dz),nx=dz/length,nz=-dx/length;int start=VertexCount;
        float u=part==Soil?0:ax*.5f,v=part==Soil?(2-LipDepth-high)/(2-LipDepth):az*.5f;
        Vertex(ax,high,az,nx,0,nz,u,v);Vertex(bx,high,bz,nx,0,nz,part==Soil?1:bx*.5f,part==Soil?(2-LipDepth-high)/(2-LipDepth):bz*.5f);
        Vertex(bx,low,bz,nx,0,nz,part==Soil?1:bx*.5f,part==Soil?(2-LipDepth-low)/(2-LipDepth):bz*.5f);Vertex(ax,low,az,nx,0,nz,u,part==Soil?(2-LipDepth-low)/(2-LipDepth):az*.5f);
        Triangle(part,start,start+1,start+2);Triangle(part,start,start+2,start+3);
    }
    private static TerrainCliffGeometry Build(int corner)
    {
        var g=new TerrainCliffGeometry();float[] high,low;
        if(corner<0){high=new float[]{0,0,2,0,2,2,0,2};low=new float[0];}
        else if(corner==0){high=new float[]{2,0,2,2,0,2};low=new float[]{0,0,2,0,0,2};}
        else if(corner==1){high=new float[]{0,0,2,2,0,2};low=new float[]{0,0,2,0,2,2};}
        else if(corner==2){high=new float[]{0,0,2,0,2,2};low=new float[]{0,0,2,2,0,2};}
        else{high=new float[]{0,0,2,0,0,2};low=new float[]{2,0,2,2,0,2};}
        g.Cap(high,2,Top);if(low.Length>0)g.Cap(low,0,Low);
        for(int i=0;i<high.Length/2;i++){int j=(i+1)%(high.Length/2);g.Wall(high[i*2],high[i*2+1],high[j*2],high[j*2+1],2-LipDepth,0,Soil);g.Wall(high[i*2],high[i*2+1],high[j*2],high[j*2+1],2,2-LipDepth,Lip);}
        return g;
    }
}
