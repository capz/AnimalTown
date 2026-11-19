using AnimalTown;
using Dolphin.Graphics;

// The same controller and panel serve the town settings and first-save review.
internal sealed class DaylightLocationMenu
{
    private const int ScheduleRow=0,RegionRow=1,CityRow=2;
    private static readonly Color32 Focus=new Color32(221,231,188);
    private int row,city=1,shownCity=-1,shownDay=-1;
    private bool byLocation;
    private string date="",sunrise="",sunset="",clockHint="";
    internal const string Screen="daylight-location";
    internal int Location=>byLocation?city:0;

    internal void Open(int savedLocation)
    {
        byLocation=savedLocation!=0;
        city=byLocation?savedLocation:1;
        row=ScheduleRow;
    }

    internal void Move(int direction)
    {
        if(direction==MenuNavigation.Up||direction==MenuNavigation.Down){
            if(byLocation)row=(row+(direction==MenuNavigation.Down?1:2))%3;
            return;
        }
        if(direction!=MenuNavigation.Left&&direction!=MenuNavigation.Right)return;
        int step=direction==MenuNavigation.Right?1:-1;
        if(row==ScheduleRow){byLocation=!byLocation;return;}
        int region=DaylightLocations.Get(city).Region;
        if(row==RegionRow){
            int count=DaylightLocations.RegionCount;
            int next=(region+step+count)%count;
            for(int i=1;i<=DaylightLocations.Count;i++)if(DaylightLocations.Get(i).Region==next){city=i;break;}
        }else{
            int next=city;
            do{next=(next-1+step+DaylightLocations.Count)%DaylightLocations.Count+1;}
            while(DaylightLocations.Get(next).Region!=region);
            city=next;
        }
    }

    internal void Prepare(TownClock clock)
    {
        if(!byLocation||(shownCity==city&&shownDay==clock.CivilDay))return;
        var times=TownDaylight.Times(city,clock.CivilDay);
        date="Today, "+clock.DayMonth;
        sunrise=times.Sunrise<0?"No sunrise today":"Sunrise "+Time(times.Sunrise);
        sunset=times.Sunset<0?"No sunset today":"Sunset "+Time(times.Sunset);
        clockHint="Set your GameCube clock to "+DaylightLocations.Get(city).City+" time.";
        shownCity=city;
        shownDay=clock.CivilDay;
    }

    private static string Time(int minute)
    {
        int hours=minute/60,minutes=minute%60;
        return (hours<10?"0":"")+hours+":"+(minutes<10?"0":"")+minutes;
    }

    internal void Draw(Draw2D ui,UiFont font,UiFont heading,UiShapes? shapes=null)
    {
        if(shapes==null||!shapes.DrawPanel(ui,45,56,550,374,30))TitleArt.Panel(ui,45,56,550,374);
        heading.Draw(ui,"Daylight location",82,77,TitleArt.Ink,1.17f);
        font.Draw(ui,"Shared by everyone in this town.",85,128,TitleArt.Ink,1.05f);
        DrawRow(ui,font,heading,shapes,ScheduleRow,"Schedule",byLocation?"By location":"Classic");
        if(byLocation){
            var preset=DaylightLocations.Get(city);
            DrawRow(ui,font,heading,shapes,RegionRow,"Region",DaylightLocations.RegionName(preset.Region));
            DrawRow(ui,font,heading,shapes,CityRow,"City",preset.City);
            font.Draw(ui,date,85,282,TitleArt.Ink,1f);
            font.Draw(ui,sunrise,104,310,TitleArt.Ink,1.16f);
            font.Draw(ui,sunset,331,310,TitleArt.Ink,1.16f);
            font.Draw(ui,clockHint,85,350,TitleArt.Ink,.98f);
        }else font.Draw(ui,"Choose By location for local sunrise and sunset.",85,235,TitleArt.Ink,1.02f);

        Arrow(ui,87,400,0,-1);Arrow(ui,99,400,0,1);
        font.Draw(ui,"Select",111,390,TitleArt.Ink,1f);
        Arrow(ui,203,400,-1,0);Arrow(ui,217,400,1,0);
        font.Draw(ui,"Change",230,390,TitleArt.Ink,1f);
        Button(ui,font,shapes,322,"A","Apply");
        Button(ui,font,shapes,427,"B","Back");
    }

    private void DrawRow(Draw2D ui,UiFont font,UiFont heading,UiShapes? shapes,int index,string label,string value)
    {
        int y=166+index*35;
        if(row==index){
            Rounded(ui,shapes,65,y-1,510,30,15,Focus);
            font.Draw(ui,">",75,y+3,TitleArt.Ink,1f);
            Arrow(ui,283,y+14,-1,0);
            Arrow(ui,302+font.Measure(value,1.12f)+13,y+14,1,0);
        }
        heading.Draw(ui,label,93,y+1,TitleArt.Ink,.91f);
        font.Draw(ui,value,302,y+3,TitleArt.Ink,1.12f);
    }

    private static void Button(Draw2D ui,UiFont font,UiShapes? shapes,int x,string button,string label)
    {
        Rounded(ui,shapes,x,388,23,23,11.5f,TitleArt.Ink);
        font.Draw(ui,button,x+11.5f-font.Measure(button,1f)*.5f,390,TitleArt.Cream,1f);
        font.Draw(ui,label,x+30,390,TitleArt.Ink,1f);
    }

    private static void Rounded(Draw2D ui,UiShapes? shapes,float x,float y,float w,float h,float radius,Color32 color)
    {
        if(shapes==null||!shapes.Draw(ui,x,y,w,h,radius,false,color))TitleArt.Rounded(ui,x,y,w,h,radius,color);
    }

    private static void Arrow(Draw2D ui,float x,float y,int dx,int dy)
    {
        ui.FillTriangle(new Vector2(x+dx*5,y+dy*5),new Vector2(x-dx*3-dy*4,y-dy*3+dx*4),
            new Vector2(x-dx*3+dy*4,y-dy*3-dx*4),TitleArt.Ink);
    }
}
