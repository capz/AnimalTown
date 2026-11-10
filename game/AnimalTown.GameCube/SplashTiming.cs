using System;

// Presentation time, not wall time: a slow disc read must not skip a logo/fade.
internal sealed class SplashTiming
{
    internal const long Fade=650000,Hold=1200000,Duration=Fade*2+Hold;
    private long elapsed;
    internal bool Complete=>elapsed>=Duration;
    internal bool FullyVisible=>elapsed>=Fade&&elapsed<Fade+Hold;
    internal byte Alpha=>elapsed<Fade?(byte)(elapsed*255/Fade):elapsed<Fade+Hold?(byte)255:(byte)Math.Max(0,(Duration-elapsed)*255/Fade);
    internal void Advance(long microseconds)
    {elapsed=Math.Min(Duration,elapsed+Math.Max(0,Math.Min(50000,microseconds)));}
}
