namespace AnimalTown;

// Rendered receiver heights shared by the cached floor and projected overlays.
// Collision keeps TerrainElevation's logical support/water-footprint rules.
public static class GroundSurface
{
    public static float HeightAt(Town town,int cx,int cz,float x,float z)
    {
        float h=TerrainElevation.AtCell(town,cx,cz,x,z);
        if(town.At(cx,cz)==(int)Terrain.Beach){
            bool shore=x==cx&&WaterTerrain.IsSea(town,cx-1,cz)||x==cx+1&&WaterTerrain.IsSea(town,cx+1,cz)||z==cz&&WaterTerrain.IsSea(town,cx,cz-1)||z==cz+1&&WaterTerrain.IsSea(town,cx,cz+1);
            if(shore)h=WaterSurface.Height;
        }
        return h;
    }
}
