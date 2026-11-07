namespace AnimalTown;

public readonly struct RiverBankPoint
{
    public readonly float X,Z,Height;
    public RiverBankPoint(float x,float z,float height){X=x;Z=z;Height=height;}
}

// Banks occupy only the already impassable inland-water footprint. Land stays
// flat and bridges retain their complete approach/deck width. No save IDs change.
public static class RiverBankGeometry
{
    public const float Width=.35f;
    public static int NeighborX(int x,int side)=>x+(side==1?1:side==3?-1:0);
    public static int NeighborZ(int z,int side)=>z+(side==2?1:side==0?-1:0);
    private static bool Land(Town t,int x,int z)=>WaterSurface.IsBank(t,x,z)&&!WaterTerrain.IsCoast(t.At(x,z));
    private static void Ends(int x,int z,int side,out float ax,out float az,out float bx,out float bz)
    {
        ax=x+(side==1||side==2?1:0);az=z+(side>=2?1:0);
        bx=x+(side<=1?1:0);bz=z+(side==1||side==2?1:0);
    }
    public static bool HasEdge(Town t,int x,int z,int side)
    {
        if(t.At(x,z)!=(int)Terrain.Water)return false;
        int nx=NeighborX(x,side),nz=NeighborZ(z,side);
        if(!Land(t,nx,nz))return false;
        // Nonlinear diagonal cliff templates retain their exact wall geometry.
        if(TerrainElevation.Mode(t,x,z)>=TerrainElevation.CornerUpLeft||TerrainElevation.Mode(t,nx,nz)>=TerrainElevation.CornerUpLeft)return false;
        Ends(x,z,side,out float ax,out float az,out float bx,out float bz);
        return TerrainElevation.AtCell(t,nx,nz,ax,az)>TerrainElevation.AtCell(t,x,z,ax,az)+.001f&&TerrainElevation.AtCell(t,nx,nz,bx,bz)>TerrainElevation.AtCell(t,x,z,bx,bz)+.001f;
    }
    // Clockwise top edge and matching waterline edge. Adjacent banks meet along
    // a shared miter rather than overlapping two rectangular slopes.
    public static bool Edge(Town t,int x,int z,int side,out RiverBankPoint a,out RiverBankPoint b,out RiverBankPoint c,out RiverBankPoint d)
    {
        a=b=c=d=default;if(!HasEdge(t,x,z,side))return false;
        Ends(x,z,side,out float ax,out float az,out float bx,out float bz);
        float dx=bx-ax,dz=bz-az;
        float ta=HasEdge(t,x,z,(side+3)%4)?Width:0,tb=HasEdge(t,x,z,(side+1)%4)?Width:0;
        float ix=ax-dz*Width+dx*ta,iz=az+dx*Width+dz*ta,jx=bx-dz*Width-dx*tb,jz=bz+dx*Width-dz*tb;
        int nx=NeighborX(x,side),nz=NeighborZ(z,side);
        a=new RiverBankPoint(ax,az,TerrainElevation.AtCell(t,nx,nz,ax,az));b=new RiverBankPoint(bx,bz,TerrainElevation.AtCell(t,nx,nz,bx,bz));
        c=new RiverBankPoint(jx,jz,TerrainElevation.AtCell(t,x,z,jx,jz));d=new RiverBankPoint(ix,iz,TerrainElevation.AtCell(t,x,z,ix,iz));return true;
    }
    // Three water cells meet a convex land corner. The third cell needs this
    // triangular bevel to connect the two cardinal slopes without a gap.
    public static bool Corner(Town t,int x,int z,int corner,out RiverBankPoint a,out RiverBankPoint b,out RiverBankPoint c)
    {
        a=b=c=default;if(t.At(x,z)!=(int)Terrain.Water)return false;
        int sx=corner==0||corner==3?-1:1,sz=corner<2?-1:1;
        if(t.At(x+sx,z)!=(int)Terrain.Water||t.At(x,z+sz)!=(int)Terrain.Water||!Land(t,x+sx,z+sz))return false;
        if(TerrainElevation.Mode(t,x,z)>=TerrainElevation.CornerUpLeft||TerrainElevation.Mode(t,x+sx,z+sz)>=TerrainElevation.CornerUpLeft)return false;
        float ox=x+(sx==1?1:0),oz=z+(sz==1?1:0),h=TerrainElevation.AtCell(t,x+sx,z+sz,ox,oz);
        float bx=ox-sx*Width,bz=oz,cx=ox,cz=oz-sz*Width;
        if(h<=TerrainElevation.AtCell(t,x,z,bx,bz)+.001f||h<=TerrainElevation.AtCell(t,x,z,cx,cz)+.001f)return false;
        a=new RiverBankPoint(ox,oz,h);b=new RiverBankPoint(bx,bz,TerrainElevation.AtCell(t,x,z,bx,bz));c=new RiverBankPoint(cx,cz,TerrainElevation.AtCell(t,x,z,cx,cz));
        if(sx*sz<0){var swap=b;b=c;c=swap;}return true;
    }
    public static RiverBankPoint Lip(RiverBankPoint top,RiverBankPoint toe)
    {float t=Math.Min(1,TerrainCliffGeometry.LipDepth/(top.Height-toe.Height));return new RiverBankPoint(top.X+(toe.X-top.X)*t,top.Z+(toe.Z-top.Z)*t,top.Height+(toe.Height-top.Height)*t);}
    public static bool Join(Town t,int x,int z,int corner,out RiverBankPoint a,out RiverBankPoint b,out RiverBankPoint c)
    {
        a=b=c=default;int next=corner,previous=(corner+3)%4;
        if(!Edge(t,x,z,next,out a,out var _,out var _,out c)||!Edge(t,x,z,previous,out var _,out b,out var _,out var _))return false;
        return Math.Abs(a.Height-b.Height)>.001f;
    }
    // Transparent foam starts at the toe, on the same water plane as before.
    public static bool Foam(Town t,int x,int z,int side,out RiverBankPoint a,out RiverBankPoint b,out RiverBankPoint c,out RiverBankPoint d)
    {
        a=b=c=d=default;float ax,az,bx,bz,w=WaterSurface.FoamWidth;
        if(Edge(t,x,z,side,out var _,out var _,out var end,out var start)){
            ax=start.X;az=start.Z;bx=end.X;bz=end.Z;
            if(side==1){if(HasEdge(t,x,z,0))az+=w;if(HasEdge(t,x,z,2))bz-=w;}
            if(side==3){if(HasEdge(t,x,z,2))az-=w;if(HasEdge(t,x,z,0))bz+=w;}
        }else{
            if(t.At(x,z)!=(int)Terrain.Water||!WaterSurface.IsBank(t,NeighborX(x,side),NeighborZ(z,side)))return false;
            Ends(x,z,side,out ax,out az,out bx,out bz);
            if(side==1){if(WaterSurface.IsBank(t,x,z-1))az+=w;if(WaterSurface.IsBank(t,x,z+1))bz-=w;}
            if(side==3){if(WaterSurface.IsBank(t,x,z+1))az-=w;if(WaterSurface.IsBank(t,x,z-1))bz+=w;}
        }
        float dx=bx-ax,dz=bz-az,length=(float)Math.Sqrt(dx*dx+dz*dz),nx=-dz/length*w,nz=dx/length*w;
        a=Water(t,x,z,ax,az);b=Water(t,x,z,bx,bz);c=Water(t,x,z,bx+nx,bz+nz);d=Water(t,x,z,ax+nx,az+nz);return true;
    }
    public static RiverBankPoint FoamOuter(RiverBankPoint top,RiverBankPoint toe)
        =>new RiverBankPoint(toe.X+(toe.X-top.X)*WaterSurface.FoamWidth/Width,toe.Z+(toe.Z-top.Z)*WaterSurface.FoamWidth/Width,toe.Height);
    private static RiverBankPoint Water(Town t,int x,int z,float px,float pz)=>new RiverBankPoint(px,pz,TerrainElevation.AtCell(t,x,z,px,pz));
}
