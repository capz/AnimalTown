namespace AnimalTown.Jobs;

public static class JobCalendar
{
    public static long At(int year,int month,int day,int hour=0,int minute=0,int second=0)
    {
        if(year<1600||year>2399||month<1||month>12||day<1||day>Days(year,month)||hour<0||hour>23||minute<0||minute>59||second<0||second>59)throw new ArgumentOutOfRangeException(nameof(year));
        int y=year-(month<=2?1:0),era=y/400,yoe=y-era*400,mp=month+(month>2?-3:9);
        long days=era*146097L+yoe*365+yoe/4-yoe/100+(153*mp+2)/5+day-1-719468;
        return days*86400+hour*3600+minute*60+second;
    }
    private static int Days(int year,int month)
    {if(month==2)return year%4==0&&(year%100!=0||year%400==0)?29:28;return month==4||month==6||month==9||month==11?30:31;}
    public static void Date(long seconds,out int year,out int month,out int day)
    {
        long days=seconds/86400;if(seconds%86400<0)days--;
        long z=days+719468,era=(z>=0?z:z-146096)/146097,doe=z-era*146097,yoe=(doe-doe/1460+doe/36524-doe/146096)/365;
        year=(int)(yoe+era*400);long doy=doe-(365*yoe+yoe/4-yoe/100),mp=(5*doy+2)/153;
        day=(int)(doy-(153*mp+2)/5+1);month=(int)(mp+(mp<10?3:-9));year+=month<=2?1:0;
    }
    public static bool Season(JobTemplate template,long now,bool starting=false)
    {
        Date(now,out _,out int month,out int day);int date=month*100+day;
        if(template==JobTemplate.FavorInsect)return date>=301&&date<=1128;
        if(template==JobTemplate.FavorFlowers)return date>=225&&date<=831;
        if(template==JobTemplate.FavorSnowman){long time=(now%86400+86400)%86400;return (date>=1225||date<=217)&&(!starting||time>=8*3600&&time<17*3600);}
        return true;
    }
    public static string Display(long time)
    {
        Date(time,out _,out int month,out int day);long seconds=(time%86400+86400)%86400;
        int hour=(int)(seconds/3600),minute=(int)(seconds/60%60);
        return day+"/"+month+" "+(hour<10?"0":"")+hour+":"+(minute<10?"0":"")+minute;
    }
}
