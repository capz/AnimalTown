using AnimalTown;

internal static class TownGenerationChecks
{
    internal static void Run(Action<bool,string> check)
    {
        SavedVisitor[] guests={new SavedVisitor(12,0,"Rowan","hello",0,0,0),new SavedVisitor(12,1,"Ash","hello",1,0,0)};
        var signatures=new HashSet<uint>();var visited=new bool[Town.Cells];var blocked=new bool[Town.Cells];var queue=new int[Town.Cells];
        for(int sample=0;sample<68;sample++){
            int seed=sample<64?sample:sample==64?int.MinValue:sample==65?int.MaxValue:sample==66?-1:7319;
            var state=new GameState(seed);var town=state.Town;uint hash=2166136261;
            for(int i=0;i<Town.Cells;i++)hash=unchecked((hash^town.Tiles[i])*16777619);if(sample<64)signatures.Add(hash);
            Array.Clear(visited);Array.Clear(blocked);int trees=0;
            for(int i=0;i<town.GroundCount;i++){var g=town.Ground[i];check(town.Inside(g.X,g.Z),"generated ground records bounded");if(g.Kind==3){trees++;blocked[g.Z*Town.Width+g.X]=true;check(town.At(g.X,g.Z)==0&&!town.InReserve(g.X,g.Z),"trees avoid water, structures and starter reserve");}}
            check(trees==192&&town.GroundCount==194,"bounded deterministic vegetation population at sixty percent of the previous tree count");
            int cherries=town.Ground.Take(town.GroundCount).Count(g=>g.Kind==3&&g.Item==Catalog.CherrySapling);
            check(cherries>0&&cherries<trees/4,"each new town has a small share of cherry blossom trees");
            check(town.Elevation.Count(mode=>mode==TerrainElevation.RampNorth)==12,"three complete terraces retain their two-by-two ramps within compact bounds");
            int front=0,back=0;queue[back++]=38*Town.Width+18;visited[queue[0]]=true;
            while(front<back){int cell=queue[front++],x=cell%Town.Width,z=cell/Town.Width;
                for(int d=0;d<4;d++){int nx=x+(d==0?-1:d==1?1:0),nz=z+(d==2?-1:d==3?1:0);if(!town.Inside(nx,nz))continue;int next=nz*Town.Width+nx;if(!visited[next]&&!blocked[next]&&town.Walkable(nx,nz)&&TerrainElevation.Traverse(town,x+.5f,z+.5f,nx+.5f,nz+.5f)){visited[next]=true;queue[back++]=next;}}
            }
            bool connected=true;int bridgeCells=0,waterCells=0;for(int i=0;i<Town.Cells;i++){if(town.Walkable(i%Town.Width,i/Town.Width)&&!blocked[i]&&!visited[i])connected=false;if(town.Tiles[i]==(byte)Terrain.Bridge)bridgeCells++;if(WaterSurface.IsWater(town,i%Town.Width,i/Town.Width))waterCells++;}
            check(waterCells>=Town.Cells/4&&waterCells<=Town.Cells*2/5,"water retains the ocean band and bounded inland features");check(connected,"every playable cell reachable with tree collision enabled");check(bridgeCells>=18&&bridgeCells<=60,"two river crossings have bounded width and clear approaches");
            check(town.ValidPlot(12,22)&&visited[28*Town.Width+32]&&visited[28*Town.Width+50]&&visited[50*Town.Width+65],"mandatory plots and service entrances retained");
            state.SetTownVisitors(guests);
            int sectors=1<<PopulationSector((int)state.X,(int)state.Z);
            for(int npc=0;npc<Town.NpcActorCount;npc++){
                check(state.NpcPresent(npc),"both saved visitors join the five residents");
                var villager=town.NpcAt(npc);
                int sector=1<<PopulationSector(villager.X,villager.Z);
                check(visited[villager.Z*Town.Width+villager.X],"generated villager reachable");
                check((sectors&sector)==0,"new town spreads its population across eight different areas");
                sectors|=sector;
            }
            Array.Clear(visited);int bodies=0;
            for(int start=0;start<Town.Cells;start++)if(!visited[start]&&(WaterSurface.IsWater(town,start%Town.Width,start/Town.Width)||town.Tiles[start]==2)){
                bodies++;front=0;back=0;queue[back++]=start;visited[start]=true;
                while(front<back){int cell=queue[front++],x=cell%Town.Width,z=cell/Town.Width;for(int d=0;d<4;d++){int nx=x+(d==0?-1:d==1?1:0),nz=z+(d==2?-1:d==3?1:0);if(!town.Inside(nx,nz))continue;int next=nz*Town.Width+nx;if(!visited[next]&&(WaterSurface.IsWater(town,nx,nz)||town.Tiles[next]==2)){visited[next]=true;queue[back++]=next;}}}
            }
            check(bodies==3||bodies==4,"river joins ocean, with two or three separate ponds");
            byte[] encoded=SaveCodec.Encode(state);var loaded=SaveCodec.Decode(encoded);check(encoded.SequenceEqual(SaveCodec.Encode(loaded)),"generated map, seed and objects survive save round trip");
            check(!loaded.NpcVisitor(5).HasValue&&!loaded.NpcPresent(6),"foreign actors are projected from their source cards at town entry");
            if(sample<4){var same=new Town(seed);check(same.Tiles.SequenceEqual(town.Tiles)&&same.GroundCount==town.GroundCount,"same seed reproduces terrain");bool objects=true;for(int i=0;i<town.GroundCount;i++)objects&=same.Ground[i].X==town.Ground[i].X&&same.Ground[i].Z==town.Ground[i].Z&&same.Ground[i].Item==town.Ground[i].Item;check(objects,"same seed reproduces vegetation");same.Generate();check(same.GroundCount==town.GroundCount,"unplayed generation does not accumulate objects");}
        }
        check(signatures.Count>=60,"different seeds change terrain rather than only tree positions");
        var tutorial=new GameState(42){Tutorial=5,HousePlaced=true};
        tutorial.SetTownVisitors(Array.Empty<SavedVisitor>());
        check(!tutorial.NpcPresent(5)&&!tutorial.NpcPresent(6),"empty visitor slots leave five neighbours");
        for(int i=0;i<Town.LocalResidentCount;i++)tutorial.Talk(i);
        check(tutorial.Tutorial==6,"introductions complete after meeting the five neighbours");
        var edited=new GameState(42);edited.Town.SetTerrain(90,90,Terrain.Plaza);var roundtrip=SaveCodec.Decode(SaveCodec.Encode(edited));check(roundtrip.Town.At(90,90)==(int)Terrain.Plaza,"saved edits are not replaced with seeded terrain");
        edited.Rollover(1,1);bool refused=false;try{edited.Town.Generate();}catch(InvalidOperationException){refused=true;}check(refused,"played town cannot be accidentally regenerated");
        Console.WriteLine("Town generation: 68 seed sweep, connected playable land, river/sea/pond topology, crossings, determinism and save preservation passed");
    }

    private static int PopulationSector(int x,int z)=>z*2/(Town.Height-40)*4+x*4/Town.Width;
}
