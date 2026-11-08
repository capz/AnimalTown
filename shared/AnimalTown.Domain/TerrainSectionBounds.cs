namespace AnimalTown;

// World AABB of the complete section, including river bottoms, upper caps and
// the fractional camera bucket / 0..2m eye-height guard. X never curves.
public readonly struct TerrainSectionBounds
{
    public readonly float MinY,MaxY,MinZ,MaxZ;
    public const float SideGuard=.4f;
    public TerrainSectionBounds(CylinderRows rows,CylinderMotion motion,int top,float focusZ)
    {
        float minY=float.MaxValue,maxY=float.MinValue,minZ=float.MaxValue,maxZ=float.MinValue;
        for(int side=0;side<2;side++)for(int level=0;level<2;level++){
            var p=rows.At(top+side*TerrainStream.Height,level==0?-.6f:2.05f);motion.Point(p.Y,p.Z,out float y,out float z);
            minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);minZ=Math.Min(minZ,z);maxZ=Math.Max(maxZ,z);
        }
        if(top<=focusZ&&top+TerrainStream.Height>=focusZ)maxY=2.05f;
        MinY=minY-1.3f;MaxY=maxY+1.3f;MinZ=minZ-.6f;MaxZ=maxZ+.6f;
    }
}
