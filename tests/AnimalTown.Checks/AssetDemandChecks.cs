using AnimalTown;
internal static class AssetDemandChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var state=new GameState(204);state.HousePlaced=true;var doors=new PortalSystem();var demand=new AssetDemand();state.Species=3;state.X=state.Z=120;
        for(int i=0;i<6;i++){var v=state.Town.Residents[i];v.Space=0;v.PosX=Town.Width-2;v.PosZ=Town.Height-2;}
        demand.Prepare(state,doors,false);check(demand.Priority[state.Species]==2&&demand.Priority[0]==0,"player's shared species remains pinned even when its NPC is distant");
        for(int type=0;type<BuildingCatalog.Count;type++)for(int p=0;p<2;p++)check(demand.Priority[AssetDemand.BuildingPart(type,p)]==2,"visible exterior/door definitions never become cold");
        var v0=state.Town.Residents[0];int wanted=0,prefetch=0;
        for(int x=55;x<=185;x+=2)for(int z=55;z<=185;z+=2){
            v0.PosX=x;v0.PosZ=z;demand.Prepare(state,doors,false);var p=CylinderWorld.Surface(z,state.Z,TerrainElevation.At(state.Town,x,z)+.9f);float eye=TerrainElevation.At(state.Town,state.X,state.Z);
            bool visible=RenderView.Sphere(x,p.Y,p.Z,2.7f,state.X,state.Z,eye)&&RenderView.AboveHorizon(p.Y,p.Z,2.7f,state.Z,eye);
            if(visible){wanted++;check(demand.Priority[0]==2,"every actually visible animated envelope pins its shared model");}
            if(demand.Priority[0]==1)prefetch++;
        }
        check(wanted>50&&prefetch>100,"residency sweep exercises visible and advance-prefetch envelopes");
        v0.Space=16;v0.PosX=3;v0.PosZ=2;
        demand.Prepare(state,doors,false);check(demand.Priority[0]==0,"distant indoor journeys need no native render model");
        state.Space=16;state.X=2;state.Z=3;demand.Prepare(state,doors,false);check(demand.Priority[0]==2,"room residents are pinned for immediate camera rotation");
        int type0=state.BuildingTypes[1];for(int p=2;p<7;p++)check(demand.Priority[AssetDemand.BuildingPart(type0,p)]==2,"current shared interior definition stays pinned");
        state.Space=0;BuildingWorld.Entrance(state,1,out float ex,out float ez);state.X=ex;state.Z=ez+.5f;state.Facing=(float)Math.PI;
        demand.Prepare(state,doors,false);check(demand.Priority[AssetDemand.BuildingPart(type0,2)]==2,"approaching the door prefetches its interior");
        state.Facing=0;demand.Prepare(state,doors,false);check(demand.Priority[0]==0,"exiting facing away stops prefetching the previous room's occupants");
        state.X=Town.Width-2;state.Z=Town.Height-2;demand.Prepare(state,doors,false);check(demand.Priority[AssetDemand.BuildingPart(type0,2)]==0,"unused shared interior parts are released once no room or approach needs them");state.X=ex;state.Z=ez+.5f;
        check(doors.Request(state,0,1),"residency portal fixture starts a player entrance");
        doors.VisualReady[0]=false;for(int i=0;i<100;i++)doors.Tick(state);
        check(state.Space==0&&doors.Actors[0].Tick==PortalSystem.CommitTick-1&&doors.PlayerFade==1,"cold destination waits under an opaque fade without committing or cancelling the player");
        demand.Prepare(state,doors,false);check(demand.Priority[0]==2&&demand.Priority[AssetDemand.BuildingPart(type0,2)]==2,"pending entry prefetches destination occupants and interior parts");
        var ready=new bool[AssetDemand.Capacity];Array.Fill(ready,true);ready[0]=false;demand.UpdateDoorReadiness(state,doors,ready);check(!doors.VisualReady[0],"player entry waits for a cold destination villager as well as the interior");
        ready[0]=true;ready[AssetDemand.BuildingPart(type0,2)]=false;demand.UpdateDoorReadiness(state,doors,ready);check(!doors.VisualReady[0],"player entry waits for every shared interior part");
        ready[AssetDemand.BuildingPart(type0,2)]=true;demand.UpdateDoorReadiness(state,doors,ready);
        var before=SaveCodec.Encode(state);doors.Tick(state);check(state.Space==16,"ready assets allow exactly one normal door commit");
        var restored=SaveCodec.Decode(before);check(restored.Space==0,"saving while assets prepare preserves the valid pre-commit location");
        var offscreen=new PortalSystem();offscreen.Actors[2].Phase=DoorPhase.Opening;offscreen.Actors[2].Source=new ActorLocation(0,1,1,0);offscreen.Actors[2].Destination=new ActorLocation(17,2,3,0);
        ready[1]=false;demand.Prepare(state,offscreen,false);demand.UpdateDoorReadiness(state,offscreen,ready);check(offscreen.VisualReady[2],"an NPC entering a different room never waits for render assets");
        demand.Prepare(state,doors,true);check(demand.Priority.All(x=>x==2),"creation and explicit inspector jumps prewarm every model before changing the view");
        var saved=SaveCodec.Encode(state);long allocated=GC.GetAllocatedBytesForCurrentThread();for(int n=0;n<1000;n++)demand.Prepare(state,doors,false);
        check(GC.GetAllocatedBytesForCurrentThread()==allocated&&saved.SequenceEqual(SaveCodec.Encode(state)),"residency demand allocates no frame data and never changes saved journeys or terrain");
    }
}
