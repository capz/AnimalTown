namespace AnimalTown;

// Render-only geometry: these depths and stones never change saved terrain,
// water elevation, traversal or the bridge deck. Logical coordinates keep the
// result identical when a terrain section is rebuilt or the camera moves.
public static class WaterBedGeometry
{
    public const float MinimumDepth=.16f,MaximumDepth=.66f,UvScale=.5f;
    public const int RockSides=6,RockBaseVertices=RockSides*9,MaximumRockVertices=RockBaseVertices*3;
    public const float CausticFullDepth=.12f,CausticDryDepth=.01f;
    public const int MaximumRocksPerSection=TerrainStream.Width*TerrainStream.Height/4;
    public static bool HasBed(Town town,int x,int z)
        =>town.Inside(x,z)&&(town.At(x,z)==(int)Terrain.Water||town.At(x,z)==(int)Terrain.Bridge);

    // The existing water depth field is shoreline proximity, not physical
    // depth. Convert it into a separate, shallow visual bed profile.
    public static float Depth(Town town,int x,int z)
    {
        float t=WaterSurface.Depth(town,x,z);t=t*t*(3-2*t);
        return MinimumDepth+(MaximumDepth-MinimumDepth)*t;
    }
    public static float CornerHeight(Town town,int cx,int cz,int x,int z)
        =>WaterSurface.HeightAt(town,cx,cz,x,z)-Depth(town,x,z);
    public static float Interpolate(float a,float b,float c,float d,float x,float z)
        =>z<=x?a+(b-a)*x+(c-b)*z:a+(c-d)*x+(d-a)*z;
    // Match the two actual bed triangles, including their diagonal, rather
    // than evaluating a second curved surface under a stone or bank toe.
    public static float HeightAt(Town town,int cx,int cz,float x,float z)
        =>Interpolate(CornerHeight(town,cx,cz,cx,cz),CornerHeight(town,cx,cz,cx+1,cz),
            CornerHeight(town,cx,cz,cx+1,cz+1),CornerHeight(town,cx,cz,cx,cz+1),x-cx,z-cz);

    // The ordered upper edge faces into open water. Its lower edge is sampled
    // on the bed, closing the space below the existing dry bank's toe.
    public static bool BankEdge(Town town,int x,int z,int side,out RiverBankPoint start,out RiverBankPoint end)
    {
        start=end=default;
        if(!HasBed(town,x,z)||(uint)side>=4||!WaterSurface.IsBank(town,RiverBankGeometry.NeighborX(x,side),RiverBankGeometry.NeighborZ(z,side)))return false;
        if(RiverBankGeometry.Edge(town,x,z,side,out var _,out var _,out end,out start))return true;
        float ax=x+(side==1||side==2?1:0),az=z+(side>=2?1:0);
        float bx=x+(side<=1?1:0),bz=z+(side==1||side==2?1:0);
        start=new RiverBankPoint(ax,az,WaterSurface.HeightAt(town,x,z,ax,az));
        end=new RiverBankPoint(bx,bz,WaterSurface.HeightAt(town,x,z,bx,bz));return true;
    }
    public static bool CornerEdge(Town town,int x,int z,int corner,out RiverBankPoint start,out RiverBankPoint end)
    {
        start=end=default;
        return HasBed(town,x,z)&&RiverBankGeometry.Corner(town,x,z,corner,out var _,out end,out start);
    }

