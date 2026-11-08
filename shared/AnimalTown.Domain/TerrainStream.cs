namespace AnimalTown;

// Bounded virtual terrain cache. Movement requests small immutable sections,
// never a rebuild of the visible patch. Each request is only thirty-two cells;
// the renderer pumps requests under its per-frame time and section limits.
public sealed class TerrainStream
{
    public const int Width=8,Height=4,Capacity=256;
    public const float Prefetch=12;
    private const float HorizonRadius=3;
    private const int MinX=-(CylinderWorld.ViewHalfWidth+(int)Prefetch)/Width-1,MinZ=-(TerrainTiles.GroundRowsAhead+(int)Prefetch)/Height-1;
    private const int Columns=(Town.Width+2*CylinderWorld.ViewHalfWidth+(int)Prefetch*2)/Width+5;
    private const int Rows=(Town.Height+TerrainTiles.GroundRowsAhead+TerrainTiles.GroundRowsBehind+(int)Prefetch*2)/Height+5;
    private readonly int[] lookup=new int[Columns*Rows];
    private readonly bool[] ready=new bool[Capacity],wanted=new bool[Capacity],started=new bool[Capacity];
    private readonly int[] candidateX=new int[Capacity],candidateZ=new int[Capacity];
    private readonly float[] priority=new float[Capacity];
    private readonly bool[] candidateVisible=new bool[Capacity];
    public readonly bool[] Visible=new bool[Capacity];
    public readonly CylinderRows CylinderRows=new CylinderRows();
    public readonly int[] X=new int[Capacity],Z=new int[Capacity];
    public int Count {get;private set;}
    public int BuildSlot {get;private set;}=-1;
    public int MissingVisible {get;private set;}
    // Every visible job has at most this much work left, even if all normal
    // background time was consumed by masks. Selection always prioritizes it.
    public int RepairStages=>MissingVisible*TerrainSectionPlan.MaximumStages;
    public int MissingWanted {get;private set;}
    public int WantedCount {get;private set;}
    public int VisibleCount {get;private set;}
    public bool Ready=>MissingVisible==0&&Count!=0;
    public bool Presentable {get;private set;}
    private int cursor,building=-1;private float cachedX=float.NaN,cachedZ;
    public void Reset()
    {Array.Clear(lookup,0,lookup.Length);Array.Clear(ready,0,ready.Length);Array.Clear(started,0,started.Length);Array.Clear(Visible,0,Visible.Length);Count=0;cursor=0;building=BuildSlot=-1;MissingVisible=MissingWanted=0;cachedX=float.NaN;Presentable=false;}
    public bool InProgress(int slot)=>started[slot];
    private static int Index(int x,int z)
    {
        int col=x/Width-MinX,row=z/Height-MinZ;
        if((uint)col>=Columns||(uint)row>=Rows)throw new InvalidOperationException("Terrain stream escaped town view bounds");
        return row*Columns+col;
    }
    public void Prepare(float x,float z)
    {
        BuildSlot=-1;
        // Selection has a guard for this half-metre bucket and eye height 0..2.
        float fx=(float)Math.Floor(x*2)*.5f+.25f,fz=(float)Math.Floor(z*2)*.5f+.25f;
        if(cachedX==fx&&cachedZ==fz&&MissingWanted==0&&building<0)return;
        if(cachedX!=fx||cachedZ!=fz){
        if(!float.IsNaN(cachedX)&&(Math.Abs(fx-cachedX)>8||Math.Abs(fz-cachedZ)>8))Presentable=false;
        float leadX=float.IsNaN(cachedX)?0:fx-cachedX,leadZ=float.IsNaN(cachedX)?0:fz-cachedZ;
        float length=(float)Math.Sqrt(leadX*leadX+leadZ*leadZ);
        if(length>0){leadX*=Prefetch/length;leadZ*=Prefetch/length;}
        var motion=new CylinderMotion(fz);var ahead=new CylinderMotion(fz+leadZ);
        cachedX=fx;cachedZ=fz;WantedCount=0;
        int left=(int)Math.Floor((fx-CylinderWorld.ViewHalfWidth-Prefetch)/Width)*Width,right=(int)Math.Floor((fx+CylinderWorld.ViewHalfWidth+Prefetch)/Width)*Width;
        int top=(int)Math.Floor((fz-TerrainTiles.GroundRowsAhead-Prefetch)/Height)*Height,bottom=(int)Math.Floor((fz+TerrainTiles.GroundRowsBehind+Prefetch)/Height)*Height;
        for(int cz=top;cz<=bottom;cz+=Height){
            var center=CylinderRows.At(cz+Height/2,1);motion.Point(center.Y,center.Z,out float py,out float pz);ahead.Point(center.Y,center.Z,out float ay,out float az);az-=leadZ;
            var box=new TerrainSectionBounds(CylinderRows,motion,cz,fz);var future=new TerrainSectionBounds(CylinderRows,ahead,cz,fz+leadZ);
            for(int cx=left;cx<=right;cx+=Width){
                // Horizon bounds live in the YZ cross-section: the section's
                // east/west width must not inflate its apparent height.
                if(!RenderView.Sphere(cx+Width*.5f,py,pz,6.75f+Prefetch,fx,fz,1)||!RenderView.TerrainHorizon(py,pz,HorizonRadius+Prefetch,fz))continue;
                bool visible=RenderView.Box(cx-TerrainSectionBounds.SideGuard,box.MinY,box.MinZ,cx+Width+TerrainSectionBounds.SideGuard,box.MaxY,box.MaxZ,fx,fz)&&RenderView.TerrainHorizon(py,pz,HorizonRadius,fz);
                bool upcoming=RenderView.Box(cx-TerrainSectionBounds.SideGuard-leadX,future.MinY,future.MinZ-leadZ,cx+Width+TerrainSectionBounds.SideGuard-leadX,future.MaxY,future.MaxZ-leadZ,fx,fz)&&RenderView.TerrainHorizon(ay,az,HorizonRadius,fz);
                if(WantedCount>=Capacity)throw new InvalidOperationException("Terrain prefetch exceeds cache capacity");
                candidateX[WantedCount]=cx;candidateZ[WantedCount]=cz;candidateVisible[WantedCount]=visible;
                float dx=cx+Width*.5f-fx-leadX,dz=cz+Height*.5f-fz-leadZ;priority[WantedCount++]=dx*dx+dz*dz+(visible?0:upcoming?50000:100000);
            }
        }
        }
        Array.Clear(wanted,0,wanted.Length);Array.Clear(Visible,0,Visible.Length);MissingVisible=MissingWanted=VisibleCount=0;
        float best=float.MaxValue;int nextX=0,nextZ=0;bool workingVisible=false;
        for(int n=0;n<WantedCount;n++){
            int slot=lookup[Index(candidateX[n],candidateZ[n])]-1;bool visible=candidateVisible[n];
            if(visible)VisibleCount++;
            if(slot>=0){wanted[slot]=true;Visible[slot]=visible&&ready[slot];if(slot==building&&visible)workingVisible=true;if(ready[slot])continue;}
            MissingWanted++;if(visible)MissingVisible++;
            if(priority[n]<best){best=priority[n];nextX=candidateX[n];nextZ=candidateZ[n];}
        }
        if(MissingWanted==0&&Count!=0)Presentable=true;
        if(building>=0){
            if(wanted[building]&&(MissingVisible==0||workingVisible)){BuildSlot=building;return;}
            if(!wanted[building])started[building]=false;
            building=-1;
        }
        if(MissingWanted==0)return;
        int selected=lookup[Index(nextX,nextZ)]-1;
        if(selected<0){
            if(Count<Capacity)selected=Count++;
            else{
                for(int n=0;n<Capacity;n++){int candidate=(cursor+n)%Capacity;if(wanted[candidate])continue;selected=candidate;cursor=(candidate+1)%Capacity;break;}
                if(selected<0)throw new InvalidOperationException("No recyclable terrain cache slot");
                lookup[Index(X[selected],Z[selected])]=0;
            }
            X[selected]=nextX;Z[selected]=nextZ;lookup[Index(nextX,nextZ)]=selected+1;ready[selected]=started[selected]=false;
        }
        BuildSlot=selected;if(started[selected])building=selected;
    }
    public void Complete()
    {
        if(BuildSlot<0)throw new InvalidOperationException("No terrain section awaiting completion");
        int slot=BuildSlot;ready[slot]=true;started[slot]=false;Visible[slot]=false;
        for(int n=0;n<WantedCount;n++)if(candidateX[n]==X[slot]&&candidateZ[n]==Z[slot]){MissingWanted--;Visible[slot]=candidateVisible[n];if(candidateVisible[n])MissingVisible--;break;}
        if(MissingWanted==0&&Count!=0)Presentable=true;
        building=BuildSlot=-1;
    }
    public void Hold(){if(BuildSlot<0)throw new InvalidOperationException("No terrain section to hold");building=BuildSlot;started[BuildSlot]=true;}
}
