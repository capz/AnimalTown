namespace AnimalTown;

// Runtime-only clock state; actual movement, not a held run button, starts a streak.
public sealed class RunningMinimap
{
    public const long ShowAfter=6000000,HideAfter=4000000;
    private long runSince=-1,stopSince=-1,last=-1;
    public bool Visible {get;private set;}
    public void Update(long now,bool running,bool eligible)
    {
        if(!eligible||now<last){Visible=false;runSince=stopSince=-1;}
        last=now;if(!eligible)return;
        if(running){
            if(runSince<0)runSince=now;
            stopSince=-1;
            if(now-runSince>ShowAfter)Visible=true;
        }else{
            if(runSince>=0)stopSince=now;
            runSince=-1;
            if(Visible&&stopSince>=0&&now-stopSince>=HideAfter)Visible=false;
        }
    }
}
