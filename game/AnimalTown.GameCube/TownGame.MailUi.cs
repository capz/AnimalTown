using System;
using AnimalTown;
using AnimalTown.Jobs;
using Dolphin.Graphics;
using Dolphin.Input;
using Dolphin.Timing;

internal sealed partial class TownGame
{
    private const string LetterKeys="ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,!?'\n-:;()";
    private string letterDraft="",letterStatus="",letterBody="",letterPage="";
    private static readonly string[] LetterKeyLabels=MakeLetterKeys();
    private static string[] MakeLetterKeys(){var labels=new string[LetterKeys.Length];for(int i=0;i<labels.Length;i++)labels[i]=LetterKeys[i]=='\n'?"NL":LetterKeys[i]==' '?"_":LetterKeys.Substring(i,1);return labels;}
    private int letterCursor,letterRecipient,letterGift=-1,letterSlot,noticeSelection;
    private bool composingNotice;
    private readonly WrappedText letterWrapped=new WrappedText(48);
    private void OpenLetterComposer(bool notice=false)
    {ResetJobConversation();composingNotice=notice;letterDraft="";letterStatus="";letterGift=-1;letterRecipient=0;letterCursor=0;screen=notice?"letter-compose":"letter-recipient";}
    private int LetterRecipients=>6+GameState.ProfileCount;
    private bool RecipientAvailable(int row)=>row<6||state.Profiles[row-6].Created&&row-6!=state.ActiveProfile;
    private string RecipientName(int row)=>row<6?state.Town.Residents[row].Name:state.Profiles[row-6].Name;
    private JobIdentity RecipientIdentity(int row)=>row<6?state.Town.Jobs.Npc(row):state.Town.Jobs.Player(row-6);
    private void OpenMailLetter(int row)
    {letterSlot=row;letterBody=state.ResidentJobs.ReadLetter(row);letterCursor=0;RefreshLetterPage();screen="letter-read";}
    private bool MailUiInput(ControllerState p)
    {
        if(!JobsLive)return false;
        if(screen=="mail"&&Press(p)&&selected<state.MailCount&&state.Town.Jobs.Letter(state.ActiveProfile,selected).State==JobMailState.Inbox){OpenMailLetter(selected);return true;}
        if(!screen.StartsWith("letter-")&&screen!="notice-board")return false;
        int dir=jobNavigation.Read(screen,0,p.Stick.X,p.Stick.Y,(int)p.PressedButtons,Clock.Timestamp,true);
        int change=dir==MenuNavigation.Up?-1:dir==MenuNavigation.Down?1:0;
        if(screen=="notice-board"){
            if(p.B.WasPressed){screen="world";return true;}
            if(change!=0)noticeSelection=(noticeSelection+8+change)%8;
            if(p.X.WasPressed)OpenLetterComposer(true);return true;
        }
        if(screen=="letter-read"){
            if(p.B.WasPressed){screen="mail";selected=Math.Min(letterSlot,Math.Max(0,state.MailCount-1));return true;}
            int lines=Math.Max(1,letterBody.Length/48-6);if(change!=0){letterCursor=Math.Max(0,Math.Min(lines,letterCursor+change));RefreshLetterPage();}
            if(p.X.WasPressed){var result=state.ResidentJobs.TakeMailPresent(letterSlot,state.ResidentJobs.NextCommandSequence);letterStatus=result.Result==JobResult.Ok?"Present moved to your pockets.":result.Result==JobResult.FullPockets?"Make room in your pockets first.":"No present attached.";}
            if(p.Y.WasPressed){letterCursor=0;screen="letter-discard";}
            return true;
        }
        if(screen=="letter-discard"){
            if(p.B.WasPressed){screen="letter-read";return true;}if(change!=0)letterCursor=1-letterCursor;
            if(Press(p)){if(letterCursor==1){state.ResidentJobs.DiscardLetter(letterSlot,true,state.ResidentJobs.NextCommandSequence);screen="mail";selected=0;}else screen="letter-read";}return true;
        }
        if(screen=="letter-recipient"){
            if(p.B.WasPressed){screen="post";selected=5;return true;}
            if(change!=0){do{letterRecipient=(letterRecipient+LetterRecipients+change)%LetterRecipients;}while(!RecipientAvailable(letterRecipient));}
            if(Press(p)){screen="letter-compose";letterCursor=0;}return true;
        }
        if(screen=="letter-gift"){
            if(p.B.WasPressed){screen="letter-compose";return true;}
            if(change!=0)letterCursor=(letterCursor+state.Capacity+1+change)%(state.Capacity+1);
            if(Press(p)){int pocket=letterCursor-1;if(pocket<0||!state.IsJobItem(pocket)&&Catalog.Has(state.Pockets[pocket],ItemFlags.Gift)&&!Catalog.Has(state.Pockets[pocket],ItemFlags.Stationery)&&state.Pockets[pocket]!=19){letterGift=pocket;screen="letter-compose";}else letterStatus="Choose an ordinary gift.";}return true;
        }
        if(screen=="letter-confirm"){
            if(p.B.WasPressed){screen="letter-compose";return true;}
            if(change!=0)letterCursor=1-letterCursor;
            if(Press(p)){
                if(letterCursor==0){screen="letter-compose";return true;}
                int paper=-1;for(int i=0;i<state.Capacity;i++)if(i!=letterGift&&!state.IsJobItem(i)&&Catalog.Has(state.Pockets[i],ItemFlags.Stationery)){paper=i;break;}
                if(!composingNotice&&paper<0){letterStatus="You need writing paper. The shop cabinet sells it.";screen="letter-compose";return true;}
                var result=composingNotice?state.ResidentJobs.PostNotice(letterDraft,state.ResidentJobs.NextCommandSequence):state.ResidentJobs.SendLetter(RecipientIdentity(letterRecipient),letterDraft,paper,letterGift,state.ResidentJobs.NextCommandSequence);
                if(result.Result==JobResult.Ok){if(!composingNotice&&state.Tutorial==4)state.CreditTutorial(4);Say(composingNotice?"Notice board":"Fern",composingNotice?"Your message is on the board.":"Your letter is queued for the next 08:00 post round.");}
                else{letterStatus=result.Result==JobResult.FullOutbox?"The outgoing post is full. Try after delivery.":"This letter could not be sent. Check the text and attachment.";screen="letter-compose";}
            }return true;
        }
        if(p.B.WasPressed){if(screen=="letter-cancel"){screen="letter-compose";letterCursor=0;}else{screen="letter-cancel";letterCursor=0;}return true;}
        if(screen=="letter-cancel"){
            if(change!=0)letterCursor=1-letterCursor;
            if(Press(p)){if(letterCursor==1)screen="world";else{screen="letter-compose";letterCursor=0;}}return true;
        }
        int count=LetterKeys.Length;
        if(dir==MenuNavigation.Left)letterCursor=(letterCursor+count-1)%count;if(dir==MenuNavigation.Right)letterCursor=(letterCursor+1)%count;
        if(dir==MenuNavigation.Up)letterCursor=(letterCursor+count*10-10)%count;if(dir==MenuNavigation.Down)letterCursor=(letterCursor+10)%count;
        if(p.L.WasPressed)letterCursor=letterCursor<26?letterCursor+26:letterCursor<52?letterCursor-26:letterCursor;
        if(p.X.WasPressed&&letterDraft.Length>0)letterDraft=letterDraft.Substring(0,letterDraft.Length-1);
        if(p.Y.WasPressed&&!composingNotice){screen="letter-gift";letterCursor=0;}
        if(Press(p)&&letterDraft.Length<1000)letterDraft+=LetterKeys.Substring(letterCursor,1);
        if(p.Start.WasPressed){screen="letter-confirm";letterCursor=0;}
        return true;
    }
    private void RefreshLetterPage()
    {string text=letterWrapped.Get(letterBody);int start=0;for(int i=0;i<letterCursor&&start<text.Length;i++){int at=text.IndexOf('\n',start);start=at<0?text.Length:at+1;}letterPage=text.Substring(start,Math.Min(text.Length-start,336));}
    private bool DrawMailUi(Color32 ink)
    {
        if(!JobsLive||!screen.StartsWith("letter-")&&screen!="notice-board")return false;
        Panel(35,46,570,390);
        if(screen=="notice-board"){
            Heading("Notice board",60,67,ink);var n=state.Town.Jobs.Notice(noticeSelection);Text(n.Id.IsEmpty?"Nothing posted here yet.":JobPlayerName(n.Author)+" · "+JobCalendar.Display(n.At),62,115,ink,1.1f);
            Text(letterWrapped.Get(n.Id.IsEmpty?"Leave a message for your neighbours.":n.Text),62,151,ink,1.12f);Text("Up/Down: older posts   X: write   B: close",60,399,ink,1);return true;
        }
        Heading(composingNotice?"A message for the town":screen=="letter-read"?"Your letter":"Write a letter",60,67,ink);
        if(screen=="letter-recipient"){
            Text("To whom?",62,110,ink,1.2f);for(int i=0;i<LetterRecipients;i++){if(!RecipientAvailable(i))continue;float y=142+i*23;if(i==letterRecipient)Rounded(57,y-2,522,23,11,new Color32(221,231,188));Text(RecipientName(i),76,y,ink,1.1f);}return true;
        }
        if(screen=="letter-gift"){
            Text("Attach a present (ordinary items only)",62,111,ink,1.1f);int first=letterCursor/10*10;for(int i=first;i<Math.Min(state.Capacity+1,first+10);i++){float y=144+(i-first)*23;if(i==letterCursor)Rounded(57,y-2,522,23,11,new Color32(221,231,188));Text(i==0?"No present":state.IsJobItem(i-1)?"Parcel · protected":Catalog.Items[state.Pockets[i-1]].Name,75,y,ink,1.05f);}return true;
        }
        if(screen=="letter-confirm"||screen=="letter-discard"||screen=="letter-cancel"){
            bool discard=screen=="letter-discard",cancel=screen=="letter-cancel";
            Text(discard?"Discard this letter and any attached present?":cancel?"Discard this draft?":composingNotice?"Post this message for the town?":"Send this letter with the chosen present?",62,140,ink,1.07f);
            for(int i=0;i<2;i++){if(letterCursor==i)Rounded(57,237+i*35,522,31,14,new Color32(221,231,188));Text(i==0?"Keep editing / go back":discard||cancel?"Discard":"Send / post",75,241+i*35,ink,1.15f);}return true;
        }
        if(screen=="letter-read"){
            var m=state.Town.Jobs.Letter(state.ActiveProfile,letterSlot);Text("From "+(state.Town.Jobs.FindPlayer(m.Sender)>=0?JobPlayerName(m.Sender):JobNpcName(m.Sender)),62,108,ink,1.2f);
            Text(letterPage,62,145,ink,1.08f);Text(letterStatus,62,353,ink,1);
            Text(m.Gift!=0?"X: take present   Y: discard   B: back":"Up/Down: scroll   Y: discard   B: back",62,399,ink,1);return true;
        }
        Text(composingNotice?"Everyone can read this.":"To "+RecipientName(letterRecipient)+(letterGift>=0?" · Present attached":""),62,108,ink,1.05f);
        string tail=letterDraft.Length>210?letterDraft.Substring(letterDraft.Length-210):letterDraft;Text(letterWrapped.Get(tail),62,139,ink,1.03f);
        for(int i=0;i<LetterKeys.Length;i++){float x=68+i%10*51,y=239+i/10*19;if(letterCursor==i)Rounded(x-3,y-1,28,19,7,new Color32(221,231,188));Text(LetterKeyLabels[i],x,y,ink,1.02f);}
        Text(letterStatus.Length>0?letterStatus:"A: type   X: erase   L: case   Y: present   START: send",62,407,ink,.9f);return true;
    }
}
