using System;
using AnimalTown;
using Dolphin.Memory;
using Dolphin.Timing;

internal sealed partial class TerrainArchive
{
    private sealed class WarmState
    {
        internal readonly TerrainWarmPlan Plan=new TerrainWarmPlan();
        internal int Revision=-1,PendingView=-1,PendingEntry,PendingSection;
        internal bool Cancel;
    }
    private WarmState? warm;
    internal bool WarmPrefetch;
    internal bool WarmActive=>warm!=null;
    internal int WarmQueued {get;private set;}
    internal int WarmLateSelections {get;private set;}
    internal int WarmEvictions {get;private set;}
    internal int WarmCancellations {get;private set;}
    // Cold construction/resume keeps its explicit readiness transition. Once
    // warm, only the shared residency pump may advance native transfers.
    private bool PrepareWarm(float x,float z,CylinderRows rows,bool pump,long deadline,out bool ready)
    {
        ready=Presentable;
        if(warm==null){if(!WarmPrefetch||!Presentable||selecting||CacheSuspended)return false;warm=new WarmState();}
        var work=warm;
        if(!WarmPrefetch){
            if(work.PendingView>=0&&NativeTerrain.CancelBind()!=1){ready=false;return true;}
            for(int i=0;i<views.Length;i++)if(views[i]!.Section>=0&&!visible[views[i]!.Section])Evict(i);
            warm=null;WarmQueued=0;revision=-1;return false;
        }
        selection.Prepare(x,z,rows);
        if(work.Revision!=selection.Revision){
            work.Plan.Prepare(selection,count,x,z);work.Revision=selection.Revision;
            Array.Clear(visible,0,visible.Length);
            for(int i=0;i<selection.Count;i++)visible[selection.Sections[i]]=true;
            bool complete=CurrentBound();if(!complete)WarmLateSelections++;
            if(work.PendingView>=0&&(work.Plan.Wanted[work.PendingSection]==0||!complete&&!visible[work.PendingSection]))work.Cancel=true;
            presentable=complete;Publish();
        }
        if(!pump){ready=Presentable;return true;}
        int steps=0;
        while(steps<8&&Clock.Timestamp<deadline){
            if(work.Cancel){
                steps++;if(NativeTerrain.CancelBind()!=1){Aram.WakeTransfers();break;}
                work.Cancel=false;work.PendingView=-1;WarmCancellations++;
            }
            if(Clock.Timestamp<retryAt)break;
            if(work.PendingView<0){
                int section=-1,entry=-1;
                for(int i=0;i<work.Plan.Count;i++){
                    int candidate=work.Plan.Sections[i];if(bound[candidate])continue;
                    int end=first[candidate]+count[candidate];
                    for(int at=first[candidate];at<end;at++)if(FindEntry(at)<0){section=candidate;entry=at;break;}
                    if(entry>=0)break;bound[candidate]=true;
                }
                if(entry<0)break;
                int slot=FreeWarmView(work.Plan);
                if(slot<0)break; // Speculation yields to the fixed handle budget.
                work.PendingSection=section;work.PendingEntry=entry;work.PendingView=slot;
            }
            int result=NativeTerrain.BindStep(views[work.PendingView]!.Handle,work.PendingEntry);steps++;
            if(result==0){Aram.WakeTransfers();continue;}
            if(result<0){
                bool required=visible[work.PendingSection];work.PendingView=-1;
                // Cache capacity can be reached before the handle bound. Drop
                // speculation first and retry under a later pump's credit.
                int evicted=EvictSpeculation();
                ResidencyError=required?result:0;retryAt=evicted>0?0:Clock.Timestamp+250000;break;
            }
            var view=views[work.PendingView]!;view.Section=work.PendingSection;view.Entry=work.PendingEntry;view.Kind=NativeTerrain.Kind(view.Entry);
            bool complete=true;for(int entry=first[view.Section];entry<first[view.Section]+count[view.Section];entry++)if(FindEntry(entry)<0){complete=false;break;}
            bound[view.Section]=complete;
            work.PendingView=-1;FrameProfile.RecycledSections++;retryAt=0;ResidencyError=0;
        }
        bool nowReady=CurrentBound();if(nowReady){presentable=true;revision=selection.Revision;}
        Publish();WarmQueued=0;for(int i=0;i<work.Plan.Count;i++)if(!bound[work.Plan.Sections[i]])WarmQueued++;
        ready=Presentable;return true;
    }
    private bool CurrentBound(){for(int i=0;i<selection.Count;i++)if(!bound[selection.Sections[i]])return false;return true;}
    private int FindEntry(int entry){for(int i=0;i<views.Length;i++)if(views[i]!.Entry==entry)return i;return -1;}
    private void Evict(int index)
    {
        var view=views[index]!;if(NativeTerrain.Unbind(view.Handle)!=1)throw new InvalidOperationException("Warm terrain unbind failed");
        bound[view.Section]=false;view.Section=view.Entry=view.Kind=-1;WarmEvictions++;
    }
    private int EvictSpeculation()
    {
        int evicted=0;for(int i=0;i<views.Length;i++)if(views[i]!.Section>=0&&!visible[views[i]!.Section]){Evict(i);evicted++;}return evicted;
    }
    private int FreeWarmView(TerrainWarmPlan plan)
    {
        for(int i=0;i<views.Length;i++)if(views[i]!.Section<0)return i;
        for(int priority=0;priority<=2;priority+=2)for(int i=0;i<views.Length;i++){
            int section=views[i]!.Section;if(!visible[section]&&plan.Wanted[section]==priority){Evict(i);return i;}
        }
        return -1;
    }
}
