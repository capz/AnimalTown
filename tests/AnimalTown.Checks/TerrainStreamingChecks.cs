using AnimalTown;

internal static class TerrainStreamingChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var cachedRows=new CylinderRows();
        foreach(float focus in new[]{.25f,40.25f,159.75f})for(int offset=-60;offset<=36;offset+=4){
            int top=(int)Math.Floor(focus/4)*4+offset;var bounds=new TerrainSectionBounds(cachedRows,new CylinderMotion(focus),top,focus);
            foreach(float delta in new[]{-.249f,.249f})foreach(float eye in new[]{0f,2f})foreach(float height in new[]{-.5f,0,2.01f})for(int n=0;n<=8;n++){
                var p=CylinderWorld.Surface(top+n*.5f,focus+delta,height);float y=p.Y-(eye-1),z=p.Z-delta;
                check(y>=bounds.MinY&&y<=bounds.MaxY&&z>=bounds.MinZ&&z<=bounds.MaxZ,"section boxes include every row chord, river bottom, cap and fractional / eye-height camera shift");
            }
        }
        var flat=new Town(17);Array.Fill(flat.Tiles,(byte)Terrain.Grass);Array.Clear(flat.Elevation);
        check(TerrainSectionPlan.Kind(flat,40,40)==TerrainSectionPlan.Dry,"level dry sections use the two-stage path");
        Array.Fill(flat.Elevation,(byte)TerrainElevation.Block);
        check(TerrainSectionPlan.Kind(flat,40,40)==TerrainSectionPlan.Dry,"level upper terraces retain the fast path");
        foreach(byte mode in new byte[]{0,2,3,4,5,6,7,8,9}){
            flat.Elevation[40*Town.Width+39]=mode;
            check(TerrainSectionPlan.Kind(flat,40,40)==TerrainSectionPlan.Detailed,"height transitions in the border retain wall generation");
        }
        Array.Clear(flat.Elevation);
        foreach(Terrain kind in new[]{Terrain.Water,Terrain.CoastEast}){
            flat.Tiles[40*Town.Width+39]=(byte)kind;
            check(TerrainSectionPlan.Kind(flat,40,40)==TerrainSectionPlan.Detailed,"banks, beaches, bridges and cliff borders retain detailed geometry");
        }
        Array.Fill(flat.Tiles,(byte)Terrain.Sea);
        check(TerrainSectionPlan.Kind(flat,40,40)==TerrainSectionPlan.DeepSea,"deep ocean uses four row quads");
        flat.Tiles[37*Town.Width+37]=(byte)Terrain.Grass;
        check(TerrainSectionPlan.Kind(flat,40,40)==TerrainSectionPlan.Detailed,"all depth-tint samples must be sea before merging water quads");
        Array.Fill(flat.Tiles,(byte)Terrain.Sea);
        for(int z=40;z<=44;z++)for(int x=40;x<=48;x++)check(WaterSurface.Depth(flat,x,z)==1,"merged deep ocean has exactly the original corner tint");

        int maximum=0,misses=0,repairs=0,frames=0,starvedRepairs=0;
        foreach(int seed in new[]{17,31,68}){
            var town=new GameState(seed).Town;var stream=new TerrainStream();int frame=0;var stages=new int[TerrainStream.Capacity];
            for(int z=0;z<Town.Height;z++)for(int x=0;x<Town.Width;x+=TerrainStream.Width)if(!TerrainSectionPlan.Walls(town,x,z)){
                for(int cx=x;cx<x+TerrainStream.Width;cx++){
                    int kind=town.At(cx,z);if(kind==(int)Terrain.Bridge||WaterSurface.IsWater(town,cx,z))continue;
                    check(TerrainElevation.Mode(town,cx,z)<TerrainElevation.CornerUpLeft,"a skipped wall row contains no diagonal caps");
                    for(int side=0;side<4;side++){
                        int nx=cx+(side==1?1:side==3?-1:0),nz=z+(side==0?-1:side==2?1:0);
                        if(kind==(int)Terrain.Beach&&WaterTerrain.IsSea(town,nx,nz))continue;
                        if(RiverBankGeometry.HasEdge(town,nx,nz,(side+2)%4))continue;
                        int ax=cx+(side==1||side==2?1:0),az=z+(side>=2?1:0),bx=cx+(side<2?1:0),bz=z+(side==1||side==2?1:0);
                        check(TerrainElevation.AtCell(town,cx,z,ax,az)<=TerrainElevation.AtCell(town,nx,nz,ax,az)+.001f&&TerrainElevation.AtCell(town,cx,z,bx,bz)<=TerrainElevation.AtCell(town,nx,nz,bx,bz)+.001f,"skipped wall rows have no exposed vertical edge");
                    }
                }
            }
            void Tick(float x,float z,int budget,bool starved=false){
                stream.Prepare(x,z);maximum=Math.Max(maximum,stream.WantedCount);
                int step=0;
                void Advance(){
                    int slot=stream.BuildSlot;
                    if(!stream.InProgress(slot)){stream.Hold();stages[slot]=TerrainSectionPlan.Stages(town,stream.X[slot],stream.Z[slot]);check(stages[slot]<=TerrainSectionPlan.MaximumStages,"section work fits the recovery bound, including upload");}
                    check(!stream.Visible[slot],"partial sections remain hidden while urgent work preempts prefetch");
                    if(--stages[slot]==0){stream.Complete();stream.Prepare(x,z);}
                }
                for(;step<budget&&stream.BuildSlot>=0;step++)Advance();
                int urgent=0,limit=stream.RepairStages;while(stream.Presentable&&stream.MissingVisible>0&&urgent<limit&&stream.BuildSlot>=0){Advance();urgent++;}
                if(urgent>0){if(starved)starvedRepairs++;else repairs++;}if(!starved)frames++;
            }
            for(int n=0;!stream.Presentable&&n<4000;n++)Tick(20,20,2);
            check(stream.Presentable&&stream.MissingWanted==0,"initial preparation warms the entire prefetch band");
            float px=20,pz=20;
            foreach(var goal in new[]{(140f,20f),(140f,140f),(20f,140f),(20f,20f),(140f,140f),(20f,20f)}){
                while(Math.Abs(px-goal.Item1)>.001f||Math.Abs(pz-goal.Item2)>.001f){
                    float dx=goal.Item1-px,dz=goal.Item2-pz,d=(float)Math.Sqrt(dx*dx+dz*dz),step=Math.Min(PlayerMovement.RunStep,d);
                    px+=dx/d*step;pz+=dz/d*step;Tick(px,pz,++frame%7==0?0:2);
                    check(stream.Presentable,"running, tile crossings and reversals never reenter a loading transition");
                    if(!stream.Ready)misses++;
                }
            }
            check(misses==0,"two-stage frame budgets with periodic stalls retain all conservatively visible geometry; misses="+misses);
            // Native masks can consume several consecutive background budgets.
            // Cover full-speed diagonal turns with no normal stages at all.
            for(int n=0;n<240;n++){
                px+=PlayerMovement.RunStep*.70710678f;pz+=PlayerMovement.RunStep*.70710678f*(n<120?1:-1);
                Tick(px,pz,0,true);
                check(stream.Ready&&stream.Presentable,"work-derived recovery completes every visible section during sustained background starvation and diagonal reversals");
            }
            // A renderer hitch cannot disable controls or replace the entire
            // world with a loading panel once continuous travel has begun.
            for(int n=0;n<100;n++){px+=.1f;stream.Prepare(px,pz);check(stream.Presentable,"starved background work cannot pause established travel");}
            stream.Prepare(140,140);check(!stream.Presentable,"a genuine cold teleport still prepares its destination safely");
            stream.Reset();check(!stream.Presentable,"changing towns explicitly starts initial preparation");
        }
        check(repairs*100<frames,"bounded visible repairs are exceptional, rather than the normal streaming budget");
        Console.WriteLine("Seamless stream: max "+maximum+" / "+TerrainStream.Capacity+" slots; coverage misses "+misses+"; bounded repair frames "+repairs+" / "+frames+" under two-stage/stalled-frame budgets; "+starvedRepairs+" recovery frames during 720 fully starved diagonal frames");
    }
}
