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
    public void Update(int packed)
    {
        if(packed==previous)return;int minute=packed&63,hour=packed>>6&31,day=packed>>11&31,month=packed>>16&15,weekday=packed>>20&7;
        if(minute>59||hour>23||day<1||day>31||month<1||month>12||weekday>6)throw new InvalidOperationException("Invalid console clock");
        previous=packed;
        MinuteOfDay=hour*60+minute;
        if(previousDate!=packed>>11){
            previousDate=packed>>11;Weekday=days[weekday];MonthDay=fullMonths[month-1]+" "+day;
            Date=day+" "+months[month-1]+"  "+Weekday;
        }
        Time=(hour<10?"0":"")+hour+":"+(minute<10?"0":"")+minute;
    }
}
