using System;
using AnimalTown;

// The native union paints this shared mask into the existing coverage maps.
internal sealed class CloudShadows
{
    private readonly float[] offset=new float[2];
    internal void Animate(double seconds)
    {
        offset[0]=CloudShadowPattern.Offset(seconds,CloudShadowPattern.SpeedX);
        offset[1]=CloudShadowPattern.Offset(seconds,CloudShadowPattern.SpeedZ);
        if(NativeShadows.Cloud(offset)!=1)throw new InvalidOperationException("Cloud shadow motion failed");
    }
}
