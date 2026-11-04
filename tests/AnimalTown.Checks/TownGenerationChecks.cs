using AnimalTown;

internal static class TownGenerationChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var signatures=new HashSet<uint>();var visited=new bool[Town.Cells];var blocked=new bool[Town.Cells];var queue=new int[Town.Cells];
        for(int sample=0;sample<68;sample++){
            int seed=sample<64?sample:sample==64?int.MinValue:sample==65?int.MaxValue:sample==66?-1:7319;
            var state=new GameState(seed);var town=state.Town;uint hash=2166136261;
            for(int i=0;i<Town.Cells;i++)hash=unchecked((hash^town.Tiles[i])*16777619);if(sample<64)signatures.Add(hash);
            Array.Clear(visited);Array.Clear(blocked);int trees=0;
            for(int i=0;i<town.GroundCount;i++){var g=town.Ground[i];check(town.Inside(g.X,g.Z),"generated ground records bounded");if(g.Kind==3){trees++;blocked[g.Z*Town.Width+g.X]=true;check(town.At(g.X,g.Z)==0&&!town.InReserve(g.X,g.Z),"trees avoid water, structures and starter reserve");}}
            check(trees==320&&town.GroundCount==322,"bounded deterministic vegetation population");
            int front=0,back=0;queue[back++]=38*Town.Width+18;visited[queue[0]]=true;
            while(front<back){int cell=queue[front++],x=cell%Town.Width,z=cell/Town.Width;
                for(int d=0;d<4;d++){int nx=x+(d==0?-1:d==1?1:0),nz=z+(d==2?-1:d==3?1:0);if(!town.Inside(nx,nz))continue;int next=nz*Town.Width+nx;if(!visited[next]&&!blocked[next]&&town.Walkable(nx,nz)){visited[next]=true;queue[back++]=next;}}
            }
            bool connected=true;int bridgeCells=0;for(int i=0;i<Town.Cells;i++){if(town.Walkable(i%Town.Width,i/Town.Width)&&!blocked[i]&&!visited[i])connected=false;if(town.Tiles[i]==(byte)Terrain.Bridge)bridgeCells++;}
            check(connected,"every playable cell reachable with tree collision enabled");check(bridgeCells>=18&&bridgeCells<=60,"two river crossings have bounded width and clear approaches");
            check(town.ValidPlot(12,22)&&visited[28*Town.Width+32]&&visited[28*Town.Width+50]&&visited[50*Town.Width+65],"mandatory plots and service entrances retained");
            for(int i=0;i<6;i++)check(visited[town.Residents[i].Z*Town.Width+town.Residents[i].X],"generated resident reachable");
            Array.Clear(visited);int bodies=0;
            for(int start=0;start<Town.Cells;start++)if(!visited[start]&&(town.Tiles[start]==1||town.Tiles[start]==2)){
                bodies++;front=0;back=0;queue[back++]=start;visited[start]=true;
                while(front<back){int cell=queue[front++],x=cell%Town.Width,z=cell/Town.Width;for(int d=0;d<4;d++){int nx=x+(d==0?-1:d==1?1:0),nz=z+(d==2?-1:d==3?1:0);if(!town.Inside(nx,nz))continue;int next=nz*Town.Width+nx;if(!visited[next]&&(town.Tiles[next]==1||town.Tiles[next]==2)){visited[next]=true;queue[back++]=next;}}}
            }
            check(bodies==3||bodies==4,"river joins ocean, with two or three separate ponds");
            byte[] encoded=SaveCodec.Encode(state);var loaded=SaveCodec.Decode(encoded);check(encoded.SequenceEqual(SaveCodec.Encode(loaded)),"generated map, seed and objects survive save round trip");
            if(sample<4){var same=new Town(seed);check(same.Tiles.SequenceEqual(town.Tiles)&&same.GroundCount==town.GroundCount,"same seed reproduces terrain");bool objects=true;for(int i=0;i<town.GroundCount;i++)objects&=same.Ground[i].X==town.Ground[i].X&&same.Ground[i].Z==town.Ground[i].Z&&same.Ground[i].Item==town.Ground[i].Item;check(objects,"same seed reproduces vegetation");same.Generate();check(same.GroundCount==town.GroundCount,"unplayed generation does not accumulate objects");}
        }
        check(signatures.Count>=60,"different seeds change terrain rather than only tree positions");
        var edited=new GameState(42);edited.Town.SetTerrain(90,90,Terrain.Plaza);var roundtrip=SaveCodec.Decode(SaveCodec.Encode(edited));check(roundtrip.Town.At(90,90)==(int)Terrain.Plaza,"saved edits are not replaced with seeded terrain");
        edited.Rollover(1,1);bool refused=false;try{edited.Town.Generate();}catch(InvalidOperationException){refused=true;}check(refused,"played town cannot be accidentally regenerated");
        Console.WriteLine("Town generation: 68 seed sweep, connected playable land, river/sea/pond topology, crossings, determinism and save preservation passed");
    }
}
