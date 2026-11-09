namespace AnimalTown;

// Renderers and projected receivers share the same support triangles as actors.
public static class GroundSurface
{
    public static float HeightAt(Town town,int cx,int cz,float x,float z)=>TerrainElevation.AtCell(town,cx,cz,x,z);
}
