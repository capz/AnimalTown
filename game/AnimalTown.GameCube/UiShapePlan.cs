using System;

// Pure bounded scheduling/lookup policy. GPU ownership stays in UiShapes.
internal sealed class UiShapePlan
{
    internal const int Capacity=64;
    internal readonly float[] Widths=new float[Capacity],Heights=new float[Capacity],Radii=new float[Capacity];
    internal readonly bool[] Bulges=new bool[Capacity];
    private readonly byte[] states=new byte[Capacity]; // 0 free, 1 requested, 2 resident
    private readonly int[] lastUse=new int[Capacity],buckets=new int[128],entryBuckets=new int[Capacity];
    private readonly int[] free=new int[Capacity],pending=new int[Capacity];
    private int freeCount=Capacity,pendingCount,frame,tab=-1;
    private string view="";
    private bool overflow;
    internal int PendingCount=>pendingCount;
    internal bool HasWork=>pendingCount!=0||overflow;
    internal UiShapePlan(){for(int i=0;i<Capacity;i++)free[i]=i;}
    internal void BeginFrame(string nextView,int nextTab,bool enabled)
    {
        frame++;
        if(view!=nextView||tab!=nextTab||!enabled){
            // Requests belong to the view that actually drew them. A closed
            // menu cannot trigger geometry creation on the following frame.
            while(pendingCount>0)Remove(pending[--pendingCount]);
            overflow=false;
        }
        view=nextView;tab=nextTab;
    }
    internal int Request(float w,float h,float radius,bool bulge)
    {
        int hash=unchecked((int)(w*2)*73856093^(int)(h*2)*19349663^(int)(radius*2)*83492791^(bulge?7919:0));
        int empty=-1;
        for(int n=0;n<buckets.Length;n++){
            int bucket=(hash+n)&127,token=buckets[bucket];
            if(token==0){if(empty<0)empty=bucket;break;}
            if(token<0){if(empty<0)empty=bucket;continue;}
            int at=token-1;
            if(Widths[at]!=w||Heights[at]!=h||Radii[at]!=radius||Bulges[at]!=bulge)continue;
            lastUse[at]=frame;return at;
        }
        if(freeCount==0){overflow=true;return -1;}
        if(empty<0)throw new InvalidOperationException("Shape lookup exhausted");
        int slot=free[--freeCount];Widths[slot]=w;Heights[slot]=h;Radii[slot]=radius;Bulges[slot]=bulge;
        states[slot]=1;lastUse[slot]=frame;entryBuckets[slot]=empty;buckets[empty]=slot+1;pending[pendingCount++]=slot;
        return slot;
    }
    internal int TakePending()=>pendingCount==0?-1:pending[--pendingCount];
    internal void Complete(int slot){states[slot]=2;}
    internal bool TakeOverflow(){bool result=overflow;overflow=false;return result;}
    internal int Oldest(bool unusedOnly=false)
    {
        int oldest=-1;
        for(int i=0;i<Capacity;i++)if(states[i]==2&&(!unusedOnly||lastUse[i]<frame-1)&&(oldest<0||lastUse[i]<lastUse[oldest]))oldest=i;
        return oldest;
    }
    internal void Remove(int slot)
    {
        if(states[slot]==0)throw new InvalidOperationException("Shape released twice");
        buckets[entryBuckets[slot]]=-1;states[slot]=0;free[freeCount++]=slot;
    }
}
