namespace AnimalTown;

// One page of lookahead, admitted as whole sections inside the existing view
// budget. Actual camera demand always precedes speculation. No geometry copies.
public sealed class TerrainWarmPlan
{
    public readonly byte[] Wanted=new byte[TerrainArchivePlan.Count];
    public readonly int[] Sections=new int[TerrainStream.Capacity];
    public int Count {get;private set;}
    public int Required {get;private set;}
    public int Views {get;private set;}
    private float previousX=float.NaN,previousZ;
    public void Prepare(TerrainArchiveView view,int[] parts,float x,float z)
    {
        int dx=float.IsNaN(previousX)||x==previousX?1:x>previousX?1:-1;
        int dz=float.IsNaN(previousX)||z==previousZ?1:z>previousZ?1:-1;
        bool horizontal=float.IsNaN(previousX)||Math.Abs(x-previousX)>=Math.Abs(z-previousZ);
        previousX=x;previousZ=z;Array.Clear(Wanted,0,Wanted.Length);Count=Views=0;
        for(int i=0;i<view.Count;i++)Add(view.Sections[i],parts,1);
        Required=Count;
        // Leading edge first, then diagonals, then the trailing guard. Limits
        // apply to mesh parts, rather than assuming every page has one mesh.
        for(int direction=0;direction<8;direction++){
            int ox=direction==0?(horizontal?dx:0):direction==1?(horizontal?0:dx):direction==2?dx:direction==3?-dx:direction==4?dx:direction==5?-dx:direction==6?-dx:0;
            int oz=direction==0?(horizontal?0:dz):direction==1?(horizontal?dz:0):direction==2?dz:direction==3?dz:direction==4?-dz:direction==5?-dz:direction==6?0:-dz;
            for(int i=0;i<view.Count;i++){
                int section=view.Sections[i],px=TerrainArchivePlan.X(section)+ox*TerrainStream.Width,pz=TerrainArchivePlan.Z(section)+oz*TerrainArchivePlan.Height;
                if(px<TerrainArchivePlan.MinX||px>TerrainArchivePlan.MaxX||pz<TerrainArchivePlan.MinZ||pz>TerrainArchivePlan.MaxZ)continue;
                Add(TerrainArchivePlan.Index(px,pz),parts,2);
            }
        }
    }
    private void Add(int section,int[] parts,byte priority)
    {
        if(Wanted[section]!=0)return;
        if(Count==Sections.Length||Views+parts[section]>TerrainStream.Capacity){if(priority==1)throw new InvalidOperationException("Visible terrain exceeds fixed view budget");return;}
        Wanted[section]=priority;Sections[Count++]=section;Views+=parts[section];
    }
}
