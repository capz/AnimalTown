namespace AnimalTown;

// Acre-template generation adapted to our flat 8x8 town. The GameCube game's
// river tracing / required-feature / template-variant stages are retained;
// Nintendo's acre data, 5x6 dimensions and elevation layouts are not imported.
public static class TownGenerator
{
    public const int Version=1;
    private const int N=1,E=2,S=4,W=8,Size=Town.ScreenTileSize;
    private static int Next(ref uint random,int limit)
    {random=unchecked(random*1664525u+1013904223u);return (int)((random>>8)%(uint)limit);}
    private static void Paint(Town town,int x,int z,Terrain value)
    {if(town.Inside(x,z))town.Tiles[z*Town.Width+x]=(byte)value;}
    private static void Stroke(Town town,int ax,int az,int bx,int bz)
    {
        int steps=Math.Max(Math.Abs(bx-ax),Math.Abs(bz-az));
        for(int i=0;i<=steps;i++){
            int x=steps==0?ax:ax+(bx-ax)*i/steps,z=steps==0?az:az+(bz-az)*i/steps;
            for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++)Paint(town,x+dx,z+dz,Terrain.Water);
        }
    }
    private static void RiverAcre(Town town,int column,int row,int gates,int variant)
    {
        int x=column*Size,z=row*Size;
        // All variants share the same three-cell-wide boundary sockets. These
        // authored control-point templates cannot create mismatched seams.
        if(gates==(N|S)){
            int bend=variant==0?10:variant==1?7:13;
            Stroke(town,x+10,z,x+bend,z+6);Stroke(town,x+bend,z+6,x+bend,z+13);Stroke(town,x+bend,z+13,x+10,z+19);return;
        }
        if(gates==(E|W)){
            int bend=variant==0?10:variant==1?7:13;
            Stroke(town,x,z+10,x+6,z+bend);Stroke(town,x+6,z+bend,x+13,z+bend);Stroke(town,x+13,z+bend,x+19,z+10);return;
        }
        int cx=x+10,cz=z+10;
        // Corner templates retain a shared centre; variants move only interior
        // control points, never their edge gates.
        if((gates&N)!=0)Stroke(town,cx,z,cx,cz);
        if((gates&S)!=0)Stroke(town,cx,cz,cx,z+19);
        if((gates&E)!=0)Stroke(town,cx,cz,x+19,cz);
        if((gates&W)!=0)Stroke(town,x,cz,cx,cz);
    }
    internal static void Landscape(Town town)
    {
        uint random=unchecked((uint)town.Seed^0x9e3779b9u);
        for(int i=0;i<Town.Cells;i++)town.Tiles[i]=(byte)Terrain.Grass;
        // Flat shoreline templates, with identical edge heights and a choice
        // of inward/outward shallow bays. Ocean occupies the southern two rows.
        for(int column=0;column<Town.ScreenTiles;column++){
            int bay=Next(ref random,3)-1;
            for(int local=0;local<Size;local++){
                int x=column*Size+local,shore=Town.Height-8+bay*(local>=5&&local<15?2:0);
                for(int z=shore;z<Town.Height;z++)Paint(town,x,z,z>=Town.Height-2?Terrain.Water:Terrain.Beach);
            }
        }
        var gates=new byte[Town.ScreenTiles*Town.ScreenTiles];
        int col=4+Next(ref random,3);
        for(int row=0;row<Town.ScreenTiles;row++){
            int minimum=row<3?4:1,target=col;
            // Straight upper/lower bridge acres are mandatory. Other rows may
            // trace east or west before descending, with no northward cycles.
            if(row!=0&&row!=3&&row!=6&&row!=7)target=Math.Max(minimum,Math.Min(6,col+Next(ref random,5)-2));
            int direction=target>col?1:target<col?-1:0;
            if(direction==0)gates[row*8+col]=(byte)(N|S);
            else{
                gates[row*8+col]=(byte)(N|(direction>0?E:W));
                for(int next=col+direction;next!=target;next+=direction)gates[row*8+next]=(byte)(E|W);
                gates[row*8+target]=(byte)((direction>0?W:E)|S);
            }
            col=target;
        }
        for(int row=0;row<8;row++)for(int column=0;column<8;column++){
            int mask=gates[row*8+column];if(mask!=0)RiverAcre(town,column,row,mask,Next(ref random,3));
        }
        // Bridge features are assigned after river topology, as in the original
        // staged generator. Wood floor tiles are temporary art, with real collision.
        for(int row=3;row<=6;row+=3)for(int column=0;column<8;column++)if(gates[row*8+column]==(N|S))
            for(int z=row*Size+9;z<=row*Size+11;z++)for(int x=column*Size+2;x<column*Size+18;x++)if(town.At(x,z)==(int)Terrain.Water)Paint(town,x,z,Terrain.Bridge);
        // Pond templates only occupy otherwise-flat, non-service inland acres.
        int ponds=2+Next(ref random,2),placed=0,pondStart=Next(ref random,32);
        for(int attempt=0;attempt<32&&placed<ponds;attempt++){
            int candidate=(pondStart+attempt*13)%32,column=candidate%8,row=3+candidate/8,cell=row*8+column;
            if(gates[cell]!=0||town.At(column*Size+10,row*Size+10)!=(int)Terrain.Grass)continue;
            int rx=3+Next(ref random,3),rz=3+Next(ref random,2);
            for(int dz=-rz;dz<=rz;dz++)for(int dx=-rx;dx<=rx;dx++)if(dx*dx*rz*rz+dz*dz*rx*rx<=rx*rx*rz*rz)Paint(town,column*Size+10+dx,row*Size+10+dz,Terrain.Water);
            placed++;
        }
        // World boundaries are barriers; the river mouth still joins the sea.
        for(int x=0;x<Town.Width;x++){Paint(town,x,0,Terrain.Water);Paint(town,x,Town.Height-1,Terrain.Water);}
        for(int z=0;z<Town.Height;z++){Paint(town,0,z,Terrain.Water);Paint(town,Town.Width-1,z,Terrain.Water);}
    }
    internal static void Populate(Town town)
    {
        uint random=unchecked((uint)town.Seed^0xa341316cu);
        for(int attempt=0;attempt<4000&&town.GroundCount<320;attempt++){
            int x=2+Next(ref random,Town.Width-4),z=17+Next(ref random,Town.Height-27);
            if(town.At(x,z)!=(int)Terrain.Grass||town.InReserve(x,z)||x<80&&z<42)continue;
            // Leave acre travel sockets and bridge approaches clear. Trees also
            // keep a one-cell clearance from water, structures and other trees.
            if(x%Size>=8&&x%Size<=12||z%Size>=8&&z%Size<=12)continue;
            bool clear=true;for(int dz=-1;dz<=1&&clear;dz++)for(int dx=-1;dx<=1;dx++)if(town.At(x+dx,z+dz)!=(int)Terrain.Grass||town.FindGround(x+dx,z+dz,3)>=0){clear=false;break;}
            if(clear)town.AddGround(1+Next(ref random,3),x,z,3,0);
        }
        int shells=0,start=Next(ref random,Town.Width-4);
        for(int i=0;i<Town.Width-4&&shells<2;i++){
            int x=2+(start+i)%(Town.Width-4),z=Town.Height-5-i%2;
            if(town.At(x,z)==(int)Terrain.Beach&&town.AddGround(17,x,z,0,0))shells++;
        }
    }
}
