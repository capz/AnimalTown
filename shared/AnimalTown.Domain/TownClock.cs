namespace AnimalTown;

public sealed class TownClock
{
    private int previous=-1;
    public string Date="",Time="";
    public int MinuteOfDay {get;private set;}
    public void Update(int packed)
    {
        if(packed==previous)return;previous=packed;int minute=packed&63,hour=packed>>6&31,day=packed>>11&31,month=packed>>16&15,weekday=packed>>20&7;
        if(minute>59||hour>23||day<1||day>31||month<1||month>12||weekday>6)throw new InvalidOperationException("Invalid console clock");
        MinuteOfDay=hour*60+minute;
        string[] months={"Jan","Feb","Mar","Apr","May","Jun","Jul","Aug","Sep","Oct","Nov","Dec"},days={"Sun","Mon","Tue","Wed","Thu","Fri","Sat"};
        Date=day+" "+months[month-1]+"  "+days[weekday];Time=(hour<10?"0":"")+hour+":"+(minute<10?"0":"")+minute;
    }
}
