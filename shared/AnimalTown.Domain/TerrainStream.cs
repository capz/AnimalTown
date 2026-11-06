namespace AnimalTown;

// Bounded virtual terrain cache. Movement requests small immutable sections,
// never a rebuild of the visible patch. Each request is only thirty-two cells;
// the renderer pumps requests under its per-frame time and section limits.
public sealed class TerrainStream
{
    public const int Width=8,Height=4,Capacity=160;
    public const float Prefetch=4;
    private const float Radius=6.75f;
    private const int MinX=-CylinderWorld.ViewHalfWidth/Width-2,MinZ=-TerrainTiles.GroundRowsAhead/Height-2;
    private const int Columns=(Town.Width+2*CylinderWorld.ViewHalfWidth)/Width+5;
    private const int Rows=(Town.Height+TerrainTiles.GroundRowsAhead+TerrainTiles.GroundRowsBehind)/Height+5;
    private readonly int[] lookup=new int[Columns*Rows];
    private readonly bool[] ready=new bool[Capacity],wanted=new bool[Capacity];
    private readonly int[] candidateX=new int[Capacity],candidateZ=new int[Capacity];
    private readonly float[] priority=new float[Capacity];
    private readonly bool[] candidateVisible=new bool[Capacity];
    public readonly bool[] Visible=new bool[Capacity];
    public readonly int[] X=new int[Capacity],Z=new int[Capacity];
    public int Count {get;private set;}
    public int BuildSlot {get;private set;}=-1;
    public int MissingVisible {get;private set;}
    public int MissingWanted {get;private set;}
    public int WantedCount {get;private set;}
    public int VisibleCount {get;private set;}
    public bool Ready=>MissingVisible==0&&Count!=0;
    private int cursor;private float cachedX=float.NaN,cachedZ;
    private bool buildVisible;
    public void Reset()
    {Array.Clear(lookup,0,lookup.Length);Array.Clear(ready,0,ready.Length);Array.Clear(Visible,0,Visible.Length);Count=0;cursor=0;BuildSlot=-1;MissingVisible=MissingWanted=0;cachedX=float.NaN;}
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
        if(cachedX==fx&&cachedZ==fz&&MissingWanted==0)return;
        if(cachedX!=fx||cachedZ!=fz){
        cachedX=fx;cachedZ=fz;WantedCount=0;
        int left=(int)Math.Floor((fx-CylinderWorld.ViewHalfWidth)/Width)*Width,right=(int)Math.Floor((fx+CylinderWorld.ViewHalfWidth)/Width)*Width;
        int top=(int)Math.Floor((fz-TerrainTiles.GroundRowsAhead-Height*2)/Height)*Height,bottom=(int)Math.Floor((fz+TerrainTiles.GroundRowsBehind)/Height)*Height;
        for(int cz=top;cz<=bottom;cz+=Height){
            var p=CylinderWorld.Surface(cz+Height*.5f,fz,1);
            for(int cx=left;cx<=right;cx+=Width){
                // Horizon bounds live in the YZ cross-section: the section's
                // east/west width must not inflate its apparent height.
                if(!RenderView.Sphere(cx+Width*.5f,p.Y,p.Z,Radius+Prefetch,fx,fz,1)||!RenderView.AboveHorizon(p.Y,p.Z,4+Prefetch,fz,1))continue;
                bool visible=RenderView.Sphere(cx+Width*.5f,p.Y,p.Z,Radius,fx,fz,1)&&RenderView.AboveHorizon(p.Y,p.Z,4,fz,1);
                if(WantedCount>=Capacity)throw new InvalidOperationException("Terrain prefetch exceeds cache capacity");
                candidateX[WantedCount]=cx;candidateZ[WantedCount]=cz;candidateVisible[WantedCount]=visible;
                float dx=cx+Width*.5f-fx,dz=cz+Height*.5f-fz;priority[WantedCount++]=dx*dx+dz*dz+(visible?0:100000);
            }
        }
        }
        Array.Clear(wanted,0,wanted.Length);Array.Clear(Visible,0,Visible.Length);MissingVisible=MissingWanted=VisibleCount=0;
        float best=float.MaxValue;int nextX=0,nextZ=0;bool nextVisible=false;
        for(int n=0;n<WantedCount;n++){
            int slot=lookup[Index(candidateX[n],candidateZ[n])]-1;bool visible=candidateVisible[n];
            if(visible)VisibleCount++;
            if(slot>=0){wanted[slot]=true;Visible[slot]=visible&&ready[slot];if(ready[slot])continue;}
            MissingWanted++;if(visible)MissingVisible++;
            if(priority[n]<best){best=priority[n];nextX=candidateX[n];nextZ=candidateZ[n];nextVisible=visible;}
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
            X[selected]=nextX;Z[selected]=nextZ;lookup[Index(nextX,nextZ)]=selected+1;ready[selected]=false;
        }
        BuildSlot=selected;buildVisible=nextVisible;
    }
    public void Complete()
    {
        if(BuildSlot<0)throw new InvalidOperationException("No terrain section awaiting completion");
        ready[BuildSlot]=true;Visible[BuildSlot]=buildVisible;MissingWanted--;if(buildVisible)MissingVisible--;BuildSlot=-1;
    }
}
