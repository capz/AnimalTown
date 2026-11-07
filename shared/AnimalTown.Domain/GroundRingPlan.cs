namespace AnimalTown;

// A bounded window over a reusable toroidal vertex allocation. Logical cells
// keep their slot while inside the window; only entering rows/columns change.
public sealed class GroundRingPlan
{
    public const int Columns=64,Rows=48,Ahead=36;
    public const int VertexCount=Columns*Rows*4,IndexCount=Columns*Rows*6;
    public readonly int[] EnteringRows=new int[Rows],EnteringColumns=new int[Columns];
    public int MinX {get;private set;}
    public int MinZ {get;private set;}
    public int RowCount {get;private set;}
    public int ColumnCount {get;private set;}
    public bool Reset {get;private set;}
    private bool ready;
    public static int Wrap(int value,int length){int n=value%length;return n<0?n+length:n;}
    public static int Vertex(int x,int z)=>(Wrap(z,Rows)*Columns+Wrap(x,Columns))*4;
    public void Prepare(float x,float z,bool invalidate=false)
    {
        int nx=(int)Math.Floor(x)-Columns/2,nz=(int)Math.Floor(z)-Ahead;
        Reset=invalidate||!ready||Math.Abs(nx-MinX)>=Columns||Math.Abs(nz-MinZ)>=Rows;
        RowCount=ColumnCount=0;
        for(int row=nz;row<nz+Rows;row++)if(Reset||row<MinZ||row>=MinZ+Rows)EnteringRows[RowCount++]=row;
        for(int col=nx;col<nx+Columns;col++)if(Reset||col<MinX||col>=MinX+Columns)EnteringColumns[ColumnCount++]=col;
        MinX=nx;MinZ=nz;ready=true;
    }
    // Other materials and every height/shore transition retain patch geometry.
    public static bool Uses(Town town,int x,int z,int material,int sharedMaterial)
    {
        int kind=town.At(x,z);
        return sharedMaterial>=0&&material==sharedMaterial&&TerrainElevation.Mode(town,x,z)==0&&kind!=(int)Terrain.Beach&&!WaterTerrain.IsCoast(kind)&&!WaterSurface.IsWater(town,x,z);
    }
}
