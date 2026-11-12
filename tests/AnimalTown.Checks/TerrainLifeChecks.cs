using AnimalTown;

internal static class TerrainLifeChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var state=new GameState(781);int blocks=0,ramps=0;for(int i=0;i<Town.Cells;i++){if(state.Town.Elevation[i]==1)blocks++;if(state.Town.Elevation[i]>=2)ramps++;}
        check(blocks>0&&ramps>0,"generation places raised blocks and ramp access");
        int corners=0;for(int i=0;i<Town.Cells;i++)if(state.Town.Elevation[i]>=6)corners|=1<<(state.Town.Elevation[i]-6);
        check(corners==15,"actual generated towns use all four diagonal corner pieces");
        for(int z=0;z<Town.Height;z+=2)for(int x=0;x<Town.Width;x+=2)if(TerrainElevation.Mode(state.Town,x,z)==6){
            var seen=new bool[Town.Cells];var queue=new int[400];int front=0,back=1;
            queue[0]=(z+14)*Town.Width+x+4;seen[queue[0]]=true;
            while(front<back){int cell=queue[front++],cx=cell%Town.Width,cz=cell/Town.Width;
                for(int d=0;d<4;d++){int nx=cx+(d==0?-1:d==1?1:0),nz=cz+(d==2?-1:d==3?1:0);
                    if(nx<x-1||nx>x+12||nz<z-1||nz>z+14)continue;int neighbor=nz*Town.Width+nx;
                    if(!seen[neighbor]&&TerrainElevation.Traverse(state.Town,cx+.5f,cz+.5f,nx+.5f,nz+.5f)){seen[neighbor]=true;queue[back++]=neighbor;}
                }
            }
            for(int dz=0;dz<12;dz++)for(int dx=0;dx<12;dx++)check(seen[(z+dz)*Town.Width+x+dx],"NPC cardinal planner can reach every generated corner/terrace center using its ramp");
        }
        var fixture=new GameState(7319);Array.Clear(fixture.Town.Elevation);Array.Fill(fixture.Town.Tiles,(byte)Terrain.Grass);
        for(int orientation=4;orientation<8;orientation++){
            for(int dz=0;dz<2;dz++)for(int dx=0;dx<2;dx++)fixture.Town.Elevation[(40+dz)*Town.Width+40+dx]=(byte)(orientation+2);
            for(int iz=1;iz<20;iz++)for(int ix=1;ix<20;ix++){
                float x=ix*.1f,z=iz*.1f;
                check(TerrainElevation.At(fixture.Town,40+x,40+z)==(TerrainCliffGeometry.Raised(orientation,x,z)?2:0),"collision height matches flat template half");
            }
            float hx=orientation==4||orientation==6?1.7f:.3f,hz=orientation==4||orientation==5?1.7f:.3f;
            check(!TerrainElevation.Traverse(fixture.Town,40+hx,40+hz,42-hx,42-hz),"player and NPC shared traversal rejects diagonal wall crossing");
            check(TerrainElevation.Traverse(fixture.Town,40+hx,40+hz,40+hx*.95f,40+hz*.95f),"movement along same raised half remains possible");
            for(int cellZ=39;cellZ<=42;cellZ++)for(int cellX=39;cellX<=42;cellX++)foreach(int direction in new[]{0,1}){
                float ax=cellX+.5f,az=cellZ+.5f,bx=ax+(direction==0?1:0),bz=az+(direction==1?1:0);
                if(TerrainElevation.Traverse(fixture.Town,ax,az,bx,bz))check(TerrainElevation.At(fixture.Town,ax,az)==TerrainElevation.At(fixture.Town,bx,bz),"NPC planner cannot connect low and high corner centers through a wall");
            }
            int upper=TerrainCliffBinding.Neighbor(fixture.Town,orientation,40,40,true),lower=TerrainCliffBinding.Neighbor(fixture.Town,orientation,40,40,false);
            fixture.Town.Tiles[upper]=(byte)Terrain.Plaza;fixture.Town.Tiles[lower]=(byte)Terrain.Beach;
            var split=SplitSaveCodec.Decode(SplitSaveCodec.Encode(fixture));
            check(split.Town.Elevation.SequenceEqual(fixture.Town.Elevation)&&split.Town.Tiles.SequenceEqual(fixture.Town.Tiles),"split save retains diagonal shape and both surface sources");
        }
        bool accessible=true;for(int z=0;z<Town.Height;z++)for(int x=0;x<Town.Width;x++)if(TerrainElevation.Mode(state.Town,x,z)==TerrainElevation.RampNorth&&z%2==0&&x%2==0){
            accessible&=TerrainElevation.Traverse(state.Town,x+.5f,z+2.5f,x+.5f,z-.5f);check(TerrainElevation.At(state.Town,x+.5f,z)==2&&TerrainElevation.At(state.Town,x+.5f,z+1)==1,"single ramp matches two-metre cube height");
        }check(accessible,"ramps are traversable between low and raised ground");
        bool blocked=false;for(int z=1;z<Town.Height-1&&!blocked;z++)for(int x=1;x<Town.Width-1;x++)if(TerrainElevation.Mode(state.Town,x,z)==1&&TerrainElevation.Mode(state.Town,x-1,z)==0){blocked=!TerrainElevation.Traverse(state.Town,x-.5f,z+.5f,x+.5f,z+.5f);break;}check(blocked,"vertical cube edges block traversal");
        state.Town.Residents[0].Name="Customfox";var loaded=SaveCodec.Decode(SaveCodec.Encode(state));check(loaded.Town.Elevation.SequenceEqual(state.Town.Elevation)&&loaded.Town.Residents[0].Name=="Customfox"&&loaded.Town.Residents[0].HomeX==state.Town.Residents[0].HomeX,"height, model-tied names and fixed homes persist");
        var clock=new TownClock();clock.Update(7|(23<<6)|(4<<11)|(11<<16)|(3<<20));check(clock.Time=="23:07"&&clock.Date=="4 Nov  Wed","system clock formatting");string previous=clock.Time;clock.Update(7|(23<<6)|(4<<11)|(11<<16)|(3<<20));check(object.ReferenceEquals(previous,clock.Time),"clock text retains cached strings within a minute");
        var navigation=new VillagerNavigation();var originX=new float[6];var originZ=new float[6];var visited=new bool[6];var homeX=new int[6];int kinds=0;for(int i=0;i<6;i++){originX[i]=state.Town.Residents[i].PosX;originZ[i]=state.Town.Residents[i].PosZ;homeX[i]=state.Town.Residents[i].HomeX;}
        for(int tick=0;tick<6000;tick++){
            navigation.Tick(state);check(navigation.LastExpansions<=128,"global planning has fixed per-tick node budget");
            for(int i=0;i<6;i++){var v=state.Town.Residents[i];visited[i]|=Math.Abs(v.PosX-originX[i])+Math.Abs(v.PosZ-originZ[i])>2;check(state.CanWalk(v.PosX,v.PosZ,0),"villagers never walk through water, trees, buildings or house footprint");check(v.HomeX==homeX[i],"villager movement never moves its house");kinds|=1<<navigation.Routes[i].GoalKind;}
        }
        int moved=0;for(int i=0;i<6;i++)if(visited[i])moved++;check(moved>=4,"computer villagers travel through generated town");check(navigation.LocalPlans>0,"nearby obstructions trigger bounded local detours");check((kinds&63)==63,"villagers choose diverse destinations");
        var next=SaveCodec.Decode(SaveCodec.Encode(state));for(int i=0;i<6;i++)check(next.Town.Residents[i].Name==state.Town.Residents[i].Name&&Math.Abs(next.Town.Residents[i].PosX-state.Town.Residents[i].PosX)<.002f,"moving villager identities and locations persist");
        ObstacleBitmapChecks(check);
        Console.WriteLine("Raised terrain, clock and bounded villager navigation checks passed");
    }
    private static void ObstacleBitmapChecks(Action<bool,string> check)
    {
        long before=GC.GetAllocatedBytesForCurrentThread();var navigation=new VillagerNavigation();
        check(GC.GetAllocatedBytesForCurrentThread()-before<150000,"navigation retains a compact shared obstacle map within its allocation budget");
        var state=new GameState(7319);Array.Fill(state.Town.Tiles,(byte)Terrain.Grass);Array.Clear(state.Town.Elevation);
        state.Town.GroundCount=state.Town.PlacedCount=0;
        for(int i=0;i<navigation.Paused.Length;i++)navigation.Paused[i]=true;
        navigation.Tick(state);
        // Include both sides of a 32-cell word and the final bit of the world.
        foreach(int cell in new[]{0,31,32,Town.Cells-1}){
            var tree=state.Town.Ground[0];tree.X=cell%Town.Width;tree.Z=cell/Town.Width;tree.Kind=3;tree.Item=3;state.Town.GroundCount=1;
            check(!AdvanceTo(navigation,state,cell),"a tree blocks a route at each obstacle-word/world boundary");
            tree.Kind=0;for(int i=0;i<30;i++)navigation.Tick(state);
            check(AdvanceTo(navigation,state,cell),"same-count tree removal refreshes the occupancy map at its bounded periodic rebuild");
            tree.Kind=3;for(int i=0;i<30;i++)navigation.Tick(state);
            check(!AdvanceTo(navigation,state,cell),"same-count tree insertion restores the obstacle bit at its periodic rebuild");
            state.Town.GroundCount=0;
            check(AdvanceTo(navigation,state,cell),"tree-count changes clear stale obstacle bits immediately");
        }
        // A two-cell footprint crosses a word boundary, and removal must clear
        // both cells rather than preserving a bit from the preceding rebuild.
        var table=state.Town.Placed[0];table.Item=11;table.X=31;table.Z=0;table.Rotation=0;table.Room=0;state.Town.PlacedCount=1;
        check(!AdvanceTo(navigation,state,31)&&!AdvanceTo(navigation,state,32),"furniture footprints cover both sides of an obstacle word boundary");
        state.Town.PlacedCount=0;
        check(AdvanceTo(navigation,state,31)&&AdvanceTo(navigation,state,32),"furniture-count changes clear the entire old footprint");
    }
    private static bool AdvanceTo(VillagerNavigation navigation,GameState state,int cell)
    {
        int x=cell%Town.Width,z=cell/Town.Width;var npc=state.Town.Residents[0];npc.PosX=x==0?1.5f:x-.5f;npc.PosZ=z+.5f;npc.X=(int)npc.PosX;npc.Z=z;npc.Space=0;
        var route=navigation.Routes[0];route.Path[0]=cell;route.Length=1;route.Index=route.LocalLength=route.LocalIndex=route.Wait=0;
        float beforeX=npc.PosX,beforeZ=npc.PosZ;navigation.Paused[0]=false;navigation.Tick(state);navigation.Paused[0]=true;
        return npc.PosX!=beforeX||npc.PosZ!=beforeZ;
    }

}
