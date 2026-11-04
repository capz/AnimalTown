namespace AnimalTown;

// Independent ground bindings for each level. Candidate order N/E/S/W is
// deterministic when more than one material meets a section; never cross levels.
public static class TerrainCliffBinding
{
    public static int Neighbor(Town town,int orientation,int ox,int oz,bool upper)
    {
        for(int edge=0;edge<4;edge++){
            float px=edge==1?1.99f:edge==3?.01f:1;
            float pz=edge==0?.01f:edge==2?1.99f:1;
            if(TerrainCliffGeometry.Raised(orientation,px,pz)!=upper)continue;
            int x=ox+(edge==1?2:edge==3?-1:1),z=oz+(edge==0?-1:edge==2?2:1);
            if(town.Inside(x,z)&&Math.Abs(TerrainElevation.At(town,x+.5f,z+.5f)-(upper?2:0))<.001f)return z*Town.Width+x;
        }
        // An isolated preview has no raised neighbor. Keep its own ground;
        // actual placed terrain can store independent upper/lower surface IDs.
        int fx=Math.Max(0,Math.Min(Town.Width-1,ox+1)),fz=Math.Max(0,Math.Min(Town.Height-1,oz+1));
        return fz*Town.Width+fx;
    }
    public static void PreviewOrigin(int playerX,int playerZ,int orientation,out int x,out int z)
    {x=playerX/2*2-5+orientation%4*3;z=playerZ/2*2-11+orientation/4*5;}
    public static int PreviewLowNeighbor(Town town,int playerX,int playerZ,int cellX,int cellZ)
    {
        for(int i=4;i<8;i++){
            PreviewOrigin(playerX,playerZ,i,out int ox,out int oz);
            if(cellX>=ox&&cellX<ox+2&&cellZ>=oz&&cellZ<oz+2)return Neighbor(town,i,ox,oz,false);
        }
        return -1;
    }
}
