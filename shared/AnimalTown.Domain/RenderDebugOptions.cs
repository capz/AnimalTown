namespace AnimalTown;

// Rendering debug options never mutate the calendar or saved town.
public sealed class RenderDebugOptions
{
    public const int DefaultRimPercent=100;
    public int SeasonOverride {get;private set;}=-1;
    public bool ForceAttention;
    public const int DefaultGroundMipDistance=4,MaximumGroundMipDistance=24;
    public bool GroundMipmaps=true;
    public bool ShowGroundMipBands;
    public bool ShowWorldRim,ShowObstructedEdges,ShowVisitEdges;
    public bool DiscardTerrainCacheWhenIndoors;
    public int GroundMipDistance {get;private set;} // 0 = default radius.
    public static float AttentionHeight(float highestJoint)=>float.IsFinite(highestJoint)?Math.Max(2.35f,Math.Min(5,highestJoint*1.8f+.7f)):3.6f;
    public void CycleSeason()=>SeasonOverride=SeasonOverride==3?-1:SeasonOverride+1;
    public void AdjustGroundMipDistance(int direction)=>GroundMipDistance=Math.Max(0,Math.Min(MaximumGroundMipDistance,GroundMipDistance+(direction<0?-1:1)));
    public void ResetGroundMipDistance()=>GroundMipDistance=0;
    public int GroundMipRadius=>GroundMipDistance==0?DefaultGroundMipDistance:GroundMipDistance;
    public static string GroundMipDistanceLabel(int tiles)=>tiles==0?DefaultGroundMipDistance+" tiles (default)":tiles+(tiles==1?" tile":" tiles");
    public int EffectiveSeason(int month,int configured=-1)=>TerrainTiles.Season(month,SeasonOverride>=0?SeasonOverride:configured);
}

// Process/session setting only: deliberately absent from DevConfig and town codecs.
public static class UiNoiseSettings
{
    public const int DefaultPercent=10;
}
