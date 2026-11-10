using System;

// Both logos use the same real-time deadline, including between-frame loading.
internal sealed class SplashTiming
{
    internal const long Fade=650000,Hold=3000000,Duration=Fade*2+Hold;
    private long elapsed;
    internal bool Complete=>elapsed>=Duration;
    internal bool FullyVisible=>elapsed>=Fade&&elapsed<Fade+Hold;
    internal byte Alpha=>elapsed<Fade?(byte)(elapsed*255/Fade):elapsed<Fade+Hold?(byte)255:(byte)Math.Max(0,(Duration-elapsed)*255/Fade);
    internal void Advance(long microseconds)
    {elapsed+=Math.Min(Duration-elapsed,Math.Max(0,microseconds));}
}
