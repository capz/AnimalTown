namespace AnimalTown;

// Derived winter-only coverage. Bit 64 enables native border clipping while
// preserving the terrace bit; 255 marks ground that cannot receive snow.
// Ground material patches and visible traffic paths use the same exclusion.
public sealed class SnowEdges
{
    public const float Width=.25f;
    public readonly byte[] Levels=new byte[Town.Cells],Edges=new byte[Town.Cells];
    public int Revision {get;private set;}
    private Town? town;
    private byte[]? floor;
    private int terrain=-1,paths=-1,opacity=-1;
    private ulong materials;
    public bool Prepare(GameState state,byte[] data,ulong snowMaterials)
    {
        var next=state.Town;
        if(!next.Traffic.Winter)throw new InvalidOperationException("Snow coverage requires winter");
        if(town==next&&floor==data&&terrain==next.TerrainVersion&&paths==next.Paths.Revision&&opacity==next.Traffic.OpacityRevision&&materials==snowMaterials)return false;
        bool changed=town!=next||floor!=data||materials!=snowMaterials;
        town=next;floor=data;terrain=next.TerrainVersion;paths=next.Paths.Revision;opacity=next.Traffic.OpacityRevision;materials=snowMaterials;
        for(int z=0;z<Town.Height;z++)for(int x=0;x<Town.Width;x++){
            int cell=z*Town.Width+x;
            bool snow=SnowWear.Covers(next,x,z)&&(snowMaterials&(1UL<<FloorStrips.BaseAt(state,data,x,z,0)))!=0&&
                !(TownPaths.Painted(next,x,z)&&TileTraffic.OpacityLevel(next.Traffic.Visits[cell])>0);
            byte value=snow?(byte)(next.Elevation[cell]|64):(byte)255;
            if(Levels[cell]!=value){Levels[cell]=value;changed=true;}
        }
        if(!changed)return false;
        for(int z=0;z<Town.Height;z++)for(int x=0;x<Town.Width;x++){
            int cell=z*Town.Width+x,bits=0;
            if((Levels[cell]&~1)==64){
                if(!Covered(x,z-1))bits|=1;if(!Covered(x+1,z))bits|=2;
                if(!Covered(x,z+1))bits|=4;if(!Covered(x-1,z))bits|=8;
            }
            Edges[cell]=(byte)bits;
        }
        Revision++;return true;
    }
    public bool Covered(int x,int z)=>(uint)x<Town.Width&&(uint)z<Town.Height&&(Levels[z*Town.Width+x]&~1)==64;
    // Clockwise quads; adjacent ramps share their miter and support height.
    public static void Quad(int x,int z,int edges,int side,out float ax,out float az,out float bx,out float bz,out float cx,out float cz,out float dx,out float dz)
    {
        float left=x+((edges&8)!=0?Width:0),right=x+1-((edges&2)!=0?Width:0);
        float top=z+((edges&1)!=0?Width:0),bottom=z+1-((edges&4)!=0?Width:0);
        if(side==0){ax=x;az=z;bx=x+1;bz=z;cx=right;cz=top;dx=left;dz=top;}
        else if(side==1){ax=x+1;az=z;bx=x+1;bz=z+1;cx=right;cz=bottom;dx=right;dz=top;}
        else if(side==2){ax=x+1;az=z+1;bx=x;bz=z+1;cx=left;cz=bottom;dx=right;dz=bottom;}
        else{ax=x;az=z+1;bx=x;bz=z;cx=left;cz=top;dx=left;dz=bottom;}
    }
}
