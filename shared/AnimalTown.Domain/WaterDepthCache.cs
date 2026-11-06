namespace AnimalTown;

// Depth depends on terrain, never the camera or animated UVs. Keep exact float
// values for shared logical corners, including the three-cell sea border.
public sealed class WaterDepthCache
{
    private const int Edge=Town.Width+7;
    private readonly float[] values=new float[Edge*Edge];
    private readonly bool[] valid=new bool[Edge*Edge];
    private Town? town;private int version=-1;
    public int Calculations {get;private set;}
    public void Prepare(Town next)
    {if(town==next&&version==next.TerrainVersion)return;town=next;version=next.TerrainVersion;for(int i=0;i<valid.Length;i++)valid[i]=false;Calculations=0;}
    public float At(int x,int z)
    {
        if(town==null)throw new InvalidOperationException("Prepare water depth first");
        if(x< -3||z< -3||x>Town.Width+3||z>Town.Height+3)return 1;
        int at=(z+3)*Edge+x+3;
        if(!valid[at]){values[at]=WaterSurface.Depth(town,x,z);valid[at]=true;Calculations++;}
        return values[at];
    }
}
