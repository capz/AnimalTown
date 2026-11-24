using AnimalTown;

internal static class PortalResourceWaitChecks
{
    private static GameState Outside(int building)
    {
        var state=new GameState(204){HousePlaced=true};
        Array.Fill(state.Town.Tiles,(byte)Terrain.Grass);Array.Clear(state.Town.Elevation);state.Town.GroundCount=0;
        for(int npc=0;npc<Town.SavedVillagerCount;npc++){
            var resident=state.Town.Residents[npc];resident.Space=0;resident.PosX=100+npc;resident.PosZ=90;
        }
        BuildingWorld.Entrance(state,building,out state.X,out state.Z);state.Facing=(float)Math.PI;return state;
    }
    internal static void Run(Action<bool,string> check)
    {
        var state=Outside(1);var doors=new PortalSystem();
        check(doors.Request(state,0,1),"cold interior request reserves the original doorway before preparing its resources");
        doors.ResourcesReady[0]=false;
        for(int tick=0;tick<PortalSystem.MaximumWait*4;tick++)doors.Tick(state);
        check(doors.Busy(0)&&doors.Actors[0].Wait==0&&state.Space==0,"a multi-part room load spanning more than the traffic timeout retains its requested destination");
        state.X+=1;doors.Tick(state);
        check(!doors.Busy(0)&&state.Space==0,"a stale actor still cancels while rendering resources are unavailable");

        state=Outside(0);doors=new PortalSystem();check(doors.Request(state,0,0),"cold player home can be requested");
        doors.ResourcesReady[0]=false;state.HousePlaced=false;doors.Tick(state);
        check(!doors.Busy(0)&&doors.Owner(0)<0,"removing the requested building still cancels during resource preparation");

        state=Outside(1);doors=new PortalSystem();check(doors.Request(state,0,1),"cold resident home can be requested");
        doors.ResourcesReady[0]=false;state.Town.Residents[0].HomeX+=2;doors.Tick(state);
        check(!doors.Busy(0)&&state.Space==0,"moving the requested building still rejects stale source and destination sockets");

        state=Outside(1);doors=new PortalSystem();var blocker=state.Town.Residents[0];
        blocker.Space=16;BuildingWorld.InsideDoor(state,16,out blocker.PosX,out blocker.PosZ);blocker.PosZ-=.7f;
        check(doors.Request(state,0,1),"cold room can wait before an occupied landing");doors.ResourcesReady[0]=false;
        for(int tick=0;tick<PortalSystem.MaximumWait*2;tick++)doors.Tick(state);
        check(doors.Busy(0)&&doors.Actors[0].Wait==0,"resource waiting preserves the separate crowding timeout");
        doors.ResourcesReady[0]=true;
        for(int tick=0;tick<PortalSystem.MaximumWait+1;tick++)doors.Tick(state);
        check(!doors.Busy(0)&&state.Space==0,"a landing that remains crowded after resources are ready still times out normally");
    }
}
