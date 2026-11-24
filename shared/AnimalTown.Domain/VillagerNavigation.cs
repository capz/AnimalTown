namespace AnimalTown;

// One incremental terrain planner is shared by the town's NPCs (128 expansions/tick).
// Structures are ignored by that long-range plan. Local detours are only built
// when an obstacle enters a 1.2m look-ahead, using a bounded 13x13 search.
public sealed class VillagerNavigation
{
    internal const int CrowdRadius=12;
    internal const int ResidentsPerCrossing=2;
    private const int AreaColumns=4,AreaRows=3,GoalKinds=6;
    private const int CrossingLookAhead=6,CrossingRadiusSquared=4,CrossingWaitTicks=24,MaximumCrossingWaits=3;
    public sealed class Route
    {
        public readonly int[] Path=new int[512],Detour=new int[169];
        internal readonly uint[] Observed=new uint[(Town.Cells+31)/32];
        public int Length,Index,LocalLength,LocalIndex,Resume,GoalX,GoalZ,GoalKind,Wait;
        internal int InitialGoal=-1;
        internal bool Replan,HasDestination;
        internal int Stuck;
        internal int CrossingPause,CrossingWaits;
        public bool Moving;
        internal uint Random;
    }
    public readonly Route[] Routes=new Route[Town.NpcActorCount];
    public readonly bool[] Paused=new bool[Town.NpcActorCount];
    private readonly ushort[] parent=new ushort[Town.Cells],queue=new ushort[Town.Cells];
    private readonly bool[] trees=new bool[Town.Cells];
    private readonly int[] localParent=new int[169],localQueue=new int[169];
    private readonly int[] areaPopulation=new int[AreaColumns*AreaRows];
    private GameState? state;private int worker=-1,nextPlanner,front,back,start,tick,treeCount=-1,placedCount=-1,plotStamp;
    public int LastExpansions,LocalPlans;
    public VillagerNavigation(){for(int i=0;i<Routes.Length;i++)Routes[i]=new Route();}
    private static int Next(Route r,int maximum){r.Random=unchecked(r.Random*1664525u+1013904223u);return (int)((r.Random>>8)%(uint)maximum);}
    public void Tick(GameState game,PortalSystem? doors=null)
    {
        if(state!=game){
            state=game;worker=-1;nextPlanner=tick=0;treeCount=-1;
            for(int i=0;i<Routes.Length;i++){
                var r=Routes[i];r.Length=r.Index=r.LocalLength=r.LocalIndex=0;r.Replan=r.HasDestination=false;
                r.Stuck=r.CrossingPause=r.CrossingWaits=0;r.Wait=i*15;r.InitialGoal=i%GoalKinds;r.Random=unchecked((uint)game.Town.Seed+(uint)(i+1)*747796405u);
            }
            for(int i=GoalKinds-1;i>0;i--){int j=Next(Routes[0],i+1),swap=Routes[i].InitialGoal;Routes[i].InitialGoal=Routes[j].InitialGoal;Routes[j].InitialGoal=swap;}
        }
        tick++;LastExpansions=0;var town=game.Town;
        if(treeCount!=town.GroundCount||placedCount!=town.PlacedCount||plotStamp!=town.HouseZ*Town.Width+town.HouseX||tick%30==1){
            Array.Clear(trees,0,trees.Length);for(int i=0;i<town.GroundCount;i++){var g=town.Ground[i];if(g.Kind==3&&town.Inside(g.X,g.Z))trees[g.Z*Town.Width+g.X]=true;}
            for(int i=0;i<town.PlacedCount;i++){var p=town.Placed[i];if(p.Room!=0)continue;int w=Catalog.Items[p.Item].Width,h=Catalog.Items[p.Item].Height;if((p.Rotation&1)!=0){int swap=w;w=h;h=swap;}for(int z=p.Z;z<p.Z+h;z++)for(int x=p.X;x<p.X+w;x++)if(town.Inside(x,z))trees[z*Town.Width+x]=true;}
            treeCount=town.GroundCount;placedCount=town.PlacedCount;plotStamp=town.HouseZ*Town.Width+town.HouseX;
        }
        if(worker>=0&&(Paused[worker]||!game.NpcPresent(worker)||town.NpcAt(worker).Space!=0))worker=-1;
        if(worker>=0)Search();
        int movingActors=0,earlierResidents=0;
        for(int i=0;i<Routes.Length;i++){
            var r=Routes[i];
            if(!Paused[i]&&r.Wait==0&&r.CrossingPause==0&&(r.Index<r.Length||r.LocalIndex<r.LocalLength))movingActors|=1<<(i+1);
        }
        int first=nextPlanner;
        for(int offset=0;offset<Routes.Length;offset++){
            int i=(first+offset)%Routes.Length;
            var r=Routes[i];r.Moving=false;if(Paused[i]||!game.NpcPresent(i)||town.NpcAt(i).Space!=0)continue;if(r.Wait>0){r.Wait--;continue;}
            if(r.CrossingPause>0){r.CrossingPause--;continue;}
            if(r.Index>=r.Length&&r.LocalIndex>=r.LocalLength){
                if(worker<0){
                    if(!r.Replan)Goal(i);
                    if(r.HasDestination){r.Replan=false;Begin(i);nextPlanner=(i+1)%Routes.Length;}
                    else r.Wait=60;
                }
                continue;
            }
            if(game.ResidentPresent(i)){
                // Give the earlier walkers room to pass. A limit per route keeps
                // a narrow bridge or doorway from holding a villager indefinitely.
                if(r.CrossingWaits<MaximumCrossingWaits&&SharesCrossing(r,earlierResidents)){
                    r.CrossingWaits++;r.CrossingPause=CrossingWaitTicks-1;
                    continue;
                }
                earlierResidents|=1<<i;
            }
            Walk(i,doors,movingActors);
        }
    }
    public void Target(int agent,int x,int z)
    {var r=Routes[agent];if(worker==agent)worker=-1;r.GoalX=x;r.GoalZ=z;r.Length=r.Index=r.LocalIndex=r.LocalLength=0;r.Replan=r.HasDestination=true;r.Wait=r.CrossingPause=r.CrossingWaits=0;Array.Clear(r.Observed,0,r.Observed.Length);}
    // An empty route can still be queued or searching across multiple ticks.
    // Journey callers must not restart that work every frame.
    public bool Planning(int agent)=>worker==agent||Routes[agent].Replan;
    public void Stop(int agent)
    {if(worker==agent)worker=-1;var r=Routes[agent];r.Length=r.Index=r.LocalLength=r.LocalIndex=r.CrossingPause=r.CrossingWaits=0;r.Wait=2;r.Moving=r.Replan=r.HasDestination=false;}
    private bool SharesCrossing(Route route,int earlierResidents)
    {
        if((earlierResidents&(earlierResidents-1))==0)return false;
        // Sample near-future waypoints at the same walking distance, including
        // any local obstacle detour that currently replaces part of a route.
        for(int step=0;step<CrossingLookAhead;step++){
            int cell=UpcomingCell(route,step);if(cell<0)break;
            int nearby=0;
            for(int npc=0;npc<Town.SavedVillagerCount;npc++){
                if((earlierResidents&(1<<npc))==0)continue;
                int other=UpcomingCell(Routes[npc],step);if(other<0)continue;
                int dx=cell%Town.Width-other%Town.Width,dz=cell/Town.Width-other/Town.Width;
                if(dx*dx+dz*dz<=CrossingRadiusSquared&&++nearby==ResidentsPerCrossing)return true;
            }
        }
        return false;
    }
    private static int UpcomingCell(Route route,int step)
    {
        int detour=route.LocalLength-route.LocalIndex;
        if(step<detour)return route.Detour[route.LocalIndex+step];
        int index=detour>0?route.Resume+step-detour:route.Index+step;
        return index<route.Length?route.Path[index]:-1;
    }
    internal static int NearbyActors(GameState game,int excludedNpc,int space,float x,float z,float radius)
        =>Crowd(game,excludedNpc,space,x,z,radius,null);
    private static int Crowd(GameState game,int excludedNpc,int space,float x,float z,float radius,Route[]? routes)
    {
        float range=radius*radius;
        int count=game.Space==space&&(space!=0||Near(game.X,game.Z,x,z,range))?1:0;
        for(int i=0;i<Town.NpcActorCount;i++){
            if(i==excludedNpc||!game.NpcPresent(i))continue;
            var v=game.Town.NpcAt(i);
            if(v.Space!=space)continue;
            if(space!=0||Near(v.PosX,v.PosZ,x,z,range)
                ||routes!=null&&routes[i].HasDestination&&Near(routes[i].GoalX+.5f,routes[i].GoalZ+.5f,x,z,range))count++;
        }
        return count;
    }
    private static bool Near(float ax,float az,float bx,float bz,float range)
    {float dx=ax-bx,dz=az-bz;return dx*dx+dz*dz<range;}
    private static int Area(float x,float z)=>Math.Clamp((int)z*AreaRows/WaterTerrain.SeaStart,0,AreaRows-1)*AreaColumns+Math.Clamp((int)x*AreaColumns/Town.Width,0,AreaColumns-1);
    private void Goal(int agent)
    {
        var game=state!;var town=game.Town;var v=town.NpcAt(agent);var r=Routes[agent];
        r.GoalKind=r.InitialGoal>=0?r.InitialGoal:Next(r,GoalKinds);r.InitialGoal=-1;r.HasDestination=false;
        Array.Clear(r.Observed,0,r.Observed.Length);r.Stuck=r.CrossingWaits=0;
        Array.Clear(areaPopulation,0,areaPopulation.Length);
        if(game.Space==0)areaPopulation[Area(game.X,game.Z)]++;
        for(int i=0;i<Routes.Length;i++){
            if(i==agent||!game.NpcPresent(i))continue;
            var other=town.NpcAt(i);if(other.Space!=0)continue;
            int area=Area(other.PosX,other.PosZ);areaPopulation[area]++;
            var intent=Routes[i];
            if(intent.HasDestination){int destination=Area(intent.GoalX+.5f,intent.GoalZ+.5f);if(destination!=area)areaPopulation[destination]++;}
        }

        int tile=Next(r,3);tile=tile==0?(int)Terrain.Camp:tile==1?(int)Terrain.Plaza:(int)Terrain.Grass;
        int best=int.MaxValue,firstArea=Next(r,areaPopulation.Length);
        // Sample each equal-sized land area before comparing its crowd and distance.
        // Other NPCs' planned stops reserve space while they are still en route.
        for(int offset=0;offset<areaPopulation.Length;offset++){
            int area=(firstArea+offset)%areaPopulation.Length,row=area/AreaColumns,column=area%AreaColumns;
            int x0=column*Town.Width/AreaColumns,x1=(column+1)*Town.Width/AreaColumns;
            int z0=(row*WaterTerrain.SeaStart+AreaRows-1)/AreaRows,z1=((row+1)*WaterTerrain.SeaStart+AreaRows-1)/AreaRows;
            for(int sample=0;sample<4;sample++){
                int x=x0+Next(r,x1-x0),z=z0+Next(r,z1-z0);
                if(!Ground(x,z)||Blocked(x,z,agent,false)||Near(v.PosX,v.PosZ,x+.5f,z+.5f,4))continue;
                int t=town.At(x,z);
                bool preferred=r.GoalKind<3||r.GoalKind==3&&t==(int)Terrain.Beach||r.GoalKind==4&&t==tile
                    ||r.GoalKind==5&&t==(int)Terrain.Grass&&(WaterSurface.IsWater(town,x-1,z)||WaterSurface.IsWater(town,x+1,z)||WaterSurface.IsWater(town,x,z-1)||WaterSurface.IsWater(town,x,z+1));
                int crowd=Crowd(game,agent,0,x+.5f,z+.5f,CrowdRadius,Routes);
                int distance=Math.Abs(x-(int)v.PosX)+Math.Abs(z-(int)v.PosZ);
                int score=crowd*4096+areaPopulation[area]*256+distance+(preferred?0:12);
                if(score>=best)continue;
                best=score;r.GoalX=x;r.GoalZ=z;r.HasDestination=true;
            }
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
        if(people){for(int i=0;i<Routes.Length;i++)if(i!=agent&&state.NpcPresent(i)){var v=town.NpcAt(i);if(v.Space==0&&Math.Abs(v.PosX-x-.5f)<.7f&&Math.Abs(v.PosZ-z-.5f)<.7f)return true;}
            if(state.Space==0&&Math.Abs(state.X-x-.5f)<.7f&&Math.Abs(state.Z-z-.5f)<.7f)return true;}
        return false;
    }
    private void Begin(int agent)
    {
        worker=agent;var v=state!.Town.NpcAt(agent);start=(int)v.PosZ*Town.Width+(int)v.PosX;Array.Clear(parent,0,parent.Length);front=0;back=1;queue[0]=(ushort)start;parent[start]=(ushort)(start+1);
    }
    private void Search()
    {
        var r=Routes[worker];int goal=r.GoalZ*Town.Width+r.GoalX;
        while(front<back&&LastExpansions<128){int cell=queue[front++];LastExpansions++;
            if(cell==goal){int length=0;while(cell!=start&&length<r.Path.Length){r.Path[length++]=cell;cell=parent[cell]-1;}if(cell!=start)length=0;for(int a=0;a<length/2;a++){int value=r.Path[a];r.Path[a]=r.Path[length-1-a];r.Path[length-1-a]=value;}r.Length=length;r.Index=r.LocalIndex=r.LocalLength=0;if(length==0){r.Wait=60;r.HasDestination=false;}worker=-1;return;}
            int x=cell%Town.Width,z=cell/Town.Width;for(int d=0;d<4;d++){int nx=x+(d==0?-1:d==1?1:0),nz=z+(d==2?-1:d==3?1:0);if(!Step(x,z,nx,nz,false,worker))continue;int next=nz*Town.Width+nx;if(parent[next]!=0)continue;parent[next]=(ushort)(cell+1);queue[back++]=(ushort)next;}
        }
        if(front==back){r.Length=r.Index=0;r.Wait=60;r.HasDestination=false;worker=-1;}
    }
    private bool Detour(int agent)
    {
        LocalPlans++;var r=Routes[agent];var v=state!.Town.NpcAt(agent);int originX=(int)v.PosX-6,originZ=(int)v.PosZ-6,target=-1;
        for(int n=r.Index;n<r.Length&&n<r.Index+16;n++){int x=r.Path[n]%Town.Width,z=r.Path[n]/Town.Width;if(x>=originX&&x<originX+13&&z>=originZ&&z<originZ+13&&!Blocked(x,z,agent,true)){target=(z-originZ)*13+x-originX;r.Resume=n+1;}}
        if(target<0)return false;for(int i=0;i<169;i++)localParent[i]=-1;int f=0,b=1;localQueue[0]=84;localParent[84]=84;
        int ahead=r.Path[r.Index],previous=r.Index==0?((int)v.PosZ*Town.Width+(int)v.PosX):r.Path[r.Index-1];
        int dx=ahead%Town.Width-previous%Town.Width,dz=ahead/Town.Width-previous/Town.Width;
        // Opposing walkers choose opposite sides when equally short detours exist.
        int right=dz<0?0:dz>0?2:dx>0?1:3;
        while(f<b){int cell=localQueue[f++];if(cell==target){int count=0;while(cell!=84){r.Detour[count++]=(originZ+cell/13)*Town.Width+originX+cell%13;cell=localParent[cell];}for(int i=0;i<count/2;i++){int value=r.Detour[i];r.Detour[i]=r.Detour[count-1-i];r.Detour[count-1-i]=value;}r.LocalLength=count;r.LocalIndex=0;return count>0;}
            int x=cell%13,z=cell/13;for(int d=0;d<4;d++){
                int turn=(right+4-d)%4,nx=x+(turn==2?-1:turn==0?1:0),nz=z+(turn==3?-1:turn==1?1:0);
                if(nx<0||nz<0||nx>=13||nz>=13)continue;int next=nz*13+nx;
                if(localParent[next]>=0||!Step(originX+x,originZ+z,originX+nx,originZ+nz,true,agent))continue;
                localParent[next]=cell;localQueue[b++]=next;
            }
        }return false;
    }
    private void Walk(int agent,PortalSystem? doors,int movingActors)
    {
        var r=Routes[agent];var v=state!.Town.NpcAt(agent);bool local=r.LocalIndex<r.LocalLength;int cell=local?r.Detour[r.LocalIndex]:r.Path[r.Index];float tx=cell%Town.Width+.5f,tz=cell/Town.Width+.5f,dx=tx-v.PosX,dz=tz-v.PosZ,distance=(float)Math.Sqrt(dx*dx+dz*dz);
        bool arrived=distance<.045f;
        float fraction=arrived?1:.04f/distance,oldX=v.PosX,oldZ=v.PosZ,nx=oldX+dx*fraction,nz=oldZ+dz*fraction;
        // Deviation planning is triggered only by the next 1.2 metres, never by
        // a tree/building several acres away. NPCs are resolved at contact.
        float look=distance>1.2f?1.2f/distance:1;int lx=(int)(oldX+dx*look),lz=(int)(oldZ+dz*look);
        if(Blocked(lx,lz,agent,false)||!BuildingWorld.MoveVillager(state!,agent+1,nx,nz,arrived?v.Yaw:PlayerMovement.Yaw(dx,dz),doors,movingActors)){
            if(!Detour(agent)){
                int learned=0;for(int z=(int)v.PosZ-6;z<=(int)v.PosZ+6;z++)for(int x=(int)v.PosX-6;x<=(int)v.PosX+6;x++)if(Ground(x,z)&&Blocked(x,z,agent,false)){
                    int known=z*Town.Width+x;uint bit=1u<<(known%32);if((r.Observed[known/32]&bit)==0){r.Observed[known/32]|=bit;learned++;}
                }
                if(learned>0){r.Length=r.Index=r.LocalLength=r.LocalIndex=0;r.Replan=true;r.Wait=1;}
                else{r.Wait=15+agent*3;if(++r.Stuck>8){r.Index=r.Length;r.LocalIndex=r.LocalLength;r.Wait=30;r.HasDestination=false;}}
            }return;
        }
        state.Town.Traffic.Traverse(state.Town,oldX,oldZ,nx,nz);
        if(arrived){if(local){r.LocalIndex++;if(r.LocalIndex==r.LocalLength)r.Index=r.Resume;}else r.Index++;if(r.Index>=r.Length){r.Wait=90+Next(r,180);r.Length=r.Index=0;r.HasDestination=false;}}
        r.Moving=nx!=oldX||nz!=oldZ;r.Stuck=0;
    }
}
