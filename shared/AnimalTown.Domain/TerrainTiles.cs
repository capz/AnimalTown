namespace AnimalTown;

// Render materials are independent from terrain collision/save IDs.
public static class TerrainTiles
{
    public const int SurfaceCount=13;
    public const int Grass=0,Water=1,Wood=2,Sand=3,Rock=4,Pavement=5,Dirt=6,Floor=7,Building=8;
    public const int Terracotta=9,StonePavement=10,SandPatch=11,Pond=12;
    public const int GroundRows=68;
    // Enclose the one-metre cell, then keep another full cell beyond each
    // viewport plane. GX clips the retained triangles at the actual viewport.
    public const float GroundCellRadius=.8f,ViewportPadding=1f;
    public const float GroundVisibilityRadius=GroundCellRadius+ViewportPadding;
    // Conservative 4:3 frustum envelope plus an offscreen margin; avoid sending
    // a full 128-wide strip to GX for the narrow rows nearest the camera.
    public static int RowHalfWidth(int row)=>Math.Max(8,(GroundRows-row)*3/4+6);
    public static int GroundVertexCount {get{int count=0;for(int row=0;row<GroundRows;row++)count+=RowHalfWidth(row)*12;return count;}}
    public static int Surface(int terrain)=>terrain==0?Grass:terrain==1?Water:terrain==2?Wood:terrain==3?Sand:terrain==4?Rock:terrain==5?Pavement:terrain==7?Dirt:Building;
    // Month is the existing monotonic year*12 + zero-based month save/RTC value.
    public static int Season(int month,int forced=-1)
    { if(forced>=0&&forced<4)return forced;int m=((month%12)+12)%12;return m==11||m<2?3:m<5?0:m<8?1:2; }
    public static string SeasonName(int season)=>season==0?"spring":season==1?"summer":season==2?"fall":"winter";
    public static int Variant(int x,int z,int surface,int count)
    { if(count<1)return 0;uint h=(uint)x*73856093u^(uint)z*19349663u^(uint)surface*83492791u;h^=h>>16;return (int)(h%(uint)count); }
    // Piecewise coordinates preserve fixed-size corners and stretch the center.
    public static float PatchCoordinate(float position,float length,float border)
    { if(position<=border)return position/border;if(position>=length-border)return 2+(position-(length-border))/border;return 1+(position-border)/(length-border*2); }
}
