namespace AnimalTown;

// Rendering debug options never mutate the calendar or saved town.
public sealed class RenderDebugOptions
{
    public const int DefaultRimPercent=100;
    public int SeasonOverride {get;private set;}=-1;
    public bool ForceAttention;
    public const int MaximumGroundMipDistance=10;
    public bool GroundMipmaps=true;
    public bool ShowGroundMipBands;
    public bool DiscardTerrainCacheWhenIndoors;
    public int GroundMipDistance {get;private set;} // 0 = default GX calibration.
    public static float AttentionHeight(float highestJoint)=>float.IsFinite(highestJoint)?Math.Max(2.35f,Math.Min(5,highestJoint*1.8f+.7f)):3.6f;
    public void CycleSeason()=>SeasonOverride=SeasonOverride==3?-1:SeasonOverride+1;
    public void AdjustGroundMipDistance(int direction)=>GroundMipDistance=Math.Max(0,Math.Min(MaximumGroundMipDistance,GroundMipDistance+(direction<0?-1:1)));
    public void ResetGroundMipDistance()=>GroundMipDistance=0;
    // Reference the mip 0/1 cutoff at the centre of the lower cylinder, N one-metre squares
    // ahead of the player. GX still selects per pixel by its footprint;
    // terrace heights, grazing angles and chord approximation affect the result.
    // UV0 repeats every two metres. Recalculated only after a setting/load change.
    public static float GroundMipBias(int width,int height,int squares)
    {
        // At the unadjusted GX bias the 256px floor already uses half detail
        // at the player. Keep the default transition a few tiles ahead.
        int distance=squares<=0?4:Math.Min(MaximumGroundMipDistance,squares);
        var p=CylinderWorld.Surface(-distance,0);
        float y=p.Y-CylinderWorld.CameraHeight,z=p.Z-CylinderWorld.CameraBack;
        float depth=y*RenderView.ForwardY+z*RenderView.ForwardZ;
        float facing=-(p.UpY*y+p.UpZ*z),focal=240/RenderView.TanY;
        float rho=Math.Max(width*.5f*depth/focal,height*.5f*depth*depth/(focal*facing));
        return Math.Max(-4,Math.Min(3.99f,.5f-(float)Math.Log2(rho)));
    }
    public int EffectiveSeason(int month,int configured=-1)=>TerrainTiles.Season(month,SeasonOverride>=0?SeasonOverride:configured);
}

// Process/session setting only: deliberately absent from DevConfig and town codecs.
public static class UiNoiseSettings
{
    public const int DefaultPercent=10;
}
