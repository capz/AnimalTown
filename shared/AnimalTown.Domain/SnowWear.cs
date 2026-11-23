namespace AnimalTown;

// Snow is shared by the town and independent of the road traffic field.
public sealed class SnowWear
{
    public const int Bytes=Town.Cells/2,MaxVisits=5,RecoverySeconds=60;
    // Each visit adds three depth steps. All fifteen steps fit in the
    // original four bits per tile sent to the native snow mask.
    private const int StepsPerVisit=3,MaximumWear=MaxVisits*StepsPerVisit;
    private const int TicksPerSecond=30,ClockMask=4095;
    private const int LegacyRecoverySeconds=10*60;
    private const int CheckpointHeaderWords=3,SkipToken=0x8000;
    internal const int MinCheckpointWords=CheckpointHeaderWords+1,MaxCheckpointWords=CheckpointHeaderWords+Town.Cells/2;
    public readonly byte[] Packed=new byte[Bytes];
    // Active dents retain their initial wear and entry time modulo 4096.
    // Once settled, their shallow wear lives only in Packed.
    private ushort[]? recovery;
    private SnowTransitions? transitions;
    private int ticks,snowfallSeconds;
    internal bool HasCheckpoint {get;private set;}
    public int Revision {get;private set;}
    public bool Winter {get;private set;}
    public static bool Covers(Town town,int x,int z)=>town.Inside(x,z)&&town.At(x,z)==(int)Terrain.Grass&&TerrainElevation.Mode(town,x,z)<=TerrainElevation.Block;
    public int VisitCount(int x,int z)=>WearAt(x,z)/StepsPerVisit;
    // The center crosses the GX cutout threshold two substeps after visit two.
    public bool CenterCovered(int x,int z)=>WearAt(x,z)<8;
    private int WearAt(int x,int z)
    {
        if((uint)x>=Town.Width||(uint)z>=Town.Height)return 0;
        return WearAt(z*Town.Width+x);
    }
    private int WearAt(int cell)=>(Packed[cell/2]>>((cell%2)*4))&15;
    public void SetWinter(bool winter)=>Winter=winter;
    public void SetSmoothTransitions(bool enabled)
    {
        if(enabled)transitions??=new SnowTransitions();
        else transitions=null;
    }
    public byte[]? PrepareVisuals(float x,float z,out int origin,out int revision)
    {
        var visual=transitions;
        visual?.Prepare(this,(int)x,(int)z);
        origin=visual?.Origin??0;revision=visual?.Revision??0;
        return visual?.Depths;
    }
    public void ResetForTownEntry()
    {
        transitions=null;
        if(!HasCheckpoint)return;
        Array.Clear(Packed,0,Packed.Length);
        HasCheckpoint=false;
        Revision++;
        ResetRecovery();
    }
    internal void Enter(Town town,int x,int z)
    {
        if(!Winter||!Covers(town,x,z))return;
        int cell=z*Town.Width+x,shift=(cell%2)*4;
        int wear=(Packed[cell/2]>>shift)&15;
        int next=Math.Min(MaximumWear,wear+StepsPerVisit);
        var visual=transitions;
        if(visual!=null){
            int from=visual.AtFootstep(cell,VisualWear(cell));
            if(from!=next*SnowTransitions.Unit)visual.Footstep(cell,from);
        }
        if(next>StepsPerVisit){
            recovery??=new ushort[Town.Cells];
            recovery[cell]=(ushort)((snowfallSeconds<<4)|next);
        }
        if(next==wear)return;
        Packed[cell/2]+=(byte)((next-wear)<<shift);
        HasCheckpoint=true;
        Revision++;
    }
    // Called at 30 Hz. Keep the fraction of a second when snowfall pauses.
    public void TickSnowfall(bool snowing)
    {
        transitions?.Advance();
        var dents=recovery;
        if(!Winter||!snowing||dents==null||++ticks<TicksPerSecond)return;
        ticks=0;
        snowfallSeconds=(snowfallSeconds+1)&ClockMask;
        bool changed=false,stillRefilling=false;
        for(int cell=0;cell<dents.Length;cell++){
            int stamp=dents[cell];
            if(stamp==0)continue;
            int age=(snowfallSeconds-(stamp>>4))&ClockMask;
            int wear=RemainingWear(stamp&15,age);
            int shift=(cell%2)*4,old=Packed[cell/2];
            int next=(old&~(15<<shift))|(wear<<shift);
            if(next!=old){Packed[cell/2]=(byte)next;changed=true;}
            if(wear<=StepsPerVisit)dents[cell]=0;
            else stillRefilling=true;
        }
        if(changed)Revision++;
        if(!stillRefilling)ResetRecovery();
    }
    private static int RemainingWear(int wear,int age)
        =>Math.Min(wear,StepsPerVisit)+
            (Math.Max(0,wear-StepsPerVisit)*Math.Max(0,RecoverySeconds-age)+RecoverySeconds-1)/RecoverySeconds;
    internal int VisualSnowfallClock=>snowfallSeconds*TicksPerSecond+ticks;
    internal int VisualWear(int cell)
    {
        int stamp=recovery==null?0:recovery[cell];
        if(stamp==0)return WearAt(cell)*SnowTransitions.Unit;
        int duration=RecoverySeconds*TicksPerSecond;
        int age=((snowfallSeconds-(stamp>>4))&ClockMask)*TicksPerSecond+ticks;
        int excess=((stamp&15)-StepsPerVisit)*SnowTransitions.Unit;
        return StepsPerVisit*SnowTransitions.Unit+(excess*Math.Max(0,duration-age)+duration/2)/duration;
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
        int tokenCount=0;
        for(int cell=0;cell<Town.Cells;){CheckpointToken(ref cell);tokenCount++;}
        var words=new int[CheckpointHeaderWords+(tokenCount+1)/2];
        // Header word zero used to hold the most recent snowfall day.
        words[1]=ticks;words[2]=tokenCount;
        int next=0;
        for(int i=0;i<tokenCount;i++)words[CheckpointHeaderWords+i/2]|=CheckpointToken(ref next)<<((i%2)*16);
        return words;
    }
    private int CheckpointToken(ref int cell)
    {
        int wear=WearAt(cell),stamp=recovery==null?0:recovery[cell];
        int start=cell++;
        if(stamp!=0)return (((snowfallSeconds-(stamp>>4))&ClockMask)<<4)|(stamp&15);
        if(wear!=0)return wear;
        while(cell<Town.Cells&&WearAt(cell)==0)cell++;
        return SkipToken|(cell-start);
    }
    internal void RestoreCheckpoint(int[] words)
    {
        int tokenCount=words[2];
        if(words[0]<0||(uint)words[1]>=TicksPerSecond||tokenCount<1||tokenCount>Town.Cells||
            words.Length!=CheckpointHeaderWords+(tokenCount+1)/2)
            throw new InvalidOperationException("Invalid snow checkpoint header");
        ushort[]? dents=null;
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
            if(age>=LegacyRecoverySeconds||wear==0)throw new InvalidOperationException("Invalid saved snow dent");
            int remaining=RemainingWear(wear,age);
            if(remaining>StepsPerVisit){
                dents??=new ushort[Town.Cells];
                dents[cell]=(ushort)(((-age&ClockMask)<<4)|wear);
            }
            Packed[cell/2]|=(byte)(remaining<<((cell%2)*4));
            cell++;hasWear=true;
        }
        if(cell!=Town.Cells||!hasWear)throw new InvalidOperationException("Incomplete snow checkpoint");
        Winter=true;HasCheckpoint=true;ticks=dents!=null?words[1]:0;recovery=dents;Revision++;
    }
}
