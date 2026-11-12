namespace AnimalTown;

// Stable save IDs: Water remains inland water (rivers and ponds). Coast IDs
// describe a two-metre section with one metre of land and one metre of sea.
public static class WaterTerrain
{
    public const float RiverHeight=-.50f;
    public const int SeaStart=Town.Height-32;
    public static bool IsCoast(int t)=>t>=(int)Terrain.CoastNorth&&t<=(int)Terrain.CoastWest;
    public static bool CoastWater(int t,int x,int z)=>t==(int)Terrain.CoastNorth?(z&1)==0:t==(int)Terrain.CoastEast?(x&1)==1:t==(int)Terrain.CoastSouth?(z&1)==1:t==(int)Terrain.CoastWest&&(x&1)==0;
    public static bool IsSea(Town town,int x,int z){int t=town.At(x,z);return t==(int)Terrain.Sea||CoastWater(t,x,z);}
    // Historical templates have a deliberate southern mouth, not a flood-fill
    // boundary. Relabel water only: never repaint edited dry cells or objects.
    public static void Migrate(Town town)=>Migrate(town,true);
    // A decoded split town publishes one final terrain revision after all
    // migration/validation, just as importing through the combined codec did.
    internal static void Migrate(Town town,bool notify)
    {
        for(int z=SeaStart;z<Town.Height;z++)for(int x=0;x<Town.Width;x++)if(town.At(x,z)==(int)Terrain.Water)town.Tiles[z*Town.Width+x]=(byte)Terrain.Sea;
        if(notify)town.NotifyTerrainChanged();
    }
    public static void GenerateCoast(Town town)
    {
        // Southern sections alternate short soil headlands with broad beaches.
        for(int x=12;x<Town.Width-12;x+=40){
            bool clear=true;for(int dz=0;dz<2;dz++)for(int dx=0;dx<6;dx++)if(town.At(x+dx,SeaStart-2+dz)!=(int)Terrain.Beach)clear=false;
            if(clear)for(int dz=0;dz<2;dz++)for(int dx=0;dx<6;dx++)town.Tiles[(SeaStart-2+dz)*Town.Width+x+dx]=(byte)Terrain.CoastSouth;
        }
    }
}
