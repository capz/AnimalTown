using AnimalTown;

internal static class WorldLightingChecks
{
    internal static void Run(Action<bool,string> check)
    {
        check(WorldLighting.Tint(720)==0xffffff&&WorldLighting.Lamps(720)==0,"noon keeps original colors and extinguishes lamps");
        foreach(int minute in new[]{390,1110}){
            int c=WorldLighting.Tint(minute);check((c>>16)>(c>>8&255)&&(c>>8&255)>(c&255),"dawn and sunset have orange tint");
        }
        check(WorldLighting.Lamps(0)==255&&WorldLighting.Lamps(1200)==255&&WorldLighting.Lamps(1080)>0,"static lights fade in through evening and remain on overnight");
        check(WorldLighting.Sunlight(0)==0&&WorldLighting.Sunlight(329)==0&&WorldLighting.Sunlight(1170)==0&&WorldLighting.Sunlight(1439)==0,"cloud and sun-cast shadows are fully absent overnight");
        check(WorldLighting.Sunlight(330)==0&&WorldLighting.Sunlight(331)>0&&WorldLighting.Sunlight(450)==255,"clouds and cast shadows return gradually after sunrise");
        check(WorldLighting.Sunlight(1020)==255&&WorldLighting.Sunlight(1110)>0&&WorldLighting.Sunlight(1110)<255&&WorldLighting.Sunlight(1170)==0,"outdoor shadows fade through dusk until fully dark");
        check(WorldLighting.Tint(1440)==WorldLighting.Tint(0)&&WorldLighting.Tint(-1)==WorldLighting.Tint(1439),"lighting wraps continuously at midnight");
        for(int minute=0;minute<1440;minute++){
            int a=WorldLighting.Tint(minute),b=WorldLighting.Tint(minute+1);
            check(Math.Abs((a>>16)-(b>>16))<=4&&Math.Abs((a>>8&255)-(b>>8&255))<=4&&Math.Abs((a&255)-(b&255))<=4,"minute transitions do not jump between lighting keyframes");
            int sunlight=WorldLighting.Sunlight(minute),next=WorldLighting.Sunlight(minute+1);
            check(sunlight>=0&&sunlight<=255&&sunlight+WorldLighting.Lamps(minute)==255,"lamps replace disappearing sun shadows without exceeding their intensity range");
            check(Math.Abs(next-sunlight)<=3,"shadow and cloud fades have no abrupt clock or midnight boundary");
            if(minute>=330&&minute<450)check(next>=sunlight,"sunrise restores shadow coverage monotonically");
            if(minute>=1020&&minute<1170)check(next<=sunlight,"sunset removes shadow coverage monotonically");
        }
        check(WorldLighting.Sunlight(-1)==WorldLighting.Sunlight(1439)&&WorldLighting.Sunlight(1440)==WorldLighting.Sunlight(0),"shadow timing wraps the same way as environment lighting");
        var state=new GameState(73);state.PlaceHouse(12,22,false);
        check(WorldLighting.StreetLamp(state,0,out float x,out float z)&&state.CanWalk(x,z,0),"home street lamp has a walkable terrain socket beside the entrance");
        check(!WorldLighting.StreetLamp(state,1,out _,out _),"resident houses supply window pools without multiplying street lamps");
        var clock=new TownClock();clock.Update(15<<11|4<<16|18<<6|30);
        check(clock.MinuteOfDay==1110,"lighting reads the same console clock as the HUD");
    }
}
