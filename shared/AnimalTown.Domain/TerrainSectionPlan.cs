namespace AnimalTown;

// Fast sections retain the same row chords, materials and UVs. Dry sections
// must have no wall work; the sea border covers every depth/foam sample.
public static class TerrainSectionPlan
{
    public const int Detailed=0,Dry=1,DeepSea=2;
    public const int MaximumStages=TerrainStream.Height*2+1;
    public static int Kind(Town town,int left,int top)
    {
        bool dry=true;
        for(int z=top;z<top+TerrainStream.Height&&dry;z++)for(int x=left;x<left+TerrainStream.Width;x++){
            int t=town.At(x,z);
            if(TerrainElevation.Mode(town,x,z)>TerrainElevation.Block||t==(int)Terrain.Cliff||t==(int)Terrain.Bridge||WaterTerrain.IsCoast(t)||WaterSurface.IsWater(town,x,z)){dry=false;break;}
        }
        if(dry)for(int z=top;z<top+TerrainStream.Height;z++)if(Walls(town,left,z)){dry=false;break;}
        if(dry)return Dry;
        for(int z=top-3;z<=top+TerrainStream.Height+3;z++)for(int x=left-3;x<=left+TerrainStream.Width+3;x++)
            if(town.At(x,z)!=(int)Terrain.Sea||TerrainElevation.Mode(town,x,z)!=0)return Detailed;
        return DeepSea;
    }
    public static bool Walls(Town town,int left,int z)
    {
        for(int x=left;x<left+TerrainStream.Width;x++){
            int t=town.At(x,z),m=TerrainElevation.Mode(town,x,z);
            if(t==(int)Terrain.Water)return true;
            if(t==(int)Terrain.Bridge||WaterSurface.IsWater(town,x,z))continue;
            if(WaterTerrain.IsCoast(t)||m>=2)return true;
            for(int side=0;side<4;side++){
                int nx=x+(side==1?1:side==3?-1:0),nz=z+(side==0?-1:side==2?1:0),nt=town.At(nx,nz);
                if(t==(int)Terrain.Beach&&WaterTerrain.IsSea(town,nx,nz))continue;
                if(TerrainElevation.Mode(town,nx,nz)!=m||WaterTerrain.IsCoast(nt)||WaterSurface.IsWater(town,nx,nz))return true;
            }
        }
        return false;
    }
    public static int Stages(Town town,int left,int top)
    {
        if(Kind(town,left,top)!=Detailed)return 2;
        int count=TerrainStream.Height+1;for(int z=top;z<top+TerrainStream.Height;z++)if(Walls(town,left,z))count++;
        return count;
    }
}
