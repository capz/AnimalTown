using AnimalTown;
using Dolphin.Graphics;
using Hypercube.GameCube;

// Seven clipped receivers feed the shared coverage mask. Only small dirty mask
// pages change while walking; ground geometry remains immutable.
internal sealed class VillagerShadows
{
    private sealed class Entry
    {
        internal bool Active,Ready,Flat;
        internal int Revision;
        internal VillagerShadowPatch Wanted,Built;
        internal readonly float[] Bounds=new float[4],Center=new float[2];
        internal int Count;
    }
    private readonly Entry[] entries=new Entry[BuildingWorld.Actors];
    private readonly GroundShadow staging=new GroundShadow();
    private Town? town;
    private int building=-1,jobRevision,cursor=1;
    private VillagerShadowPatch job;
    internal bool Enabled=true;
    internal VillagerShadows()
    {
        for(int i=0;i<entries.Length;i++)entries[i]=new Entry();
    }
    internal void BeginFrame(Town source)
    {
        if(town!=source){town=source;building=-1;for(int i=0;i<entries.Length;i++){entries[i].Ready=false;entries[i].Revision++;}}
        for(int i=0;i<entries.Length;i++)entries[i].Active=false;
    }
    internal void Set(int actor,int space,float x,float z,int size,bool entryMat)
    {
        var e=entries[actor];var p=new VillagerShadowPatch(town!,space,x,z,size,entryMat);
        if(!e.Wanted.SameRegion(p)||e.Revision==0)e.Revision++;
        e.Wanted=p;e.Active=true;
        e.Center[0]=x;e.Center[1]=z;
    }
    private bool Dirty(int i)=>entries[i].Active&&(!entries[i].Ready||!entries[i].Built.SameRegion(entries[i].Wanted));
    internal bool Advance()
    {
        if(!Enabled||town==null)return false;
        if(building>=0&&(!entries[building].Active||entries[building].Revision!=jobRevision))building=-1;
        if(building<0){
            if(Dirty(0))building=0;
            else for(int n=0;n<entries.Length-1;n++){int i=1+(cursor-1+n)%(entries.Length-1);if(Dirty(i)){building=i;break;}}
            if(building<0)return false;
            var e=entries[building];job=e.Wanted;jobRevision=e.Revision;
            if(job.Space==0)staging.BeginCoverage(town,job.Receiver);else staging.BeginIndoorCoverage(job.Receiver,job.Size,job.EntryMat);
        }
        if(!staging.Advance())return true;
        var target=entries[building];target.Count=staging.Overflow?0:staging.Vertices;
        target.Bounds[0]=job.Left;target.Bounds[1]=job.Top;target.Bounds[2]=job.Left+job.Span;target.Bounds[3]=job.Top+job.Span;
        if(NativeShadows.Mesh(building+ObjectShadows.Capacity,target.Count,4,staging.Uvs,target.Bounds)!=1)throw new System.InvalidOperationException("Invalid villager shadow coverage");
        target.Built=job;target.Flat=staging.FlatCoverage;target.Ready=true;cursor=1+building%(entries.Length-1);building=-1;return true;
    }
    internal void Publish()
    {
        if(town==null)return;
        for(int i=0;i<entries.Length;i++){
            var e=entries[i];bool active=Enabled&&e.Active&&e.Ready&&e.Count>0&&e.Built.Covers(town,e.Wanted,e.Flat);
            NativeShadows.Active(i+ObjectShadows.Capacity,active?1:0,e.Center);
        }
    }
}
