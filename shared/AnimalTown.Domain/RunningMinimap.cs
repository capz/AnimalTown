namespace AnimalTown;

// Runtime-only clock state; actual movement, not a held run button, starts a streak.
public sealed class RunningMinimap
{
    public const long ShowAfter=2_000_000,HideAfter=4_000_000;
    private long runSince=-1,stopSince=-1,last=-1;
    private bool show;
    private HudFade fade;
    public byte Opacity=>fade.Opacity;
    public void Update(long now,bool running,bool eligible)
    {
        if(!eligible||now<last){show=false;fade=default;runSince=stopSince=-1;}
        last=now;if(!eligible)return;
        if(running){
            if(runSince<0)runSince=now;
            stopSince=-1;
            if(now-runSince>=ShowAfter)show=true;
        }else{
            if(runSince>=0)stopSince=now;
            runSince=-1;
            if(stopSince>=0&&now-stopSince>=HideAfter)show=false;
        }
        fade.Update(now,show);
    }
}

public struct HudFade
{
    public const long Duration=250_000;
    private long last,amount;
    private bool showing;
    public readonly byte Opacity=>(byte)((amount*255+Duration/2)/Duration);

    public void Update(long now,bool show)
    {
        if(now<last)amount=0;
        else{
            long elapsed=System.Math.Min(now-last,Duration);
            amount=System.Math.Clamp(amount+(showing?elapsed:-elapsed),0,Duration);
        }
        last=now;
        showing=show;
    }
}
