using System;
using AnimalTown;
using AnimalTown.Jobs;
using Dolphin.Graphics;
using Dolphin.Input;
using Dolphin.Timing;

internal sealed partial class TownGame
{
    private ChatEntry? letterEntry;
    private string letterDraft="",letterStatus="",letterBody="",letterPage="",letterPreview="",letterFrom="",noticeHeading="";
    private readonly string?[] mailLabels=new string?[OfflineJobLedger.MailboxCapacity];
    private int mailLabelsRevision=-1;
    private int letterCursor,letterRecipient,letterGift=-1,letterSlot,noticeSelection;
    private int letterPaper=-1,letterReturnTab,letterReturnSelection,letterGiftCount;
    private string letterReturnScreen="world";
    private readonly int[] letterGiftRows=new int[GameState.MaximumPockets+1];
    private bool composingNotice;
    private readonly TextPages letterPages=new TextPages(48),draftPages=new TextPages(48);
    private void OpenLetterComposer(bool notice=false,int paper=-1,int gift=-1)
    {
        letterReturnScreen=screen=="item"?"menu":screen;letterReturnTab=screen=="item"?0:menuTab;letterReturnSelection=screen=="item"?slot:selected;
        ResetJobConversation();composingNotice=notice;letterEntry=new ChatEntry(JobRulesV1.LetterMaximumScalars,true);letterDraft="";RefreshLetterPreview();letterStatus="";letterGift=gift;
        letterPaper=notice?-1:paper>=0?paper:PocketMenu.WritingPaper(state,gift);
        letterRecipient=0;letterCursor=0;screen=notice?"letter-compose":"letter-recipient";
    }
    private void OpenPocketLetter(PocketAction action)
    {
        if(!PocketMenu.HasAction(state,slot,room,action))return;
        int gift=action==PocketAction.AttachToLetter?slot:-1;
        int paper=action==PocketAction.WriteLetter?slot:PocketMenu.WritingPaper(state,gift);
        if(paper<0){Say("Letter","You need writing paper. The shop cabinet sells it.");returnToMenu=true;returnMenuTab=0;returnMenuSelection=slot;return;}
        OpenLetterComposer(paper:paper,gift:gift);
    }
    private void ReturnFromLetter()
    {screen=letterReturnScreen;menuTab=letterReturnTab;selected=letterReturnSelection;ReleaseLetterEntry();letterGift=-1;letterPaper=-1;}
    private void ReleaseLetterEntry(){letterEntry=null;letterDraft="";letterPreview="";draftPages.Set("");}
    private void OpenLetterGifts()
    {
        letterGiftCount=1;letterGiftRows[0]=-1;letterCursor=0;
        for(int i=0;i<state.Capacity;i++)if(PocketMenu.CanAttach(state,i,letterPaper)){
            if(i==letterGift)letterCursor=letterGiftCount;
            letterGiftRows[letterGiftCount++]=i;
        }
        screen="letter-gift";
    }
    private int LetterRecipients=>6+GameState.ProfileCount;
    private bool RecipientAvailable(int row)=>row<6||state.Profiles[row-6].Created&&row-6!=state.ActiveProfile;
    private string RecipientName(int row)=>row<6?state.Town.Residents[row].Name:state.Profiles[row-6].Name;
    private JobIdentity RecipientIdentity(int row)=>row<6?state.Town.Jobs.Npc(row):state.Town.Jobs.Player(row-6);
    private void OpenMailLetter(int row)
    {letterSlot=row;letterBody=state.ResidentJobs.ReadLetter(row);var m=state.Town.Jobs.Letter(state.ActiveProfile,row);letterFrom="From "+MailSender(m.Sender);letterStatus="";letterCursor=0;RefreshLetterPage();screen="letter-read";}
    private string MailSender(JobIdentity sender)=>state.Town.Jobs.FindPlayer(sender)>=0?JobPlayerName(sender):JobNpcName(sender);
    private string MailboxRow(int row)
    {
        if(row>=state.MailCount)return "No mail";
        if(!JobsLive)return state.Mail[row]<0?"Arrives tomorrow":Catalog.Items[state.Mail[row]].Name;
        if(mailLabelsRevision!=state.Town.Jobs.Revision){Array.Clear(mailLabels,0,mailLabels.Length);mailLabelsRevision=state.Town.Jobs.Revision;}
        if(mailLabels[row]==null){var m=state.Town.Jobs.Letter(state.ActiveProfile,row);mailLabels[row]=m.State==JobMailState.Inbox?MailSender(m.Sender)+(m.Read?" · Read":m.Reply?" · New reply":" · New letter")+(m.Gift!=0?" · Present":""):state.Mail[row]<0?"Arrives tomorrow":Catalog.Items[state.Mail[row]].Name;}
        return mailLabels[row]!;
    }
    private void OpenNoticeBoard()
    {screen="notice-board";noticeSelection=0;RefreshNotice();}
    private void RefreshNotice()
    {var n=state.Town.Jobs.Notice(noticeSelection);letterBody=n.Id.IsEmpty?"Leave a message for your neighbours.":n.Text;noticeHeading=n.Id.IsEmpty?"Nothing posted here yet.":JobPlayerName(n.Author)+" · "+JobCalendar.Display(n.At);letterCursor=0;RefreshLetterPage(11);}
    private bool MailUiInput(ControllerState p)
    {
        if(!JobsLive)return false;
        if(screen=="mail"&&Press(p)&&selected<state.MailCount&&state.Town.Jobs.Letter(state.ActiveProfile,selected).State==JobMailState.Inbox){OpenMailLetter(selected);return true;}
        if(!screen.StartsWith("letter-")&&screen!="notice-board")return false;
        if(screen=="letter-compose"){
            // Dialog navigation must also rearm when the same dialog is
            // reopened after typing with a held stick.
            jobNavigation.Read(screen,0,p.Stick.X,p.Stick.Y,0,Clock.Timestamp,false);
            // Match chat's action priority: sending or leaving never types a
            // simultaneous A press into the draft. The keyboard owns its cursor
            // and case independently of attachment/confirmation selections.
            if(p.B.WasPressed){screen="letter-cancel";letterCursor=0;return true;}
            if(p.Start.WasPressed){screen="letter-confirm";letterCursor=0;return true;}
            if(p.Y.WasPressed&&!composingNotice){OpenLetterGifts();return true;}
            var entry=letterEntry!;string before=entry.Text;KeyboardInput(entry,screen,p);
            if(entry.Text!=before){letterDraft=entry.Text;RefreshLetterPreview();letterStatus="";}
            return true;
        }
        // Suspend the shared keyboard while a mail dialog owns navigation;
        // a stick held when returning must be recentered just like in chat.
        menuNavigation.Read(screen,0,p.Stick.X,p.Stick.Y,0,Clock.Timestamp,false);
        int dir=jobNavigation.Read(screen,0,p.Stick.X,p.Stick.Y,(int)p.PressedButtons,Clock.Timestamp,true);
        int change=dir==MenuNavigation.Up?-1:dir==MenuNavigation.Down?1:0;
        if(screen=="notice-board"){
            if(p.B.WasPressed){screen="world";return true;}
            if(change!=0){noticeSelection=(noticeSelection+8+change)%8;RefreshNotice();}
            if(dir==MenuNavigation.Left||dir==MenuNavigation.Right){letterCursor=Math.Max(0,Math.Min(Math.Max(0,letterPages.Lines-11),letterCursor+(dir==MenuNavigation.Left?-10:10)));RefreshLetterPage(11);}
            if(p.X.WasPressed)OpenLetterComposer(true);return true;
        }
        if(screen=="letter-read"){
            if(p.B.WasPressed){screen="mail";selected=Math.Min(letterSlot,Math.Max(0,state.MailCount-1));return true;}
            int lines=Math.Max(0,letterPages.Lines-8);if(change!=0){letterCursor=Math.Max(0,Math.Min(lines,letterCursor+change));RefreshLetterPage();}
            if(p.X.WasPressed){var result=state.ResidentJobs.TakeMailPresent(letterSlot,state.ResidentJobs.NextCommandSequence);letterStatus=result.Result==JobResult.Ok?"Present moved to your pockets.":result.Result==JobResult.FullPockets?"Make room in your pockets first.":"No present attached.";}
            if(p.Y.WasPressed){letterCursor=0;screen="letter-discard";}
            return true;
        }
        if(screen=="letter-discard"){
            if(p.B.WasPressed){screen="letter-read";return true;}if(change!=0)letterCursor=1-letterCursor;
            if(Press(p)){if(letterCursor==1){state.ResidentJobs.DiscardLetter(letterSlot,true,state.ResidentJobs.NextCommandSequence);screen="mail";selected=0;}else screen="letter-read";}return true;
        }
        if(screen=="letter-recipient"){
            if(p.B.WasPressed){ReturnFromLetter();return true;}
            if(change!=0){do{letterRecipient=(letterRecipient+LetterRecipients+change)%LetterRecipients;}while(!RecipientAvailable(letterRecipient));}
            if(Press(p)){screen="letter-compose";letterCursor=0;}return true;
        }
        if(screen=="letter-gift"){
            if(p.B.WasPressed){screen="letter-compose";return true;}
            if(change!=0)letterCursor=(letterCursor+letterGiftCount+change)%letterGiftCount;
            if(Press(p)){int pocket=letterGiftRows[letterCursor];if(pocket<0||PocketMenu.CanAttach(state,pocket,letterPaper)){letterGift=pocket;screen="letter-compose";letterCursor=0;letterStatus="";}else letterStatus="Choose an ordinary gift.";}return true;
        }
        if(screen=="letter-confirm"){
            if(p.B.WasPressed){screen="letter-compose";return true;}
            if(change!=0)letterCursor=1-letterCursor;
            if(Press(p)){
                if(letterCursor==0){screen="letter-compose";return true;}
                int paper=letterPaper;
                // A backpack action selects an actual sheet, never silently
                // substitute the first paper found when the player sends.
                if(paper>=0&&((uint)paper>=state.Capacity||state.IsJobItem(paper)||!Catalog.Has(state.Pockets[paper],ItemFlags.Stationery)))paper=-1;
                if(!composingNotice&&paper<0){letterStatus="You need writing paper. The shop cabinet sells it.";screen="letter-compose";return true;}
                var result=composingNotice?state.ResidentJobs.PostNotice(letterDraft,state.ResidentJobs.NextCommandSequence):state.ResidentJobs.SendLetter(RecipientIdentity(letterRecipient),letterDraft,paper,letterGift,state.ResidentJobs.NextCommandSequence);
                if(result.Result==JobResult.Ok){if(!composingNotice&&state.Tutorial==4)state.CreditTutorial(4);ReleaseLetterEntry();Say(composingNotice?"Notice board":"Fern",composingNotice?"Your message is on the board.":"Your letter is queued for the next 08:00 post round.");}
                else{letterStatus=result.Result==JobResult.FullOutbox?"The outgoing post is full. Try after delivery.":"This letter could not be sent. Check the text and attachment.";screen="letter-compose";}
            }return true;
        }
        if(p.B.WasPressed){if(screen=="letter-cancel"){screen="letter-compose";letterCursor=0;}else{screen="letter-cancel";letterCursor=0;}return true;}
        if(screen=="letter-cancel"){
            if(change!=0)letterCursor=1-letterCursor;
            if(Press(p)){if(letterCursor==1)ReturnFromLetter();else{screen="letter-compose";letterCursor=0;}}return true;
        }
        return true;
    }
    private void RefreshLetterPage(int lines=8)
    {letterPages.Set(letterBody);letterPage=letterPages.Window(letterCursor,lines);}
    private void RefreshLetterPreview()
    {draftPages.Set(letterDraft);letterPreview=draftPages.Window(Math.Max(0,draftPages.Lines-3),3);}
    private bool DrawMailUi(Color32 ink)
    {
        if(!JobsLive||!screen.StartsWith("letter-")&&screen!="notice-board")return false;
        if(screen=="letter-compose"){
            var entry=letterEntry!;string error=entry.Status.Length!=0?entry.Status:letterStatus;
            DrawTextEntry(entry,composingNotice?"A message for the town":"Write a letter",composingNotice?"Everyone can read this.":"To "+RecipientName(letterRecipient)+(letterGift>=0?" · Present attached":""),letterPreview,"What would you like to write?",error.Length!=0?error:"NL starts a new line. R adds a space.",error.Length!=0,ink,!composingNotice,1.03f);
            return true;
        }
        Panel(35,46,570,390);
        if(screen=="notice-board"){
            Heading("Notice board",60,67,ink);Text(noticeHeading,62,115,ink,1.1f);
            Text(letterPage,62,151,ink,1.12f);Text("Up/Down: posts   Left/Right: scroll   X: write   B: close",60,399,ink,.9f);return true;
        }
        Heading(composingNotice?"A message for the town":screen=="letter-read"?"Your letter":"Write a letter",60,67,ink);
        if(screen=="letter-recipient"){
            Text("To whom?",62,110,ink,1.2f);for(int i=0;i<LetterRecipients;i++){if(!RecipientAvailable(i))continue;float y=142+i*23;if(i==letterRecipient)Rounded(57,y-2,522,23,11,new Color32(221,231,188));Text(RecipientName(i),76,y,ink,1.1f);}return true;
        }
        if(screen=="letter-gift"){
            Text("Attach a present (ordinary items only)",62,111,ink,1.1f);int first=letterCursor/10*10;for(int i=first;i<Math.Min(letterGiftCount,first+10);i++){float y=144+(i-first)*23;if(i==letterCursor)Rounded(57,y-2,522,23,11,new Color32(221,231,188));int pocket=letterGiftRows[i];Text(pocket<0?"No present":Catalog.Items[state.Pockets[pocket]].Name,75,y,ink,1.05f);}return true;
        }
        if(screen=="letter-confirm"||screen=="letter-discard"||screen=="letter-cancel"){
            bool discard=screen=="letter-discard",cancel=screen=="letter-cancel";
            Text(discard?"Discard this letter and any attached present?":cancel?"Discard this draft?":composingNotice?"Post this message for the town?":"Send this letter with the chosen present?",62,140,ink,1.07f);
            for(int i=0;i<2;i++){if(letterCursor==i)Rounded(57,237+i*35,522,31,14,new Color32(221,231,188));Text(i==0?"Keep editing / go back":discard||cancel?"Discard":"Send / post",75,241+i*35,ink,1.15f);}return true;
        }
        if(screen=="letter-read"){
            var m=state.Town.Jobs.Letter(state.ActiveProfile,letterSlot);Text(letterFrom,62,108,ink,1.2f);
            Text(letterPage,62,145,ink,1.08f);Text(letterStatus,62,353,ink,1);
            Text(m.Gift!=0?"X: take present   Y: discard   B: back":"Up/Down: scroll   Y: discard   B: back",62,399,ink,1);return true;
        }
        return true;
    }
}
