namespace AnimalTown;

// Calendar weather is repeatable for the same town and time. Preview overrides are transient.
public sealed class DebugWeather
{
    public bool RainEnabled,RoughOceanWhenRaining;
    public bool SnowEnabled=true;
    private bool scheduledRain,winter;
    public const int RainMinutes=35;
    public static bool ScheduledRain(int seed,int day,int minute,int season)
    {
        int interval=season==2?360:season==1?4320:season==0?2160:0;
        if(interval==0)return false;
        long time=(long)day*1440+minute;
        long slot=time/interval;
        int within=(int)(time%interval);
        uint hash=unchecked((uint)slot*0x9e3779b9u+(uint)seed*0x85ebca6bu+(uint)season);
        hash=unchecked((hash^(hash>>16))*0x7feb352du);
        hash=unchecked((hash^(hash>>15))*0x846ca68bu);
        hash^=hash>>16;
        int start=(int)(hash%(uint)(interval-RainMinutes-9));
        return within>=start&&within<start+RainMinutes;
    }
    public void Update(int seed,int day,int minute,int season)
    {
        winter=season==3;
        scheduledRain=ScheduledRain(seed,day,minute,season);
    }
    public bool Raining(int room)=>(RainEnabled||scheduledRain)&&room==0;
    public bool Snowing(int room)=>winter&&SnowEnabled&&room==0&&!Raining(room);
    public bool RoughOcean(int room)=>Raining(room)&&RoughOceanWhenRaining;
    public const int DropCount=64;
    public static void Drop(int index,float focusX,float focusZ,double seconds,out float x,out float z,out float height,bool snow=false)
    {
        int cx=(int)Math.Floor(focusX/3)-4+index%8,cz=(int)Math.Floor(focusZ/3)-6+index/8;
        uint hash=unchecked((uint)cx*73856093u^(uint)cz*19349663u);hash^=hash>>13;hash=unchecked(hash*1274126177u);
        double phase=seconds*(snow?.34:1.6)+((hash>>16)&65535)/65536.0;phase-=Math.Floor(phase);
        x=cx*3+(hash&255)/85f+(float)phase*.4f;
        z=cz*3+((hash>>8)&255)/85f+(snow?(float)Math.Sin(phase*6.28)*.28f:0);height=(float)(1-phase)*7;
    }
}
