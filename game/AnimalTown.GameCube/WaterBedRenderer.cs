using System;
using AnimalTown;
using Dolphin.Graphics;
using Hypercube.GameCube;

// Opaque geometry below the inland surface. Built only with its terrain
// section and archived with it; no per-frame stone objects or mesh uploads.
internal sealed class WaterBedRenderer : IDisposable
{
    internal const int BedPart=108,RocksPart=109;
    private const int BedCapacity=TerrainStream.Width*TerrainStream.Height*54;
    private const int RockCapacity=WaterBedGeometry.MaximumRocksPerSection*WaterBedGeometry.MaximumRockVertices;
    private VertexPositionColorTexture[] bed=new VertexPositionColorTexture[0],rocks=new VertexPositionColorTexture[0];
    private DynamicMeshBuffer? bedMesh,rockMesh;
    private int bedCount,rockCount,originX,originZ,cachedRow=int.MinValue;
    private long revision;
    private Town town=null!;
    private CylinderRows rows=null!;
    private CylinderPose rowA,rowB;
    private float[]? depths;
    private bool[]? depthReady;
    internal Mesh? BedMesh=>bedCount==0?null:bedMesh?.Mesh;
    internal Mesh? RocksMesh=>rockCount==0?null:rockMesh?.Mesh;
    internal int Triangles=>(bedCount+rockCount)/3;

