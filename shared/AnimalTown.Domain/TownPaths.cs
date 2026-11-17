namespace AnimalTown;

// An authoritative overlay: terrain, water, collisions and planting semantics
// keep their existing identities. Two bits per metre, independent of seasons.
public sealed class TownPaths
{
    public const int PackedBytes=Town.Cells/4,Material=FloorStrips.MaximumMaterials-1;
    public readonly byte[] Packed=new byte[PackedBytes];
    public int Unconnected {get;internal set;}
    public int At(int x,int z){if((uint)x>=Town.Width||(uint)z>=Town.Height)return 0;int cell=z*Town.Width+x;return (Packed[cell>>2]>>((cell&3)*2))&3;}
    public void Set(int x,int z,int style)
    {if((uint)x>=Town.Width||(uint)z>=Town.Height||(uint)style>1)throw new ArgumentOutOfRangeException();int cell=z*Town.Width+x,shift=(cell&3)*2;Packed[cell>>2]=(byte)((Packed[cell>>2]&~(3<<shift))|(style<<shift));}
    public void Clear(){Array.Clear(Packed,0,Packed.Length);Unconnected=0;}
    public int Count {get{int n=0;for(int i=0;i<Town.Cells;i++)if(((Packed[i>>2]>>((i&3)*2))&3)!=0)n++;return n;}}
    public static bool Surface(Town t,int x,int z)=>t.At(x,z)==(int)Terrain.Grass||t.At(x,z)==(int)Terrain.Camp;
    public static bool Painted(Town t,int x,int z)=>t.Paths.At(x,z)==1&&Surface(t,x,z)&&TerrainElevation.Mode(t,x,z)<TerrainElevation.CornerUpLeft;
    public void Validate(){foreach(byte b in Packed)for(int shift=0;shift<8;shift+=2)if(((b>>shift)&3)>1)throw new InvalidOperationException("Unsupported path style");}
}

