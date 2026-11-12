using AnimalTown;

internal static class ModelResidencyDemandChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var state=new GameState(204){HousePlaced=true,Species=0,Space=1,X=2,Z=2};
        var doors=new PortalSystem();var demand=new ModelResidencyDemand();
        for(int i=0;i<6;i++){state.Town.Residents[i].Space=16+i;state.Town.Residents[i].PosX=2;state.Town.Residents[i].PosZ=2;}
        demand.Prepare(state,doors);
        check(demand.Required[0]&&demand.Required.Count(x=>x)==1,"the player pins its shared species even when its NPC lives elsewhere");
        state.Town.Residents[2].Space=1;demand.Prepare(state,doors);
        check(demand.Required[2],"every current-room model stays pinned independently of camera culling");
        state.Town.Residents[2].Space=18;
        var npc=doors.Actors[3];npc.Phase=DoorPhase.Waiting;npc.Source=new ActorLocation(1,2,2,0);npc.Destination=new ActorLocation(18,2,2,0);
        demand.Prepare(state,doors);check(demand.Required[2],"a pending doorway keeps both sides of its model active");npc.Phase=DoorPhase.Idle;
        var player=doors.Actors[0];player.Phase=DoorPhase.Waiting;player.Source=new ActorLocation(1,2,2,0);player.Destination=new ActorLocation(19,2,2,0);
        demand.Prepare(state,doors);check(demand.Required[3]&&demand.DestinationSpace==19,"a player doorway prefetches the destination before its location commits");
        player.Phase=DoorPhase.Idle;demand.Prepare(state,doors,true);check(demand.Required.All(x=>x),"UV/creation inspection can pin the complete shared set");
        demand.Prepare(state,doors);check(!demand.Required[3],"obsolete destination and debug demand do not become permanent pins");

        state.Space=16;BuildingWorld.Entrance(state,1,out float ex,out float ez);
        state.Town.Residents[1].Space=0;state.Town.Residents[1].PosX=ex+1;state.Town.Residents[1].PosZ=ez-2;
        state.Town.Residents[2].Space=0;state.Town.Residents[2].PosX=ex<80?159:0;state.Town.Residents[2].PosZ=ez<80?159:0;
        demand.Prepare(state,doors,false,0);
        check(demand.Required[1]&&!demand.Required[2],"outdoor destination restoration includes a generous guard without fetching distant hidden species");
        state.Space=0;state.X=ex;state.Z=ez;
        var visibility=new ActorFrameVisibility();var camera=new InteriorCamera();
        for(int species=0;species<6;species++)state.Town.Residents[species].Space=0;
        for(int z=8;z<153;z+=24)for(int x=8;x<153;x+=24){
            state.X=x;state.Z=z;demand.Prepare(state,doors);visibility.Prepare(state,doors,camera,0,TerrainElevation.At(state.Town,x,z));
            for(int actor=1;actor<7;actor++)check(!visibility.Visible[actor]||demand.Required[actor-1],"every drawable outdoor actor is inside residency lookahead");
        }
        demand.Prepare(state,doors);long allocated=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<10000;i++)demand.Prepare(state,doors);
        check(GC.GetAllocatedBytesForCurrentThread()==allocated,"shared-model demand and lookahead are allocation-free");

        var inspection=new GameState(204){HousePlaced=true,Species=0,Space=0,X=20,Z=20};
        Array.Fill(inspection.Town.Tiles,(byte)Terrain.Grass);
        inspection.Town.Tiles[80*Town.Width+80]=(byte)Terrain.Water;inspection.Town.Tiles[81*Town.Width+80]=(byte)Terrain.Water;inspection.Town.NotifyTerrainChanged();
        var inspected=inspection.Town.Residents[1];inspected.Space=0;inspected.PosX=80.5f;inspected.PosZ=86.5f;
        var inspectionDoors=new PortalSystem();
        check(!ModelResidencyDemand.DrawableUsesSpace(inspection,inspectionDoors,1,0),"a distant inspection actor is initially outside the camera");
        WaterInspection.Focus(inspection,1);visibility.Prepare(inspection,inspectionDoors,camera,0,TerrainElevation.At(inspection.Town,inspection.X,inspection.Z));
        check(visibility.Visible[2]&&ModelResidencyDemand.DrawableUsesSpace(inspection,inspectionDoors,1,0),"final camera readiness follows a late water-inspection focus change");

        state.Space=16;state.X=2;state.Z=2;BuildingWorld.Entrance(state,1,out ex,out ez);
        player=doors.Actors[0];player.Phase=DoorPhase.Waiting;player.Source=new ActorLocation(16,2,2,0);player.Destination=new ActorLocation(0,ex,ez+1,0);
        var edge=state.Town.Residents[1];edge.Space=0;bool distinguishesLanding=false;
        for(float z=Math.Max(1,ez-45);z<Math.Min(159,ez+12);z+=.5f)for(float x=Math.Max(1,ex-30);x<Math.Min(159,ex+30);x+=.5f){
            edge.PosX=x;edge.PosZ=z;
            bool indoor=ModelResidencyDemand.DrawableUsesSpace(state,doors,1,0);
            state.Space=0;state.X=ex;state.Z=ez+1;bool landing=ModelResidencyDemand.DrawableUsesSpace(state,doors,1,0);
            state.Z=ez;bool socket=ModelResidencyDemand.DrawableUsesSpace(state,doors,1,0);
            state.Space=16;state.X=2;state.Z=2;
            check(indoor==landing,"door admission uses the exact committed outdoor landing camera");
            distinguishesLanding|=landing!=socket;
        }
        check(distinguishesLanding,"the destination fixture includes actors affected by the one-metre landing offset");
        player.Phase=DoorPhase.Idle;
        state.Space=0;state.X=80;state.Z=80;edge.PosX=80;edge.PosZ=80;
        var visual=doors.Actors[2];visual.Phase=DoorPhase.Crossing;visual.Building=1;visual.Tick=14;visual.Source=new ActorLocation(0,80,80,0);visual.Destination=new ActorLocation(16,2,2,0);
        var actual=doors.VisualLocation(state,2);state.X=actual.X;state.Z=actual.Z;
        visibility.Prepare(state,doors,camera,0,TerrainElevation.At(state.Town,state.X,state.Z));
        check(visibility.Visible[2]&&ModelResidencyDemand.DrawableUsesSpace(state,doors,1,0),"readiness uses doorway visual interpolation rather than stale logical coordinates");

        var gated=new GameState(204){HousePlaced=true};BuildingWorld.Entrance(gated,1,out ex,out ez);gated.X=ex;gated.Z=ez;gated.Facing=(float)Math.PI;
        for(int i=0;i<6;i++){gated.Town.Residents[i].Space=0;gated.Town.Residents[i].PosX=100+i;gated.Town.Residents[i].PosZ=100;}
        var portal=new PortalSystem();check(portal.ResourcesReady.All(x=>x)&&portal.Request(gated,0,1),"existing portal users start with rendering admission granted");
        portal.ResourcesReady[0]=false;for(int i=0;i<90;i++)portal.Tick(gated);
        check(portal.Actors[0].Phase==DoorPhase.Waiting&&gated.Space==0&&gated.X==ex,"pending restore cannot open or commit a doorway");
        portal.ResourcesReady[0]=true;for(int i=0;i<PortalSystem.CommitTick-1;i++)portal.Tick(gated);
        portal.ResourcesReady[0]=false;for(int i=0;i<10;i++)portal.Tick(gated);
        check(portal.Actors[0].Tick==PortalSystem.CommitTick-1&&gated.Space==0,"lost readiness also protects the atomic commit boundary");
        portal.ResourcesReady[0]=true;portal.Tick(gated);
        check(gated.Space==16&&portal.Actors[0].Committed,"successful restoration admits the original destination exactly once");
        var restored=SaveCodec.Decode(SaveCodec.Encode(gated));check(restored.Space==16,"resource admission does not change persistent space semantics");
    }
}
