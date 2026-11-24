using System;
using AnimalTown;
using Dolphin.Graphics;
using Dolphin.Memory;
using Dolphin.Timing;

// Cold-load construction uses one scratch section; the full immutable archive
// lives in ARAM, outside the small managed heap. At runtime the
// fixed mesh pool draws a bounded MEM1 cache of the original bytes.
internal sealed partial class TerrainArchive
{
    private sealed class View : Mesh
    {
        internal readonly int Handle;
        internal int Section=-1,Entry=-1,Kind=-1;
        internal View():this(NativeTerrain.View()){}
        private View(int handle):base(handle){if(handle==0)throw new InvalidOperationException("Terrain view allocation failed");Handle=handle;}
    }
    private const int Capacity=256;
    private readonly View?[] views=new View?[Capacity];
    private readonly bool[] visible=new bool[TerrainArchivePlan.Count],bound=new bool[TerrainArchivePlan.Count],drawable=new bool[TerrainArchivePlan.Count];
    private readonly int[] first=new int[TerrainArchivePlan.Count],count=new int[TerrainArchivePlan.Count],triangles=new int[TerrainArchivePlan.Count];
    private readonly int[] heads=new int[WaterRenderer.PartKinds],links=new int[Capacity];
    private readonly TerrainArchiveView selection=new TerrainArchiveView();
    private readonly TerrainArchiveView drawSelection=new TerrainArchiveView();
    private readonly float[] prefetchPriority=new float[TerrainStream.Capacity];
    private TerrainSection? scratch;
    private int baked,half,revision=-1,drawRevision=-1;
    private bool building,collect,selecting,presentable,suspended,restoreMarkerPending,saveResumePending,prefetchBlocked,pendingRequired;
    private bool resetPending,suppressRestoreMarker;
    internal VisualMemoryPressure? Memory;
    internal const int AllocationGuardBytes=8*1024,CachePeakBytes=1024*1024+AllocationGuardBytes,ResetPeakBytes=CachePeakBytes;
    // Six metres cover a two-metre selection bucket plus several frames of
    // travel. Only these prefetched pages are retained, never drawn offscreen.
    internal const float PrefetchGuard=6,PrefetchBucket=2;
    private const int PrefetchViewBudget=128;
    private int pendingView=-1,pendingEntry=-1,pendingSection=-1;
    private float focusX,focusZ,leadX,leadZ;
    private long retryAt;
    internal int ResidencyError {get;private set;}
    internal bool Presentable=>Ready&&presentable;
    internal bool CacheSuspended=>Ready&&suspended;
    internal bool Ready=>baked==TerrainArchivePlan.Count;
    internal bool PrefetchComplete=>Ready&&!selecting;
    internal int Remaining=>TerrainArchivePlan.Count-baked;
    internal int Triangles {get;private set;}
    internal ulong GroundMask {get;private set;}
    internal int ViewCount {get;private set;}
    internal int Bytes=>NativeTerrain.Stat(0);
    internal bool HasPaths(Town town)
    {
        for(int i=0;i<selection.Count;i++){int section=selection.Sections[i];if(visible[section]&&town.Paths.Any(TerrainArchivePlan.X(section),TerrainArchivePlan.Z(section),TerrainStream.Width,TerrainArchivePlan.Height))return true;}
        return false;
    }
    private void ReleaseViews()
    {
        // Drop every view before freeing its parent pages. Archive reset rejects
        // live aliases; resource release also holds pages until GX has retired.
        for(int i=0;i<views.Length;i++){views[i]?.Dispose();views[i]=null;}
        scratch?.Dispose();scratch=null;
    }
    internal void Reset()
    {
        ReleaseViews();
        Dolphin.Diagnostics.DebugOverlay.WriteLine("ANIMALTOWN TERRAIN RESET BEGIN");
        Array.Clear(bound,0,bound.Length);baked=half=0;building=collect=selecting=presentable=suspended=restoreMarkerPending=saveResumePending=prefetchBlocked=pendingRequired=false;revision=drawRevision=-1;pendingView=pendingEntry=pendingSection=-1;selection.Reset();drawSelection.Reset();
        retryAt=0;ResidencyError=0;suppressRestoreMarker=false;WarmLateSelections=WarmEvictions=WarmCancellations=0;resetPending=true;TryInitialize();
    }
    private bool TryInitialize()
    {
        if(!resetPending)return true;
        long now=Clock.Timestamp;if(now<retryAt)return false;
        // Native reset reuses a warm cache and borrows its staging area. Only
        // genuinely new bytes compete with other owners' outstanding tickets.
        int peak=NativeTerrain.Stat(8)>0?AllocationGuardBytes:ResetPeakBytes;
        var memory=Memory;int ticket=0;
        if(memory!=null&&!memory.TryReserve(peak,now,true,out ticket)){ResidencyError=-2;return false;}
        try{
            int result=NativeTerrain.Reset(TerrainArchivePlan.Count);
            if(result!=1){
                if(result!=-2)throw new InvalidOperationException("Terrain archive initialization: "+result);
                ResidencyError=result;retryAt=now+250000;memory?.AllocationFailed(peak,now);return false;
            }
            memory?.Complete(ticket);ticket=0;resetPending=false;ResidencyError=0;retryAt=0;
            Dolphin.Diagnostics.DebugOverlay.WriteLine("ANIMALTOWN TERRAIN RESET READY");return true;
        }finally{if(ticket!=0)memory!.Cancel(ticket);}
    }
    internal bool TryReset()
    {
        if(NativeTerrain.CancelBind()!=1)return false;
        Reset();return true;
    }
    internal bool TryDispose()
    {
        if(NativeTerrain.CancelBind()!=1)return false;
        ReleaseViews();if(NativeTerrain.Reset(0)!=1)throw new InvalidOperationException("Terrain archive disposal failed");
        selecting=presentable=suspended=restoreMarkerPending=saveResumePending=resetPending=false;return true;
    }
    internal void Dispose()
    {if(!TryDispose())throw new InvalidOperationException("Drain terrain residency before disposal");}
    internal bool Suspend()
    {
        if(!Ready||CacheSuspended)return true;
        if(NativeTerrain.CancelBind()!=1)return false;
        return SuspendCache(true);
    }
    private bool SuspendCache(bool diagnostic)
    {
        for(int i=0;i<views.Length;i++){
            var view=views[i]!;if(view.Section<0)continue;
            if(NativeTerrain.Unbind(view.Handle)!=1)throw new InvalidOperationException("Terrain view unbind failed");
            view.Section=view.Entry=view.Kind=-1;
        }
        Array.Clear(bound,0,bound.Length);Array.Clear(visible,0,visible.Length);Array.Clear(drawable,0,drawable.Length);
        for(int i=0;i<heads.Length;i++)heads[i]=-1;
        revision=drawRevision=-1;pendingView=pendingEntry=pendingSection=-1;selecting=presentable=prefetchBlocked=false;selection.Reset();drawSelection.Reset();
        ViewCount=Triangles=0;GroundMask=0;retryAt=0;ResidencyError=0;
        int before=NativeTerrain.Stat(0);
        int result=NativeTerrain.Suspend();if(result<0)throw new InvalidOperationException("Terrain cache suspension failed: "+result);
        if(result==1){
            suspended=true;restoreMarkerPending=false;
            if(diagnostic)Dolphin.Diagnostics.DebugOverlay.WriteLine("ANIMALTOWN TERRAIN CACHE SUSPENDED cache bytes "+NativeTerrain.Stat(8)+
                " reclaimed bytes "+(before-NativeTerrain.Stat(0))+" archive bytes "+NativeTerrain.Stat(5));
        }
        return result==1;
    }
    internal bool SuspendForSave()
    {
        // Explicit card I/O is already blocking. Drain this single owned DMA
        // chunk before releasing RAM for the card transaction. A debug-cold
        // cache stays cold after saving instead of being spuriously restored.
        if(!Ready||CacheSuspended)return false;
        int result;while((result=NativeTerrain.CancelBind())==0){}
        if(result<0)throw new InvalidOperationException("Terrain save DMA drainage failed");
        saveResumePending=SuspendCache(false);return saveResumePending;
    }
    internal void ResumeAfterSave()
    {
        if(!saveResumePending)return;
        // The ordinary bounded restore owns memory admission too. Allocating
        // here would bypass pending model/building tickets after card buffers
        // have changed total headroom. Preserve the save-only diagnostic policy.
        saveResumePending=false;suppressRestoreMarker=true;
    }
    // False means no stage could run (for example denied cache admission).
    // An unfinished fine stage still did useful work and returns true.
    internal bool Bake(GameState state,TerrainRenderer owner,FloorStrips staging,byte[] data)
    {
        if(!TryInitialize()||Ready)return false;
        if(scratch==null)scratch=owner.CreateSection();
        if(!building){scratch.Begin(state,TerrainArchivePlan.X(baked),TerrainArchivePlan.Z(baked)+half*TerrainStream.Height,owner.Rows);building=true;}
        if(!scratch.Advance(state,owner,staging,data))return true;
        if(baked==0&&half==0)Dolphin.Diagnostics.DebugOverlay.WriteLine("ANIMALTOWN TERRAIN APPEND BEGIN");
        scratch.Archive(baked,half==1);building=false;
        if(baked==0&&half==0)Dolphin.Diagnostics.DebugOverlay.WriteLine("ANIMALTOWN TERRAIN APPEND READY");
        if(++half==2){
            baked++;half=0;
            if((baked&127)==0&&!Ready)Dolphin.Diagnostics.DebugOverlay.WriteLine("ANIMALTOWN TERRAIN BAKE PROGRESS sections "+baked+" / "+TerrainArchivePlan.Count);
        }
        if(!Ready)return true;
        scratch.Dispose();scratch=null;collect=true;
        for(int i=0;i<first.Length;i++){first[i]=NativeTerrain.Info(i,0);count[i]=NativeTerrain.Info(i,1);triangles[i]=NativeTerrain.Info(i,2);}
        for(int i=0;i<views.Length;i++)views[i]=new View();
        Dolphin.Diagnostics.DebugOverlay.WriteLine("Terrain archive: "+NativeTerrain.Stat(5)+" ARAM bytes / "+Bytes+" MEM1 bytes / "+NativeTerrain.Stat(1)+" parts");
        Dolphin.Diagnostics.DebugOverlay.WriteLine("ANIMALTOWN TERRAIN BAKE READY sections "+baked);
        return true;
    }
    internal void Prepare(float x,float z,CylinderRows rows)
    {
        // Startup, ordinary travel and cold resume all use the same bounded
        // DMA pump. No draw preparation can busy-wait for ARAM completion.
        PrepareSelection(x,z,rows,Clock.Timestamp+500);
    }
    internal bool PrepareAsync(float x,float z,CylinderRows rows,long deadline)
        =>PrepareSelection(x,z,rows,deadline);
    private bool PrepareSelection(float x,float z,CylinderRows rows,long deadline)
    {
        if(!Ready)return false;
        if(Clock.Timestamp>=deadline)return false;
        if(Clock.Timestamp<retryAt)return false;
        if(CacheSuspended){
            var memory=Memory;int ticket=0;
            if(memory!=null&&!memory.TryReserve(CachePeakBytes,Clock.Timestamp,true,out ticket)){ResidencyError=-2;return false;}
            try{
                int result=NativeTerrain.Resume();
                if(result!=1){ResidencyError=result;retryAt=Clock.Timestamp+250000;memory?.AllocationFailed(CachePeakBytes,Clock.Timestamp);return false;}
                memory?.Complete(ticket);ticket=0;
            }finally{if(ticket!=0)memory!.Cancel(ticket);}
            suspended=false;restoreMarkerPending=!suppressRestoreMarker;suppressRestoreMarker=false;
        }
        // The scratch section is no longer a root when this collection runs.
        // Cold cache suspension/resumption does not request a collection.
        if(collect){AnimalTown.GameCube.NativeTexturePages.Collect();collect=false;}
        float dx=x-focusX,dz=z-focusZ,length=(float)Math.Sqrt(dx*dx+dz*dz);
        if(length>.00001f){leadX=length<=8?dx/length:0;leadZ=length<=8?dz/length:0;}
        drawSelection.Prepare(x,z,rows);
        focusX=x;focusZ=z;
        float bucketX=(float)Math.Floor(x/PrefetchBucket)*PrefetchBucket+PrefetchBucket*.5f;
        float bucketZ=(float)Math.Floor(z/PrefetchBucket)*PrefetchBucket+PrefetchBucket*.5f;
        selection.Prepare(bucketX,bucketZ,rows,PrefetchGuard);
        bool changed=revision!=selection.Revision||drawRevision!=drawSelection.Revision;
        if(changed){
            Array.Clear(visible,0,visible.Length);
            Array.Clear(drawable,0,drawable.Length);
            int requiredParts=0;
            for(int i=0;i<drawSelection.Count;i++){
                int section=drawSelection.Sections[i];visible[section]=drawable[section]=true;requiredParts+=count[section];
            }
            PrioritizePrefetch();
            int allowance=Math.Min(PrefetchViewBudget,Math.Max(0,Capacity-requiredParts));
            for(int i=0;i<selection.Count;i++){
                int section=selection.Sections[i];if(drawable[section]||count[section]>allowance)continue;
                visible[section]=true;allowance-=count[section];
            }
            for(int i=0;i<views.Length;i++){
                var view=views[i]!;
                if(view.Section<0||visible[view.Section])continue;
                if(NativeTerrain.Unbind(view.Handle)!=1)throw new InvalidOperationException("Terrain view unbind failed");
                bound[view.Section]=false;view.Section=view.Entry=view.Kind=-1;WarmEvictions++;
            }
            revision=selection.Revision;drawRevision=drawSelection.Revision;selecting=true;prefetchBlocked=false;
            if(pendingView>=0)pendingRequired=drawable[pendingSection];
        }
        if(!changed&&!selecting&&pendingView<0)return presentable;
        bool rebound=false;
        for(int steps=0;steps<8&&Clock.Timestamp<deadline;steps++){
            if(pendingView<0){
                pendingRequired=NextMissing(drawSelection,false,out pendingSection,out pendingEntry);
                if(!pendingRequired&&(prefetchBlocked||!NextMissing(selection,true,out pendingSection,out pendingEntry))){selecting=false;break;}
                for(int i=0;i<views.Length;i++)if(views[i]!.Section<0){pendingView=i;break;}
                if(pendingView<0){
                    if(pendingRequired&&EvictPrefetched()){steps--;continue;}
                    if(!pendingRequired){selecting=false;break;}
                    throw new InvalidOperationException("Visible terrain exceeds fixed native view budget");
                }
            }
            var view=views[pendingView]!;
            int result=NativeTerrain.BindStep(view.Handle,pendingEntry);
            if(result==0){Aram.WakeTransfers();continue;}
            if(result<0){
                pendingView=pendingEntry=pendingSection=-1;
                if(result==-2){
                    if(pendingRequired&&EvictPrefetched())continue;
                    if(!pendingRequired){prefetchBlocked=true;selecting=false;break;}
                }
                PublishDraw();ResidencyError=result;retryAt=Clock.Timestamp+250000;return presentable;
            }
            // Finish an owned transfer even if the camera reverses. Cancelling
            // on every half-metre bucket could starve transfers indefinitely.
            view.Section=pendingSection;view.Entry=pendingEntry;view.Kind=NativeTerrain.Kind(pendingEntry);
            if(!visible[pendingSection]){
                if(NativeTerrain.Unbind(view.Handle)!=1)throw new InvalidOperationException("Obsolete terrain prefetch unbind failed");
                bound[pendingSection]=false;view.Section=view.Entry=view.Kind=-1;
            }
            pendingView=pendingEntry=pendingSection=-1;rebound=true;FrameProfile.RecycledSections++;
        }
        if(changed||rebound||!presentable)PublishDraw();
        return presentable;
    }
    private bool NextMissing(TerrainArchiveView requested,bool speculative,out int section,out int entry)
    {
        int live=0,extra=0;
        if(speculative)for(int i=0;i<views.Length;i++)if(views[i]!.Section>=0){live++;if(!drawable[views[i]!.Section])extra++;}
        for(int i=0;i<requested.Count;i++){
            section=requested.Sections[i];if(bound[section]||!visible[section])continue;
            if(speculative&&!drawable[section]){
                int resident=0;for(int v=0;v<views.Length;v++)if(views[v]!.Section==section)resident++;
                int missing=count[section]-resident;
                if(live+missing>Capacity||extra+missing>PrefetchViewBudget)continue;
            }
            int end=first[section]+count[section];
            for(entry=first[section];entry<end;entry++){
                bool found=false;
                for(int v=0;v<views.Length;v++)if(views[v]!.Entry==entry){found=true;break;}
                if(!found)return true;
            }
            bound[section]=true;
        }
        section=entry=-1;return false;
    }
    private void PrioritizePrefetch()
    {
        // Fill the nearest border of the drawable set first, rather than
        // spending the fixed speculative budget on ground behind the camera.
        for(int i=0;i<selection.Count;i++){
            int section=selection.Sections[i];float nearest=float.MaxValue;
            float x=TerrainArchivePlan.X(section),z=TerrainArchivePlan.Z(section);
            for(int n=0;n<drawSelection.Count;n++){
                int current=drawSelection.Sections[n];float dx=x-TerrainArchivePlan.X(current),dz=z-TerrainArchivePlan.Z(current);
                // The first entering border gets the earliest DMA slot. Keep
                // the all-direction guard for immediate running reversals.
                nearest=Math.Min(nearest,dx*dx+dz*dz-(dx*leadX+dz*leadZ)*TerrainStream.Width*2);
            }
            float fx=x+TerrainStream.Width*.5f-focusX,fz=z+TerrainArchivePlan.Height*.5f-focusZ;
            prefetchPriority[i]=nearest+(fx*fx+fz*fz)*.001f;
        }
        for(int i=1;i<selection.Count;i++){
            int section=selection.Sections[i],at=i;float priority=prefetchPriority[i];
            while(at>0&&prefetchPriority[at-1]>priority){selection.Sections[at]=selection.Sections[at-1];prefetchPriority[at]=prefetchPriority[at-1];at--;}
            selection.Sections[at]=section;prefetchPriority[at]=priority;
        }
    }
    private bool EvictPrefetched()
    {
        int section=-1;float farthest=-1;
        for(int i=0;i<views.Length;i++){
            int candidate=views[i]!.Section;if(candidate<0||drawable[candidate])continue;
            float dx=TerrainArchivePlan.X(candidate)+TerrainStream.Width*.5f-focusX,dz=TerrainArchivePlan.Z(candidate)+TerrainArchivePlan.Height*.5f-focusZ;
            float distance=dx*dx+dz*dz;if(distance>farthest){section=candidate;farthest=distance;}
        }
        if(section<0)return false;
        for(int i=0;i<views.Length;i++){
            var view=views[i]!;if(view.Section!=section)continue;
            if(NativeTerrain.Unbind(view.Handle)!=1)throw new InvalidOperationException("Terrain prefetch eviction failed");
            view.Section=view.Entry=view.Kind=-1;WarmEvictions++;
        }
        bound[section]=false;return true;
    }
    private void PublishDraw()
    {
        // Partial or speculative sections never appear in the GX draw chains.
        // Normally travel is already covered by the previous guard. Teleports
        // and transfer failure deliberately retain the existing loading path.
        bool previouslyPresentable=presentable;presentable=true;
        for(int i=0;i<drawSelection.Count;i++){
            int section=drawSelection.Sections[i];if(bound[section])continue;
            int end=first[section]+count[section];
            for(int entry=first[section];entry<end;entry++){
                bool found=false;for(int v=0;v<views.Length;v++)if(views[v]!.Entry==entry){found=true;break;}
                if(!found){presentable=false;break;}
            }
            if(!presentable)break;bound[section]=true;
        }
        if(!presentable){if(previouslyPresentable)WarmLateSelections++;return;}
        Triangles=ViewCount=0;GroundMask=0;
        for(int i=0;i<drawSelection.Count;i++){
            int section=drawSelection.Sections[i];Triangles+=triangles[section];
        }
        for(int i=0;i<heads.Length;i++)heads[i]=-1;
        for(int i=views.Length-1;i>=0;i--){
            var view=views[i]!;if(view.Section<0||!drawable[view.Section])continue;
            ViewCount++;if(view.Kind<104)GroundMask|=1UL<<(view.Kind%52);
            int kind=view.Kind>=52&&view.Kind<104?view.Kind-52:view.Kind;
            links[i]=heads[kind];heads[kind]=i;
        }
        ResidencyError=0;retryAt=0;
        if(restoreMarkerPending){
            restoreMarkerPending=false;
            Dolphin.Diagnostics.DebugOverlay.WriteLine("ANIMALTOWN TERRAIN CACHE READY cache bytes "+NativeTerrain.Stat(8)+
                " archive bytes "+NativeTerrain.Stat(5));
        }
    }
    internal void GroundBounds(ulong materials,out int minZ,out int maxZ)
    {
        minZ=Town.Height;maxZ=0;
        materials&=GroundMask;
        for(int slot=0;slot<FloorStrips.MaximumMaterials;slot++){
            if((materials&(1UL<<slot))==0)continue;
            for(int index=heads[slot];index>=0;index=links[index]){
                var view=views[index]!;
                if(view.Kind>=FloorStrips.MaximumMaterials)continue;
                int x=TerrainArchivePlan.X(view.Section);
                if(x>=Town.Width||x+TerrainStream.Width<=0)continue;
                int z=TerrainArchivePlan.Z(view.Section);
                minZ=Math.Min(minZ,z);maxZ=Math.Max(maxZ,z+TerrainArchivePlan.Height);
            }
        }
    }
    internal int First(int kind)=>heads[kind];
    internal int Next(int index)=>links[index];
    internal Mesh Mesh(int index)=>views[index]!;
    internal int Section(int index)=>views[index]!.Section;
    internal bool IsGroundStrip(int index)=>views[index]!.Kind<FloorStrips.MaximumMaterials;
}
