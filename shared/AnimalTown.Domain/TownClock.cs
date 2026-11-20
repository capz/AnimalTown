namespace AnimalTown;

public sealed class TownClock
{
    private int previous=-1;
    private int previousDate=-1;
    private static readonly string[] months={"Jan","Feb","Mar","Apr","May","Jun","Jul","Aug","Sep","Oct","Nov","Dec"};
    private static readonly string[] fullMonths={"January","February","March","April","May","June","July","August","September","October","November","December"};
    private static readonly string[] days={"Sun","Mon","Tue","Wed","Thu","Fri","Sat"};
    public string Date="",Time="";
    public string MonthDay {get;private set;}="";
    public string Weekday {get;private set;}="";
    public int MinuteOfDay {get;private set;}
    public int CivilDay {get;private set;} // Days since 2000-01-01, at local midnight.
    public int CalendarMonth {get;private set;} // Year * 12 + zero-based month, for saves.
    public const int DayRolloverMinute=6*60;
    public bool CherryBlossomsBloom
    {
        get
        {
            int day=(previous>>11&31)-(MinuteOfDay<DayRolloverMinute?1:0);
            return CalendarMonth%12==2&&day>=1&&day<=10;
        }
    }
    public string DayMonth=>(previous>>11&31)+" "+fullMonths[(previous>>16&15)-1];
    public void Update(int packed)
    {
        if(packed==previous)return;int minute=packed&63,hour=packed>>6&31,day=packed>>11&31,month=packed>>16&15,weekday=packed>>20&7;
        int year=2000+(packed>>23&127);
        if(minute>59||hour>23||day<1||day>31||month<1||month>12||weekday>6||year>2099)throw new InvalidOperationException("Invalid console clock");
        previous=packed;
        MinuteOfDay=hour*60+minute;
        if(previousDate!=packed>>11){
            previousDate=packed>>11;Weekday=days[weekday];MonthDay=fullMonths[month-1]+" "+day;
            Date=day+" "+months[month-1]+"  "+Weekday;
            CalendarMonth=year*12+month-1;
            int precedingMonths=(367*month-362)/12;
            if(month>2)precedingMonths-=year%4==0?1:2;
            CivilDay=(year-2000)*365+(year-1997)/4+precedingMonths+day-1;
        }
        Time=FormatTime(MinuteOfDay);
    }
    public static string FormatTime(int minuteOfDay)
    {
        int hour=minuteOfDay/60,minute=minuteOfDay%60;
        return (hour<10?"0":"")+hour+":"+(minute<10?"0":"")+minute;
    }
}
