namespace AnimalTown;

// A temporary, player-centered window. Depth outside it uses the saved wear.
internal sealed class SnowTransitions
{
    internal const int Side=49,Unit=16; // Matches the GX snow mask raster.
    private const int FootstepTicks=9,SampleTicks=3;
    private struct Step
    {
        internal int Cell,Start,From;
    }
    private readonly Step[] steps=new Step[64];
    private readonly byte[] row=new byte[Side];
    private int tick,count,lastSample=-SampleTicks,lastWearRevision=-1,lastSnowfallClock=-1;
    private bool hadFootsteps;
    internal readonly byte[] Depths=new byte[Side*Side];
    internal int Origin {get;private set;}=-1;
    internal int Revision {get;private set;}
    internal SnowTransitions(int revision){Revision=revision;}

    internal void Advance()
    {
        tick++;
        for(int i=count-1;i>=0;i--)
            if(tick-steps[i].Start>=FootstepTicks)steps[i]=steps[--count];
    }
    private int Ease(Step step,int target)
    {
        int age=tick-step.Start;
        int fraction=age*age*(3*FootstepTicks-2*age);
        return step.From+(target-step.From)*fraction/(FootstepTicks*FootstepTicks*FootstepTicks);
    }
    private int Find(int cell)
    {
        for(int i=0;i<count;i++)if(steps[i].Cell==cell)return i;
        return -1;
    }
    internal int AtFootstep(int cell,int target)
    {
        int i=Find(cell);
        return i<0?target:Ease(steps[i],target);
    }
    internal void Footstep(int cell,int from)
    {
        int i=Find(cell);
        if(i<0){
            if(count==steps.Length)return;
            i=count++;
        }
        steps[i]=new Step{Cell=cell,Start=tick,From=from};
    }
    internal void Prepare(SnowWear snow,int focusX,int focusZ)
    {
        if(tick-lastSample<SampleTicks)return;
        int left=Math.Clamp(focusX-Side/2,0,Town.Width-Side),top=Math.Clamp(focusZ-Side/2,0,Town.Height-Side);
        int origin=top*Town.Width+left;
        if(origin==Origin&&lastWearRevision==snow.Revision&&lastSnowfallClock==snow.VisualSnowfallClock&&count==0&&!hadFootsteps)return;
        lastSample=tick;lastWearRevision=snow.Revision;
        lastSnowfallClock=snow.VisualSnowfallClock;hadFootsteps=count!=0;
        bool changed=Origin!=origin;
        for(int dy=0;dy<Side;dy++){
            int first=origin+dy*Town.Width;
            for(int x=0;x<Side;x++)row[x]=(byte)snow.VisualWear(first+x);
            for(int i=0;i<count;i++){
                int offset=steps[i].Cell-first;
                if((uint)offset<Side)row[offset]=(byte)Ease(steps[i],row[offset]);
            }
            for(int x=0;x<Side;x++){
                int at=dy*Side+x;
                if(Depths[at]==row[x])continue;
                Depths[at]=row[x];changed=true;
            }
        }
        Origin=origin;
        if(changed)Revision++;
    }
}
