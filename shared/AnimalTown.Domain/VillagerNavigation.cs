namespace AnimalTown;

// One incremental terrain planner is shared by six agents (128 expansions/tick).
// Structures are ignored by that long-range plan. Local detours are only built
// when an obstacle enters a 1.2m look-ahead, using a bounded 13x13 search.
public sealed class VillagerNavigation
{
    public sealed class Route
    {
        public readonly int[] Path=new int[512],Detour=new int[169];
        internal readonly uint[] Observed=new uint[(Town.Cells+31)/32];
        public int Length,Index,LocalLength,LocalIndex,Resume,GoalX,GoalZ,GoalKind,Wait;
        internal int InitialGoal=-1;
        internal bool Replan;
        internal int Stuck;
        public bool Moving;
        internal uint Random;
    }
    public readonly Route[] Routes=new Route[6];
    public readonly bool[] Paused=new bool[6];
    private readonly ushort[] parent=new ushort[Town.Cells],queue=new ushort[Town.Cells];
    private readonly bool[] trees=new bool[Town.Cells];
    private readonly int[] localParent=new int[169],localQueue=new int[169];
    private GameState? state;private int worker=-1,front,back,start,tick,treeCount=-1,placedCount=-1,plotStamp;
    public int LastExpansions,LocalPlans;
    public VillagerNavigation(){for(int i=0;i<6;i++)Routes[i]=new Route();}
    private static int Next(Route r,int maximum){r.Random=unchecked(r.Random*1664525u+1013904223u);return (int)((r.Random>>8)%(uint)maximum);}
    public void Tick(GameState game)
    {
        if(state!=game){
            state=game;worker=-1;tick=0;treeCount=-1;for(int i=0;i<6;i++){var r=Routes[i];r.Length=r.LocalLength=0;r.Replan=false;r.Stuck=0;r.Wait=i*15;r.InitialGoal=i;r.Random=unchecked((uint)game.Town.Seed+(uint)(i+1)*747796405u);}
            for(int i=5;i>0;i--){int j=Next(Routes[0],i+1),swap=Routes[i].InitialGoal;Routes[i].InitialGoal=Routes[j].InitialGoal;Routes[j].InitialGoal=swap;}
        }
        tick++;LastExpansions=0;var town=game.Town;
        if(treeCount!=town.GroundCount||placedCount!=town.PlacedCount||plotStamp!=town.HouseZ*Town.Width+town.HouseX||tick%30==1){
            Array.Clear(trees,0,trees.Length);for(int i=0;i<town.GroundCount;i++){var g=town.Ground[i];if(g.Kind==3&&town.Inside(g.X,g.Z))trees[g.Z*Town.Width+g.X]=true;}
            for(int i=0;i<town.PlacedCount;i++){var p=town.Placed[i];if(p.Room!=0)continue;int w=Catalog.Items[p.Item].Width,h=Catalog.Items[p.Item].Height;if((p.Rotation&1)!=0){int swap=w;w=h;h=swap;}for(int z=p.Z;z<p.Z+h;z++)for(int x=p.X;x<p.X+w;x++)if(town.Inside(x,z))trees[z*Town.Width+x]=true;}
            treeCount=town.GroundCount;placedCount=town.PlacedCount;plotStamp=town.HouseZ*Town.Width+town.HouseX;
        }
        if(worker>=0&&Paused[worker])worker=-1;
        if(worker>=0)Search();
        for(int i=0;i<6;i++){
            var r=Routes[i];r.Moving=false;if(Paused[i]||town.Residents[i].Space!=0)continue;if(r.Wait>0){r.Wait--;continue;}
            if(r.Index>=r.Length&&r.LocalIndex>=r.LocalLength){if(worker<0){if(!r.Replan)Goal(i);r.Replan=false;Begin(i);}continue;}
            if(town.Residents[i].Space==0)Walk(i);
        }
    }
    public void Target(int agent,int x,int z)
    {var r=Routes[agent];if(worker==agent)worker=-1;r.GoalX=x;r.GoalZ=z;r.Length=r.Index=r.LocalIndex=r.LocalLength=0;r.Replan=true;r.Wait=0;Array.Clear(r.Observed,0,r.Observed.Length);}
    // An empty route can still be queued or searching across multiple ticks.
    // Journey callers must not restart that work every frame.
    public bool Planning(int agent)=>worker==agent||Routes[agent].Replan;
    public void Stop(int agent)
    {if(worker==agent)worker=-1;var r=Routes[agent];r.Length=r.Index=r.LocalLength=r.LocalIndex=0;r.Wait=2;r.Moving=false;}
    private void Goal(int agent)
    {
        var r=Routes[agent];var town=state!.Town;r.GoalKind=r.InitialGoal>=0?r.InitialGoal:Next(r,6);r.InitialGoal=-1;
        Array.Clear(r.Observed,0,r.Observed.Length);r.Stuck=0;
        int target=Next(r,6);if(target==agent)target=(target+1)%6;
        r.GoalX=r.GoalKind==0?32:r.GoalKind==1?50:town.Residents[target].HomeX+1;
        r.GoalZ=r.GoalKind<2?28:town.Residents[target].HomeZ+4;
        int tile=Next(r,3);tile=tile==0?(int)Terrain.Camp:tile==1?(int)Terrain.Plaza:(int)Terrain.Grass;
        if(r.GoalKind>=3)for(int n=0;n<1024;n++){
            int x=2+Next(r,Town.Width-4),z=2+Next(r,Town.Height-4),t=town.At(x,z);bool wanted=r.GoalKind==3?t==(int)Terrain.Beach:r.GoalKind==4?t==tile:t==(int)Terrain.Grass&&(town.At(x-1,z)==1||town.At(x+1,z)==1||town.At(x,z-1)==1||town.At(x,z+1)==1);
            if(wanted&&!Blocked(x,z,agent,false)){r.GoalX=x;r.GoalZ=z;break;}
        }
    }
    private bool Ground(int x,int z)
    {var town=state!.Town;int t=town.At(x,z);return town.Inside(x,z)&&!WaterSurface.IsWater(town,x,z)&&!WaterTerrain.IsCoast(t)&&t!=(int)Terrain.Cliff;}
    private bool Step(int ax,int az,int bx,int bz,bool structures,int agent)
    {
        if(!Ground(bx,bz)||structures&&Blocked(bx,bz,agent,true))return false;var town=state!.Town;
        int cell=bz*Town.Width+bx;if(!structures&&(Routes[agent].Observed[cell/32]&(1u<<(cell%32)))!=0)return false;
        if(TerrainElevation.Mode(town,ax,az)==0&&TerrainElevation.Mode(town,bx,bz)==0)return true;
        return TerrainElevation.Traverse(town,ax+.5f,az+.5f,bx+.5f,bz+.5f);
    }
    private bool Blocked(int x,int z,int agent,bool people)
    {
        var town=state!.Town;if(!town.Walkable(x,z)||trees[z*Town.Width+x])return true;
        if(state.HomeBlocks(x,z))return true;
        if(people){for(int i=0;i<6;i++)if(i!=agent){var v=town.Residents[i];if(v.Space==0&&Math.Abs(v.PosX-x-.5f)<.7f&&Math.Abs(v.PosZ-z-.5f)<.7f)return true;}
            if(state.Space==0&&Math.Abs(state.X-x-.5f)<.7f&&Math.Abs(state.Z-z-.5f)<.7f)return true;}
        return false;
    }
    private void Begin(int agent)
    {
        worker=agent;var v=state!.Town.Residents[agent];start=(int)v.PosZ*Town.Width+(int)v.PosX;Array.Clear(parent,0,parent.Length);front=0;back=1;queue[0]=(ushort)start;parent[start]=(ushort)(start+1);
    }
    private void Search()
    {
        var r=Routes[worker];int goal=r.GoalZ*Town.Width+r.GoalX;
        while(front<back&&LastExpansions<128){int cell=queue[front++];LastExpansions++;
            if(cell==goal){int length=0;while(cell!=start&&length<r.Path.Length){r.Path[length++]=cell;cell=parent[cell]-1;}if(cell!=start)length=0;for(int a=0;a<length/2;a++){int value=r.Path[a];r.Path[a]=r.Path[length-1-a];r.Path[length-1-a]=value;}r.Length=length;r.Index=r.LocalIndex=r.LocalLength=0;if(length==0)r.Wait=60;worker=-1;return;}
            int x=cell%Town.Width,z=cell/Town.Width;for(int d=0;d<4;d++){int nx=x+(d==0?-1:d==1?1:0),nz=z+(d==2?-1:d==3?1:0);if(!Step(x,z,nx,nz,false,worker))continue;int next=nz*Town.Width+nx;if(parent[next]!=0)continue;parent[next]=(ushort)(cell+1);queue[back++]=(ushort)next;}
        }
        if(front==back){r.Length=r.Index=0;r.Wait=60;worker=-1;}
    }
    private bool Detour(int agent)
    {
        LocalPlans++;var r=Routes[agent];var v=state!.Town.Residents[agent];int originX=(int)v.PosX-6,originZ=(int)v.PosZ-6,target=-1;
        for(int n=r.Index;n<r.Length&&n<r.Index+16;n++){int x=r.Path[n]%Town.Width,z=r.Path[n]/Town.Width;if(x>=originX&&x<originX+13&&z>=originZ&&z<originZ+13&&!Blocked(x,z,agent,true)){target=(z-originZ)*13+x-originX;r.Resume=n+1;}}
        if(target<0)return false;for(int i=0;i<169;i++)localParent[i]=-1;int f=0,b=1;localQueue[0]=84;localParent[84]=84;
        while(f<b){int cell=localQueue[f++];if(cell==target){int count=0;while(cell!=84){r.Detour[count++]=(originZ+cell/13)*Town.Width+originX+cell%13;cell=localParent[cell];}for(int i=0;i<count/2;i++){int value=r.Detour[i];r.Detour[i]=r.Detour[count-1-i];r.Detour[count-1-i]=value;}r.LocalLength=count;r.LocalIndex=0;return count>0;}
            int x=cell%13,z=cell/13;for(int d=0;d<4;d++){int nx=x+(d==0?-1:d==1?1:0),nz=z+(d==2?-1:d==3?1:0);if(nx<0||nz<0||nx>=13||nz>=13)continue;int next=nz*13+nx;if(localParent[next]>=0||!Step(originX+x,originZ+z,originX+nx,originZ+nz,true,agent))continue;localParent[next]=cell;localQueue[b++]=next;}
        }return false;
    }
    private void Walk(int agent)
    {
        var r=Routes[agent];var v=state!.Town.Residents[agent];bool local=r.LocalIndex<r.LocalLength;int cell=local?r.Detour[r.LocalIndex]:r.Path[r.Index];float tx=cell%Town.Width+.5f,tz=cell/Town.Width+.5f,dx=tx-v.PosX,dz=tz-v.PosZ,distance=(float)Math.Sqrt(dx*dx+dz*dz);
        if(distance<.045f){v.PosX=tx;v.PosZ=tz;if(local){r.LocalIndex++;if(r.LocalIndex==r.LocalLength)r.Index=r.Resume;}else r.Index++;if(r.Index>=r.Length){r.Wait=90+Next(r,180);r.Length=r.Index=0;}v.X=(int)v.PosX;v.Z=(int)v.PosZ;return;}
        float step=Math.Min(.04f,distance),nx=v.PosX+dx/distance*step,nz=v.PosZ+dz/distance*step;
        // Deviation planning is triggered only by the next 1.2 metres, never by
        // a tree/building several acres away. Moving crowds are resampled here.
        float look=Math.Min(1.2f,distance);int lx=(int)(v.PosX+dx/distance*look),lz=(int)(v.PosZ+dz/distance*look);
        if(Blocked(lx,lz,agent,true)||Blocked((int)nx,(int)nz,agent,true)){
            if(!Detour(agent)){
                int learned=0;for(int z=(int)v.PosZ-6;z<=(int)v.PosZ+6;z++)for(int x=(int)v.PosX-6;x<=(int)v.PosX+6;x++)if(Ground(x,z)&&Blocked(x,z,agent,false)){
                    int known=z*Town.Width+x;uint bit=1u<<(known%32);if((r.Observed[known/32]&bit)==0){r.Observed[known/32]|=bit;learned++;}
                }
                if(learned>0){r.Length=r.Index=r.LocalLength=r.LocalIndex=0;r.Replan=true;r.Wait=1;}
                else{r.Wait=15+agent*3;if(++r.Stuck>8){r.Index=r.Length;r.LocalIndex=r.LocalLength;r.Wait=30;}}
            }return;
        }
        if(!TerrainElevation.Traverse(state.Town,v.PosX,v.PosZ,nx,nz)){r.Index=r.Length;r.LocalIndex=r.LocalLength;r.Wait=30;return;}
        v.PosX=nx;v.PosZ=nz;v.X=(int)nx;v.Z=(int)nz;v.Yaw=PlayerMovement.Yaw(dx,dz);r.Moving=true;r.Stuck=0;
    }
}
