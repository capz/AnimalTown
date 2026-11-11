using System;
using AnimalTown;
using Dolphin.Diagnostics;
using Dolphin.Graphics;
using Dolphin.Input;
using Dolphin.Timing;

// Choosing a resident never renders or simulates the town in the background.
internal static class ProfilePicker
{
    internal static bool Run(GameState state,bool savedTown)
    {
        bool performance=DebugOverlay.PerformanceVisible,text=DebugOverlay.TextVisible;
        try{
            using var view=new ProfilePickerUi(state,savedTown);var navigation=new MenuNavigation();
            while(true){
                DebugOverlay.PerformanceVisible=false;DebugOverlay.TextVisible=false;
                view.Draw();RuntimeOptions.Wait();
                Controller.Update();var pad=Controller.GetState(0);int direction=navigation.Read("villager-profile",0,pad.Stick.X,pad.Stick.Y,(int)pad.PressedButtons,Clock.Timestamp,true);
                if(direction==MenuNavigation.Left||direction==MenuNavigation.Up)view.Selected=(view.Selected+3)%4;
                if(direction==MenuNavigation.Right||direction==MenuNavigation.Down)view.Selected=(view.Selected+1)%4;
                if(pad.B.WasPressed)return false;
                if(pad.A.WasPressed||pad.Start.WasPressed){state.SelectProfile(view.Selected);return true;}
            }
        }finally{DebugOverlay.PerformanceVisible=performance;DebugOverlay.TextVisible=text;}
    }
}
