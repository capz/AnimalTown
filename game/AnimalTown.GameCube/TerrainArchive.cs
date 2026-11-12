using System;
using AnimalTown;
using Dolphin.Graphics;

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
    private bool building,collect,beds;
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
        Array.Clear(bound,0,bound.Length);baked=half=0;building=collect=false;revision=-1;selection.Reset();
    }
    internal void Dispose()
    {
        ReleaseViews();if(NativeTerrain.Reset(0)!=1)throw new InvalidOperationException("Terrain archive disposal failed");
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
    internal void Prepare(float x,float z,CylinderRows rows,bool drawBeds)
    {
        if(!Ready)return;
        // Called after Bake has returned so the scratch arrays are no longer GC
        // roots in the AOT native frame. There are no collections in travel.
        if(collect){AnimalTown.GameCube.NativeTexturePages.Collect();collect=false;}
        selection.Prepare(x,z,rows);bool styleChanged=beds!=drawBeds;
        if(revision==selection.Revision&&!styleChanged)return;revision=selection.Revision;beds=drawBeds;
        Array.Clear(visible,0,visible.Length);Triangles=0;GroundMask=0;
        for(int i=0;i<selection.Count;i++){int section=selection.Sections[i];visible[section]=true;Triangles+=triangles[section]-(drawBeds?0:bedTriangles[section]);}
        for(int i=0;i<views.Length;i++){
            var view=views[i]!;
            if(view.Section<0||!styleChanged&&visible[view.Section])continue;
            if(NativeTerrain.Unbind(view.Handle)!=1)throw new InvalidOperationException("Terrain view unbind failed");
            bound[view.Section]=false;view.Section=view.Entry=view.Kind=-1;
        }
        int free=0;ViewCount=0;
        for(int n=0;n<selection.Count;n++){
            int section=selection.Sections[n];bool retained=bound[section];
            for(int entry=first[section];entry<first[section]+count[section];entry++){
                int kind=NativeTerrain.Kind(entry);if(!drawBeds&&kind==WaterRenderer.BedKind)continue;if(kind<104)GroundMask|=1UL<<(kind%52);ViewCount++;
                if(retained)continue;
                while(free<views.Length&&views[free]!.Section>=0)free++;
                if(free==views.Length)throw new InvalidOperationException("Visible terrain exceeds fixed native view budget");
                var view=views[free++]!;
                int result=NativeTerrain.Bind(view.Handle,entry);if(result!=1)throw new InvalidOperationException("Terrain view bind: "+result+" cache peak "+NativeTerrain.Stat(7));
                view.Section=section;view.Entry=entry;view.Kind=kind;FrameProfile.RecycledSections++;
            }
            bound[section]=true;
        }
        for(int i=0;i<heads.Length;i++)heads[i]=-1;
        for(int i=views.Length-1;i>=0;i--){
            var view=views[i]!;if(view.Section<0)continue;
            int kind=view.Kind>=52&&view.Kind<104?view.Kind-52:view.Kind;
            links[i]=heads[kind];heads[kind]=i;
        }
    }
    internal int First(int kind)=>heads[kind];
    internal int Next(int index)=>links[index];
    internal Mesh Mesh(int index)=>views[index]!;
}
