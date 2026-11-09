namespace AnimalTown;

// One section spans two fine cells. Blocks and single-section ramps both have
// a 2x2m footprint and a 2m rise. Values are persisted, never regenerated on load.
public static class TerrainElevation
{
    public const float Section=2,Height=2;
    public const byte Flat=0,Block=1,RampNorth=2,RampEast=3,RampSouth=4,RampWest=5;
    public const byte CornerUpLeft=6,CornerUpRight=7,CornerDownLeft=8,CornerDownRight=9,MaximumMode=9;
    public static byte Mode(Town town,int x,int z)=>town.Inside(x,z)?town.Elevation[z*Town.Width+x]:(byte)0;
    public static float AtCell(Town town,int cellX,int cellZ,float x,float z)
    {
        int terrain=town.At(cellX,cellZ);if(terrain==(int)Terrain.Water)return BaseAtCell(town,cellX,cellZ,x,z)+WaterTerrain.RiverHeight;if(WaterTerrain.IsSea(town,cellX,cellZ))return BaseAtCell(town,cellX,cellZ,x,z)+WaterSurface.Height;if(WaterTerrain.IsCoast(terrain))return Height;if(terrain==(int)Terrain.Beach)return BeachAtCell(town,cellX,cellZ,x,z);return BaseAtCell(town,cellX,cellZ,x,z);
    }
    // Match the floor's two triangles (A-B-C, A-C-D), including corner
    // beaches. Bilinear interpolation would float feet over the diagonal.
    private static float BeachAtCell(Town town,int cx,int cz,float x,float z)
    {
        float u=Math.Max(0,Math.Min(1,x-cx)),v=Math.Max(0,Math.Min(1,z-cz));
        float a=BeachCorner(town,cx,cz,cx,cz),b=BeachCorner(town,cx,cz,cx+1,cz);
        float c=BeachCorner(town,cx,cz,cx+1,cz+1),d=BeachCorner(town,cx,cz,cx,cz+1);
        return u>=v?a+(b-a)*u+(c-b)*v:a+(c-d)*u+(d-a)*v;
    }
    private static float BeachCorner(Town town,int cx,int cz,int x,int z)
    {
        bool shore=x==cx&&WaterTerrain.IsSea(town,cx-1,cz)||x==cx+1&&WaterTerrain.IsSea(town,cx+1,cz)||z==cz&&WaterTerrain.IsSea(town,cx,cz-1)||z==cz+1&&WaterTerrain.IsSea(town,cx,cz+1);
        return shore?WaterSurface.Height:BaseAtCell(town,cx,cz,x,z);
    }
    private static float BaseAtCell(Town town,int cellX,int cellZ,float x,float z)
    {
        int mode=Mode(town,cellX,cellZ);if(mode==0)return 0;if(mode==Block)return 2;
        float lx=Math.Max(0,Math.Min(2,x-cellX/2*2)),lz=Math.Max(0,Math.Min(2,z-cellZ/2*2));
        if(mode>=CornerUpLeft)return TerrainCliffGeometry.Raised(mode-2,lx,lz)?2:0;
        return mode==RampNorth?2-lz:mode==RampEast?lx:mode==RampSouth?lz:mode==RampWest?2-lx:0;
    }
    public static float At(Town town,float x,float z)=>x<0||z<0?0:AtCell(town,(int)x,(int)z,x,z);
    public static bool Traverse(Town town,float ax,float az,float bx,float bz)
    {
        if(!town.Inside((int)ax,(int)az)||!town.Inside((int)bx,(int)bz)||ax<0||az<0||bx<0||bz<0)return false;
        // Sloped banks are still water footprints. Clip exactly so a short
        // diagonal cannot slip through a wet corner between height samples.
        for(int cz=(int)Math.Min(az,bz);cz<=(int)Math.Max(az,bz);cz++)for(int cx=(int)Math.Min(ax,bx);cx<=(int)Math.Max(ax,bx);cx++){
            if(!WaterSurface.IsWater(town,cx,cz))continue;float lo=0,hi=1,dx=bx-ax,dz=bz-az;
            if(!Clip(ax,dx,cx,cx+1,ref lo,ref hi)||!Clip(az,dz,cz,cz+1,ref lo,ref hi)||hi-lo<.000001f)continue;
            float mid=(lo+hi)*.5f;if((int)(ax+dx*mid)==cx&&(int)(az+dz*mid)==cz)return false;
        }
        // Exact clipping catches even a very short passage through a high
        // triangle at a corner; sampling alone can tunnel across its tip.
        for(int oz=(int)Math.Min(az,bz)/2*2;oz<=(int)Math.Max(az,bz)/2*2;oz+=2)for(int ox=(int)Math.Min(ax,bx)/2*2;ox<=(int)Math.Max(ax,bx)/2*2;ox+=2){
            int mode=Mode(town,ox,oz);if(mode<CornerUpLeft)continue;
            float lo=0,hi=1,dx=bx-ax,dz=bz-az;
            if(!Clip(ax,dx,ox,ox+2,ref lo,ref hi)||!Clip(az,dz,oz,oz+2,ref lo,ref hi))continue;
            float a=Side(mode,ax+dx*lo-ox,az+dz*lo-oz),b=Side(mode,ax+dx*hi-ox,az+dz*hi-oz);
            if(Math.Min(a,b)<-.00001f&&Math.Max(a,b)>=0)return false;
        }
        float distance=(float)Math.Sqrt((bx-ax)*(bx-ax)+(bz-az)*(bz-az));int steps=Math.Max(1,(int)Math.Ceiling(distance/.2f));float previous=At(town,ax,az);
        for(int i=1;i<=steps;i++){float x=ax+(bx-ax)*i/steps,z=az+(bz-az)*i/steps;if(!town.Walkable((int)x,(int)z))return false;float height=At(town,x,z);if(Math.Abs(height-previous)>distance/steps*1.1f+.04f)return false;previous=height;}return true;
    }
    private static float Side(int mode,float x,float z)=>mode==6?x+z-2:mode==7?z-x:mode==8?x-z:2-x-z;
    private static bool Clip(float a,float d,float min,float max,ref float lo,ref float hi)
    {if(Math.Abs(d)<.000001f)return a>=min&&a<=max;float p=(min-a)/d,q=(max-a)/d;if(p>q){float v=p;p=q;q=v;}lo=Math.Max(lo,p);hi=Math.Min(hi,q);return lo<=hi;}
    public static void Generate(Town town)
    {
        Array.Clear(town.Elevation,0,town.Elevation.Length);uint random=unchecked((uint)town.Seed^0xb5297a4du);int placed=0,start=(int)(random%24);
        for(int n=0;n<24&&placed<3;n++){
            int candidate=(start+n*5)%24,col=candidate%8,row=2+candidate/8;if(col<4&&row==2)continue;
            int x=col*20+4,z=row*20+2;bool clear=true;
            for(int dz=0;dz<16&&clear;dz++)for(int dx=0;dx<14;dx++)if(town.At(x+dx,z+dz)!=(int)Terrain.Grass||town.InReserve(x+dx,z+dz)){clear=false;break;}
            if(!clear)continue;
            for(int dz=0;dz<12;dz++)for(int dx=0;dx<12;dx++)town.Elevation[(z+dz)*Town.Width+x+dx]=Block;
            Corner(town,x,z,CornerUpLeft);Corner(town,x+10,z,CornerUpRight);
            Corner(town,x,z+10,CornerDownLeft);Corner(town,x+10,z+10,CornerDownRight);
            for(int dz=12;dz<14;dz++)for(int dx=4;dx<6;dx++)town.Elevation[(z+dz)*Town.Width+x+dx]=RampNorth;
            placed++;
        }
    }
    private static void Corner(Town town,int x,int z,byte mode)
    {for(int dz=0;dz<2;dz++)for(int dx=0;dx<2;dx++)town.Elevation[(z+dz)*Town.Width+x+dx]=mode;}
}
