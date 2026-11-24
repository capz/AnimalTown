using System;
using AnimalTown;
using Dolphin.Graphics;
using Dolphin.Input;
using Dolphin.Timing;

internal sealed partial class TownGame
{
    // One controller mapping and one drawing path for chat, letters and notices.
    // Each caller handles send/back before editing, and owns its draft lifetime.
    private void KeyboardInput(ChatEntry entry,string view,ControllerState pad)
    {
        entry.Move(menuNavigation.Read(view,0,pad.Stick.X,pad.Stick.Y,(int)pad.PressedButtons,Clock.Timestamp,true));
        if(pad.L.WasPressed)entry.ToggleCase();
        if(pad.R.WasPressed)entry.Append(' ');
        if(pad.X.WasPressed)entry.Erase();
        if(pad.A.WasPressed)entry.Type();
    }
    private void DrawTextEntry(ChatEntry entry,string title,string context,string preview,string placeholder,string status,bool error,Color32 ink,bool present=false,float previewScale=1.2f)
    {
        Panel(20,16,600,448);Heading(title,43,30,ink,1);
        Text(context,43,79,ink,1.1f);Text(entry.CountLabel,Math.Min(522,599-font.Measure(entry.CountLabel,.9f/1.5f)),80,ink,.9f);
        Rounded(39,108,560,83,18,new Color32(239,232,208));
        if(entry.Text.Length==0)Text(placeholder,53,122,new Color32(109,122,102),1.2f);
        else Text(preview,53,114,ink,previewScale);
        Text(status,43,195,error?new Color32(166,65,49):ink,.95f);
        for(int i=0;i<ChatEntry.Columns*ChatEntry.Rows;i++){
            float x=43+i%ChatEntry.Columns*55,y=223+i/ChatEntry.Columns*37;
            bool selected=i==entry.Selected;string label=entry.Label(i);
            Rounded(x,y,51,33,11,selected?new Color32(53,105,84):new Color32(222,231,199));
            Strong(label,x+(51-strongFont.Measure(label,1.2f/1.5f))*.5f,y+3,selected?Color32.White:ink,1.2f);
        }
        Text("A Type   X Erase   START Send   B Back",43,421,ink,1.1f);
        Text(present?(entry.Uppercase?"L abc     R Space     Y Present":"L ABC     R Space     Y Present"):(entry.Uppercase?"L abc     R Space":"L ABC     R Space"),43,445,ink,1.1f);
    }
}
