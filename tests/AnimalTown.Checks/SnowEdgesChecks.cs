using AnimalTown;
internal static class SnowEdgesChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var state=new GameState(739);var town=state.Town;Array.Fill(town.Tiles,(byte)Terrain.Grass);Array.Clear(town.Elevation);
        check(town.Traffic.Snow==null,"a new nonwinter town has no snow owner or buffers");
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<1000;i++)town.Traffic.Enter(town,20,40);
        check(town.Traffic.Snow==null&&GC.GetAllocatedBytesForCurrentThread()==before,"ordinary nonwinter travel allocates no snow data");
        town.Traffic.SetWinter(true);var snow=town.Traffic.Snow!;
        for(int i=0;i<3;i++)town.Traffic.Enter(town,20,40);
        _=snow.RecoveryStamps;
        town.Traffic.SetWinter(false);
        check(town.Traffic.Snow==null&&snow.Packed.Length==0&&snow.RecoveryStamps==null&&!snow.HasCheckpoint,"winter exit releases wear, sparse recovery, borrowed stamps and the checkpoint");
        town.Traffic.SetWinter(true);check(town.Traffic.Snow!=snow&&town.Traffic.Snow!.Packed.All(b=>b==0),"winter reentry creates fresh storage without reviving stale dents");
        var data=new byte[FloorStrips.Records+28];
        void Put(int at,int value){for(int i=0;i<4;i++)data[at+i]=(byte)((uint)value>>(i*8));}
        for(int surface=0;surface<TerrainTiles.SurfaceCount;surface++){Put(20+surface*20,1);Put(24+surface*20,surface);}
        // A clear patch overrides grass without changing its collision tile.
        Put(280,1);Put(284,TerrainTiles.Dirt);Put(288,TerrainTiles.Grass);Put(292,41);Put(296,41);Put(300,3);Put(304,3);Put(308,1);
        var edges=new SnowEdges();check(edges.Prepare(state,data,1),"winter coverage is derived on demand");
        check(!edges.Covered(42,42)&&edges.Covered(40,42)&&(edges.Edges[42*Town.Width+40]&2)!=0,"clear ground patches receive an edge ramp even when their terrain ID stays grass");
        town.Tiles[40*Town.Width+20]=(byte)Terrain.Camp;town.NotifyTerrainChanged();edges.Prepare(state,data,1);
        check(!edges.Covered(20,40)&&(edges.Edges[40*Town.Width+19]&2)!=0&&(edges.Edges[39*Town.Width+20]&4)!=0,"ordinary unsnowed ground receives the same boundary ramp on both axes");
        town.Paths.Set(30,40,1);town.Traffic.Visits[40*Town.Width+30]=35;edges.Prepare(state,data,1);
        check(!edges.Covered(30,40)&&(edges.Edges[40*Town.Width+29]&2)!=0,"visible traffic paths share the snow exclusion and ramp");
        int revision=edges.Revision;before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<100;i++)check(!edges.Prepare(state,data,1),"unchanged coverage does no rebuilding");
        check(edges.Revision==revision&&GC.GetAllocatedBytesForCurrentThread()==before,"steady winter coverage preparation allocates no scratch");
        for(int bits=1;bits<16;bits++)for(int side=0;side<4;side++)if((bits&(1<<side))!=0){
            SnowEdges.Quad(0,0,bits,side,out float ax,out float az,out float bx,out float bz,out float cx,out float cz,out float dx,out float dz);
            float area=(ax*bz+bx*cz+cx*dz+dx*az-az*bx-bz*cx-cz*dx-dz*ax)/2;
            check(area>0&&new[]{ax,bx,cx,dx,az,bz,cz,dz}.All(v=>v>=0&&v<=1),"ramps have the terrain winding and stay on the snowy tile at every corner combination");
            float distance=side==0?cz:side==1?1-cx:side==2?1-cz:cx;
            check(distance==.25f,"each ramp spans a quarter tile from the bare edge to the snow cap");
        }
    }
}
