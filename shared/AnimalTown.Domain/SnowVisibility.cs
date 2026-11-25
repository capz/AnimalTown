namespace AnimalTown;

// Hide geometry only beneath uninterrupted, level snow. The halo preserves
// exposed edges, neighbouring dent rims and path cutouts without sampling GX.
public static class SnowVisibility
{
    public const float Height=.20f;
    private const float BuriedTop=.15f;
    public static bool Buried(Town town,float x,float z,float width,float depth,float top)
    {
        if(!float.IsFinite(x)||!float.IsFinite(z)||!float.IsFinite(width)||!float.IsFinite(depth)||
            !float.IsFinite(top)||width<=0||depth<=0||top<0||top>BuriedTop)return false;
        return CoveredGround(town,(int)Math.Floor(x),(int)Math.Floor(z),
            (int)Math.Ceiling(x+width)-(int)Math.Floor(x),(int)Math.Ceiling(z+depth)-(int)Math.Floor(z));
    }
    public static bool CoveredGround(Town town,int left,int top,int width,int height)
    {
        if(!town.Traffic.Winter||width<=0||height<=0||left<1||top<1||
            left>=Town.Width-width||top>=Town.Height-height)return false;
        // Flat ground only: raised blocks/ramps and their vertical silhouettes
        // keep the complete base pass even where their caps receive snow.
        var packed=town.Traffic.Snow!.Packed;
        for(int z=top-1;z<=top+height;z++)for(int x=left-1;x<=left+width;x++){
            int cell=z*Town.Width+x;
            if(town.Tiles[cell]!=(byte)Terrain.Grass||town.Elevation[cell]!=TerrainElevation.Flat||
                town.Paths.At(x,z)!=0||((packed[cell/2]>>((cell%2)*4))&15)!=0)return false;
        }
        return true;
    }
}

// Classify each immutable page once per authoritative coverage change. This
// avoids rescanning a page for every material draw or stationary frame.
public sealed class SnowGroundVisibility
{
    private readonly int[] classified=new int[TerrainArchivePlan.Count];
    private readonly bool[] covered=new bool[TerrainArchivePlan.Count];
    private Town? town;
    private int serial,snowRevision,pathRevision,terrainRevision;
    private bool winter;
    public void Prepare(Town next)
    {
        var snow=next.Traffic.Snow;
        if(snow==null){town=null;return;}
        if(town==next&&snowRevision==snow.Revision&&pathRevision==next.Paths.Revision&&
            terrainRevision==next.TerrainVersion&&winter==snow.Winter)return;
        town=next;snowRevision=snow.Revision;pathRevision=next.Paths.Revision;
        terrainRevision=next.TerrainVersion;winter=snow.Winter;
        if(++serial==int.MaxValue){Array.Clear(classified,0,classified.Length);serial=1;}
    }
    public bool Covered(int section)
    {
        if(town==null||(uint)section>=classified.Length)return false;
        if(classified[section]!=serial){
            classified[section]=serial;
            covered[section]=SnowVisibility.CoveredGround(town,TerrainArchivePlan.X(section),
                TerrainArchivePlan.Z(section),TerrainStream.Width,TerrainArchivePlan.Height);
        }
        return covered[section];
    }
}
