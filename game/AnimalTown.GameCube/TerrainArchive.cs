using System;
using AnimalTown;
using Dolphin.Graphics;
using Dolphin.Memory;
using Dolphin.Timing;

// Cold-load construction uses one scratch section; the full immutable archive
// lives in ARAM, outside the small managed heap. At runtime the
// fixed mesh pool draws a bounded MEM1 cache of the original bytes.
internal sealed class TerrainArchive
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
    private readonly bool[] visible=new bool[TerrainArchivePlan.Count],bound=new bool[TerrainArchivePlan.Count];
    private readonly int[] first=new int[TerrainArchivePlan.Count],count=new int[TerrainArchivePlan.Count],triangles=new int[TerrainArchivePlan.Count],bedTriangles=new int[TerrainArchivePlan.Count];
    private readonly int[] heads=new int[WaterRenderer.PartKinds],links=new int[Capacity];
    private readonly TerrainArchiveView selection=new TerrainArchiveView();
    private TerrainSection? scratch;
    private int baked,half,revision=-1;
    private bool building,collect,selecting,presentable,suspended,restoreMarkerPending,beds,saveResumePending;
    private int nextSection,nextEntry,freeView;
    private float requestX=float.NaN,requestZ;
    private long retryAt;
    internal int ResidencyError {get;private set;}
    internal bool Presentable=>Ready&&presentable;
    internal bool CacheSuspended=>Ready&&suspended;
    internal bool Ready=>baked==TerrainArchivePlan.Count;
    internal int Remaining=>TerrainArchivePlan.Count-baked;
    internal int Triangles {get;private set;}
    internal ulong GroundMask {get;private set;}
    internal int ViewCount {get;private set;}
    internal int Bytes=>NativeTerrain.Stat(0);
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
        int result=NativeTerrain.Reset(TerrainArchivePlan.Count);if(result!=1)throw new InvalidOperationException("Terrain archive initialization: "+result);
        Array.Clear(bound,0,bound.Length);baked=half=0;building=collect=selecting=presentable=suspended=restoreMarkerPending=beds=saveResumePending=false;revision=-1;selection.Reset();
    }
    internal bool TryDispose()
    {
        if(NativeTerrain.CancelBind()!=1)return false;
        ReleaseViews();if(NativeTerrain.Reset(0)!=1)throw new InvalidOperationException("Terrain archive disposal failed");
        selecting=presentable=suspended=restoreMarkerPending=saveResumePending=false;return true;
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
        Array.Clear(bound,0,bound.Length);Array.Clear(visible,0,visible.Length);
        for(int i=0;i<heads.Length;i++)heads[i]=-1;
        revision=-1;requestX=float.NaN;selecting=presentable=false;selection.Reset();
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
        if(NativeTerrain.Cache(1)!=1)throw new InvalidOperationException("Terrain save cache restore failed");
        saveResumePending=suspended=restoreMarkerPending=false;
        // Normal warm preparation republishes the original selection just as
        // before the card operation; save cleanup adds no idle-debug marker.
    }
    internal void Bake(GameState state,TerrainRenderer owner,FloorStrips staging,byte[] data)
    {
        if(Ready)return;
        if(scratch==null)scratch=owner.CreateSection();
        if(!building){scratch.Begin(state,TerrainArchivePlan.X(baked),TerrainArchivePlan.Z(baked)+half*TerrainStream.Height,owner.Rows);building=true;}
        if(!scratch.Advance(state,owner,staging,data))return;
        scratch.Archive(baked,half==1);building=false;
        if(++half==2){baked++;half=0;}
        if(!Ready)return;
        scratch.Dispose();scratch=null;collect=true;
        for(int i=0;i<first.Length;i++){first[i]=NativeTerrain.Info(i,0);count[i]=NativeTerrain.Info(i,1);triangles[i]=NativeTerrain.Info(i,2);bedTriangles[i]=NativeTerrain.Info(i,3);}
        for(int i=0;i<views.Length;i++)views[i]=new View();
        Dolphin.Diagnostics.DebugOverlay.WriteLine("Terrain archive: "+NativeTerrain.Stat(5)+" ARAM bytes / "+Bytes+" MEM1 bytes / "+NativeTerrain.Stat(1)+" parts");
    }
    internal void Prepare(float x,float z,CylinderRows rows,bool drawBeds=true)
    {
        // Warm travel retains the existing demand-bind behavior. Cold resume
        // uses PrepareAsync, and never enters a synchronous DMA wait here.
        PrepareSelection(x,z,rows,drawBeds,selecting||CacheSuspended,long.MaxValue);
    }
    internal bool PrepareAsync(float x,float z,CylinderRows rows,long deadline,bool drawBeds=true)
        =>PrepareSelection(x,z,rows,drawBeds,true,deadline);
    private bool PrepareSelection(float x,float z,CylinderRows rows,bool drawBeds,bool asynchronous,long deadline)
    {
        if(!Ready)return false;
        if(asynchronous&&Clock.Timestamp>=deadline)return false;
        float bucketX=(float)Math.Floor(x*2)*.5f+.25f,bucketZ=(float)Math.Floor(z*2)*.5f+.25f;
        if(selecting&&(requestX!=bucketX||requestZ!=bucketZ||beds!=drawBeds)){
            if(NativeTerrain.CancelBind()!=1)return false;
            selecting=false;revision=-1;
        }
        if(Clock.Timestamp<retryAt)return false;
        if(CacheSuspended){
            int result=NativeTerrain.Resume();
            if(result!=1){ResidencyError=result;retryAt=Clock.Timestamp+250000;return false;}
            suspended=false;restoreMarkerPending=true;
        }
        // The scratch section is no longer a root when this collection runs.
        // Cold cache suspension/resumption does not request a collection.
        if(collect){AnimalTown.GameCube.NativeTexturePages.Collect();collect=false;}
        if(!selecting){
            selection.Prepare(x,z,rows);bool styleChanged=beds!=drawBeds;
            if(revision==selection.Revision&&presentable&&!styleChanged)return true;
            requestX=bucketX;requestZ=bucketZ;selecting=true;presentable=false;beds=drawBeds;
            // A filtered section may have no aliases to invalidate below.
            if(styleChanged)Array.Clear(bound,0,bound.Length);
            Array.Clear(visible,0,visible.Length);
            for(int i=0;i<selection.Count;i++)visible[selection.Sections[i]]=true;
            for(int i=0;i<views.Length;i++){
                var view=views[i]!;
                if(view.Section<0||!styleChanged&&visible[view.Section])continue;
                if(NativeTerrain.Unbind(view.Handle)!=1)throw new InvalidOperationException("Terrain view unbind failed");
                bound[view.Section]=false;view.Section=view.Entry=view.Kind=-1;
            }
            nextSection=freeView=0;nextEntry=selection.Count==0?0:first[selection.Sections[0]];
        }
        int steps=0;
        while(nextSection<selection.Count){
            int section=selection.Sections[nextSection];
            if(bound[section]||nextEntry==first[section]+count[section]){
                bound[section]=true;nextSection++;
                if(nextSection<selection.Count)nextEntry=first[selection.Sections[nextSection]];
                continue;
            }
            if(asynchronous&&(steps>=8||Clock.Timestamp>=deadline))return false;
            if(!drawBeds&&NativeTerrain.Kind(nextEntry)==WaterRenderer.BedKind){nextEntry++;steps++;continue;}
            while(freeView<views.Length&&views[freeView]!.Section>=0)freeView++;
            if(freeView==views.Length)throw new InvalidOperationException("Visible terrain exceeds fixed native view budget");
            var view=views[freeView]!;
            int result=asynchronous?NativeTerrain.BindStep(view.Handle,nextEntry):NativeTerrain.Bind(view.Handle,nextEntry);
            steps++;
            if(result==0){Aram.WakeTransfers();continue;} // Pending polls share the same eight-step/time credit.
            if(result<0){
                if(!asynchronous)throw new InvalidOperationException("Terrain view bind: "+result+" cache peak "+NativeTerrain.Stat(7));
                ResidencyError=result;retryAt=Clock.Timestamp+250000;return false;
            }
            view.Section=section;view.Entry=nextEntry;view.Kind=NativeTerrain.Kind(nextEntry);
            nextEntry++;freeView++;FrameProfile.RecycledSections++;
        }
        Triangles=ViewCount=0;GroundMask=0;
        for(int i=0;i<selection.Count;i++){
            int section=selection.Sections[i];Triangles+=triangles[section]-(drawBeds?0:bedTriangles[section]);
        }
        for(int i=0;i<heads.Length;i++)heads[i]=-1;
        for(int i=views.Length-1;i>=0;i--){
            var view=views[i]!;if(view.Section<0)continue;
            ViewCount++;if(view.Kind<104)GroundMask|=1UL<<(view.Kind%52);
            int kind=view.Kind>=52&&view.Kind<104?view.Kind-52:view.Kind;
            links[i]=heads[kind];heads[kind]=i;
        }
        revision=selection.Revision;selecting=false;presentable=true;ResidencyError=0;retryAt=0;
        if(restoreMarkerPending){
            restoreMarkerPending=false;
            Dolphin.Diagnostics.DebugOverlay.WriteLine("ANIMALTOWN TERRAIN CACHE READY cache bytes "+NativeTerrain.Stat(8)+
                " archive bytes "+NativeTerrain.Stat(5));
        }
        return true;
    }
    internal int First(int kind)=>heads[kind];
    internal int Next(int index)=>links[index];
    internal Mesh Mesh(int index)=>views[index]!;
}
