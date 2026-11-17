namespace AnimalTown;

// Shared edge sockets span a whole metre. Only exposed outer corners are
// bevelled, so neighbouring cells, T junctions and crossings meet exactly.
// The grass triangles replace the cut corners; there is no coplanar overlay.
public static class PathShape
{
    public const float Bevel=.22f;
    public static int Corners(Town t,int x,int z)
    {
        if(!TownPaths.Painted(t,x,z)||TerrainElevation.Mode(t,x,z)>=TerrainElevation.RampNorth)return 0;
        bool n=TownPaths.Painted(t,x,z-1),e=TownPaths.Painted(t,x+1,z),s=TownPaths.Painted(t,x,z+1),w=TownPaths.Painted(t,x-1,z);
        return (!n&&!w?1:0)|(!n&&!e?2:0)|(!s&&!e?4:0)|(!s&&!w?8:0);
    }
    // Clockwise convex polygon, no repeated/degenerate vertices.
    public static int Polygon(int corners,float[] x,float[] z)
    {
        int at=0;
        for(int c=0;c<4;c++){
            float cx=c==1||c==2?1:0,cz=c>=2?1:0;
            if((corners&(1<<c))==0){x[at]=cx;z[at++]=cz;continue;}
            x[at]=cx+(c==0?0:c==1?-Bevel:c==2?0:Bevel);z[at++]=cz+(c==0?Bevel:c==1?0:c==2?-Bevel:0);
            x[at]=cx+(c==0?Bevel:c==1?0:c==2?-Bevel:0);z[at++]=cz+(c==0?0:c==1?Bevel:c==2?0:-Bevel);
        }
        return at;
    }
}
