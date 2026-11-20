namespace AnimalTown;

// Rendering debug options never mutate the calendar or saved town.
public sealed class RenderDebugOptions
{
    public const int DefaultRimPercent=100;
    private const int MinutesPerDay=1440,TimeStep=15;
    public int SeasonOverride {get;private set;}=-1;
    public int SeasonSelection {get;private set;}=-1;
    public int TimeOverride {get;private set;}=-1;
    public int TimeSelection {get;private set;}=-1;
    public bool ForceAttention;
    public const int DefaultGroundMipDistance=4,MaximumGroundMipDistance=24;
    public bool GroundMipmaps=true;
    public bool ShowGroundMipBands;
    public bool ShowWorldRim,ShowObstructedEdges,ShowVisitEdges;
    public bool DiscardTerrainCacheWhenIndoors;
    public int GroundMipDistance {get;private set;} // 0 = default radius.
    public static float AttentionHeight(float highestJoint)=>float.IsFinite(highestJoint)?Math.Max(2.35f,Math.Min(5,highestJoint*1.8f+.7f)):3.6f;
    public void SetSeason(int season)=>SeasonSelection=SeasonOverride=season;
    public void AdjustSeason(int direction)
    {
        int index=SeasonSelection+1,step=direction<0?-1:1;
        SeasonSelection=(index+step+5)%5-1;
    }
    public void ApplySeason()=>SeasonOverride=SeasonSelection;
    public void CancelSeasonSelection()=>SeasonSelection=SeasonOverride;
    public void AdjustTime(int direction,int clockMinute)
    {
        int minute=TimeSelection<0?clockMinute:TimeSelection;
        int step=direction>0?TimeStep:minute%TimeStep==0?-TimeStep:0;
        TimeSelection=(minute-minute%TimeStep+step+MinutesPerDay)%MinutesPerDay;
    }
    public void SelectLiveTime()=>TimeSelection=-1;
    public void ApplyTime()=>TimeOverride=TimeSelection;
    public void CancelTimeSelection()=>TimeSelection=TimeOverride;
    public int EffectiveMinute(int clockMinute)=>TimeOverride<0?clockMinute:TimeOverride;
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