    internal void Begin(GameState state,int left,int top,CylinderRows cache)
    {
        town=state.Town;originX=left;originZ=top;rows=cache;cachedRow=int.MinValue;bedCount=rockCount=0;
        if(depthReady!=null)Array.Clear(depthReady,0,depthReady.Length);
    }
    internal void AppendCell(int x,int z)
    {
        if(!WaterBedGeometry.HasBed(town,x,z))return;
        var a=BedVertex(x,z,x,z);var b=BedVertex(x,z,x+1,z);var c=BedVertex(x,z,x+1,z+1);var d=BedVertex(x,z,x,z+1);
        BedQuad(a,b,c,d);
        for(int side=0;side<4;side++)if(WaterBedGeometry.BankEdge(town,x,z,side,out var start,out var end))Skirt(x,z,start,end,a,b,c,d);
        for(int corner=0;corner<4;corner++)if(WaterBedGeometry.CornerEdge(town,x,z,corner,out var start,out var end))Skirt(x,z,start,end,a,b,c,d);
        if(WaterBedGeometry.TryRock(town,x,z,out var rock))Rock(z,rock);
    }
    internal void Finish()
    {
        revision++;
        if(bedCount>0)bedMesh!.Commit(bedCount,revision,FrameProfile.Counters);
        else if(bedMesh!=null){bedMesh.Dispose();bedMesh=null;bed=new VertexPositionColorTexture[0];}
        if(rockCount>0)rockMesh!.Commit(rockCount,revision,FrameProfile.Counters);
        else if(rockMesh!=null){rockMesh.Dispose();rockMesh=null;rocks=new VertexPositionColorTexture[0];}
    }
    internal void Archive(int section)
    {
        TerrainSection.ArchivePart(section,BedPart,BedMesh,bedCount);
        TerrainSection.ArchivePart(section,RocksPart,RocksMesh,rockCount);
    }
    public void Dispose()
    {
        bedMesh?.Dispose();rockMesh?.Dispose();bedMesh=rockMesh=null;bedCount=rockCount=0;
        bed=rocks=new VertexPositionColorTexture[0];depths=null;depthReady=null;
    }
    private float Depth(int x,int z)
    {
        if(depths==null){depths=new float[(TerrainStream.Width+1)*(TerrainStream.Height+1)];depthReady=new bool[depths.Length];}
        int dx=x-originX,dz=z-originZ;
        if((uint)dx>TerrainStream.Width||(uint)dz>TerrainStream.Height)return WaterBedGeometry.Depth(town,x,z);
        int at=dz*(TerrainStream.Width+1)+dx;
        if(!depthReady![at]){depths[at]=WaterBedGeometry.Depth(town,x,z);depthReady[at]=true;}return depths[at];
    }
    private VertexPositionColorTexture BedVertex(int cx,int cz,int x,int z)
    {
        float depth=Depth(x,z);byte shade=(byte)(255-25*(depth-WaterBedGeometry.MinimumDepth)/(WaterBedGeometry.MaximumDepth-WaterBedGeometry.MinimumDepth));
        return Vertex(cz,new RiverBankPoint(x,z,WaterSurface.HeightAt(town,cx,cz,x,z)-depth),new Color32(shade,shade,shade));
    }
    private void Skirt(int x,int z,RiverBankPoint start,RiverBankPoint end,VertexPositionColorTexture a,VertexPositionColorTexture b,VertexPositionColorTexture c,VertexPositionColorTexture d)
    {
        var tint=new Color32(218,218,218);
        var sa=Vertex(z,start,tint);var sb=Vertex(z,end,tint);
        var da=new VertexPositionColorTexture(BedPosition(a.Position,b.Position,c.Position,d.Position,start.X-x,start.Z-z),tint,sa.TextureCoordinate);
        var db=new VertexPositionColorTexture(BedPosition(a.Position,b.Position,c.Position,d.Position,end.X-x,end.Z-z),tint,sb.TextureCoordinate);
        BedQuad(sa,sb,db,da);
    }
    private static Vector3 BedPosition(Vector3 a,Vector3 b,Vector3 c,Vector3 d,float x,float z)
        =>new Vector3(WaterBedGeometry.Interpolate(a.X,b.X,c.X,d.X,x,z),WaterBedGeometry.Interpolate(a.Y,b.Y,c.Y,d.Y,x,z),WaterBedGeometry.Interpolate(a.Z,b.Z,c.Z,d.Z,x,z));
    private void BedQuad(VertexPositionColorTexture a,VertexPositionColorTexture b,VertexPositionColorTexture c,VertexPositionColorTexture d)
    {
        if(bedCount+6>BedCapacity)throw new InvalidOperationException("Riverbed geometry exceeds section budget");
        if(bedMesh==null)bedMesh=new DynamicMeshBuffer(BedCapacity);
        bed=GeometryStaging.Reserve(bedMesh,bed,bedCount,bedCount+6,FrameProfile.Counters);
        bed[bedCount++]=a;bed[bedCount++]=b;bed[bedCount++]=c;bed[bedCount++]=a;bed[bedCount++]=c;bed[bedCount++]=d;
    }
    private void Rock(int z,WaterBedRock rock)
    {
        if(rockCount+WaterBedGeometry.MaximumRockVertices>RockCapacity)throw new InvalidOperationException("River stones exceed section budget");
        if(rockMesh==null)rockMesh=new DynamicMeshBuffer(RockCapacity);
        rocks=GeometryStaging.Reserve(rockMesh,rocks,rockCount,rockCount+WaterBedGeometry.MaximumRockVertices,FrameProfile.Counters);
        int variation=(int)((rock.Variation>>20)&15);var top=new RiverBankPoint(rock.X,rock.Z,rock.Top);
        for(int side=0;side<WaterBedGeometry.RockSides;side++){
            var a=WaterBedGeometry.RockPoint(rock,side,false);var b=WaterBedGeometry.RockPoint(rock,side+1,false);
            var c=WaterBedGeometry.RockPoint(rock,side+1,true);var d=WaterBedGeometry.RockPoint(rock,side,true);
            float light=.72f+.23f*(float)(.5+.5*Math.Cos(rock.Angle+(side+.5)*6.28318530718/WaterBedGeometry.RockSides-.8));
            var tint=new Color32((byte)((170+variation)*light),(byte)((165+variation)*light),(byte)((147+variation)*light));
            RockTriangle(z,a,b,c,tint);RockTriangle(z,a,c,d,tint);
            var cap=new Color32((byte)(184+variation),(byte)(180+variation),(byte)(164+variation));
            RockTriangle(z,d,c,top,cap);
        }
    }
    private void RockTriangle(int row,RiverBankPoint a,RiverBankPoint b,RiverBankPoint c,Color32 tint)
    {
        int x=(int)Math.Floor(a.X);
        float da=WaterBedGeometry.SubmersionDistance(town,x,row,a),db=WaterBedGeometry.SubmersionDistance(town,x,row,b),dc=WaterBedGeometry.SubmersionDistance(town,x,row,c);
        if(Math.Min(da,Math.Min(db,dc))>=0){RockTriangle(row,x,a,b,c,tint,true);return;}
        if(Math.Max(da,Math.Max(db,dc))<=0){RockTriangle(row,x,a,b,c,tint,false);return;}
        int wet=(da>0?1:0)+(db>0?1:0)+(dc>0?1:0);
        // Rotate without reversing winding so a is the one vertex on its side.
        if(wet==1?db>0:db<=0){var p=a;a=b;b=c;c=p;float t=da;da=db;db=dc;dc=t;}
        else if(wet==1?dc>0:dc<=0){var p=a;a=c;c=b;b=p;float t=da;da=dc;dc=db;db=t;}
        var ab=WaterBedGeometry.WaterlineIntersection(a,b,da,db);var ac=WaterBedGeometry.WaterlineIntersection(a,c,da,dc);
        bool loneWet=wet==1;
        RockTriangle(row,x,a,ab,ac,tint,loneWet);
        RockTriangle(row,x,b,c,ac,tint,!loneWet);RockTriangle(row,x,b,ac,ab,tint,!loneWet);
    }
    private void RockTriangle(int row,int cellX,RiverBankPoint a,RiverBankPoint b,RiverBankPoint c,Color32 tint,bool wet)
    {
        rocks[rockCount++]=RockVertex(row,cellX,a,tint,wet);rocks[rockCount++]=RockVertex(row,cellX,b,tint,wet);rocks[rockCount++]=RockVertex(row,cellX,c,tint,wet);
    }
    private VertexPositionColorTexture RockVertex(int row,int cellX,RiverBankPoint p,Color32 tint,bool wet)
        =>Vertex(row,p,new Color32(tint.R,tint.G,tint.B,WaterBedGeometry.RockAlpha(town,cellX,row,p,wet)));
    private VertexPositionColorTexture Vertex(int row,RiverBankPoint p,Color32 tint)
        =>new VertexPositionColorTexture(Position(row,p.X,p.Z,p.Height),tint,new Vector2(p.X*WaterBedGeometry.UvScale,p.Z*WaterBedGeometry.UvScale));
    private Vector3 Position(int row,float x,float z,float height)
    {
        if(cachedRow!=row){rowA=rows.At(row);rowB=rows.At(row+1);cachedRow=row;}
        float ay=rowA.Y+height*rowA.UpY,az=rowA.Z+height*rowA.UpZ,by=rowB.Y+height*rowB.UpY,bz=rowB.Z+height*rowB.UpZ,t=z-row;
        return new Vector3(x,ay+(by-ay)*t,az+(bz-az)*t);
    }
}
