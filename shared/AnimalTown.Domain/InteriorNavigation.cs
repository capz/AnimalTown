namespace AnimalTown;

// One 32x32 scratch search shared by all occupants. No per-frame allocation.
public sealed class InteriorNavigation
{
    private sealed class Route
    {public readonly ushort[] Path=new ushort[512];public int Space=-1,Goal=-1,Count,Index,Wait;public bool Arrived;}
    private readonly Route[] routes=new Route[BuildingWorld.Actors];
    private readonly ushort[] parents=new ushort[1024],queue=new ushort[1024];
    private int worker=-1,front,back,start,cells,space;
    public int LastExpansions {get;private set;}
    public InteriorNavigation(){for(int i=0;i<routes.Length;i++)routes[i]=new Route();}
    public void BeginTick(){LastExpansions=0;}
    public void Reset(int actor){var r=routes[actor];r.Space=r.Goal=-1;r.Count=r.Index=0;r.Arrived=false;if(worker==actor)worker=-1;}
    public bool Arrived(int actor)=>routes[actor].Arrived;
    public bool Tick(GameState s,int actor,float goalX,float goalZ)
    {
        var p=BuildingWorld.Location(s,actor);var r=routes[actor];if(p.Space.IsTown){Reset(actor);return false;}
        int n=BuildingWorld.Size(s,p.Space.Value)*2,gx=Math.Min(n-1,Math.Max(0,(int)(goalX*2))),gz=Math.Min(n-1,Math.Max(0,(int)(goalZ*2))),goal=gz*n+gx;
        if(r.Space!=p.Space.Value||r.Goal!=goal){Reset(actor);r.Space=p.Space.Value;r.Goal=goal;r.Wait=0;}
        if(r.Wait>0){r.Wait--;return false;}
        float dx=goalX-p.X,dz=goalZ-p.Z;if(dx*dx+dz*dz<.18f){r.Arrived=true;return false;}r.Arrived=false;
        if(r.Index>=r.Count){
            if(worker<0){
                cells=n;space=p.Space.Value;start=(int)(p.Z*2)*n+(int)(p.X*2);if(start<0||start>=n*n)return false;
                worker=actor;front=0;back=1;Array.Clear(parents,0,parents.Length);parents[start]=(ushort)(start+1);queue[0]=(ushort)start;
            }
            if(worker==actor)Search(s,actor);return false;
        }
        int cell=r.Path[r.Index];float tx=cell%n*.5f+.25f,tz=cell/n*.5f+.25f;dx=tx-p.X;dz=tz-p.Z;float length=(float)Math.Sqrt(dx*dx+dz*dz);
        if(length<.035f){r.Index++;return false;}float step=Math.Min(.04f,length),nx=p.X+dx/length*step,nz=p.Z+dz/length*step;
        if(!BuildingWorld.Segment(s,p.Space.Value,p.X,p.Z,nx,nz,actor,true)){r.Count=r.Index=0;r.Wait=15+actor;return false;}
        BuildingWorld.SetLocation(s,actor,new ActorLocation(p.Space.Value,nx,nz,PlayerMovement.Yaw(dx,dz)));return true;
    }
    private void Search(GameState s,int actor)
    {
        var r=routes[actor];
        while(front<back&&LastExpansions<128){
            int cell=queue[front++];LastExpansions++;
            if(cell==r.Goal){int length=0;while(cell!=start&&length<r.Path.Length){r.Path[length++]=(ushort)cell;cell=parents[cell]-1;}
                if(cell!=start){r.Wait=60;length=0;}for(int a=0;a<length/2;a++){ushort value=r.Path[a];r.Path[a]=r.Path[length-1-a];r.Path[length-1-a]=value;}
                r.Count=length;r.Index=0;worker=-1;return;
            }
            int x=cell%cells,z=cell/cells;for(int d=0;d<4;d++){
                int nx=x+(d==0?-1:d==1?1:0),nz=z+(d==2?-1:d==3?1:0);if(nx<0||nz<0||nx>=cells||nz>=cells)continue;int next=nz*cells+nx;if(parents[next]!=0)continue;
                var actorPosition=BuildingWorld.Location(s,actor);
                float ax=cell==start?actorPosition.X:x*.5f+.25f,az=cell==start?actorPosition.Z:z*.5f+.25f;
                if(!BuildingWorld.Segment(s,space,ax,az,nx*.5f+.25f,nz*.5f+.25f,actor,true))continue;
                parents[next]=(ushort)(cell+1);queue[back++]=(ushort)next;
            }
        }
        if(front==back){r.Wait=60;r.Count=r.Index=0;worker=-1;}
    }
}
