using AnimalTown;

internal static class SnowEdgeAllocationChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var state=new GameState(831);var town=state.Town;
        Array.Fill(town.Tiles,(byte)Terrain.Grass);Array.Clear(town.Elevation);town.Paths.Clear();
        town.Traffic.SetWinter(true);
        // Every alternate cell is bare ground: exercise four ramp sides and
        // their corner caps within one ordinary 8 m archive page.
        for(int z=32;z<40;z++)for(int x=32;x<40;x++)if((x+z)%2==0)town.Tiles[z*Town.Width+x]=(byte)Terrain.Camp;
        var floor=new byte[FloorStrips.Records];
        void Put(int at,int value){for(int b=0;b<4;b++)floor[at+b]=(byte)((uint)value>>(b*8));}
        for(int surface=0;surface<TerrainTiles.SurfaceCount;surface++){Put(20+surface*20,1);Put(24+surface*20,surface);}
        var coverage=new SnowEdges();coverage.Prepare(state,floor,1);
        var memory=new VisualMemoryPressure();memory.Observe(8*1024*1024,0,0,0);
        Action<int> completed=memory.Complete;
        var edges=new SnowEdgeRenderer();edges.Begin();edges.Include(32,32,8);
        check(edges.Prepare(state,coverage,memory,completed)&&edges.HasGeometry&&GraphicsMemory.Bytes>0,
            "a checkerboard page fits the bounded ramp and corner staging/index budgets");
        memory.Observe(8*1024*1024-GraphicsMemory.Bytes,0,0,1);
        int uploads=NativeFrame.Uploads,allocations=GraphicsMemory.Allocations;long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<100;i++){edges.Begin();edges.Include(32,32,8);check(edges.Prepare(state,coverage,memory,completed)&&edges.HasGeometry,"cached ramp pages keep their mask demand on stationary frames");}
        check(NativeFrame.Uploads==uploads&&GraphicsMemory.Allocations==allocations&&GC.GetAllocatedBytesForCurrentThread()==before,
            "stationary ramp pages neither rebuild nor allocate managed/native storage");
        town.Tiles[70*Town.Width+70]=(byte)Terrain.Camp;town.NotifyTerrainChanged();coverage.Prepare(state,floor,1);
        check(edges.Prepare(state,coverage,memory,completed)&&NativeFrame.Uploads==uploads,
            "a coverage edit outside the page preserves its unchanged indexed mesh");
        town.Tiles[34*Town.Width+34]=(byte)Terrain.Grass;town.NotifyTerrainChanged();coverage.Prepare(state,floor,1);
        check(edges.Prepare(state,coverage,memory,completed)&&NativeFrame.Uploads>uploads,
            "a local coverage edit refreshes the ramp mesh");
        memory.Observe(8*1024*1024-GraphicsMemory.Bytes,0,0,2);
        edges.Begin();check(edges.Prepare(state,coverage,memory,completed)&&!edges.HasGeometry&&GraphicsMemory.Bytes==0,
            "departed ramp pages release their meshes before admitting replacements");
        edges.Dispose();check(GraphicsMemory.Bytes==0,"winter ramp teardown returns every native allocation once");
    }
}