    // One candidate per fixed 2x2m block, selected before terrain rejection.
    // This gives a strict per-section bound without camera-dependent caps.
    public static bool TryRock(Town town,int x,int z,out WaterBedRock rock)
    {
        rock=default;if(!town.Inside(x,z)||town.At(x,z)!=(int)Terrain.Water)return false;
        int bx=x&~1,bz=z&~1;uint hash=Hash(town.Seed,bx,bz);
        if((hash&255)>=96)return false;
        int slot=(int)((hash>>8)&3);if(x!=bx+(slot&1)||z!=bz+(slot>>1))return false;
        uint shape=Mix(hash^0x83d2e519u);
        float px=x+.38f+.24f*Unit(shape),pz=z+.38f+.24f*Unit(shape>>8);
        bool emerging=((hash>>10)&3)==0;
        float rx=(emerging?.24f:.16f)+.07f*Unit(shape>>16),rz=rx*(.82f+.18f*Unit(shape>>24));
        float angle=Unit(Mix(shape))*6.28318530718f;
        float surface=WaterSurface.HeightAt(town,x,z,px,pz),bottom=HeightAt(town,x,z,px,pz);
        var footprint=new WaterBedRock(px,pz,rx,rz,angle,bottom,surface,shape);
        for(int i=0;i<RockSides;i++){
            var p=RockPoint(footprint,i,false);
            if(p.X<x+.03f||p.X>x+.97f||p.Z<z+.03f||p.Z>z+.97f||!ClearOfBanks(town,x,z,p.X,p.Z))return false;
            bottom=Math.Min(bottom,HeightAt(town,x,z,p.X,p.Z));
        }
        // Only a shallow bed can support a low stone above the waterline.
        // Keep deeper candidates submerged instead of stretching the same
        // small footprint into a tall post just to reach the surface.
        emerging=emerging&&surface-bottom<=.30f;
        bottom-=.025f; // bury the complete lower ring in the opaque bed
        float top=emerging?surface+.08f+.06f*Unit(shape>>12):Math.Min(surface-.06f,bottom+.10f+.10f*Unit(shape>>12));
        rock=new WaterBedRock(px,pz,rx,rz,angle,bottom,top,shape);return true;
    }
    public static RiverBankPoint RockPoint(WaterBedRock rock,int side,bool upper)
    {
        double angle=rock.Angle+side*(6.283185307179586/RockSides);
        float scale=upper?.78f:1;
        return new RiverBankPoint(rock.X+(float)Math.Cos(angle)*rock.RadiusX*scale,
            rock.Z+(float)Math.Sin(angle)*rock.RadiusZ*scale,
            upper?rock.Bottom+(rock.Top-rock.Bottom)*.58f:rock.Bottom);
    }
    // Physical coverage and the caustic fade have different boundaries. Split
    // once at the water surface so the capture includes even its quiet rim.
    public static float SubmersionDistance(Town town,int cx,int cz,RiverBankPoint p)
        =>WaterSurface.HeightAt(town,cx,cz,p.X,p.Z)-p.Height;
    public static float CausticDistance(Town town,int cx,int cz,RiverBankPoint p)
        =>WaterSurface.HeightAt(town,cx,cz,p.X,p.Z)-CausticDryDepth-p.Height;
    public static byte SubmersionMask(Town town,int cx,int cz,RiverBankPoint p)
        =>(byte)(255*Math.Max(0,Math.Min(1,CausticDistance(town,cx,cz,p)/(CausticFullDepth-CausticDryDepth))));
    // Boundary vertices are duplicated by the physical split. Their wet-side
    // copy reserves alpha 1 for capture; dry triangles remain exactly zero.
    public static byte RockAlpha(Town town,int cx,int cz,RiverBankPoint p,bool wet)
        =>wet?(byte)Math.Max(1,(int)SubmersionMask(town,cx,cz,p)):(byte)0;
    public static RiverBankPoint WaterlineIntersection(RiverBankPoint a,RiverBankPoint b,float da,float db)
    {
        float t=da/(da-db);
        return new RiverBankPoint(a.X+(b.X-a.X)*t,a.Z+(b.Z-a.Z)*t,a.Height+(b.Height-a.Height)*t);
    }
    public static int RockVertexCount(Town town,int cx,int cz,WaterBedRock rock)
    {
        int count=0;var top=new RiverBankPoint(rock.X,rock.Z,rock.Top);
        for(int side=0;side<RockSides;side++){
            var a=RockPoint(rock,side,false);var b=RockPoint(rock,side+1,false);
            var c=RockPoint(rock,side+1,true);var d=RockPoint(rock,side,true);
            count+=TriangleVertices(a,b,c)+TriangleVertices(a,c,d)+TriangleVertices(d,c,top);
        }
        return count;
        int TriangleVertices(RiverBankPoint a,RiverBankPoint b,RiverBankPoint c){
            float da=SubmersionDistance(town,cx,cz,a),db=SubmersionDistance(town,cx,cz,b),dc=SubmersionDistance(town,cx,cz,c);
            return Math.Min(da,Math.Min(db,dc))>=0||Math.Max(da,Math.Max(db,dc))<=0?3:9;
        }
    }
    private static bool ClearOfBanks(Town town,int x,int z,float px,float pz)
    {
        for(int side=0;side<4;side++)if(BankEdge(town,x,z,side,out var a,out var b)&&Side(a,b,px,pz)<.015f)return false;
        for(int corner=0;corner<4;corner++)if(CornerEdge(town,x,z,corner,out var a,out var b)&&Side(a,b,px,pz)<.015f)return false;
        return true;
    }
    private static float Side(RiverBankPoint a,RiverBankPoint b,float x,float z)
    {
        float dx=b.X-a.X,dz=b.Z-a.Z;
        return (dx*(z-a.Z)-dz*(x-a.X))/(float)Math.Sqrt(dx*dx+dz*dz);
    }
    private static float Unit(uint value)=>(value&255)/255f;
    private static uint Hash(int seed,int x,int z)=>Mix(unchecked((uint)seed^(uint)x*0x9e3779b9u^(uint)z*0x85ebca6bu));
    private static uint Mix(uint n){n^=n>>16;n*=0x7feb352du;n^=n>>15;n*=0x846ca68bu;return n^(n>>16);}
}

public readonly struct WaterBedRock
{
    public readonly float X,Z,RadiusX,RadiusZ,Angle,Bottom,Top;
    public readonly uint Variation;
    public WaterBedRock(float x,float z,float rx,float rz,float angle,float bottom,float top,uint variation)
    {X=x;Z=z;RadiusX=rx;RadiusZ=rz;Angle=angle;Bottom=bottom;Top=top;Variation=variation;}
}
