using AnimalTown;
using AnimalTown.Jobs;
internal static class BuildingChecks
{
    internal static void Run(Action<bool,string> check)
    {
        BuildingEntryChecks.Run(check);
        CrowdsAndPickups(check);
        QuietJourneys(check);
        ShopChecks.Run(check);
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
        var camera=new InteriorCamera();
        check(camera.WallVisible(0)&&camera.WallVisible(1)&&camera.WallVisible(3)&&!camera.WallVisible(2),"default indoor view shows back and side walls but never the front wall");
        camera.Tick(1,false);int quarter=camera.Quarter;for(int i=0;i<20;i++)camera.Tick(1,false);check(quarter==1&&camera.Quarter==1,"held C-stick causes only one quarter-turn");camera.Tick(0,false);camera.Tick(-1,false);check(camera.Quarter==0,"C-stick must rearm before another turn");
        int[] frontWalls={1,0,3,2};
        for(int turn=0;turn<4;turn++){
            camera.Tick(0,false);camera.Tick(1,false);for(int i=0;i<40;i++)camera.Tick(0,false);
            camera.Movement(1,0,out float x,out float y);check(Math.Abs(x*x+y*y-1)<.00001f,"camera-relative movement preserves speed");
            check(camera.Sphere(2,1,2,1,4),"room occupants remain visible at every camera angle");
            int count=0;for(int wall=0;wall<4;wall++)if(camera.WallVisible(wall))count++;
            check(count==3&&!camera.WallVisible(frontWalls[turn]),"each cardinal view retains both side walls and the back while cutting away its front wall");
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
        var visitors=new[]{new SavedVisitor(171,0,new JobIdentity(171,1),"Juniper","hello",2,0),new SavedVisitor(283,1,new JobIdentity(283,2),"Sorrel","hiya",3,0)};
        int everyone=(1<<Town.NpcActorCount)-1,peakCrowd=0;
        foreach(int seed in new[]{204,931,911,912}){
            var generated=new GameState(seed){Space=16,X=2,Z=2};generated.SetTownVisitors(visitors);
            var travel=new VillagerJourney();int visited=0,returned=0;
            for(int frame=0;frame<7200&&returned!=everyone;frame++){
                travel.Tick(generated);check(travel.Outdoors.LastExpansions<=128&&travel.Indoors.LastExpansions<=128,"generated-town journeys preserve both shared search budgets");
                for(int i=0;i<Town.NpcActorCount;i++){
                    if(generated.Town.NpcAt(i).Space!=0)visited|=1<<i;else if((visited&(1<<i))!=0)returned|=1<<i;
                }
                if(frame%60==0)peakCrowd=Math.Max(peakCrowd,PeakNpcCrowd(generated));
            }
            check(visited==everyone&&returned==everyone,$"all residents and visitors enter and leave buildings from distributed spawns in town {seed} (entered {visited:x}, returned {returned:x})");
        }
        check(peakCrowd<Town.NpcActorCount,$"no full NPC crowd within 12m or sharing a room during seeded journeys (observed peak {peakCrowd})");
        Console.WriteLine($"Generated journeys: peak {peakCrowd} NPCs within 12m of one NPC or in one room");
        var houseState=new GameState(912){HousePlaced=true,Space=16};Array.Fill(houseState.Town.Tiles,(byte)Terrain.Grass);Array.Clear(houseState.Town.Elevation);houseState.Town.GroundCount=0;
        var route=new VillagerNavigation();var walker=houseState.Town.Residents[0];walker.PosX=houseState.Town.HouseX+1.5f;walker.PosZ=houseState.Town.HouseZ+2.5f;route.Tick(houseState);route.Target(0,houseState.Town.HouseX+5,houseState.Town.HouseZ+2);
        for(int step=0;step<600;step++){route.Tick(houseState);check(!(walker.PosX>=houseState.Town.HouseX+2&&walker.PosX<houseState.Town.HouseX+5&&walker.PosZ>=houseState.Town.HouseZ+1&&walker.PosZ<houseState.Town.HouseZ+4),"player home remains an NPC obstacle while player is indoors");}
        var relocation=new GameState(93){HousePlaced=true};BuildingWorld.Entrance(relocation,0,out relocation.X,out relocation.Z);var movingDoor=new PortalSystem();check(movingDoor.Request(relocation,0,0),"player house portal requested");movingDoor.Tick(relocation);relocation.Town.HouseX+=20;movingDoor.Tick(relocation);check(!movingDoor.Busy(0)&&movingDoor.Owner(0)<0&&relocation.Space==0,"moving a building cancels an uncommitted doorway instead of teleporting to stale coordinates");
        var traffic=new GameState(911);BuildingWorld.Entrance(traffic,1,out traffic.X,out traffic.Z);var visitor=traffic.Town.Residents[0];visitor.Space=16;BuildingWorld.InsideDoor(traffic,16,out visitor.PosX,out visitor.PosZ);var crossing=new PortalSystem();check(crossing.Request(traffic,0,1)&&crossing.Request(traffic,1,1),"opposite-direction door requests can queue");
        for(int frame=0;frame<240;frame++)crossing.Tick(traffic);
        check(traffic.Space==16&&visitor.Space==0&&!crossing.Busy(0)&&!crossing.Busy(1),"opposite door traffic steps aside and completes without deadlock");
        var migration=SaveCodec.EncodeLegacy(s);int extension=BuildingSave.LegacyFieldCount*4;Array.Resize(ref migration,migration.Length-extension);migration[4]=6;Write(migration,16,migration.Length);Write(migration,20,(int)SaveCodec.Crc(migration,24,migration.Length-24));var migrated=SaveCodec.Decode(migration);check(migrated.Space==0&&migrated.Town.Residents.All(v=>v.Space==0),"v6 saves migrate actors outdoors without inventing room state");
        var wrong=BuildingSave.Encode(s);wrong[0]=15;bool rejected=false;try{BuildingSave.Decode(s,wrong);}catch(InvalidOperationException){rejected=true;}check(rejected,"unknown saved space rejected");
        static void Write(byte[] b,int at,int value){for(int i=0;i<4;i++)b[at+i]=(byte)((uint)value>>(i*8));}
    }

    private static void QuietJourneys(Action<bool,string> check)
    {
        var s=new GameState(931){Space=BuildingWorld.Space(1),X=2,Z=2};
        s.SetTownVisitors(Array.Empty<SavedVisitor>());
        Array.Fill(s.Town.Tiles,(byte)Terrain.Grass);Array.Clear(s.Town.Elevation);s.Town.GroundCount=0;
        for(int i=0;i<Town.LocalResidentCount;i++){
            var v=s.Town.NpcAt(i);v.PosX=100+i*2;v.PosZ=70;v.Dwell=5000;
        }
        var resident=s.Town.NpcAt(0);
        BuildingWorld.Entrance(s,1,out resident.PosX,out resident.PosZ);resident.Dwell=1;
        var incoming=s.Town.NpcAt(1);incoming.JourneyPhase=1;incoming.JourneyBuilding=2;incoming.Dwell=0;
        var travel=new VillagerJourney();travel.Tick(s);travel.Tick(s);
        check(resident.JourneyPhase==1&&resident.JourneyBuilding>2,"a villager chooses an empty room over rooms with an occupant or an approaching villager");
    }

    private static int PeakNpcCrowd(GameState s)
    {
        int peak=0;
        for(int i=0;i<Town.NpcActorCount;i++){
            var center=s.Town.NpcAt(i);int crowd=0;
            for(int j=0;j<Town.NpcActorCount;j++){
                var other=s.Town.NpcAt(j);
                float dx=center.PosX-other.PosX,dz=center.PosZ-other.PosZ;
                if(center.Space==other.Space&&(center.Space!=0||dx*dx+dz*dz<=12*12))crowd++;
            }
            peak=Math.Max(peak,crowd);
        }
        return peak;
    }

    private static void CrowdsAndPickups(Action<bool,string> check)
    {
        var s=new GameState(567);
        Array.Fill(s.Town.Tiles,(byte)Terrain.Grass);Array.Clear(s.Town.Elevation);
        s.Town.GroundCount=0;s.X=64.5f;s.Z=60.5f;
        check(s.Town.AddGround(10,64,61,0,0)&&s.Pickup(64,61)&&s.Find(10)>=0,"pickup still puts the item in the backpack");
        var drop=s.Town.Ground[0];
        float dx=drop.X+.5f-s.X,dz=drop.Z+.5f-s.Z;
        check(s.Town.GroundCount==1&&dx*dx+dz*dz>=3600&&s.Town.FindGround(64,61)<0&&s.CanWalk(drop.X+.5f,drop.Z+.5f,0),
            "replacement appears on eligible ground at least three map tiles from the player");
        Array.Fill(s.Pockets,10);
        check(!s.Pickup(drop.X,drop.Z)&&s.Town.GroundCount==1,"full backpacks do not move the ground item");

        BuildingWorld.Entrance(s,1,out float ex,out float ez);
        var doors=new PortalSystem();var a=s.Town.Residents[0];var b=s.Town.Residents[1];
        a.PosX=b.PosX=ex;a.PosZ=b.PosZ=ez;
        doors.ResourcesReady[1]=false;
        check(doors.Request(s,1,1)&&doors.Request(s,2,1)&&a.Space==16&&b.Space==16&&doors.Owner(1)<0,
            "NPCs enter an unobserved house immediately without reserving the door or rendering resources");
        foreach(var resident in new[]{a,b})
            check(BuildingWorld.CanStand(s,16,resident.PosX,resident.PosZ,people:false)&&
                !BuildingWorld.DoorClearance(4,resident.PosX,resident.PosZ,.22f,.22f),"scattered occupants stay off fixtures and out of the doorway");
        check(BuildingWorld.CanStand(s,16,a.PosX,a.PosZ,1,true),"scattered occupants have separate positions");

        s.Space=16;BuildingWorld.InsideDoor(s,16,out s.X,out s.Z);
        a.Space=b.Space=0;a.PosX=b.PosX=ex;a.PosZ=ez;b.PosZ=ez+1;
        check(doors.Request(s,0,1),"player can request an exit with NPCs in the approach");
        for(int i=0;i<PortalSystem.Duration;i++)doors.Tick(s);
        check(s.Space==0&&Math.Abs(s.X-ex)<.001f&&Math.Abs(a.PosX-ex)>=.44f&&Math.Abs(b.PosX-ex)>=.44f,
            "player exit clears both the exterior doorway and the landing");

        s.Space=16;a.PosX=32.5f;a.PosZ=b.PosZ=40.5f;b.PosX=33;
        var nav=new VillagerNavigation();
        Array.Fill(nav.Paused,true);nav.Tick(s);nav.Paused[0]=false;
        var route=nav.Routes[0];route.Path[0]=40*Town.Width+34;route.Length=1;route.Wait=0;
        for(int tick=0;tick<7;tick++)nav.Tick(s);
        check(a.PosX>32.6f&&b.PosX>33.05f&&b.PosX-a.PosX>=.44f,
            "outdoor villagers can push an idle villager without occupying the same space");

        a.PosX=b.PosX=33.5f;a.PosZ=45.5f;b.PosZ=46.5f;nav.Paused[1]=false;
        nav.Target(0,33,51);nav.Target(1,33,40);
        for(int tick=0;tick<500&&(a.PosZ<=48||b.PosZ>=44);tick++)nav.Tick(s);
        check(a.PosZ>48&&b.PosZ<44,"walkers on opposing routes use different sides and continue past each other");

        s.Space=0;a.Space=b.Space=16;a.PosX=1.25f;b.PosX=1.75f;a.PosZ=b.PosZ=2.75f;
        var indoors=new InteriorNavigation();
        for(int tick=0;tick<40;tick++){indoors.BeginTick();indoors.Tick(s,1,3.25f,2.75f);}
        check(a.PosX>1.5f&&b.PosX>1.9f&&BuildingWorld.CanStand(s,16,b.PosX,b.PosZ,2,true),
            "indoor villagers can push an idle villager along clear floor");

        Array.Clear(s.Pockets);
        Array.Fill(s.Town.Tiles,(byte)Terrain.Sea);
        check(s.Pickup(drop.X,drop.Z)&&s.Town.GroundCount==0,
            "pickup succeeds without placing a nearby replacement when there is no eligible ground");
    }
}
