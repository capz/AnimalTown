namespace AnimalTown;

// Snow is shared by the town and independent of the road traffic field.
public sealed class SnowWear
{
    public const int Bytes=Town.Cells/2,MaxVisits=5,RecoverySeconds=10*60;
    // Each visit adds three depth steps. All fifteen steps fit in the
    // original four bits per tile sent to the native snow mask.
    private const int StepsPerVisit=3,MaximumWear=MaxVisits*StepsPerVisit;
    private const int TicksPerSecond=30,ClockMask=4095;
    private const int CheckpointHeaderWords=3,SkipToken=0x8000;
    internal const int MinCheckpointWords=CheckpointHeaderWords+1,MaxCheckpointWords=CheckpointHeaderWords+Town.Cells/2;
    public readonly byte[] Packed=new byte[Bytes];
    // Low bits retain the wear at the last entry. The other twelve bits
    // store its snowfall second modulo 4096, longer than any dent survives.
    private ushort[]? recovery;
    private int ticks,snowfallSeconds,lastSnowDay=-1;
    internal bool HasCheckpoint=>recovery!=null&&lastSnowDay>=0;
    public int Revision {get;private set;}
    public bool Winter {get;private set;}
    public static bool Covers(Town town,int x,int z)=>town.Inside(x,z)&&town.At(x,z)==(int)Terrain.Grass&&TerrainElevation.Mode(town,x,z)<=TerrainElevation.Block;
    public int VisitCount(int x,int z)=>WearAt(x,z)/StepsPerVisit;
    // The center crosses the GX cutout threshold two substeps after visit two.
    public bool CenterCovered(int x,int z)=>WearAt(x,z)<8;
    private int WearAt(int x,int z)
    {
        if((uint)x>=Town.Width||(uint)z>=Town.Height)return 0;
        int cell=z*Town.Width+x;
        return (Packed[cell/2]>>((cell%2)*4))&15;
    }
    public void SetWinter(bool winter)
    {
        if(Winter==winter)return;
        Winter=winter;
        if(!winter){ClearDents();lastSnowDay=-1;}
    }
    public void ObserveSnowDay(int latestSnowDay,bool startingSession)
    {
        // Only a new play session catches up instantly. While playing, snowfall
        // recovers the snow gradually even when today's window just began.
        if(startingSession&&recovery!=null&&latestSnowDay>lastSnowDay)ClearDents();
        lastSnowDay=latestSnowDay;
    }
    internal void Enter(Town town,int x,int z)
    {
        if(!Winter||!Covers(town,x,z))return;
        int cell=z*Town.Width+x,shift=(cell%2)*4;
        int wear=(Packed[cell/2]>>shift)&15;
        int next=Math.Min(MaximumWear,wear+StepsPerVisit);
        recovery??=new ushort[Town.Cells];
        recovery[cell]=(ushort)((snowfallSeconds<<4)|next);
        if(next==wear)return;
        Packed[cell/2]+=(byte)((next-wear)<<shift);
        Revision++;
    }
    // Called at 30 Hz. Keep the fraction of a second when snowfall pauses.
    public void TickSnowfall(bool snowing)
    {
        var dents=recovery;
        if(!snowing||dents==null||++ticks<TicksPerSecond)return;
        ticks=0;
        snowfallSeconds=(snowfallSeconds+1)&ClockMask;
        bool changed=false,anyWear=false;
        for(int cell=0;cell<dents.Length;cell++){
            int stamp=dents[cell];
            if(stamp==0)continue;
            int age=(snowfallSeconds-(stamp>>4))&ClockMask;
            int wear=RemainingWear(stamp&15,age);
            int shift=(cell%2)*4,old=Packed[cell/2];
            int next=(old&~(15<<shift))|(wear<<shift);
            if(next!=old){Packed[cell/2]=(byte)next;changed=true;}
            if(wear==0)dents[cell]=0;
            else anyWear=true;
        }
        if(changed)Revision++;
        if(!anyWear)ResetRecovery();
    }
    private static int RemainingWear(int wear,int age)
        =>(wear*Math.Max(0,RecoverySeconds-age)+RecoverySeconds-1)/RecoverySeconds;
    private void ClearDents()
    {
        Array.Clear(Packed,0,Packed.Length);Revision++;
        ResetRecovery();
    }
    private void ResetRecovery()
    {
        recovery=null;ticks=snowfallSeconds=0;
    }

    // Two 16-bit tokens per word. A token is an age and wear, or a run of
    // undented tiles. The saved size never exceeds two bytes per town tile.
    internal int[]? EncodeCheckpoint()
    {
        if(!HasCheckpoint)return null;
        var dents=recovery!;
        int tokenCount=0;
        for(int cell=0;cell<dents.Length;){CheckpointToken(dents,ref cell);tokenCount++;}
        var words=new int[CheckpointHeaderWords+(tokenCount+1)/2];
        words[0]=lastSnowDay;words[1]=ticks;words[2]=tokenCount;
        int next=0;
        for(int i=0;i<tokenCount;i++)words[CheckpointHeaderWords+i/2]|=CheckpointToken(dents,ref next)<<((i%2)*16);
        return words;
    }
    private int CheckpointToken(ushort[] dents,ref int cell)
    {
        int stamp=dents[cell++];
        if(stamp!=0)return (((snowfallSeconds-(stamp>>4))&ClockMask)<<4)|(stamp&15);
        int start=cell-1;
        while(cell<dents.Length&&dents[cell]==0)cell++;
        return SkipToken|(cell-start);
    }
    internal void RestoreCheckpoint(int[] words)
    {
        int tokenCount=words[2];
        if(words[0]<0||(uint)words[1]>=TicksPerSecond||tokenCount<1||tokenCount>Town.Cells||
            words.Length!=CheckpointHeaderWords+(tokenCount+1)/2)
            throw new InvalidOperationException("Invalid snow checkpoint header");
        var dents=new ushort[Town.Cells];
        int cell=0;bool hasWear=false;
        for(int i=0;i<tokenCount;i++){
            int token=(words[CheckpointHeaderWords+i/2]>>((i%2)*16))&65535;
            if(cell>=Town.Cells||token==0)throw new InvalidOperationException("Invalid snow checkpoint tile");
            if((token&SkipToken)!=0){
                int run=token&~SkipToken;
                if(run==0||run>Town.Cells-cell)throw new InvalidOperationException("Invalid saved snow run");
                cell+=run;continue;
            }
            int age=token>>4,wear=token&15;
            if(age>=RecoverySeconds||wear==0)throw new InvalidOperationException("Invalid saved snow dent");
            dents[cell]=(ushort)(((-age&ClockMask)<<4)|wear);
            Packed[cell/2]|=(byte)(RemainingWear(wear,age)<<((cell%2)*4));
            cell++;hasWear=true;
        }
        if(cell!=Town.Cells||!hasWear)throw new InvalidOperationException("Incomplete snow checkpoint");
        Winter=true;lastSnowDay=words[0];ticks=words[1];recovery=dents;Revision++;
    }
}
