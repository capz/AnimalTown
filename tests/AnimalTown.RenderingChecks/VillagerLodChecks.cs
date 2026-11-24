using AnimalTown;

internal static class VillagerLodChecks
{
    internal static void Run(Action<bool,string> check)
    {
        foreach(float pixels in new[]{24f,48,60,120}){
            float depth=VillagerLod.ModelHeight*480/(2*RenderView.TanY*pixels);
            float y=CylinderWorld.CameraHeight+depth*RenderView.ForwardY,z=CylinderWorld.CameraBack+depth*RenderView.ForwardZ;
            check(Math.Abs(VillagerLod.PixelHeight(y,z,0,0)-pixels)<.0001f,"LOD screen size matches the fixed camera perspective projection");
        }
        check(VillagerLod.Select(24,0)==1&&VillagerLod.Select(25,0)==0&&VillagerLod.Select(29,1)==1&&VillagerLod.Select(30,1)==0,"coarse geometry enters below 24 pixels and leaves above 30 with hysteresis");
        check(VillagerLod.SelectAnimation(48,0)==1&&VillagerLod.SelectAnimation(49,0)==0&&VillagerLod.SelectAnimation(59,1)==1&&VillagerLod.SelectAnimation(60,1)==0,"independent animation LOD enters below 48 pixels and leaves above 60");
        check(.045224f*VillagerLod.FullPixels<1.36f,"sampled native fox animation stays within the accepted 1.36px estimate at the full-detail return threshold");
        check(VillagerLod.DepthAtPixels(24)>35&&VillagerLod.DepthAtPixels(30)>28&&VillagerLod.DepthAtPixels(48)>17,"LOD thresholds derive reproducible camera depths rather than a player-distance cutoff");
        check(VillagerLod.CoarseAvailable(0)&&VillagerLod.CoarseAvailable(1)&&VillagerLod.CoarseAvailable(2)&&Enumerable.Range(3,3).All(x=>!VillagerLod.CoarseAvailable(x)),"only species with measured useful geometry reduction admit a second variant");
        check(VillagerLod.MaximumPoseError*VillagerLod.FullPixels<=1.5f,"native pose qualification limits the estimated return-threshold error for every eligible species");
        var clock=new AnimationFrameClock(7);
        clock.Step(1,0,true);check(clock.Consume(1,true,2)==0,"a distant actor defers its first simulation pose");
        for(int i=0;i<20;i++)check(clock.Consume(1,true,2)==0,"repeated rendered frames without simulation ticks do not cause skinning");
        clock.Step(1,0,true);check(Math.Abs(clock.Consume(1,true,2)-2f/30)<.000001f,"distant animation samples at fifteen Hz while preserving elapsed clip time");
        clock.Step(1,0,true);check(clock.Consume(1,false,2)==0,"occluded actors discard any partial cadence interval");
        clock.Step(1,0,true);check(clock.Consume(1,true,2)==0,"reappearing actors cannot catch up hidden animation time");
        clock.Step(1,1,true);check(clock.Consume(1,true,2)==0,"a new clip restarts its own cadence");
        clock.Step(1,1,true);check(Math.Abs(clock.Consume(1,true,2)-2f/30)<.000001f,"clip transitions preserve the reduced animation sample cadence");
        clock.Step(1,2,true);check(clock.Clip(1)==2&&clock.Consume(1,true,2)==0,"an authored idle clip remains selected while its distant pose is deferred");
        clock.Step(1,2,true);check(Math.Abs(clock.Consume(1,true,2)-2f/30)<.000001f,"authored idle animation advances at the same bounded fifteen-Hz cadence");
        Occlusion(check);
    }
    private static void Occlusion(Action<bool,string> check)
    {
        var state=new GameState(17){X=80,Z=80,HousePlaced=true};
        Array.Fill(state.Town.Tiles,(byte)Terrain.Grass);Array.Clear(state.Town.Elevation);
        // Isolate the generated shop rear-wall proxy and move other cottage
        // origins away from this view; a missing kit never earns an occluder.
        state.Town.HouseX=5;state.Town.HouseZ=5;
        for(int i=0;i<6;i++){state.Town.Residents[i].HomeX=2;state.Town.Residents[i].HomeZ=2;state.Town.Residents[i].Space=16+i;}
        state.BuildingTypes[7]=1;state.X=32.5f;state.Z=30;
        var proxies=new OpaqueActorOcclusion();proxies.Prepare(state,0,0,false);
        check(proxies.Count==0&&!proxies.Hidden(32.5f,1,20,.2f),"unloaded or undrawn building types cannot hide an actor");
        proxies.Prepare(state,0,2,false);check(proxies.Count>0,"ready generated building art contributes an opaque rear-wall core");
        int hidden=0;
        // An independent ray test validates every accepted sphere sample
        // against the actual opaque rear-wall plane. This catches unsafe
        // screen-box/projection shortcuts around either side or roof edge.
        BuildingWorld.Origin(state,7,out float bx,out float bz);
        var wall=CylinderWorld.Surface(bz,state.Z,TerrainElevation.At(state.Town,bx+1.5f,bz+1.5f));
        for(float z=10;z<25;z+=1)for(float y=-2;y<3;y+=.25f)for(float x=30;x<35;x+=.5f){
            const float radius=.2f;if(!proxies.Hidden(x,y,z,radius))continue;hidden++;
            for(int a=0;a<12;a++)for(int b=0;b<8;b++){
                float yaw=a*(float)Math.PI/6,pitch=b*(float)Math.PI/7;
                float px=x+radius*(float)(Math.Cos(yaw)*Math.Sin(pitch)),py=y+radius*(float)Math.Cos(pitch),pz=z+radius*(float)(Math.Sin(yaw)*Math.Sin(pitch));
                float ey=CylinderWorld.CameraHeight,ez=state.Z+CylinderWorld.CameraBack;
                float from=(ey-wall.Y)*wall.UpZ-(ez-wall.Z)*wall.UpY;
                float delta=(py-ey)*wall.UpZ-(pz-ez)*wall.UpY;
                float t=(-.06f-from)/delta;
                float ix=state.X+(px-state.X)*t;
                float iy=ey+(py-ey)*t,iz=ez+(pz-ez)*t;
                float height=(iy-wall.Y)*wall.UpY+(iz-wall.Z)*wall.UpZ;
                check(t>0&&t<1&&ix>bx&&ix<bx+3&&height>0&&height<2.35f,"every sampled ray to an occluded sphere intersects the authored solid rear wall before reaching it");
            }
        }
        check(hidden>0,"the building proxy rejects fully hidden bounds rather than only testing an empty feature path");
        check(!proxies.Hidden(32.5f,4,25,.2f)&&!proxies.Hidden(28,1,20,.2f),"silhouettes above the roof or beside a building remain submitted");
        var actors=new ActorFrameVisibility();var doors=new PortalSystem();var camera=new InteriorCamera();
        state.Town.HouseX=60;state.Town.HouseZ=55;state.X=63.5f;state.Z=60;
        var resident=state.Town.Residents[0];resident.Space=0;resident.PosX=63.5f;resident.PosZ=25;
        actors.Prepare(state,doors,camera,0,0);check(actors.Visible[1],"a real 2.7m animated envelope survives the previous viewport/horizon culling behind a nearby cottage");
        actors.Prepare(state,doors,camera,0,0,1);check(!actors.Visible[1]&&actors.OccludedActors>0,"opaque building rejection removes an actual established actor bound before animation and submission");
        resident.PosX+=4;actors.Prepare(state,doors,camera,0,0,1);check(actors.Visible[1],"a real animated silhouette beside the opaque core remains visible");
        state.X=32.5f;state.Z=30;
        proxies.Prepare(state,0,0,true);check(proxies.Count==0,"flat ground does not invent raised-cliff occluders");
        int builds=proxies.ProjectionBuilds;proxies.Prepare(state,0,0,true);check(proxies.ProjectionBuilds==builds,"stationary view and unchanged terrain reuse the projected opaque proxies");
        state.X=20;state.Z=70;
        for(int z=40;z<72;z++)for(int x=21;x<60;x++)state.Town.Elevation[z*Town.Width+x]=TerrainElevation.Block;
        state.Town.NotifyTerrainChanged();proxies.Prepare(state,0,0,true);check(proxies.Count>0,"an interior raised flat block contributes exact one-metre terrain row chords");
        int hiddenByCliff=0;
        for(int z=25;z<40;z++){
            var center=CylinderWorld.Surface(z,state.Z,.9f);
            if(!RenderView.Sphere(36,center.Y,center.Z,2.7f,state.X,state.Z)||!RenderView.AboveHorizon(center.Y,center.Z,2.7f,state.Z)||!proxies.Hidden(36,center.Y,center.Z,2.7f))continue;
            hiddenByCliff++;
            for(int a=0;a<12;a++)for(int b=0;b<8;b++){
                float yaw=a*(float)Math.PI/6,pitch=b*(float)Math.PI/7;
                float px=36+2.7f*(float)(Math.Cos(yaw)*Math.Sin(pitch)),py=center.Y+2.7f*(float)Math.Cos(pitch),pz=center.Z+2.7f*(float)(Math.Sin(yaw)*Math.Sin(pitch));
                bool hit=false;
                for(int row=40;row<72&&!hit;row++)for(int col=21;col<60&&!hit;col++){
                    var lo=CylinderWorld.Surface(row,state.Z,2);var hi=CylinderWorld.Surface(row+1,state.Z,2);
                    hit=Triangle(state.X,CylinderWorld.CameraHeight,state.Z+CylinderWorld.CameraBack,px,py,pz,col,lo.Y,lo.Z,col+1,lo.Y,lo.Z,col+1,hi.Y,hi.Z)||
                        Triangle(state.X,CylinderWorld.CameraHeight,state.Z+CylinderWorld.CameraBack,px,py,pz,col,lo.Y,lo.Z,col+1,hi.Y,hi.Z,col,hi.Y,hi.Z);
                }
                check(hit,"every sampled ray to a cliff-occluded real actor bound crosses an actual rendered opaque terrain triangle");
            }
        }
        check(hiddenByCliff>0,"raised terrain occlusion rejects an actual established actor envelope that survives previous culling");
        for(int z=40;z<72;z++)for(int x=21;x<60;x++)state.Town.Elevation[z*Town.Width+x]=TerrainElevation.RampNorth;
        state.Town.NotifyTerrainChanged();proxies.Prepare(state,0,0,true);check(proxies.Count==0,"ramps, open edges and changing terrain invalidate opaque block proxies");
        proxies.Prepare(state,0,2,true);builds=proxies.ProjectionBuilds;
        long allocated=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1000;i++){proxies.Prepare(state,0,2,true);proxies.Hidden(32.5f,1,20,.2f);}
        check(allocated==GC.GetAllocatedBytesForCurrentThread(),"stable occlusion preparation and tests allocate no frame-time managed memory");
        check(proxies.ProjectionBuilds==builds,"a thousand stationary actor frames do no additional proxy projection or cylinder-row work");
        state.Town.HouseX++;proxies.Prepare(state,0,2,true);check(proxies.ProjectionBuilds==++builds,"building relocation invalidates projected occluders even if terrain and object counts do not change");
        state.X+=.1f;proxies.Prepare(state,0,2,true);check(proxies.ProjectionBuilds==++builds,"camera movement rebuilds projections using the shared reference-row scroll transform");
        proxies.Prepare(state,.1f,2,true);check(proxies.ProjectionBuilds==++builds,"eye elevation invalidates opaque projection cache");
        proxies.Prepare(state,.1f,0,true);check(proxies.ProjectionBuilds==++builds,"retired building kits immediately invalidate their occluder mask");
        state.Town.NotifyTerrainChanged();proxies.Prepare(state,.1f,0,true);check(proxies.ProjectionBuilds==++builds,"terrain revision invalidates projected opaque proxies");
        var empty=new ActorFrameVisibility();for(int i=0;i<6;i++)state.Town.Residents[i].Space=16+i;
        empty.Prepare(state,new PortalSystem(),new InteriorCamera(),0,0,3,true);
        check(empty.OcclusionBuilds==0,"a view with no drawable NPCs prepares no opaque proxies even when the player is visible");
    }
    private static bool Triangle(float ex,float ey,float ez,float tx,float ty,float tz,float ax,float ay,float az,float bx,float by,float bz,float cx,float cy,float cz)
    {
        float dx=tx-ex,dy=ty-ey,dz=tz-ez,ux=bx-ax,uy=by-ay,uz=bz-az,vx=cx-ax,vy=cy-ay,vz=cz-az;
        float px=dy*vz-dz*vy,py=dz*vx-dx*vz,pz=dx*vy-dy*vx,det=ux*px+uy*py+uz*pz;
        if(Math.Abs(det)<1e-8f)return false;
        float sx=ex-ax,sy=ey-ay,sz=ez-az,u=(sx*px+sy*py+sz*pz)/det;
        if(u<-.00001f||u>1.00001f)return false;
        float qx=sy*uz-sz*uy,qy=sz*ux-sx*uz,qz=sx*uy-sy*ux,v=(dx*qx+dy*qy+dz*qz)/det;
        if(v<-.00001f||u+v>1.00001f)return false;
        float t=(vx*qx+vy*qy+vz*qz)/det;return t>0&&t<1;
    }
}
