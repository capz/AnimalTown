namespace AnimalTown;

// One cached, world-aligned I8 mask. Each step emits at most one GX 8x4 tile;
// the game uploads only that 32-byte tile, then publishes the completed texture.
public sealed class CliffShadowMap
{
    public const int TextureSize=512,TexelsPerMetre=2,Opacity=66;
    public const float SunX=.6f,SunZ=.4f;
    public readonly byte[] Chunk=new byte[32];
    private readonly bool[] candidates=new bool[Town.Cells];
    private Town town=null!;
    private int sourceCell,tile;
    private const int Columns=(Town.Width*TexelsPerMetre+2+7)/8,Rows=(Town.Height*TexelsPerMetre+2+3)/4;
    public int Offset {get;private set;}
    public bool ChunkReady {get;private set;}
    public bool HasCoverage {get;private set;}
    public void Begin(Town source)
    {town=source;sourceCell=tile=0;ChunkReady=HasCoverage=false;Array.Clear(candidates,0,candidates.Length);}
    public bool Advance()
    {
        ChunkReady=false;
        if(sourceCell<Town.Cells){
            int end=Math.Min(Town.Cells,sourceCell+256);
            while(sourceCell<end){
                int cell=sourceCell++,x=cell%Town.Width,z=cell/Town.Width;
                if(TerrainElevation.Mode(town,x,z)==0&&!WaterTerrain.IsCoast(town.At(x,z)))continue;
                // A two-metre cliff reaches at most 1.2m east and .8m south.
                for(int dz=0;dz<=1;dz++)for(int dx=0;dx<=2;dx++)if(town.Inside(x+dx,z+dz))candidates[(z+dz)*Town.Width+x+dx]=true;
            }
            return false;
        }
        int limit=Math.Min(Columns*Rows,tile+16);
        while(tile<limit){
            int at=tile++,tx=at%Columns,tz=at/Columns;
            bool wanted=false;
            for(int z=Math.Max(0,tz*2-1);z<=Math.Min(Town.Height-1,tz*2+1)&&!wanted;z++)
                for(int x=Math.Max(0,tx*4-1);x<=Math.Min(Town.Width-1,tx*4+3);x++)if(candidates[z*Town.Width+x]){wanted=true;break;}
            if(!wanted)continue;
            Array.Clear(Chunk,0,Chunk.Length);
            for(int y=0;y<4;y++)for(int x=0;x<8;x++){
                float wx=(tx*8+x-.5f)/TexelsPerMetre,wz=(tz*4+y-.5f)/TexelsPerMetre;
                int hits=0;
                for(int sy=0;sy<2;sy++)for(int sx=0;sx<2;sx++)
                    if(Blocked(town,wx+(sx==0?-.125f:.125f),wz+(sy==0?-.125f:.125f)))hits++;
                byte alpha=(byte)(hits*Opacity/4);Chunk[y*8+x]=alpha;
                if(alpha!=0)ChunkReady=HasCoverage=true;
            }
            Offset=(tz*(TextureSize/8)+tx)*32;
            return tile>=Columns*Rows;
        }
        return tile>=Columns*Rows;
    }
    public static bool Blocked(Town town,float x,float z)
    {
        if(x<0||z<0||x>=Town.Width||z>=Town.Height||town.At((int)x,(int)z)==(int)Terrain.Building)return false;
        float receiver=GroundSurface.HeightAt(town,(int)x,(int)z,x,z);
        if(receiver>=TerrainElevation.Height-.02f)return false;
        // Sample toward the same fixed north-west sunlight as scenery shadows.
        // Sixteen height samples bound work and resolve below the texture's .5m pitch.
        for(int i=0;i<16;i++){
            float rise=(i+.5f)*((TerrainElevation.Height-receiver)/16);
            float px=x-SunX*rise,pz=z-SunZ*rise;
            if(px<0||pz<0||!town.Inside((int)px,(int)pz)||WaterSurface.IsWater(town,(int)px,(int)pz))continue;
            float h=TerrainElevation.At(town,px,pz);
            if(h>.02f&&h>receiver+rise+.02f)return true;
        }
        return false;
    }
}
