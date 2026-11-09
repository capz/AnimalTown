using AnimalTown;

internal sealed class CliffShadows
{
    private readonly CliffShadowMap map=new CliffShadowMap();
    private static readonly byte[] empty=new byte[0];
    private Town? town;
    private int version=-1;
    private bool pending;
    internal void Prepare(Town current)
    {
        if(town==current&&version==current.TerrainVersion)return;
        town=current;version=current.TerrainVersion;map.Begin(current);pending=true;
        if(NativeShadows.Terrain(empty,0,0)!=1)throw new System.InvalidOperationException("Cliff shadow reset failed");
    }
    internal bool Advance()
    {
        if(!pending)return false;
        bool complete=map.Advance();
        if(map.ChunkReady&&NativeShadows.Terrain(map.Chunk,map.Offset,1)!=1)throw new System.InvalidOperationException("Cliff shadow tile failed");
        if(complete){
            if(map.HasCoverage&&NativeShadows.Terrain(empty,0,2)!=1)throw new System.InvalidOperationException("Cliff shadow publication failed");
            pending=false;
        }
        return true;
    }
}
