using AnimalTown;

internal static class WorldLightingChecks
{
    internal static void Run(Action<bool,string> check)
    {
        check(WorldLighting.Tint(720)==0xffffff&&WorldLighting.Lamps(720)==0,"noon keeps original colors and extinguishes lamps");
        foreach(int minute in new[]{390,1110}){
            int c=WorldLighting.Tint(minute);check((c>>16)>(c>>8&255)&&(c>>8&255)>(c&255),"dawn and sunset have orange tint");
        }
        check(WorldLighting.Lamps(0)==255&&WorldLighting.Lamps(1200)==255,"static lights remain on overnight");
        check(WorldLighting.Lamps(330)==255&&WorldLighting.Lamps(390)==128&&WorldLighting.Lamps(450)==0,"lamp gain fades from full to zero at sunrise");
        check(WorldLighting.Lamps(1020)==0&&WorldLighting.Lamps(1095)==127&&WorldLighting.Lamps(1170)==255,"lamp gain rises from zero to full at dusk");
        check(WorldLighting.Tint(1440)==WorldLighting.Tint(0)&&WorldLighting.Tint(-1)==WorldLighting.Tint(1439),"lighting wraps continuously at midnight");
        for(int minute=0;minute<1440;minute++){
            int a=WorldLighting.Tint(minute),b=WorldLighting.Tint(minute+1);
            check(Math.Abs((a>>16)-(b>>16))<=4&&Math.Abs((a>>8&255)-(b>>8&255))<=4&&Math.Abs((a&255)-(b&255))<=4,"minute transitions do not jump between lighting keyframes");
            int gain=WorldLighting.Lamps(minute),next=WorldLighting.Lamps(minute+1);
            check((uint)gain<=255&&Math.Abs(gain-next)<=3,"lamp gain stays in range and continuous across the clock wrap");
        }
        var state=new GameState(73);state.PlaceHouse(12,22,false);
        check(WorldLighting.StreetLamp(state,0,out float x,out float z)&&state.CanWalk(x,z,0),"home street lamp has a walkable terrain socket beside the entrance");
        check(!WorldLighting.StreetLamp(state,1,out _,out _),"resident houses supply window pools without multiplying street lamps");
        var clock=new TownClock();clock.Update(15<<11|4<<16|18<<6|30);
        check(clock.MinuteOfDay==1110,"lighting reads the same console clock as the HUD");
    }
}
