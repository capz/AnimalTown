namespace AnimalTown;

// Rendering debug options never mutate the calendar or saved town.
public sealed class RenderDebugOptions
{
    public const int DefaultRimPercent=100,MaximumRimPercent=200,RimStep=5;
    public int SeasonOverride {get;private set;}=-1;
    public bool ForceAttention;
    public int RimPercent {get;private set;}=DefaultRimPercent;
    public const int MaximumGroundMipDistance=10;
    public WaterRenderStyle WaterStyle {get;private set;}=WaterRenderStyle.WindWaker;
    public void CycleWaterStyle()=>WaterStyle=WaterStyle==WaterRenderStyle.WindWaker?WaterRenderStyle.Sunshine:WaterRenderStyle.WindWaker;
    public void ResetWaterStyle()=>WaterStyle=WaterRenderStyle.WindWaker;
    public bool GroundMipmaps=true;
    public bool DiscardTerrainCacheWhenIndoors;
    public int GroundMipDistance {get;private set;} // 0 = automatic GX footprint.
    public static float AttentionHeight(float highestJoint)=>float.IsFinite(highestJoint)?Math.Max(2.35f,Math.Min(5,highestJoint*1.8f+.7f)):3.6f;
    public void CycleSeason()=>SeasonOverride=SeasonOverride==3?-1:SeasonOverride+1;
    public void AdjustRim(int direction)=>RimPercent=Math.Max(0,Math.Min(MaximumRimPercent,RimPercent+(direction<0?-RimStep:RimStep)));
    public void ResetRim()=>RimPercent=DefaultRimPercent;
    public void AdjustGroundMipDistance(int direction)=>GroundMipDistance=Math.Max(0,Math.Min(MaximumGroundMipDistance,GroundMipDistance+(direction<0?-1:1)));
    public void ResetGroundMipDistance()=>GroundMipDistance=0;
    // Reference mip 1 at the centre of the lower cylinder, N one-metre squares
    // ahead of the player. GX still selects/blends per pixel by its footprint;
    // terrace heights, grazing angles and chord approximation affect the result.
    // UV0 repeats every two metres. Recalculated only after a setting/load change.
    public static float GroundMipBias(int width,int height,int squares)
    {
        if(squares<=0)return 0;
        int distance=Math.Min(MaximumGroundMipDistance,squares);
        var p=CylinderWorld.Surface(-distance,0);
        float y=p.Y-CylinderWorld.CameraHeight,z=p.Z-CylinderWorld.CameraBack;
        float depth=y*RenderView.ForwardY+z*RenderView.ForwardZ;
        float facing=-(p.UpY*y+p.UpZ*z),focal=240/RenderView.TanY;
        float rho=Math.Max(width*.5f*depth/focal,height*.5f*depth*depth/(focal*facing));
        return Math.Max(-4,Math.Min(3.99f,1-(float)Math.Log2(rho)));
    }
    public int EffectiveSeason(int month,int configured=-1)=>TerrainTiles.Season(month,SeasonOverride>=0?SeasonOverride:configured);
}

// Process/session setting only: deliberately absent from DevConfig and town codecs.
public static class UiNoiseSettings
{
    public const int DefaultPercent=30,MaximumPercent=300,Step=5;
    public static int Percent {get;private set;}=DefaultPercent;
    public static void Adjust(int direction)=>Percent=Math.Max(0,Math.Min(MaximumPercent,Percent+(direction<0?-Step:Step)));
    public static void Reset()=>Percent=DefaultPercent;
}
