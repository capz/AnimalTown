namespace AnimalTown;

// Snow starts fresh on entering winter. The packed field describes the
// current town session and is independent of persistent road traffic.
public sealed class SnowWear
{
    public const int Bytes=Town.Cells/2,MaxVisits=5;
    public readonly byte[] Packed=new byte[Bytes];
    public int Revision {get;private set;}
    public bool Winter {get;private set;}
    public static bool Covers(Town town,int x,int z)=>town.Inside(x,z)&&town.At(x,z)==(int)Terrain.Grass&&TerrainElevation.Mode(town,x,z)<=TerrainElevation.Block;
    public int VisitCount(int x,int z)
    {
        if((uint)x>=Town.Width||(uint)z>=Town.Height)return 0;
        int cell=z*Town.Width+x;
        return (Packed[cell/2]>>((cell%2)*4))&15;
    }
    public void SetWinter(bool winter)
    {
        if(Winter==winter)return;
        Winter=winter;
        if(!winter){Array.Clear(Packed,0,Packed.Length);Revision++;}
    }
    internal void Enter(Town town,int x,int z)
    {
        if(!Winter||!Covers(town,x,z))return;
        int cell=z*Town.Width+x,shift=(cell%2)*4;
        if(((Packed[cell/2]>>shift)&15)==MaxVisits)return;
        Packed[cell/2]+=(byte)(1<<shift);
        Revision++;
    }
}
