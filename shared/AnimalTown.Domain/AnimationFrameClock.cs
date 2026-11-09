namespace AnimalTown;

// Simulation selects clips and advances time at 30 Hz. The renderer consumes
// the final clip/time once, so catch-up ticks do not reskin intermediate poses.
public sealed class AnimationFrameClock
{
    private readonly int[] clips;private readonly float[] elapsed;
    public AnimationFrameClock(int actors){clips=new int[actors];elapsed=new float[actors];for(int i=0;i<actors;i++)clips[i]=-1;}
    public int Clip(int actor)=>clips[actor];
    public void Reset(int actor){clips[actor]=-1;elapsed[actor]=0;}
    public void Step(int actor,int clip,bool advance)
    {
        if(clips[actor]!=clip){clips[actor]=clip;elapsed[actor]=0;}
        if(advance&&clip>=0)elapsed[actor]+=1f/30;
    }
    public float Consume(int actor)=>Consume(actor,true);
    // Hidden elapsed time is discarded, never accumulated into a costly
    // catch-up pose when a room/camera/doorway makes the actor visible again.
    public float Consume(int actor,bool visible){float seconds=elapsed[actor];elapsed[actor]=0;return visible?seconds:0;}
}
