namespace AnimalTown;

// A service plot is an overlay, like a player house. Keeping it separate from
// saved terrain lets older towns gain the building without repainting their
// paths, moving belongings or invalidating immutable terrain snapshots.
public sealed class KobanSite
{
    public const int Building=12,Room=26,Definition=2,PreferredX=40,PreferredZ=25,Width=3,Depth=3;
    public int X {get;private set;}=PreferredX;
    public int Z {get;private set;}=PreferredZ;
    public bool Enabled=>X>=0;
    public bool Contains(int x,int z)=>Enabled&&x>=X&&x<X+Width&&z>=Z&&z<Z+Depth;
    internal void Restore(int x,int z)
    {
        if((x==-1)!=(z==-1)||x<-1||z<-1||x>=0&&(x<1||z<1||x>Town.Width-Width-1||z>Town.Height-Depth-2))throw new InvalidOperationException("Invalid koban plot");
        X=x;Z=z;
    }
    internal void Migrate(GameState s)
    {
        X=Z=-1;
        for(int radius=0;radius<Math.Max(Town.Width,Town.Height);radius++){
            for(int dz=-radius;dz<=radius;dz++)for(int dx=-radius;dx<=radius;dx++){
                if(Math.Abs(dx)!=radius&&Math.Abs(dz)!=radius)continue;
                int x=PreferredX+dx,z=PreferredZ+dz;
                if(!Clear(s,x,z,true))continue;
                X=x;Z=z;return;
            }
        }
        // A completely occupied imported town remains loadable. Its items stay
        // intact rather than being displaced to make room for a service.
    }
    internal void Validate(GameState s)
    {if(Enabled&&!Clear(s,X,Z,false))throw new InvalidOperationException("Koban plot overlaps saved town content");}
    private static bool Clear(GameState s,int x,int z,bool migrating)
    {
        if(x<1||z<1||x>Town.Width-Width-1||z>Town.Height-Depth-2)return false;
        // The normal portal lands 1m beyond the approach socket, at z+4.5.
        // Keep both the doorstep and that final exit cell clear on migration.
        var t=s.Town;int depth=Depth+(migrating?2:0);
        for(int dz=0;dz<depth;dz++)for(int dx=0;dx<Width;dx++){
            int at=(z+dz)*Town.Width+x+dx,terrain=t.Tiles[at];
            if(terrain!=(int)Terrain.Grass&&terrain!=(int)Terrain.Plaza&&terrain!=(int)Terrain.Camp||t.Elevation[at]!=0||t.Paths.At(x+dx,z+dz)!=0)return false;
        }
        for(int p=0;p<GameState.ProfileCount;p++)if(s.HomePlaced(p)&&x<s.HomeX(p)+15&&x+Width>s.HomeX(p)&&z<s.HomeZ(p)+13&&z+depth>s.HomeZ(p))return false;
        for(int i=0;i<t.GroundCount;i++){
            var g=t.Ground[i];if(g.Kind!=4&&g.X>=x&&g.X<x+Width&&g.Z>=z&&g.Z<z+depth)return false;
        }
        for(int i=0;i<t.PlacedCount;i++){
            var p=t.Placed[i];if(p.Room!=0||!Catalog.Valid(p.Item))continue;
            BuildingWorld.Footprint(p,out int w,out int d);
            if(p.X<x+Width&&p.X+w>x&&p.Z<z+depth&&p.Z+d>z)return false;
        }
        if(migrating){
            for(int p=0;p<GameState.ProfileCount;p++){
                var v=s.Profiles[p];float px=p==s.ActiveProfile?s.X:v.X,pz=p==s.ActiveProfile?s.Z:v.Z;int space=p==s.ActiveProfile?s.Space:v.Space;
                if(v.Created&&space==0&&px>=x&&px<x+Width&&pz>=z&&pz<z+Depth)return false;
            }
            for(int i=0;i<t.Residents.Length;i++){var v=t.Residents[i];if(v.Space==0&&v.PosX>=x&&v.PosX<x+Width&&v.PosZ>=z&&v.PosZ<z+Depth)return false;}
            var guest=t.Jobs.GuestProfile;
            if(t.Jobs.ActiveVisitor&&guest!=null&&guest.Space==0&&guest.X>=x&&guest.X<x+Width&&guest.Z>=z&&guest.Z<z+Depth)return false;
        }
        return true;
    }
    public static bool InReach(GameState s)
    {
        if(s.Space!=Room)return false;
        var points=BuildingWorld.Definition(s,Building).Activities;
        for(int i=0;i<points.Length;i+=2){float dx=s.X-points[i],dz=s.Z-points[i+1];if(dx*dx+dz*dz<=1.44f)return true;}
        return false;
    }
}

public sealed partial class Town
{
    public readonly KobanSite Koban=new KobanSite();
}
