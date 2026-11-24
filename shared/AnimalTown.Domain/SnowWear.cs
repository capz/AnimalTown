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
    // Low bits retain wear; twelve high bits store the snowfall second modulo
    // 4096. Allocate only touched two-row chunks and visit only active chunks.
    // A first dent needs about 1 KiB rather than a 30 KiB full-town array.
    private const int RecoveryShift=8,RecoveryChunk=1<<RecoveryShift,RecoveryChunks=(Town.Cells+RecoveryChunk-1)/RecoveryChunk;
    private ushort[]?[]? recovery;
    private ushort[]? activeRecovery;
    private int recoveryCount;
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
        if(recovery==null){recovery=new ushort[RecoveryChunks][];activeRecovery=new ushort[RecoveryChunks];}
        int chunk=cell>>RecoveryShift;
        var stamps=recovery[chunk];
        if(stamps==null){stamps=recovery[chunk]=new ushort[RecoveryChunk];activeRecovery![recoveryCount++]=(ushort)chunk;}
        stamps[cell&(RecoveryChunk-1)]=(ushort)((snowfallSeconds<<4)|next);
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
        bool changed=false;
        for(int active=0;active<recoveryCount;){
            int chunk=activeRecovery![active],start=chunk<<RecoveryShift;
            var stamps=dents[chunk]!;bool anyWear=false;
            for(int index=0;index<stamps.Length&&start+index<Town.Cells;index++){
                int stamp=stamps[index];if(stamp==0)continue;
                int age=(snowfallSeconds-(stamp>>4))&ClockMask;
                int wear=RemainingWear(stamp&15,age),cell=start+index;
                int shift=(cell%2)*4,old=Packed[cell/2];
                int next=(old&~(15<<shift))|(wear<<shift);
                if(next!=old){Packed[cell/2]=(byte)next;changed=true;}
                if(wear==0)stamps[index]=0;else anyWear=true;
            }
            if(anyWear){active++;continue;}
            dents[chunk]=null;activeRecovery[active]=activeRecovery[--recoveryCount];
        }
        if(changed)Revision++;
        if(recoveryCount==0)ResetRecovery();
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
        recovery=null;activeRecovery=null;recoveryCount=0;ticks=snowfallSeconds=0;
    }

    // Two 16-bit tokens per word. A token is an age and wear, or a run of
    // undented tiles. The saved size never exceeds two bytes per town tile.
    internal int[]? EncodeCheckpoint()
    {
        if(!HasCheckpoint)return null;
        int tokenCount=0;
        for(int cell=0;cell<Town.Cells;){CheckpointToken(ref cell);tokenCount++;}
        var words=new int[CheckpointHeaderWords+(tokenCount+1)/2];
        words[0]=lastSnowDay;words[1]=ticks;words[2]=tokenCount;
        int next=0;
        for(int i=0;i<tokenCount;i++)words[CheckpointHeaderWords+i/2]|=CheckpointToken(ref next)<<((i%2)*16);
        return words;
    }
    private int RecoveryAt(int cell)=>recovery![cell>>RecoveryShift]?[cell&(RecoveryChunk-1)]??0;
    private int CheckpointToken(ref int cell)
    {
        int stamp=RecoveryAt(cell++);
        if(stamp!=0)return (((snowfallSeconds-(stamp>>4))&ClockMask)<<4)|(stamp&15);
        int start=cell-1;
        while(cell<Town.Cells&&RecoveryAt(cell)==0)cell++;
        return SkipToken|(cell-start);
    }
    internal void RestoreCheckpoint(int[] words)
    {
        int tokenCount=words[2];
        if(words[0]<0||(uint)words[1]>=TicksPerSecond||tokenCount<1||tokenCount>Town.Cells||
            words.Length!=CheckpointHeaderWords+(tokenCount+1)/2)
            throw new InvalidOperationException("Invalid snow checkpoint header");
        var dents=new ushort[RecoveryChunks][];var chunks=new ushort[RecoveryChunks];
        int cell=0,count=0;bool hasWear=false;
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
            int chunk=cell>>RecoveryShift;
            if(dents[chunk]==null){dents[chunk]=new ushort[RecoveryChunk];chunks[count++]=(ushort)chunk;}
            dents[chunk][cell&(RecoveryChunk-1)]=(ushort)(((-age&ClockMask)<<4)|wear);
            Packed[cell/2]|=(byte)(RemainingWear(wear,age)<<((cell%2)*4));
            cell++;hasWear=true;
        }
        if(cell!=Town.Cells||!hasWear)throw new InvalidOperationException("Incomplete snow checkpoint");
        Winter=true;lastSnowDay=words[0];ticks=words[1];recovery=dents;activeRecovery=chunks;recoveryCount=count;Revision++;
    }
}
