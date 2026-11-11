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
        var font=UiFont.LoadSystem();
        using var ui=new Draw2D(GraphicsDevice.Default,new Draw2DOptions{Width=640,Height=480,VertexCapacity=4096,SpriteCapacity=128});
        using var background=new TitleBackdrop();var navigation=new MenuNavigation();
        int selected=state.ActiveProfile;
        try{
            while(true){
                DebugOverlay.PerformanceVisible=false;DebugOverlay.TextVisible=false;
                var device=GraphicsDevice.Default;device.BeginFrame(TitleBackdrop.Color);ui.Begin();background.Draw(ui);
                TitleArt.Panel(ui,70,60,500,360);font.Draw(ui,"Who is playing today?",108,84,TitleArt.Ink,1.2f);
                font.Draw(ui,"Four villagers. One shared town.",108,122,TitleArt.Ink,.85f);
                for(int i=0;i<GameState.ProfileCount;i++){
                    float y=167+i*48;bool focus=selected==i;var p=state.Profiles[i];
                    TitleArt.Rounded(ui,104,y,432,40,20,focus?TitleArt.Leaf:new Color32(236,226,197));
                    string label=(i+1)+". "+(p.Created&&(savedTown||i!=0)?i==state.ActiveProfile?state.Name:p.Name:"Move in a new villager");
                    font.Draw(ui,label,124,y+8,focus?TitleArt.Cream:TitleArt.Ink,.95f);
                }
                font.Draw(ui,"A: continue     B: title screen",108,379,TitleArt.Ink,.75f);ui.End();device.EndFrame();RuntimeOptions.Wait();
                Controller.Update();var pad=Controller.GetState(0);int direction=navigation.Read("villager-profile",0,pad.Stick.X,pad.Stick.Y,(int)pad.PressedButtons,Clock.Timestamp,true);
                if(direction==MenuNavigation.Up)selected=(selected+3)%4;if(direction==MenuNavigation.Down)selected=(selected+1)%4;
                if(pad.B.WasPressed)return false;
                if(pad.A.WasPressed||pad.Start.WasPressed){state.SelectProfile(selected);return true;}
            }
        }finally{font.Release();DebugOverlay.PerformanceVisible=performance;DebugOverlay.TextVisible=text;}
    }
}
