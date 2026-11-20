using AnimalTown;
using Dolphin.Input;

internal static class WorldSimulationChecks
{
    private static readonly string[] overlays={"menu","item","map","dialogue","post","mail","lost","shop","store","sell","shop-display","shop-list","shop-price","shop-stored","plot","daylight-location"};
    internal static void Run(Action<bool,string> check)
    {
        // Every overlay must produce the same live town as leaving it on screen
        // with no input. Include NPC entries/exits, indoor walks and traffic.
        foreach(string overlay in overlays){
            var world=new TownGame(JourneyState());var menu=new TownGame(JourneyState()){screen=overlay};
            int entered=0,left=0;bool animated=false;
            for(int frame=0;frame<1200;frame++){
                world.Step(default);menu.Step(default);
                check(BuildingSave.Encode(world.state).SequenceEqual(BuildingSave.Encode(menu.state)),overlay+" keeps journeys and portal positions in sync with the world");
                for(int i=0;i<6;i++){
                    if(menu.state.Town.Residents[i].Space!=0)entered|=1<<i;else if((entered&(1<<i))!=0)left|=1<<i;
                    if(menu.villagersMoving[i]&&menu.animationClock.Consume(i+1)>0)animated=true;
                    world.animationClock.Consume(i+1);
                }
            }
            check(entered==63&&left==63,overlay+" allows all six villagers to enter and leave homes");
            check(animated,overlay+" advances walking animation in the player's room");
            check(menu.state.Town.Traffic.Visits.SequenceEqual(world.state.Town.Traffic.Visits),overlay+" records the same anonymous traffic as the world");
            check(menu.state.Town.Day==11118&&menu.townClock.MinuteOfDay==750,overlay+" advances calendar and clock");
        }
        // Held navigation and run buttons belong to the UI, indoors and out.
        var navigation=new ControllerState(1,(int)(ControllerButton.B|ControllerButton.DPadRight),0,0,new StickState(1,1),new StickState(1,0),0,0);
        foreach(string overlay in overlays.Where(s=>s!="plot"))foreach(int space in new[]{0,16}){
            var game=new TownGame(new GameState(931){Space=space,X=2,Z=2}){screen=overlay,moveClip=1,playerRunning=true};
            for(int i=0;i<60;i++)game.Step(navigation);
            check(game.state.X==2&&game.state.Z==2&&game.state.Space==space,overlay+" consumes navigation without moving the player");
            check(!game.playerRunning&&game.animationClock.Clip(0)==-1,overlay+" stops the player's prior run animation");
            check(game.indoorCamera.Angle==0,overlay+" does not rotate the room from menu input");
        }
        // Scripted movement must still complete if an overlay gains focus.
        foreach(string overlay in overlays){
            var game=new TownGame(new GameState(931)){screen=overlay};
            BuildingWorld.Entrance(game.state,1,out game.state.X,out game.state.Z);
            foreach(var resident in game.state.Town.Residents){resident.Space=TownShop.Room;resident.PosX=2;resident.PosZ=2;}
            check(game.villagers.Doors.Request(game.state,0,1),"queued player portal fixture is valid");
            bool walking=false;
            for(int i=0;i<PortalSystem.Duration+2;i++){
                game.Step(default);walking|=game.animationClock.Clip(0)==0;
            }
            check(walking&&game.state.Space==16&&!game.villagers.Doors.Busy(0),overlay+" completes an automated player doorway transition");
            check(game.animationClock.Clip(0)==-1&&game.facingYaw==game.state.Facing,overlay+" returns the player to idle facing the destination");
        }
        var typing=new TownGame(JourneyState()){screen="dialogue",message="Hello neighbour"};
        for(int i=0;i<30;i++)typing.Step(default);
        check(typing.typeAt==typing.message.Length,"dialogue text continues alongside world ticks");
        var loading=new TownGame(JourneyState()){resourcesReady=false};
        loading.Step(default);check(loading.tick==0&&loading.state.Town.Residents[0].Space==0,"unloaded assets still gate initial simulation");
        loading.resourcesReady=true;loading.state.Space=0;loading.terrain.Ready=false;
        loading.Step(default);check(loading.tick==0,"unready outdoor terrain still gates initial simulation");
        var preview=new TownGame(JourneyState()){screen="plot"};
        using(var draft=new OnboardingDraft(preview.state,0,false)){
            preview.session.Draft=draft;var before=BuildingSave.Encode(preview.state);
            for(int i=0;i<120;i++)preview.Step(default);
            check(BuildingSave.Encode(preview.state).SequenceEqual(before),"onboarding preview keeps its uncommitted town frozen");
        }
        var winter=new TownGame(new GameState(931){Space=16,X=2,Z=2}){screen="menu"};
        for(int i=0;i<4;i++)winter.renderDebug.CycleSeason();
        winter.Step(default);
        var town=winter.state.Town;
        const int snowX=100,snowZ=80;
        town.Tiles[snowZ*Town.Width+snowX]=(byte)Terrain.Grass;town.Elevation[snowZ*Town.Width+snowX]=0;
        for(int i=0;i<SnowWear.MaxVisits;i++)town.Traffic.Enter(town,snowX,snowZ);
        winter.SetSnowfall(false);
        for(int i=0;i<1200;i++)winter.Step(default);
        check(town.Traffic.Snow.VisitCount(snowX,snowZ)==SnowWear.MaxVisits,"the live simulation pauses recovery when the snowfall toggle is off");
        winter.SetSnowfall(true);
        for(int i=0;i<1200;i++)winter.Step(default);
        check(town.Traffic.Snow.VisitCount(snowX,snowZ)==SnowWear.MaxVisits-1,"outdoor snowfall refills dents while the player is indoors");
        Console.WriteLine("Live world simulation behind gameplay overlays checks passed");
    }
    private static GameState JourneyState()
    {
        var state=new GameState(931){Space=16,X=2,Z=2};
        for(int i=0;i<6;i++){
            BuildingWorld.Entrance(state,i+1,out float x,out float z);var v=state.Town.Residents[i];
            v.PosX=x;v.PosZ=z;v.X=(int)x;v.Z=(int)z;v.JourneyBuilding=i+1;v.JourneyPhase=1;v.JourneyNextX=32;v.JourneyNextZ=28;
        }
        return state;
    }
}
