namespace AnimalTown;

// Session-only rendering previews: never mutate the calendar or saved town.
public sealed class RenderDebugOptions
{
    public const int DefaultRimPercent=100,MaximumRimPercent=200,RimStep=5;
    public int SeasonOverride {get;private set;}=-1;
    public bool ForceAttention;
    public int RimPercent {get;private set;}=DefaultRimPercent;
    public static float AttentionHeight(float highestJoint)=>float.IsFinite(highestJoint)?Math.Max(2.35f,Math.Min(5,highestJoint*1.8f+.7f)):3.6f;
    public void CycleSeason()=>SeasonOverride=SeasonOverride==3?-1:SeasonOverride+1;
    public void AdjustRim(int direction)=>RimPercent=Math.Max(0,Math.Min(MaximumRimPercent,RimPercent+(direction<0?-RimStep:RimStep)));
    public void ResetRim()=>RimPercent=DefaultRimPercent;
    public int EffectiveSeason(int month,int configured=-1)=>TerrainTiles.Season(month,SeasonOverride>=0?SeasonOverride:configured);
}