// Runs once for new towns, after trees and natural ramps exist. Every main
// segment, bend and junction is a validated 2x2 footprint. BFS grows a compact
// connecting tree without demolishing trees, gardens or crossing water.
public static class TownPathPlanner
{
    // New home placement adds one narrow doorway spur. Existing saved towns
    // with an empty overlay stay empty; placing a home never replans the town.
    public static bool ConnectHome(GameState state)
    {
        var t=state.Town;if(t.Paths.Count==0)return false;
        int hx=t.HouseX,hz=t.HouseZ,start=(hz+4)*Town.Width+hx+3;
        var parent=new short[Town.Cells];Array.Fill(parent,(short)-2);var queue=new ushort[Town.Cells];var blocked=new byte[Town.Cells];
        for(int i=0;i<t.GroundCount;i++){var g=t.Ground[i];if(g.Kind!=2&&g.Kind!=3)continue;for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++)if(t.Inside(g.X+dx,g.Z+dz))blocked[(g.Z+dz)*Town.Width+g.X+dx]=1;}
        for(int i=0;i<t.PlacedCount;i++){var p=t.Placed[i];if(p.Room!=0)continue;BuildingWorld.Footprint(p,out int w,out int d);for(int dz=0;dz<d;dz++)for(int dx=0;dx<w;dx++)if(t.Inside(p.X+dx,p.Z+dz))blocked[(p.Z+dz)*Town.Width+p.X+dx]=1;}
        bool Allowed(int x,int z){
            if(!t.Inside(x,z)||!t.Walkable(x,z)||blocked[z*Town.Width+x]!=0||TerrainElevation.Mode(t,x,z)>=TerrainElevation.CornerUpLeft)return false;
            for(int p=0;p<GameState.ProfileCount;p++)if(state.HomePlaced(p)&&x>=state.HomeX(p)&&x<state.HomeX(p)+15&&z>=state.HomeZ(p)&&z<state.HomeZ(p)+13&&!(p==state.ActiveProfile&&x==hx+3&&z>=hz+4))return false;
            return true;
        }
        if(!Allowed(hx+3,hz+4)){t.Paths.Unconnected++;return false;}
        int head=0,tail=0,found=-1;queue[tail++]=(ushort)start;parent[start]=-1;
        while(head<tail){int cell=queue[head++],x=cell%Town.Width,z=cell/Town.Width;if(t.Paths.At(x,z)!=0){found=cell;break;}
            for(int dir=0;dir<4;dir++){int next=cell+(dir==0?-1:dir==1?1:dir==2?-Town.Width:Town.Width);if(next<0||next>=Town.Cells||parent[next]!=-2)continue;int nx=next%Town.Width,nz=next/Town.Width;if(Math.Abs(nx-x)+Math.Abs(nz-z)!=1||!Allowed(nx,nz)||!TerrainElevation.Traverse(t,x+.5f,z+.5f,nx+.5f,nz+.5f))continue;parent[next]=(short)cell;queue[tail++]=(ushort)next;}
        }
        if(found<0){t.Paths.Unconnected++;return false;}
        for(int at=found;at>=0;at=parent[at]){int x=at%Town.Width,z=at/Town.Width;if(TownPaths.Surface(t,x,z))t.Paths.Set(x,z,1);}t.NotifyTerrainChanged();return true;
    }
    public static void Generate(Town t)
    {
        t.Paths.Clear();var planner=new Planner(t);
        planner.Root(32,30);
        planner.Connect(32,28);planner.Connect(50,28);
        for(int i=0;i<6;i++)planner.Connect(t.Residents[i].HomeX+1,t.Residents[i].HomeZ+3);
        planner.Connect(65,54);
        // Existing bridge landings are anchors; bridge decking stays intact.
        for(int z=1;z<Town.Height-2;z++)for(int x=1;x<Town.Width-2;x++)
            if(t.At(x,z)==(int)Terrain.Bridge&&t.At(x-1,z)!=(int)Terrain.Bridge&&t.At(x,z-1)!=(int)Terrain.Bridge)planner.Connect(x,z);
    }
    private sealed class Planner
    {
        private readonly Town town;
        private readonly byte[] allowed=new byte[Town.Cells],network=new byte[Town.Cells];
        private readonly short[] parent=new short[Town.Cells];
        private readonly ushort[] queue=new ushort[Town.Cells];
        internal Planner(Town t)
        {
            town=t;var blocked=new byte[Town.Cells];
            for(int i=0;i<t.GroundCount;i++){var g=t.Ground[i];if(g.Kind!=2&&g.Kind!=3)continue;for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++)if(t.Inside(g.X+dx,g.Z+dz))blocked[(g.Z+dz)*Town.Width+g.X+dx]=1;}
            for(int z=1;z<Town.Height-2;z++)for(int x=1;x<Town.Width-2;x++){
                bool ok=true;for(int dz=0;dz<2;dz++)for(int dx=0;dx<2;dx++){
                    int xx=x+dx,zz=z+dz,kind=t.At(xx,zz);
                    if(blocked[zz*Town.Width+xx]!=0||t.InReserve(xx,zz)||!t.Walkable(xx,zz)||TerrainElevation.Mode(t,xx,zz)>=TerrainElevation.CornerUpLeft||kind==(int)Terrain.Beach)ok=false;
                }
                if(ok&&TerrainElevation.Traverse(t,x+.5f,z+.5f,x+1.5f,z+.5f)&&TerrainElevation.Traverse(t,x+.5f,z+1.5f,x+1.5f,z+1.5f)&&TerrainElevation.Traverse(t,x+.5f,z+.5f,x+.5f,z+1.5f)&&TerrainElevation.Traverse(t,x+1.5f,z+.5f,x+1.5f,z+1.5f))allowed[z*Town.Width+x]=1;
            }
        }
        internal void Root(int x,int z){if(allowed[z*Town.Width+x]!=0)Paint(z*Town.Width+x);}
        internal void Connect(int x,int z)
        {
            // Move a blocked anchor to a nearby walkable apron, never clear it.
            int start=-1;
            for(int radius=0;radius<=3&&start<0;radius++)for(int dz=-radius;dz<=radius&&start<0;dz++)for(int dx=-radius;dx<=radius;dx++){
                int xx=x+dx,zz=z+dz;if(town.Inside(xx,zz)&&allowed[zz*Town.Width+xx]!=0){start=zz*Town.Width+xx;break;}
            }
            if(start<0){town.Paths.Unconnected++;return;}
            Array.Fill(parent,(short)-2);int head=0,tail=0;queue[tail++]=(ushort)start;parent[start]=-1;int found=-1;
            while(head<tail){
                int cell=queue[head++];if(network[cell]!=0){found=cell;break;}
                for(int d=0;d<4;d++){
                    int next=cell+(d==0?-1:d==1?1:d==2?-Town.Width:Town.Width);
                    if(next<0||next>=Town.Cells||parent[next]!=-2||allowed[next]==0||!Step(cell,next))continue;
                    parent[next]=(short)cell;queue[tail++]=(ushort)next;
                }
            }
            if(found<0){town.Paths.Unconnected++;return;}
            for(int at=found;at>=0;at=parent[at])Paint(at);
        }
        private bool Step(int a,int b)
        {
            int ax=a%Town.Width,az=a/Town.Width,bx=b%Town.Width,bz=b/Town.Width;if(Math.Abs(ax-bx)+Math.Abs(az-bz)!=1)return false;
            for(int dz=0;dz<2;dz++)for(int dx=0;dx<2;dx++)if(!TerrainElevation.Traverse(town,ax+dx+.5f,az+dz+.5f,bx+dx+.5f,bz+dz+.5f))return false;
            return true;
        }
        private void Paint(int cell)
        {network[cell]=1;int x=cell%Town.Width,z=cell/Town.Width;for(int dz=0;dz<2;dz++)for(int dx=0;dx<2;dx++)if(TownPaths.Surface(town,x+dx,z+dz))town.Paths.Set(x+dx,z+dz,1);}
    }
}
