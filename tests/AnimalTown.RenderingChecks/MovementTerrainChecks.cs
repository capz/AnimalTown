using AnimalTown;

internal static class MovementTerrainChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var rows=new CylinderRows();
        for(int focus=0;focus<=160;focus+=5){
            var motion=new CylinderMotion(focus+.137f);
            for(int z=-64;z<=224;z++)foreach(float h in new[]{-.5f,0,2}){
                var stored=rows.At(z,h);motion.Point(stored.Y,stored.Z,out float y,out float worldZ);
                var direct=CylinderWorld.Surface(z,focus+.137f,h);
                check(Math.Abs(y-direct.Y)<.0001f&&Math.Abs(worldZ-direct.Z)<.0001f,"rigid terrain motion equals direct cylinder projection at every height");
                var chord=rows.Chord(z+.37f,h);motion.Point(chord.Y,chord.Z,out y,out worldZ);
                var end=CylinderWorld.Surface(z+1,focus+.137f,h);
                check(Math.Abs(y-(direct.Y+(end.Y-direct.Y)*.37f))<.0001f&&Math.Abs(worldZ-(direct.Z+(end.Z-direct.Z)*.37f))<.0001f,"floor/water/cliff chord stays aligned during fractional camera motion");
            }
        }
        // Conservative patch bounds include caps that extend one cell past a
        // gallery origin's patch boundary, plus river/sea/raised-land heights.
        for(int oz=-64;oz<=216;oz+=8){
            var center=rows.At(oz+4,1);
            for(int z=oz;z<=oz+9;z++)foreach(float h in new[]{-.5f,0,2}){
                var p=rows.At(z,h);float dy=p.Y-center.Y,dz=p.Z-center.Z;
                check(25+dy*dy+dz*dz<64,"patch culling sphere encloses ground, banks and gallery caps");
            }
        }
        var state=new GameState(17);var depth=new WaterDepthCache();depth.Prepare(state.Town);
        for(int z=-3;z<=Town.Height+3;z+=3)for(int x=-3;x<=Town.Width+3;x+=3)check(depth.At(x,z)==WaterSurface.Depth(state.Town,x,z),"cached water corners preserve exact depth at shores and the world boundary");
        int computations=depth.Calculations;depth.At(0,0);int after=depth.Calculations;depth.At(0,0);check(depth.Calculations==after&&after<=computations+1,"repeated corners avoid rescanning banks");
        state.Town.NotifyTerrainChanged();depth.Prepare(state.Town);check(depth.Calculations==0,"edited terrain invalidates depth");
        check(depth.At(0,0)==WaterSurface.Depth(state.Town,0,0)&&depth.Calculations==1,"edited corner is recomputed once");
        depth.Prepare(new Town(18));check(depth.Calculations==0,"loading another town invalidates water data");
        var clock=new AnimationFrameClock(7);
        for(int tick=0;tick<3;tick++)clock.Step(0,0,true);
        check(Math.Abs(clock.Consume(0)-.1f)<.000001f,"three simulation ticks produce one animation update with the full elapsed time");
        check(clock.Consume(0)==0,"rendering twice without a simulation tick does not advance animation");
        clock.Step(0,0,true);clock.Step(0,1,true);check(clock.Clip(0)==1&&Math.Abs(clock.Consume(0)-1f/30)<.000001f,"clip change discards elapsed time belonging to the former clip");
        clock.Step(1,0,false);check(clock.Consume(1)==0,"offscreen animation stays frozen");clock.Step(0,1,true);clock.Step(0,-1,false);check(clock.Consume(0)==0,"stopping discards unrendered walking time");
        var data=new byte[FloorStrips.Records];
        void Put(int at,int value){for(int i=0;i<4;i++)data[at+i]=(byte)(value>>(i*8));}
        int[] header={0x32525441,1,13,13,FloorTexture.Maximum};for(int i=0;i<header.Length;i++)Put(i*4,header[i]);
        for(int s=0;s<13;s++){Put(20+s*20,1);Put(24+s*20,s);for(int v=1;v<4;v++)Put(24+s*20+v*4,-1);}FloorStrips.Validate(data);
        for(int z=64;z<88;z++)for(int x=48;x<72;x++){state.Town.Tiles[z*Town.Width+x]=(byte)(((x+z)%4==0)?Terrain.Water:(x+z)%4==1?Terrain.Grass:(x+z)%4==2?Terrain.Beach:Terrain.Plaza);state.Town.Elevation[z*Town.Width+x]=0;}
        var strips=new FloorStrips(64);
        for(int oz=64;oz<88;oz+=8)for(int ox=48;ox<72;ox+=8){
            strips.PreparePatch(state,data,ox,oz,8,false,0,0);check(strips.Count<=64,"checkerboard patch stays inside its fixed strip budget");
            for(int z=oz;z<oz+8;z++)for(int x=ox;x<ox+8;x++){
                int hits=0;
                for(int n=0;n<strips.Count;n++)if(x>=strips.X0[n]&&x<strips.X1[n]&&z>=strips.Z0[n]&&z<strips.Z1[n]){hits++;check(strips.Material[n]==FloorStrips.At(state,data,x,z,0)&&strips.Water[n]==WaterSurface.IsWater(state.Town,x,z),"patch keeps the original cell material and water classification");}
                check(hits==1,"adjacent patches cover each cell exactly once");
            }
        }
    }
}
