internal static class MenuNavigationChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var nav=new MenuNavigation();
        int Read(float x=0,float y=0,int pressed=0,long now=0,string view="menu",int tab=5,bool active=true)=>nav.Read(view,tab,x,y,pressed,now,active);
        check(Read()==0,"neutral settings input does not navigate");
        check(Read(y:-.8f)==MenuNavigation.Down,"stick down navigates settings immediately");
        check(Read(y:-.8f,now:349999)==0,"holding stick waits before repeating");
        check(Read(y:-.8f,now:350000)==MenuNavigation.Down,"stick repeats after 350 ms");
        check(Read(y:-.8f,now:469999)==0&&Read(y:-.8f,now:470000)==MenuNavigation.Down,"stick repeat interval is 120 ms");
        check(Read(y:-.8f,now:2000000)==MenuNavigation.Down&&Read(y:-.8f,now:2000001)==0,"slow frames emit one navigation step without catch-up jumps");
        check(Read(y:-.4f,now:2000002)==0,"stick hysteresis keeps a held direction without re-pressing");
        check(Read(y:-.2f,now:2000003)==0&&Read(y:-.5f,now:2000004)==0,"neutral releases and dead zone rejects drift");
        check(Read(y:.8f,now:2000005)==MenuNavigation.Up,"stick up navigates settings");
        check(Read(x:.9f,y:.7f,now:2000006)==MenuNavigation.Right,"diagonal navigation uses only the dominant axis");
        check(Read(x:-.9f,now:2000007)==MenuNavigation.Left,"changing stick direction responds immediately");
        check(Read(x:-.9f,pressed:MenuNavigation.Down,now:2000008)==MenuNavigation.Down,"D-pad press takes priority over the stick");
        check(Read(pressed:MenuNavigation.Up)==MenuNavigation.Up&&Read(pressed:MenuNavigation.Down)==MenuNavigation.Down&&Read(pressed:MenuNavigation.Left)==MenuNavigation.Left&&Read(pressed:MenuNavigation.Right)==MenuNavigation.Right,"all D-pad directions remain available");
        check(Read(pressed:0x100)==0,"action buttons do not become directions");
        check(Read(pressed:MenuNavigation.Up|MenuNavigation.Right)==MenuNavigation.Up,"simultaneous D-pad directions produce one step");
        check(Read(x:.8f,view:"world",active:false)==0&&Read(x:.8f,now:3000000)==0&&Read(x:.8f,now:5000000)==0,"walking stick held across opening settings cannot move the menu");
        check(Read(now:5000001)==0&&Read(x:.8f,now:5000002)==MenuNavigation.Right,"recentering arms stick navigation after opening");
        check(Read(x:.8f,tab:0,now:6000000)==0&&Read(x:.8f,tab:0,now:7000000)==0,"held stick cannot spill into another tab");
        check(Read(tab:0,now:7000001)==0&&Read(y:-.8f,tab:0,now:7000002)==MenuNavigation.Down,"backpack accepts a fresh stick gesture");
        check(Read(y:-.8f,tab:0,active:false,now:8000000)==0&&Read(y:-.8f,tab:0,now:9000000)==0,"debug chords suppress stick navigation until release");
        check(Read(pressed:MenuNavigation.Down,tab:0,now:9000001)==MenuNavigation.Down,"D-pad remains usable while stick waits for neutral");
        foreach(string view in new[]{"keyboard","create","item","sell","shop","store","post","mail","lost"}){
            Read(view:view);
            check(Read(view:view,y:-.8f)==MenuNavigation.Down,"stick navigates "+view);
        }
        Read(view:"menu",tab:0);
        check(Read(view:"menu",tab:0,y:-1,pressed:MenuNavigation.Down)==MenuNavigation.Down,"one shared Dolphin binding produces one backpack movement");
        check(Read(view:"item",tab:-1,y:-1)==0,"holding the selection direction while opening actions waits for neutral");
        check(Read(view:"item",tab:-1)==0&&Read(view:"item",tab:-1,y:-1,pressed:MenuNavigation.Down,now:100)==MenuNavigation.Down,"released shared binding responds again inside item actions");
        check(Read(view:"item",tab:-1,y:-1,now:200)==0&&Read(view:"item",tab:-1,y:-1,now:350100)==MenuNavigation.Down,"shared mapping has one repeat timer inside item actions");
    }
}
