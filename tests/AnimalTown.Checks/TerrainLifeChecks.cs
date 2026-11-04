using AnimalTown;

internal static class TerrainLifeChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var state=new GameState(781);int blocks=0,ramps=0;for(int i=0;i<Town.Cells;i++){if(state.Town.Elevation[i]==1)blocks++;if(state.Town.Elevation[i]>=2)ramps++;}
        check(blocks>0&&ramps>0,"generation places raised blocks and ramp access");
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
        Console.WriteLine("Raised terrain, clock and bounded villager navigation checks passed");
    }
}
