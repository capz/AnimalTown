using AnimalTown;

internal static class TownDaylightChecks
{
    private static int Day(int year,int month,int day)=>(new DateTime(year,month,day)-new DateTime(2000,1,1)).Days;
    private static void Clock(TownClock clock,int year,int month,int day,int hour,int minute)
    {
        int weekday=(int)new DateTime(year,month,day).DayOfWeek;
        clock.Update(minute|(hour<<6)|(day<<11)|(month<<16)|(weekday<<20)|((year-2000)<<23));
    }
    private static int Location(string city)=>Enumerable.Range(1,DaylightLocations.Count).Single(id=>DaylightLocations.Get(id).City==city);

    internal static void Run(Action<bool,string> check)
    {
        var clock=new TownClock();Clock(clock,2028,2,29,23,59);
        check(clock.CivilDay==Day(2028,2,29)&&clock.CalendarMonth==2028*12+1&&clock.DayMonth=="29 February","RTC year makes leap-day solar and save dates agree");
        Clock(clock,2028,3,1,0,0);
        check(clock.CivilDay==Day(2028,3,1)&&clock.MinuteOfDay==0,"the civil clock advances across leap-day midnight");
        foreach(var (month,day,hour,minute,bloom) in new[]{
            (2,29,23,59,false),(3,1,5,59,false),(3,1,6,0,true),
            (3,10,23,59,true),(3,11,5,59,true),(3,11,6,0,false),(4,1,6,0,false)
        }){
            Clock(clock,2028,month,day,hour,minute);
            check(clock.CherryBlossomsBloom==bloom,"cherry blossoms follow the first ten 06:00 town days of spring");
        }

        int amsterdam=Location("Amsterdam");
        var june=TownDaylight.Times(amsterdam,Day(2026,6,21));
        var december=TownDaylight.Times(amsterdam,Day(2026,12,21));
        var mockupDate=TownDaylight.Times(amsterdam,Day(2026,11,19));
        check(june.Sunrise==318&&june.Sunset==1326,"Amsterdam June solstice matches the reference solar times");
        check(december.Sunrise==528&&december.Sunset==989,"Amsterdam December solstice matches the reference solar times");
        check(mockupDate.Sunrise==488&&mockupDate.Sunset==1003,"location preview matches the mockup date");

        var before=TownDaylight.Times(amsterdam,Day(2026,3,28));
        var after=TownDaylight.Times(amsterdam,Day(2026,3,29));
        check(after.Sunrise-before.Sunrise is >=55 and <=60&&after.Sunset-before.Sunset is >=60 and <=65,"spring DST changes civil display by an hour without jumping day length");
        before=TownDaylight.Times(amsterdam,Day(2026,10,24));after=TownDaylight.Times(amsterdam,Day(2026,10,25));
        check(before.Sunrise-after.Sunrise is >=55 and <=60&&before.Sunset-after.Sunset is >=60 and <=65,"autumn DST uses the correct offset for both events");
        int sydney=Location("Sydney");
        var southernWinter=TownDaylight.Times(sydney,Day(2026,6,21));
        var southernSummer=TownDaylight.Times(sydney,Day(2026,12,21));
        check(southernSummer.Sunset-southernSummer.Sunrise>southernWinter.Sunset-southernWinter.Sunrise+240,"Southern Hemisphere daylight seasons reverse independently of the town art calendar");

        int tromso=Location("Tromso");
        foreach(int month in new[]{6,12}){
            var polar=TownDaylight.Times(tromso,Day(2026,month,21));
            check(polar.Sunrise==-1&&polar.Sunset==-1,"polar daylight and darkness report an absent crossing");
        }
        for(int city=1;city<=DaylightLocations.Count;city++){
            var times=TownDaylight.Times(city,Day(2028,2,29));
            check(times.Sunrise is >=0 and <1440&&times.Sunset is >=0 and <1440,"all city presets resolve to bounded local times on leap day");
        }

        var daylight=new TownDaylight();
        Clock(clock,2026,6,21,4,0);daylight.Update(amsterdam,clock,0);
        check(daylight.Lighting.Sunlight==0&&daylight.Lighting.Lamps==255,"astronomical night uses lamps and no cast shadows");
        Clock(clock,2026,6,21,5,0);daylight.Update(amsterdam,clock,3600000000);
        check(daylight.Lighting.Sunlight==0&&daylight.Lighting.Lamps is >0 and <255,"civil dawn fades lamps ahead of sunlight");
        Clock(clock,2026,6,21,6,30);daylight.Update(amsterdam,clock,9000000000);
        check(daylight.Lighting.Tint==0xffffff&&daylight.Lighting.Sunlight==255&&daylight.Lighting.Lamps==0,"established daylight reaches the original full sun lighting");
        daylight.Update(amsterdam,clock,9000000001,0);
        check(daylight.Lighting.Sunlight==0&&daylight.Lighting.Lamps==255&&clock.MinuteOfDay==390&&clock.CivilDay==Day(2026,6,21),
            "time preview changes solar light on the real date without moving the clock");
        daylight.Update(amsterdam,clock,9000000002);
        check(daylight.Lighting.Sunlight==255&&daylight.Lighting.Lamps==0,"returning to Live restores solar daylight immediately");
        daylight.Update(0,clock,9000000000);
        check(daylight.Lighting.Sunlight==WorldLighting.Sunlight(390)&&daylight.Lighting.Tint==WorldLighting.Tint(390),"switching to Classic immediately restores the original schedule");
        Clock(clock,2026,6,21,0,0);daylight.Update(tromso,clock,9100000000);
        check(daylight.Lighting.Sunlight>0,"midnight sun still lights the scene without a sunrise event");
        Clock(clock,2026,12,21,12,0);daylight.Update(tromso,clock,9200000000);
        check(daylight.Lighting.Sunlight==0&&daylight.Lighting.Lamps>0,"polar night retains lamps even at noon");
        Console.WriteLine("Location daylight checks passed: dates, solstices, DST, polar light and Classic fallback");
    }
}
