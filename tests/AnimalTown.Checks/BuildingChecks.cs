using AnimalTown;
internal static class BuildingChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var s=new GameState(204);s.HousePlaced=true;
        check(BuildingWorld.Space(0)==1&&BuildingWorld.Space(1)==16&&BuildingWorld.Space(6)==21&&BuildingWorld.Space(7)==24&&BuildingWorld.Space(8)==25,"stable distinct building spaces");
        for(int b=0;b<BuildingWorld.Count;b++)check(BuildingWorld.Building(BuildingWorld.Space(b))==b,"building and space identity round trip");
        int home=1;BuildingWorld.Entrance(s,home,out float ex,out float ez);s.X=ex;s.Z=ez;s.Facing=(float)Math.PI;
        var doors=new PortalSystem();check(doors.Nearby(s,0,s.Facing)==home&&doors.Request(s,0,home),"player discovers and requests the real door");
        check(!doors.Request(s,0,home),"repeated input cannot double-enter");
        for(int i=0;i<PortalSystem.CommitTick-1;i++)doors.Tick(s);
        check(s.Space==0&&s.X==ex&&s.Z==ez,"animation does not mutate committed source position");
        var before=SaveCodec.Decode(SaveCodec.Encode(s));check(before.Space==0&&before.X==ex,"save before door commit stays outside");
        doors.Tick(s);check(s.Space==16&&BuildingWorld.CanStand(s,16,s.X,s.Z),"portal commits one valid indoor location");
        var after=SplitSaveCodec.Decode(SplitSaveCodec.Encode(s));check(after.Space==16&&Math.Abs(after.X-s.X)<.001f&&Math.Abs(after.Z-s.Z)<.001f,"split save persists committed player interior");
        for(int i=0;i<60;i++)doors.Tick(s);
        check(doors.Request(s,0,home),"same portal supports exit after cooldown");for(int i=0;i<40;i++)doors.Tick(s);
        check(s.Space==0&&Math.Abs(s.X-ex)<.001f&&Math.Abs(s.Z-ez-1)<.001f,"exit resolves the building's exterior socket");
        s.Space=16;s.X=2;s.Z=3.35f;
        check(!BuildingWorld.CanStand(s,16,.6f,1)&&BuildingWorld.CanStand(s,16,2,2),"furniture collision and free interior floor agree");
        for(int i=0;i<6;i++){var v=s.Town.Residents[i];v.Space=0;v.PosX=80+i;v.PosZ=80;}
        var npc=s.Town.Residents[0];npc.Space=16;npc.PosX=2;npc.PosZ=2.65f;
        s.Space=0;s.X=ex;s.Z=ez;
        var blocked=new PortalSystem();check(blocked.Request(s,0,home),"request can queue at occupied landing");for(int i=0;i<20;i++)blocked.Tick(s);
        check(s.Space==0&&blocked.Actors[0].Phase==DoorPhase.Waiting,"occupied destination prevents door transfer");npc.PosX=3;npc.PosZ=2;
        for(int i=0;i<40;i++)blocked.Tick(s);check(s.Space==16,"queued request proceeds once landing clears");
        var nav=new InteriorNavigation();s.X=2;s.Z=3.35f;npc.Space=16;npc.PosX=.5f;npc.PosZ=2.5f;
        for(int tick=0;tick<300;tick++){nav.BeginTick();nav.Tick(s,1,3,2);check(nav.LastExpansions<=128,"indoor searches obey shared per-tick budget");check(BuildingWorld.CanStand(s,16,npc.PosX,npc.PosZ),"indoor movement never crosses furniture or walls");}
        check(nav.Arrived(1),"NPC can find a furniture-aware indoor activity route");
        s.HouseTier=0;check(!s.CanPlace(10,1,3,0,1,0),"new furniture cannot occupy doorway clearance");
        var p=s.Town.Placed[0];p.Item=10;p.X=1;p.Z=3;p.Room=1;p.Owner=0;s.Town.PlacedCount=1;
        var preserved=SaveCodec.Decode(SaveCodec.Encode(s));check(preserved.Town.PlacedCount==1&&preserved.Town.Placed[0].Z==3,"legacy doorway-overlapping possessions are retained on load");
        s.Town.PlacedCount=0;
        var camera=new InteriorCamera();camera.Tick(1,false);int quarter=camera.Quarter;for(int i=0;i<20;i++)camera.Tick(1,false);check(quarter==1&&camera.Quarter==1,"held C-stick causes only one quarter-turn");camera.Tick(0,false);camera.Tick(-1,false);check(camera.Quarter==0,"C-stick must rearm before another turn");
        for(int turn=0;turn<4;turn++){
            camera.Tick(0,false);camera.Tick(1,false);for(int i=0;i<40;i++)camera.Tick(0,false);
            camera.Movement(1,0,out float x,out float y);check(Math.Abs(x*x+y*y-1)<.00001f,"camera-relative movement preserves speed");
            check(camera.Sphere(2,1,2,1,4),"room occupants remain visible at every camera angle");
            int count=0;for(int wall=0;wall<4;wall++)if(camera.WallVisible(wall))count++;check(count>=1&&count<=2,"camera-facing walls are cut away without transparent overdraw");
        }
        var journeyState=new GameState(931);journeyState.Space=16;journeyState.X=2;journeyState.Z=2;
        for(int i=0;i<6;i++){BuildingWorld.Entrance(journeyState,i+1,out float x,out float z);var v=journeyState.Town.Residents[i];v.PosX=x;v.PosZ=z;v.X=(int)x;v.Z=(int)z;v.JourneyBuilding=i+1;v.JourneyPhase=1;v.JourneyNextX=32;v.JourneyNextZ=28;}
        var journeys=new VillagerJourney();int entered=0,left=0,resumed=0;
        for(int tick=0;tick<2200;tick++){
            for(int i=0;i<6;i++)if(journeyState.Town.Residents[i].Space!=0)entered|=1<<i;
            journeys.Tick(journeyState);check(journeys.Indoors.LastExpansions<=128,"all NPC room planning shares one expansion budget");
            for(int i=0;i<6;i++){var v=journeyState.Town.Residents[i];if((entered&(1<<i))!=0&&v.Space==0)left|=1<<i;if(v.JourneyPhase==5&&v.JourneyNextX==32&&v.JourneyNextZ==28)resumed|=1<<i;}
            if(tick==150){var fields=BuildingSave.Encode(journeyState);var loaded=SaveCodec.Decode(SaveCodec.Encode(journeyState));check(BuildingSave.Encode(loaded).SequenceEqual(fields),"NPC journey intentions, dwell and spaces survive legacy save");var split=SplitSaveCodec.Decode(SplitSaveCodec.Encode(journeyState));check(BuildingSave.Encode(split).SequenceEqual(fields),"NPC journeys survive split save");}
        }
        if(entered!=63||left!=63){Console.WriteLine($"journey entered={entered} left={left}");for(int i=0;i<6;i++){var v=journeyState.Town.Residents[i];Console.WriteLine($"{i}: space={v.Space} phase={v.JourneyPhase} p={v.PosX},{v.PosZ} dwell={v.Dwell} door={journeys.Doors.Actors[i+1].Phase}");}}
        check(entered==63&&left==63,"all six NPCs enter and leave homes while player remains indoors");
        check(resumed==63,"every villager resumes its saved outdoor waypoint after the visit");
        check(journeyState.Space==16,"NPC journeys never switch the player's room");
        var houseState=new GameState(912){HousePlaced=true,Space=16};Array.Fill(houseState.Town.Tiles,(byte)Terrain.Grass);Array.Clear(houseState.Town.Elevation);houseState.Town.GroundCount=0;
        var route=new VillagerNavigation();var walker=houseState.Town.Residents[0];walker.PosX=houseState.Town.HouseX+1.5f;walker.PosZ=houseState.Town.HouseZ+2.5f;route.Tick(houseState);route.Target(0,houseState.Town.HouseX+5,houseState.Town.HouseZ+2);
        for(int step=0;step<600;step++){route.Tick(houseState);check(!(walker.PosX>=houseState.Town.HouseX+2&&walker.PosX<houseState.Town.HouseX+5&&walker.PosZ>=houseState.Town.HouseZ+1&&walker.PosZ<houseState.Town.HouseZ+4),"player home remains an NPC obstacle while player is indoors");}
        var migration=SaveCodec.Encode(s);int extension=BuildingSave.FieldCount*4;Array.Resize(ref migration,migration.Length-extension);migration[4]=6;Write(migration,16,migration.Length);Write(migration,20,(int)SaveCodec.Crc(migration,24,migration.Length-24));var migrated=SaveCodec.Decode(migration);check(migrated.Space==0&&migrated.Town.Residents.All(v=>v.Space==0),"v6 saves migrate actors outdoors without inventing room state");
        var wrong=BuildingSave.Encode(s);wrong[0]=15;bool rejected=false;try{BuildingSave.Decode(s,wrong);}catch(InvalidOperationException){rejected=true;}check(rejected,"unknown saved space rejected");
        static void Write(byte[] b,int at,int value){for(int i=0;i<4;i++)b[at+i]=(byte)((uint)value>>(i*8));}
    }
}
