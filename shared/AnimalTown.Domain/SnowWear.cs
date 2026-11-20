namespace AnimalTown;

// Snow starts fresh on entering winter. The packed field describes the
// current town session and is independent of persistent road traffic.
public sealed class SnowWear
{
    public const int Bytes=Town.Cells/2,MaxVisits=5,RecoverySeconds=10*60;
    // Each visit adds three depth steps. All fifteen steps fit in the
    // original four bits per tile sent to the native snow mask.
    private const int StepsPerVisit=3,MaximumWear=MaxVisits*StepsPerVisit;
    private const int TicksPerSecond=30,ClockMask=4095;
    public readonly byte[] Packed=new byte[Bytes];
    // Low bits retain the wear at the last entry. The other twelve bits
    // store its snowfall second modulo 4096, longer than any dent survives.
    private ushort[]? recovery;
    private int ticks,snowfallSeconds;
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
        if(!winter){
            Array.Clear(Packed,0,Packed.Length);Revision++;
            ResetRecovery();
        }
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
            int remaining=Math.Max(0,RecoverySeconds-age);
            int wear=((stamp&15)*remaining+RecoverySeconds-1)/RecoverySeconds;
            int shift=(cell%2)*4,old=Packed[cell/2];
            int next=(old&~(15<<shift))|(wear<<shift);
            if(next!=old){Packed[cell/2]=(byte)next;changed=true;}
            if(wear==0)dents[cell]=0;
            else anyWear=true;
        }
        if(changed)Revision++;
        if(!anyWear)ResetRecovery();
    }
    private void ResetRecovery()
    {
        recovery=null;ticks=snowfallSeconds=0;
    }
}
