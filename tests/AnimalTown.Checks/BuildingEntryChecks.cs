using AnimalTown;

internal static class BuildingEntryChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var state=new GameState(7319);Array.Fill(state.Town.Tiles,(byte)Terrain.Grass);Array.Clear(state.Town.Elevation);state.Town.GroundCount=0;
        var prompt=new BuildingPrompt();string[] names={"Alex","Milo","Ness","O'Leaf"};
        for(int p=0;p<4;p++){state.SelectProfile(p);state.Name=names[p];state.HousePlaced=true;state.Town.HouseX=10+p*25;state.Town.HouseZ=60;}
        for(int p=0;p<4;p++){
            int building=BuildingWorld.PlayerBuilding(p);
            check(BuildingWorld.Role(building)==BuildingRole.House&&BuildingWorld.OwnerName(state,building)==names[p],"each home identifies its owning profile, independent of the current player");
            check(prompt.Enter(state,building)=="Enter "+names[p]+"'s House","entry uses the owner's possessive house name");
        }
        for(int n=0;n<6;n++)check(BuildingWorld.Role(n+1)==BuildingRole.House&&prompt.Enter(state,n+1)=="Enter "+state.Town.Residents[n].Name+"'s House","all six NPC homes use their resident's name");
        check(BuildingWorld.Role(7)==BuildingRole.Store&&prompt.Enter(state,7)=="Enter Hearth's Store","store name stays commercial even though its art is a cottage");
        check(BuildingWorld.Role(8)==BuildingRole.TownHall&&prompt.Enter(state,8)=="Enter Town Hall","town hall has its structure name without a house suffix");
        check(BuildingWorld.OwnerName(state,7)==""&&BuildingWorld.OwnerName(state,8)=="","public structures do not acquire a household owner");
        state.Name="WWWWWWWWWWWWWWWW";check(prompt.Enter(state,BuildingWorld.PlayerBuilding(3))=="Enter WWWWWWWWWWWWWWWW's House","long current-profile names refresh cached prompts");
        state.Town.Residents[0].Name="Fern";check(prompt.Enter(state,1)=="Enter Fern's House","renamed residents refresh their cached entry labels");
        for(int i=0;i<10;i++)prompt.Enter(state,1);
        long allocated=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1000;i++)prompt.Enter(state,1);
        check(GC.GetAllocatedBytesForCurrentThread()==allocated,"unchanged entry labels allocate no frame garbage");
        bool rejected=false;try{BuildingWorld.Role(-1);}catch(ArgumentOutOfRangeException){rejected=true;}check(rejected,"invalid building identities are rejected");

        var s=new GameState(204){HousePlaced=true};Array.Fill(s.Town.Tiles,(byte)Terrain.Grass);Array.Clear(s.Town.Elevation);s.Town.GroundCount=s.Town.PlacedCount=0;
        const int home=1;BuildingWorld.Entrance(s,home,out float ex,out float ez);
        foreach(var offset in new[]{(x:0f,z:1.5f),(x:1.4f,z:.3f),(x:-1.4f,z:.3f),(x:0f,z:PortalSystem.PlayerEntryRadius)}){
            s.Space=0;s.X=ex+offset.x;s.Z=ez+offset.z;s.Facing=PlayerMovement.Yaw(ex-s.X,ez-s.Z);var doors=new PortalSystem();
            check(doors.Nearby(s,0,s.Facing)==home&&doors.Request(s,0,home),"expanded front and side approaches show an actionable prompt, including the exact radius");
            for(int tick=0;tick<PortalSystem.Duration;tick++)doors.Tick(s);
            check(s.Space==BuildingWorld.Space(home),"entry from the enlarged reach actually completes the door transition");
        }
        s.Space=0;s.X=ex;s.Z=ez+PortalSystem.PlayerEntryRadius+.02f;s.Facing=(float)Math.PI;
        var reach=new PortalSystem();check(reach.Nearby(s,0,s.Facing)<0&&!reach.Request(s,0,home),"prompt and request both reject points outside the expanded radius");
        s.Z=ez-.3f;s.Facing=0;check(reach.Nearby(s,0,s.Facing)<0&&!reach.Request(s,0,home),"extra reach cannot enter through the rear side of the door");
        s.Z=ez+1.5f;s.Facing=0;check(reach.Nearby(s,0,s.Facing)<0,"looking away still hides the entry prompt");s.Facing=(float)Math.PI;
        var tree=s.Town.Ground[0];tree.Kind=3;tree.Item=1;tree.X=(int)ex;tree.Z=(int)(ez+.5f);s.Town.GroundCount=1;
        check(reach.Nearby(s,0,s.Facing)<0&&!reach.Request(s,0,home),"expanded entry cannot cross a tree-blocked approach");s.Town.GroundCount=0;
        s.Town.SetTerrain((int)ex,(int)(ez+.5f),Terrain.Water);check(reach.Nearby(s,0,s.Facing)<0&&!reach.Request(s,0,home),"expanded entry cannot jump across water");s.Town.SetTerrain((int)ex,(int)(ez+.5f),Terrain.Grass);
        var chair=s.Town.Placed[0];chair.Item=10;chair.X=(int)ex;chair.Z=(int)(ez+.5f);chair.Room=0;s.Town.PlacedCount=1;
        check(reach.Nearby(s,0,s.Facing)<0&&!reach.Request(s,0,home),"expanded entry cannot pass through outdoor furniture");s.Town.PlacedCount=0;
        int cliffX=(int)ex/2*2,cliffZ=(int)(ez+.5f)/2*2;for(int z=cliffZ;z<cliffZ+2;z++)for(int x=cliffX;x<cliffX+2;x++)s.Town.Elevation[z*Town.Width+x]=TerrainElevation.Block;s.Town.NotifyTerrainChanged();
        check(reach.Nearby(s,0,s.Facing)<0&&!reach.Request(s,0,home),"expanded entry cannot bypass an abrupt cliff step");Array.Clear(s.Town.Elevation);s.Town.NotifyTerrainChanged();
        var npc=s.Town.Residents[0];npc.Space=0;npc.PosX=ex;npc.PosZ=ez+1.5f;
        check(!new PortalSystem().Request(s,1,home),"NPC journey reach stays unchanged");npc.PosZ=ez+.8f;check(new PortalSystem().Request(s,1,home),"NPCs can still use their established approach reach");
        s.Space=16;BuildingWorld.InsideDoor(s,16,out s.X,out s.Z);s.Z-=1.1f;
        check(new PortalSystem().Nearby(s,0,0)<0&&!new PortalSystem().Request(s,0,home),"interior exit range stays at its original reach");s.Z+=.3f;
        check(new PortalSystem().Nearby(s,0,0)==home&&new PortalSystem().Request(s,0,home),"interior exit prompt and request remain consistent");
    }
}
