namespace AnimalTown;

// Anonymous outdoor tile entries, not frames spent standing on a tile. No
// actor IDs, timestamps or individual routes are retained. Visibility milestones
// are exact; only excess above full opacity is renormalized on overflow.
public sealed class TileTraffic
{
    public const int Bytes=Town.Cells,RefreshTicks=90;
    // Keep the native I4 conversion in tools/native-path-mask.h in sync.
    public const int MinimumVisits=35,FullOpacityVisits=100;
    public readonly byte[] Visits=new byte[Bytes];
    private readonly ushort[] histogram=new ushort[256];
    private bool dirty;
    private int ticks;
    public int EligibleVisited {get;private set;}
    public int HighOutliers {get;private set;}
    public int Cutoff {get;private set;}
    public int Normalizations {get;private set;}
    public int OpacityRevision {get;private set;}
    public static int OpacityLevel(int visits)=>visits<MinimumVisits?0:visits>=FullOpacityVisits?15:2+(visits-MinimumVisits)*13/(FullOpacityVisits-MinimumVisits);
    public void Enter(Town town,int x,int z)
    {
        if(!town.Walkable(x,z))return;
        int cell=z*Town.Width+x;
        int before=Visits[cell];
        if(Visits[cell]==255){
            for(int i=0;i<Visits.Length;i++)if(Visits[i]>FullOpacityVisits)
                Visits[i]=(byte)(FullOpacityVisits+(Visits[i]-FullOpacityVisits+1)/2);
            Normalizations++;
        }
        Visits[cell]++;dirty=true;
        if(town.Paths.At(x,z)!=0&&OpacityLevel(before)!=OpacityLevel(Visits[cell]))OpacityRevision++;
    }
    // Movement callers pass only successful steps, never spawn/portal/debug
    // relocations. DDA also records intermediate cells if a step spans a tile.
    public void Traverse(Town town,float ax,float az,float bx,float bz)
    {
        if(!(ax>=0&&az>=0&&bx>=0&&bz>=0&&ax<Town.Width&&bx<Town.Width&&az<Town.Height&&bz<Town.Height))return;
        int x=(int)Math.Floor(ax),z=(int)Math.Floor(az),endX=(int)Math.Floor(bx),endZ=(int)Math.Floor(bz);
        if(!town.Inside(x,z)||!town.Inside(endX,endZ))return;
        float dx=bx-ax,dz=bz-az;
        int sx=dx>0?1:-1,sz=dz>0?1:-1;
        float stepX=dx==0?float.PositiveInfinity:1/Math.Abs(dx),stepZ=dz==0?float.PositiveInfinity:1/Math.Abs(dz);
        float nextX=dx==0?float.PositiveInfinity:(dx>0?x+1-ax:ax-x)*stepX;
        float nextZ=dz==0?float.PositiveInfinity:(dz>0?z+1-az:az-z)*stepZ;
        for(int n=0;n<Town.Width+Town.Height&&(x!=endX||z!=endZ);n++){
            if(nextX<nextZ){x+=sx;nextX+=stepX;}
            else if(nextZ<nextX){z+=sz;nextZ+=stepZ;}
            else{x+=sx;z+=sz;nextX+=stepX;nextZ+=stepZ;}
            Enter(town,x,z);
        }
    }
    public void Tick(Town town)
    {if(dirty&&++ticks>=RefreshTicks)Refresh(town);}
    private static bool Eligible(Town town,int cell)=>TownPaths.Surface(town,cell%Town.Width,cell/Town.Width)&&town.Elevation[cell]<TerrainElevation.CornerUpLeft;
    private int Quantile(int population,int percent)
    {
        int rank=(population*percent+99)/100,seen=0;
        for(int i=1;i<256;i++){seen+=histogram[i];if(seen>=rank)return i;}
        return 0;
    }
    public void Refresh(Town town)
    {
        Array.Clear(histogram,0,histogram.Length);int count=0;
        for(int i=0;i<Visits.Length;i++)if(Visits[i]!=0&&Eligible(town,i)){histogram[Visits[i]]++;count++;}
        EligibleVisited=count;HighOutliers=0;Cutoff=0;
        if(count>0){
            int q1=Quantile(count,25),q3=Quantile(count,75),fence=q3+(q3-q1)*3/2;
            for(int i=Math.Min(255,fence)+1;i<256;i++)HighOutliers+=histogram[i];
            int quota=(count-HighOutliers+4)/5,above=0;
            for(int i=Math.Min(255,fence);i>0;i--){if(above+histogram[i]>=quota){Cutoff=i;break;}above+=histogram[i];}
            int ties=quota-above,oldTies=0;
            for(int i=0;i<Visits.Length;i++)if(Visits[i]==Cutoff&&Eligible(town,i)&&town.Paths.At(i%Town.Width,i/Town.Width)!=0)oldTies++;
            int keep=Math.Min(ties,oldTies),add=ties-keep;
            for(int i=0;i<Visits.Length;i++){
                bool paint=Visits[i]>Cutoff&&Eligible(town,i);
                if(Visits[i]==Cutoff&&Eligible(town,i)){
                    bool old=town.Paths.At(i%Town.Width,i/Town.Width)!=0;
                    if(old&&keep>0){paint=true;keep--;}
                    else if(!old&&add>0){paint=true;add--;}
                }
                town.Paths.Set(i%Town.Width,i/Town.Width,paint&&Visits[i]>=MinimumVisits?1:0);
            }
        }else town.Paths.Clear();
        // Paths have their own revision. A footstep never invalidates the
        // immutable terrain archive or any shadow receiver geometry.
        ticks=0;dirty=false;
    }
}
