namespace AnimalTown;

// One section spans two fine cells. Blocks and single-section ramps both have
// a 2x2m footprint and a 2m rise. Values are persisted, never regenerated on load.
public static class TerrainElevation
{
    public const float Section=2,Height=2;
    public const byte Flat=0,Block=1,RampNorth=2,RampEast=3,RampSouth=4,RampWest=5;
    public static byte Mode(Town town,int x,int z)=>town.Inside(x,z)?town.Elevation[z*Town.Width+x]:(byte)0;
    public static float AtCell(Town town,int cellX,int cellZ,float x,float z)
    {
        int mode=Mode(town,cellX,cellZ);if(mode==0)return 0;if(mode==Block)return 2;
        float lx=Math.Max(0,Math.Min(2,x-cellX/2*2)),lz=Math.Max(0,Math.Min(2,z-cellZ/2*2));
        return mode==RampNorth?2-lz:mode==RampEast?lx:mode==RampSouth?lz:mode==RampWest?2-lx:0;
    }
    public static float At(Town town,float x,float z)=>x<0||z<0?0:AtCell(town,(int)x,(int)z,x,z);
    public static bool Traverse(Town town,float ax,float az,float bx,float bz)
    {
        if(Mode(town,(int)ax,(int)az)==0&&Mode(town,(int)bx,(int)bz)==0)return town.Walkable((int)bx,(int)bz);
        float distance=(float)Math.Sqrt((bx-ax)*(bx-ax)+(bz-az)*(bz-az));int steps=Math.Max(1,(int)Math.Ceiling(distance/.2f));float previous=At(town,ax,az);
        for(int i=1;i<=steps;i++){float x=ax+(bx-ax)*i/steps,z=az+(bz-az)*i/steps;if(!town.Walkable((int)x,(int)z))return false;float height=At(town,x,z);if(Math.Abs(height-previous)>distance/steps*1.1f+.04f)return false;previous=height;}return true;
    }
    public static void Generate(Town town)
    {
        Array.Clear(town.Elevation,0,town.Elevation.Length);uint random=unchecked((uint)town.Seed^0xb5297a4du);int placed=0,start=(int)(random%24);
        for(int n=0;n<24&&placed<3;n++){
            int candidate=(start+n*5)%24,col=candidate%8,row=2+candidate/8;if(col<4&&row==2)continue;
            int x=col*20+4,z=row*20+2;bool clear=true;
            for(int dz=0;dz<16&&clear;dz++)for(int dx=0;dx<14;dx++)if(town.At(x+dx,z+dz)!=(int)Terrain.Grass||town.InReserve(x+dx,z+dz)){clear=false;break;}
            if(!clear)continue;
            for(int dz=0;dz<12;dz++)for(int dx=0;dx<12;dx++)town.Elevation[(z+dz)*Town.Width+x+dx]=Block;
            for(int dz=12;dz<14;dz++)for(int dx=4;dx<6;dx++)town.Elevation[(z+dz)*Town.Width+x+dx]=RampNorth;
            placed++;
        }
    }
}
