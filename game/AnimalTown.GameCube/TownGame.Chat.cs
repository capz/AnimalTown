using AnimalTown;
using Dolphin.Graphics;
using Dolphin.Input;
using Dolphin.Timing;
using Dolphin.Diagnostics;

internal sealed partial class TownGame
{
    private ChatEntry? chatEntry;
    private float[]? chatAdvances;
    private ChatSpeech? chatDraftLayout,chatSpeech;
    private string chatDraft="",chatSpeakerLabel="";
    private int chatPage,attendingVillager=-1;
    private bool chatFromMenu;

    private void OpenChat()
    {
        chatFromMenu=screen=="menu";chatEntry=new ChatEntry();chatAdvances=new float[95];
        chatSpeakerLabel="Speaking as "+state.Name;
        for(int i=0;i<95;i++)chatAdvances[i]=font.MeasureGlyph((char)(i+32));
        chatDraft="";chatDraftLayout=null;screen=ChatEntry.Screen;
        DebugOverlay.WriteLine("ANIMALTOWN CHAT OPEN");
    }
    private void ReleaseChatEntry()
    {chatEntry=null;chatAdvances=null;chatDraftLayout=null;chatDraft="";chatSpeakerLabel="";}
    private void ClearChatAttention()
    {
        attendingVillager=-1;
        if(!renderDebug.ForceAttention)attention.Dispose();
    }
    private void ClearChatPresentation(){chatSpeech=null;chatPage=0;ClearChatAttention();}
    private void ChatInput(ControllerState pad)
    {
        var entry=chatEntry!;
        if(pad.B.WasPressed){screen=chatFromMenu?"menu":"world";ReleaseChatEntry();return;}
        if(pad.Start.WasPressed){
            var line=TownChat.Submit(state,entry.Text,DebugMenuSettings.Visible);
            if(line.Kind==TownChatKind.Error){entry.Status=line.Text;DebugOverlay.WriteLine("ANIMALTOWN CHAT INVALID");return;}
            var speech=new ChatSpeech(line.Text,chatAdvances!,528/(1.4f/1.5f));
            ReleaseChatEntry();screen="world";Say(line.Speaker,speech.Page(0));
            chatSpeech=speech;chatPage=0;wrappedDialogue=message;
            if(line.Kind==TownChatKind.Attend)attendingVillager=line.Villager;
            // Report routing/pages for native validation, never private text.
            DebugOverlay.WriteLine("ANIMALTOWN CHAT SPEECH actor "+(attendingVillager+1)+" pages "+speech.Count);
            return;
        }
        entry.Move(menuNavigation.Read(ChatEntry.Screen,0,pad.Stick.X,pad.Stick.Y,(int)pad.PressedButtons,Clock.Timestamp,true));
        if(pad.L.WasPressed)entry.ToggleCase();
        if(pad.R.WasPressed)entry.Append(' ');
        if(pad.X.WasPressed)entry.Erase();
        if(pad.A.WasPressed)entry.Type();
    }
    private bool NextChatPage()
    {
        if(chatSpeech==null||chatPage+1>=chatSpeech.Count)return false;
        message=chatSpeech.Page(++chatPage);wrappedDialogue=message;typeAt=0;voiceTick=0;dialogueVoice.Begin();return true;
    }
    private void DrawChat(Color32 ink)
    {
        var entry=chatEntry!;
        Panel(20,16,600,448);Heading("Chat",43,30,ink,1);
        Text(chatSpeakerLabel,43,79,ink,1.1f);
        Text(entry.CountLabel,522,80,ink,.9f);
        Rounded(39,108,560,83,18,new Color32(239,232,208));
        if(entry.Text.Length==0)Text("What would you like to say?",53,122,new Color32(109,122,102),1.2f);
        else{
            if(chatDraft!=entry.Text){chatDraft=entry.Text;chatDraftLayout=new ChatSpeech(chatDraft,chatAdvances!,534/(1.2f/1.5f));}
            Text(chatDraftLayout!.Page(chatDraftLayout.Count-1),53,114,ink,1.2f);
        }
        string status=entry.Status.Length!=0?entry.Status:DebugMenuSettings.Visible?"Debug: /attend lowercase-name text":"L switches letter case. R adds a space.";
        Text(status,43,195,entry.Status.Length!=0?new Color32(166,65,49):ink,.95f);
        for(int i=0;i<ChatEntry.Columns*ChatEntry.Rows;i++){
            float x=43+i%ChatEntry.Columns*55,y=223+i/ChatEntry.Columns*37;
            bool selected=i==entry.Selected;string label=entry.Label(i);
            Rounded(x,y,51,33,11,selected?new Color32(53,105,84):new Color32(222,231,199));
            Strong(label,x+(51-strongFont.Measure(label,1.2f/1.5f))*.5f,y+3,selected?Color32.White:ink,1.2f);
        }
        Text("A Type   X Erase   START Send   B Back",43,421,ink,1.1f);
        Text(entry.Uppercase?"L abc     R Space":"L ABC     R Space",43,445,ink,1.1f);
    }
}
