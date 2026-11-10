namespace AnimalTown;

// Full camera envelope, including ocean beyond the playable town. Each baked
// page retains its 8x8m boundary so every ground draw fits one shadow page.
public static class TerrainArchivePlan
{
    public const int Height=TerrainStream.Height*2;
    private const int Pad=(int)TerrainStream.Prefetch;
    public const int MinX=-((CylinderWorld.ViewHalfWidth+Pad+TerrainStream.Width-1)/TerrainStream.Width)*TerrainStream.Width;
    public const int MaxX=(Town.Width+CylinderWorld.ViewHalfWidth+Pad)/TerrainStream.Width*TerrainStream.Width;
    public const int MinZ=-((TerrainTiles.GroundRowsAhead+Pad+Height-1)/Height)*Height;
    public const int MaxZ=(Town.Height+TerrainTiles.GroundRowsBehind+Pad)/Height*Height;
    public const int Columns=(MaxX-MinX)/TerrainStream.Width+1,Rows=(MaxZ-MinZ)/Height+1,Count=Columns*Rows;
    public static int Index(int x,int z)
    {
        if(x<MinX||x>MaxX||z<MinZ||z>MaxZ||x%TerrainStream.Width!=0||z%Height!=0)throw new InvalidOperationException("Terrain escaped its baked envelope");
        return (z-MinZ)/Height*Columns+(x-MinX)/TerrainStream.Width;
    }
    public static int X(int index)=>MinX+index%Columns*TerrainStream.Width;
    public static int Z(int index)=>MinZ+index/Columns*Height;
}
