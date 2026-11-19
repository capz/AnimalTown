namespace AnimalTown;

public readonly struct DaylightTimes
{
    // Local minutes after midnight. A negative value means no crossing that day.
    public readonly int Sunrise,Sunset;
    internal DaylightTimes(int sunrise,int sunset){Sunrise=sunrise;Sunset=sunset;}
}

public sealed class TownDaylight
{
    private int previousLocation=-1,previousLocalMinute,previousUtcMinute;
    private long previousTimestamp;
    private SolarCurve curve;
    public OutdoorLighting Lighting {get;private set;}=WorldLighting.Classic(0);

    public void Update(int location,TownClock clock,long timestamp)
    {
        int localMinute=clock.CivilDay*1440+clock.MinuteOfDay;
        bool sameLocation=location==previousLocation;
        if(sameLocation&&localMinute==previousLocalMinute)return;
        if(location==0)Lighting=WorldLighting.Classic(clock.MinuteOfDay);
        else{
            var preset=DaylightLocations.Get(location);
            if(!sameLocation)curve=new SolarCurve(preset);
            int expectedUtc=sameLocation?previousUtcMinute+(int)((timestamp-previousTimestamp+30000000)/60000000):int.MinValue;
            previousUtcMinute=preset.Zone.ToUtc(localMinute,expectedUtc);
            Lighting=WorldLighting.FromSolarElevation(curve.ElevationSine(previousUtcMinute));
        }
        previousLocation=location;
        previousLocalMinute=localMinute;
        previousTimestamp=timestamp;
    }

    public static DaylightTimes Times(int location,int civilDay)
    {
        var preset=DaylightLocations.Get(location);
        var solar=new SolarCurve(preset);
        int midnight=civilDay*1440;
        int start=preset.Zone.ToUtc(midnight),end=preset.Zone.ToUtc(midnight+1440);
        int rise=-1,set=-1;
        double previous=start,longitudeMinutes=preset.Longitude*4;
        // Bracket crossings on either side of the solar maximum/minimum. This
        // finds even a few minutes of daylight near the polar circle and does
        // not assume the civil day is 24 hours or that sunrise precedes sunset.
        int halfDay=(int)Math.Floor((start+longitudeMinutes)/720);
        for(double center=halfDay*720-longitudeMinutes;center<end+30;center+=720,halfDay++){
            double at=solar.Extremum(center,(halfDay&1)!=0);
            if(at<=start||at>=end)continue;
            RecordCrossing(ref solar,preset.Zone,previous,at,midnight,end,ref rise,ref set);
            previous=at;
        }
        RecordCrossing(ref solar,preset.Zone,previous,end,midnight,end,ref rise,ref set);
        return new DaylightTimes(rise,set);
    }

    private static void RecordCrossing(ref SolarCurve solar,DaylightZone zone,double low,double high,int midnight,int end,ref int rise,ref int set)
    {
        bool rising=solar.ElevationSine(high)>=WorldLighting.HorizonSine;
        if(rising==(solar.ElevationSine(low)>=WorldLighting.HorizonSine))return;
        for(int i=0;i<16;i++){
            double middle=(low+high)*.5;
            if((solar.ElevationSine(middle)>=WorldLighting.HorizonSine)==rising)high=middle;
            else low=middle;
        }
        double utc=(low+high)*.5;
        if(utc>=end-1.0/120)return;
        int local=(int)Math.Floor(utc+.5)+zone.OffsetAt((int)Math.Floor(utc))-midnight;
        local=Math.Max(0,Math.Min(1439,local));
        if(rising)rise=local;
        else set=local;
    }

    private struct SolarCurve
    {
        private const double Radians=Math.PI/180;
        private readonly double sinLatitude,cosLatitude,longitudeMinutes;
        private int day;
        private double a,b,equation,nextA,nextB,nextEquation;

        internal SolarCurve(DaylightLocation place)
        {
            sinLatitude=Math.Sin(place.Latitude*Radians);
            cosLatitude=Math.Cos(place.Latitude*Radians);
            longitudeMinutes=place.Longitude*4;
            day=int.MinValue;
            a=b=equation=nextA=nextB=nextEquation=0;
        }

        internal double ElevationSine(double utcMinute)
        {
            int utcDay=(int)Math.Floor(utcMinute/1440);
            if(day!=utcDay){
                if(day+1==utcDay){a=nextA;b=nextB;equation=nextEquation;}
                else Terms(utcDay,out a,out b,out equation);
                Terms(utcDay+1,out nextA,out nextB,out nextEquation);
                day=utcDay;
            }
            double minute=utcMinute-utcDay*1440,t=minute/1440;
            double angle=(minute+longitudeMinutes+equation+(nextEquation-equation)*t-720)*(Math.PI/720);
            return a+(nextA-a)*t+(b+(nextB-b)*t)*Math.Cos(angle);
        }

        internal double Extremum(double center,bool maximum)
        {
            // The equation of time shifts apparent solar noon by under 17 min.
            double low=center-30,high=center+30;
            for(int i=0;i<16;i++){
                double first=low+(high-low)/3,second=high-(high-low)/3;
                if((ElevationSine(first)<ElevationSine(second))==maximum)low=first;
                else high=second;
            }
            return (low+high)*.5;
        }

        // USNO approximate solar coordinates, evaluated at UTC midnight.
        // https://aa.usno.navy.mil/faq/sun_approx
        private void Terms(int utcDay,out double a,out double b,out double equation)
        {
            double days=utcDay-.5; // J2000.0 is 2000-01-01 12:00 UTC.
            double anomaly=((357.529+.98560028*days)%360)*Radians;
            double mean=(280.459+.98564736*days)%360;
            double longitude=(mean+1.915*Math.Sin(anomaly)+.020*Math.Sin(2*anomaly))*Radians;
            double tilt=(23.439-.00000036*days)*Radians;
            double sinLongitude=Math.Sin(longitude),sinDeclination=Math.Sin(tilt)*sinLongitude;
            a=sinLatitude*sinDeclination;
            b=cosLatitude*Math.Sqrt(1-sinDeclination*sinDeclination);
            double rightAscension=Math.Atan2(Math.Cos(tilt)*sinLongitude,Math.Cos(longitude))/Radians;
            double difference=mean-rightAscension;
            equation=4*(difference-360*Math.Floor((difference+180)/360));
        }
    }
}
