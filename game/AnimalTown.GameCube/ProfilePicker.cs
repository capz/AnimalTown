using System;
using AnimalTown;
using Dolphin.Diagnostics;
using Dolphin.Graphics;
using Dolphin.Input;
using Dolphin.Timing;

// Choosing a resident never renders or simulates the town in the background.
internal static class ProfilePicker
{
    internal static bool Run(TownSession session)
    {
        bool performance=DebugOverlay.PerformanceVisible,text=DebugOverlay.TextVisible;
        try{
            using var view=new ProfilePickerUi(session.State,session.Loaded);var navigation=new MenuNavigation();
            while(true){
                DebugOverlay.PerformanceVisible=false;DebugOverlay.TextVisible=false;
                view.Draw();RuntimeOptions.Wait();
                Controller.Update();var pad=Controller.GetState(0);int direction=navigation.Read(view.Modal==0?"villager-profile":"delete-profile",0,pad.Stick.X,pad.Stick.Y,(int)pad.PressedButtons,Clock.Timestamp,true);
                if(view.Modal==ProfilePickerUi.Failure){if(pad.A.WasPressed||pad.B.WasPressed||pad.Start.WasPressed)return false;continue;}
                if(view.Modal==ProfilePickerUi.Confirm){
                    if(pad.B.WasPressed){view.Modal=0;continue;}
                    if(direction==MenuNavigation.Left||direction==MenuNavigation.Up)view.DeleteFocus=false;
                    if(direction==MenuNavigation.Right||direction==MenuNavigation.Down)view.DeleteFocus=true;
                    if(pad.A.WasPressed||pad.Start.WasPressed){
                        if(!view.DeleteFocus){view.Modal=0;continue;}
                        view.Modal=ProfilePickerUi.Busy;view.Draw();
                        if(!session.DeleteProfile(view.Selected)){view.Modal=ProfilePickerUi.Failure;continue;}
                        view.Refresh(session.State,session.Loaded);view.Modal=0;view.Status="Villager deleted.";
                    }
                    continue;
                }
                if(pad.Z.WasPressed&&view.Occupied&&session.Loaded&&!session.Damaged){view.DeleteFocus=false;view.Modal=ProfilePickerUi.Confirm;view.Status="";continue;}
                if(direction==MenuNavigation.Left||direction==MenuNavigation.Up)view.Selected=(view.Selected+3)%4;
                if(direction==MenuNavigation.Right||direction==MenuNavigation.Down)view.Selected=(view.Selected+1)%4;
                if(direction!=0)view.Status="";
                if(pad.B.WasPressed)return false;
                if(pad.A.WasPressed||pad.Start.WasPressed){session.SelectProfile(view.Selected);return true;}
            }
        }finally{DebugOverlay.PerformanceVisible=performance;DebugOverlay.TextVisible=text;}
    }
}
