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
        KeyboardInput(entry,ChatEntry.Screen,pad);
    }
    private bool NextChatPage()
    {
        if(chatSpeech==null||chatPage+1>=chatSpeech.Count)return false;
        message=chatSpeech.Page(++chatPage);wrappedDialogue=message;typeAt=0;voiceTick=0;dialogueVoice.Begin();return true;
    }
    private void DrawChat(Color32 ink)
    {
        var entry=chatEntry!;
        if(chatDraft!=entry.Text){chatDraft=entry.Text;chatDraftLayout=new ChatSpeech(chatDraft,chatAdvances!,534/(1.2f/1.5f));}
        string status=entry.Status.Length!=0?entry.Status:DebugMenuSettings.Visible?"Debug: /attend lowercase-name text":"L switches letter case. R adds a space.";
        DrawTextEntry(entry,"Chat",chatSpeakerLabel,chatDraftLayout==null?"":chatDraftLayout.Page(chatDraftLayout.Count-1),"What would you like to say?",status,entry.Status.Length!=0,ink);
    }
}
