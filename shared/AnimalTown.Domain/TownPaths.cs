namespace AnimalTown;

// An authoritative overlay: terrain, water, collisions and planting semantics
// keep their existing identities. Two bits per metre, independent of seasons.
public sealed class TownPaths
{
    public const int PackedBytes=Town.Cells/4,Material=FloorStrips.MaximumMaterials-1;
    public readonly byte[] Packed=new byte[PackedBytes];
    public int Revision {get;private set;}
    public int Unconnected {get;internal set;}
    public int At(int x,int z){if((uint)x>=Town.Width||(uint)z>=Town.Height)return 0;int cell=z*Town.Width+x;return (Packed[cell>>2]>>((cell&3)*2))&3;}
    public void Set(int x,int z,int style)
    {if((uint)x>=Town.Width||(uint)z>=Town.Height||(uint)style>1)throw new ArgumentOutOfRangeException();if(At(x,z)==style)return;Revision++;int cell=z*Town.Width+x,shift=(cell&3)*2;Packed[cell>>2]=(byte)((Packed[cell>>2]&~(3<<shift))|(style<<shift));}
    public void Clear(){Revision++;Array.Clear(Packed,0,Packed.Length);Unconnected=0;}
    public int Count {get{int n=0;for(int i=0;i<Town.Cells;i++)if(((Packed[i>>2]>>((i&3)*2))&3)!=0)n++;return n;}}
    public static bool Surface(Town t,int x,int z)=>t.At(x,z)==(int)Terrain.Grass||t.At(x,z)==(int)Terrain.Camp;
    public static bool Painted(Town t,int x,int z)=>t.Paths.At(x,z)==1&&Surface(t,x,z)&&TerrainElevation.Mode(t,x,z)<TerrainElevation.CornerUpLeft;
    public bool Any(int left,int top,int width,int height)
    {
        int right=Math.Min(Town.Width,left+width),bottom=Math.Min(Town.Height,top+height);
        for(int z=Math.Max(0,top);z<bottom;z++)for(int x=Math.Max(0,left);x<right;x++)if(At(x,z)!=0)return true;
        return false;
    }
    public void Validate(){foreach(byte b in Packed)for(int shift=0;shift<8;shift+=2)if(((b>>shift)&3)>1)throw new InvalidOperationException("Unsupported path style");}
}
