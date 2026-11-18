using System;
using AnimalTown;

// The native union paints this shared mask into the existing coverage maps.
internal sealed class CloudShadows
{
    // Normalized drift, world repeat, mask edge width, peak opacity and cutoff.
    private readonly float[] offset=new float[6];
    private bool sleeping;
    internal void Animate(double seconds,int season,int sunlight)
    {
        bool visible=sunlight>0;
        if(!visible&&sleeping)return;
        sleeping=!visible;
        float period=CloudShadowPattern.Period(season);
        offset[0]=visible?CloudShadowPattern.Offset(seconds,CloudShadowPattern.SpeedX,period):0;
        offset[1]=visible?CloudShadowPattern.Offset(seconds,CloudShadowPattern.SpeedZ,period):0;
        // The shared shader fades every caster equally. Remove cloud coverage
        // once fully dark; wake at the current wind position at sunrise.
        offset[2]=period;offset[3]=CloudShadowPattern.EdgeWidth(season);offset[4]=visible?CloudShadowPattern.Strength(season):0;
        offset[5]=CloudShadowPattern.Cutoff(season);
        if(NativeShadows.Cloud(offset)!=1)throw new InvalidOperationException("Cloud shadow motion failed");
    }
}
