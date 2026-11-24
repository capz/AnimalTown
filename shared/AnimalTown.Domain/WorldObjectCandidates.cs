namespace AnimalTown;

// One shared broad phase for trees, ground markers and placed objects. The
// eight-metre bins retain original indices and order; existing precise bounds
// still decide drawing. Membership changes only after a world edit.
public sealed class WorldObjectCandidates
{
    public const int BinSize=8;
    private const int Columns=(Town.Width+BinSize-1)/BinSize,Rows=(Town.Height+BinSize-1)/BinSize;
    private const int OutdoorBins=Columns*Rows,OverflowBin=OutdoorBins,RoomBase=OutdoorBins+1;
    private readonly short[] groundHeads=new short[OutdoorBins+1],placementHeads=new short[OutdoorBins+1+44];
    private readonly short[] groundNext=new short[512],placementNext=new short[256];
    private readonly float[] rowY=new float[Rows],rowZ=new float[Rows];
    public readonly int[] GroundIndices=new int[512],PlacementIndices=new int[256];
    public int GroundCount {get;private set;}
    public int PlacementCount {get;private set;}
    public int Rebuilds {get;private set;}
    private Town? town;
    private int groundVersion=-1,objectVersion=-1,groundCount=-1,placementCount=-1,terrainVersion=-1,space=-1;
    private float viewX=float.NaN,viewZ,viewHeight,aspect;
    // Enclose a complete bin, its 0..2m terrace range and the largest tree
    // sphere (radius 2.46m). Curvature is rigid and cannot enlarge this sphere.
    private const float BinRadius=10;
    public WorldObjectCandidates()
    {
        for(int row=0;row<Rows;row++){
            var p=CylinderWorld.Surface(row*BinSize+BinSize*.5f,0,2.5f);rowY[row]=p.Y;rowZ[row]=p.Z;
        }
    }
    private static int Bin(int x,int z)=>x>=0&&z>=0&&x<Town.Width&&z<Town.Height?z/BinSize*Columns+x/BinSize:OverflowBin;
    public void Prepare(GameState state,float focusX,float focusZ,float eyeHeight,int room)
    {
        var t=state.Town;
        bool changed=town!=t||groundVersion!=t.ShadowVersion||objectVersion!=t.ObjectVersion||groundCount!=t.GroundCount||placementCount!=t.PlacedCount;
        if(changed){
            town=t;groundVersion=t.ShadowVersion;objectVersion=t.ObjectVersion;groundCount=t.GroundCount;placementCount=t.PlacedCount;Rebuilds++;
            for(int i=0;i<groundHeads.Length;i++)groundHeads[i]=-1;
            for(int i=0;i<placementHeads.Length;i++)placementHeads[i]=-1;
            for(int i=t.GroundCount-1;i>=0;i--){var g=t.Ground[i];int bin=Bin(g.X,g.Z);groundNext[i]=groundHeads[bin];groundHeads[bin]=(short)i;}
            for(int i=t.PlacedCount-1;i>=0;i--){var p=t.Placed[i];int bin=p.Room==0?Bin(p.X,p.Z):RoomBase+Math.Clamp(p.Room,0,43);placementNext[i]=placementHeads[bin];placementHeads[bin]=(short)i;}
        }
        if(!changed&&viewX==focusX&&viewZ==focusZ&&viewHeight==eyeHeight&&space==room&&terrainVersion==t.TerrainVersion&&aspect==RenderView.AspectRatio)return;
        viewX=focusX;viewZ=focusZ;viewHeight=eyeHeight;space=room;terrainVersion=t.TerrainVersion;aspect=RenderView.AspectRatio;
        GroundCount=PlacementCount=0;
        if(room!=0){
            if((uint)room<44)for(int i=placementHeads[RoomBase+room];i>=0;i=placementNext[i])PlacementIndices[PlacementCount++]=i;
            return;
        }
        var motion=CylinderWorld.Scroll(0,focusZ);
        for(int row=0;row<Rows;row++){
            float y=motion.Cos*rowY[row]-motion.Sin*rowZ[row]+motion.Y,z=motion.Sin*rowY[row]+motion.Cos*rowZ[row]+motion.Z;
            if(!RenderView.Sphere(focusX,y,z,BinRadius,focusX,focusZ,eyeHeight)||!RenderView.AboveHorizon(y,z,BinRadius,focusZ,eyeHeight))continue;
            for(int col=0;col<Columns;col++){
                int bin=row*Columns+col;if(groundHeads[bin]<0&&placementHeads[bin]<0)continue;
                if(!RenderView.Sphere(col*BinSize+BinSize*.5f,y,z,BinRadius,focusX,focusZ,eyeHeight))continue;
                Append(bin);
            }
        }
        // Invalid/debug coordinates remain conservative instead of silently
        // disappearing because they fall outside the normal saved-town grid.
        Append(OverflowBin);
        Sort(GroundIndices,GroundCount);Sort(PlacementIndices,PlacementCount);
    }
    private void Append(int bin)
    {
        for(int i=groundHeads[bin];i>=0;i=groundNext[i])GroundIndices[GroundCount++]=i;
        for(int i=placementHeads[bin];i>=0;i=placementNext[i])PlacementIndices[PlacementCount++]=i;
    }
    private static void Sort(int[] values,int count)
    {
        // No comparer/delegate/stack allocation, and bounded O(n log n) even
        // when generated object indices are scattered randomly across bins.
        for(int root=count/2-1;root>=0;root--)Sift(values,count,root);
        for(int end=count-1;end>0;end--){
            int value=values[0];values[0]=values[end];values[end]=value;Sift(values,end,0);
        }
    }
    private static void Sift(int[] values,int count,int root)
    {
        int value=values[root];
        while(root*2+1<count){
            int child=root*2+1;if(child+1<count&&values[child]<values[child+1])child++;
            if(values[child]<=value)break;values[root]=values[child];root=child;
        }
        values[root]=value;
    }
}
