using AnimalTown;
using Dolphin.Graphics;

internal sealed class TerrainChunk
{
    internal int X,Z,Generation,VisibleStamp,WantedStamp,LastUse,Bytes;
    internal readonly FloorGeometry Floor=new FloorGeometry(TerrainRenderer.ChunkSize*TerrainRenderer.ChunkSize*6);
    internal readonly WaterRenderer Water;
    internal readonly TerrainWalls Walls;
    private readonly FloorStrips strips=new FloorStrips(TerrainRenderer.ChunkSize*TerrainRenderer.ChunkSize);
    internal TerrainChunk(WaterMaterial water,WaterDepthCache depth,CylinderRows rows){Water=new WaterRenderer(water,depth,rows,TerrainRenderer.ChunkSize*TerrainRenderer.ChunkSize);Walls=new TerrainWalls(rows);}
    internal void Build(GameState state,TerrainRenderer owner,int x,int z,int generation,bool preview,int previewX,int previewZ,CylinderRows rows)
    {
        strips.PreparePatch(state,owner.Data,x,z,TerrainRenderer.ChunkSize,preview,previewX,previewZ);
        Floor.Build(state,strips,0,rows,owner.SharedFlatMaterial);Water.Prepare(state,strips,0);Walls.Build(state,x,z,TerrainRenderer.ChunkSize,owner,preview,previewX,previewZ);
        X=x;Z=z;Generation=generation;Bytes=Floor.ResidentBytes+Water.ResidentBytes+Walls.ResidentBytes;
    }
    internal void Dispose(){Floor.Dispose();Water.Dispose();Walls.Dispose();}
}
