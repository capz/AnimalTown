using System;
using AnimalTown;
using AnimalTown.Jobs;
using Dolphin.Graphics;
using Dolphin.Input;
using Dolphin.Timing;

internal sealed partial class TownGame
{
    internal const int TravelRestart=27;
    internal bool TravelRestartRequested {get;private set;}
    private int travelChoice,travelAction,travelFrom,travelTo;
    private string travelHome="",travelDestination="",travelStatus="",travelFromName="",travelToName="";
    private bool travelReady;
    private void OpenTravel()
    {
        ResetJobConversation();travelChoice=0;travelAction=state.Town.Jobs.NpcMove.Pending?4:state.Town.Jobs.Trip.Pending&&!state.Visiting?2:state.Visiting?1:0;
        screen="travel-menu";RefreshTravel();
    }
    private void RefreshTravel()
    {
        travelReady=false;
        try{
            card?.Dispose();card=null;
            var cards=OfflineTravelCards.Preview(state,banner,icon);
            travelHome=(cards.HomeDevice==4?"Slot A":"Slot B")+" · Town "+cards.Home.Seed;
            travelDestination=(cards.DestinationDevice==4?"Slot A":"Slot B")+" · Town "+cards.Destination.Seed;
            travelFromName=cards.Home.NpcName(travelFrom);travelToName=cards.Destination.NpcName(travelTo);
            travelStatus=travelAction==1?"Bring your pockets and errands home.":travelAction==2||travelAction==4?"Both towns match. Continue the recorded handoff.":"Your home villager stays away until you return.";
            travelReady=true;
        }catch(Exception e){travelStatus=e.Message??"Check both town cards and recheck.";}
    }
    private void ExecuteTravel()
    {
        if(!travelReady)return;
        if(!SaveFromMenu()){travelStatus=saveStatus;screen="travel-menu";return;}
        screen="travel-busy";
        card?.Dispose();card=null;
        session.QueueTravel(travelAction,state.ActiveProfile,travelFrom,travelTo,JobNow);TravelRestartRequested=true;
    }
    private bool TravelInput(ControllerState p)
    {
        if(!screen.StartsWith("travel-"))return false;
        if(screen=="travel-recovery"){
            if(p.B.WasPressed||Press(p)){ExitToTitle=true;}return true;
        }
        if(screen=="travel-busy")return true;
        if(p.B.WasPressed){if(screen=="travel-confirm"){screen="travel-menu";return true;}screen="world";return true;}
        int dir=jobNavigation.Read(screen,0,p.Stick.X,p.Stick.Y,(int)p.PressedButtons,Clock.Timestamp,true);
        if(p.X.WasPressed){RefreshTravel();return true;}
        if(screen=="travel-moving"){
            if(dir==MenuNavigation.Up)travelFrom=(travelFrom+5)%6;if(dir==MenuNavigation.Down)travelFrom=(travelFrom+1)%6;
            if(dir==MenuNavigation.Left)travelTo=(travelTo+5)%6;if(dir==MenuNavigation.Right)travelTo=(travelTo+1)%6;
            if(dir!=0)RefreshTravel();if(Press(p)&&travelReady){travelAction=3;screen="travel-confirm";travelChoice=0;}return true;
        }
        if(dir==MenuNavigation.Up||dir==MenuNavigation.Down)travelChoice=1-travelChoice;
        if(screen=="travel-confirm"){
            if(Press(p)){if(travelChoice==1)ExecuteTravel();else screen="travel-menu";}return true;
        }
        if(p.Y.WasPressed&&travelReady&&!state.Town.Jobs.NpcMove.Pending){screen="travel-moving";return true;}
        if(Press(p)){if(travelChoice==1){screen="world";return true;}if(travelReady){screen="travel-confirm";travelChoice=0;}else RefreshTravel();}return true;
    }
    private bool DrawTravelUi(Color32 ink)
    {
        if(!screen.StartsWith("travel-"))return false;
        Panel(95,108,450,265);Heading(travelAction==1?"Return home":travelAction>=2?"Continue your trip":"Visit another town",117,128,ink,1.18f);
        Text("Home: "+travelHome,118,177,ink,1.05f);Text("Destination: "+travelDestination,118,205,ink,1.05f);
        string info=screen=="travel-moving"||travelAction==3?travelFromName+" moves away. "+travelToName+" moves here. Both keep their identities and friendships.":travelStatus;
        Text(saveStatusText.Get(info),118,243,ink,1.02f);
        if(screen=="travel-busy"){Text("Saving both towns...",118,331,ink,1.1f);return true;}
        if(screen=="travel-recovery"){Text("A / B: return to title to recover",118,331,ink,1.05f);return true;}
        if(screen=="travel-moving"){Text("Up/Down: home   Left/Right: destination",118,310,ink,.95f);Text("A: confirm move   X: recheck   B: back",118,340,ink,.95f);return true;}
        bool confirm=screen=="travel-confirm";
        for(int i=0;i<2;i++){float x=118+i*210;if(i==travelChoice)Rounded(x-4,313,202,29,13,new Color32(221,231,188));Text(confirm?(i==0?"Back":"Confirm"):(i==0?travelReady?"Continue":"Recheck":"Back"),x+12,317,ink,1.1f);}
        if(!confirm)Text("X: recheck   Y: move villagers",118,351,ink,.9f);return true;
    }
}
