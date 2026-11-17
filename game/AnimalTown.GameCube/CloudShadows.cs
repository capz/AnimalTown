using System;
using AnimalTown;

// The native union paints this shared mask into the existing coverage maps.
internal sealed class CloudShadows
{
    // Normalized drift, world repeat, mask edge width and peak coverage.
    private readonly float[] offset=new float[5];
    internal void Animate(double seconds,int season)
    {
        float period=CloudShadowPattern.Period(season);
        offset[0]=CloudShadowPattern.Offset(seconds,CloudShadowPattern.SpeedX,period);
        offset[1]=CloudShadowPattern.Offset(seconds,CloudShadowPattern.SpeedZ,period);
        offset[2]=period;offset[3]=CloudShadowPattern.EdgeWidth(season);offset[4]=CloudShadowPattern.Strength(season);
        if(NativeShadows.Cloud(offset)!=1)throw new InvalidOperationException("Cloud shadow motion failed");
    }
}
