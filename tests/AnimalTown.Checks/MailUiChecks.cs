using AnimalTown;
using AnimalTown.Jobs;
using Dolphin.Graphics;
using Dolphin.Input;
using Dolphin.Timing;

// Execute the production mail controller code. Only its drawing and unrelated
// dialogue owners are replaced; paper/gift selection and the mail service are real.
namespace Dolphin.Graphics
{
    internal readonly struct Color32 {internal Color32(int r,int g,int b,int a=255) {} internal static Color32 White=>new Color32(255,255,255);}
}
internal sealed partial class TownGame
{
    private readonly MenuNavigation jobNavigation=new MenuNavigation();
    private int slot,selected,menuTab,returnMenuTab,returnMenuSelection;
    private bool returnToMenu;
    private bool JobsLive=>state.Town.Jobs.Initialized&&state.ResidentJobs.Enabled;
    private bool Press(ControllerState p)=>p.A.WasPressed;
    private void ResetJobConversation(){}
    private string JobPlayerName(JobIdentity id)=>"Player";
    private string JobNpcName(JobIdentity id)=>"Neighbour";
    private void Say(string name,string text){message=text;screen="dialogue";}
    private void Panel(float x,float y,float w,float h){}
    private void Heading(string text,float x,float y,Color32 ink,float scale=1){}
    private void Text(string text,float x,float y,Color32 ink,float scale=1){}
    private void Rounded(float x,float y,float w,float h,float radius,Color32 ink){}
    private void MailButton(ControllerButton button)
    {
        Clock.Timestamp+=200000;
        MailUiInput(new ControllerState(1,(int)button,(int)button,0,default,default,0,0));
    }
    internal static void CheckMailControls(Action<bool,string> check)
    {
        var state=new GameState(32){Tutorial=7};state.SelectProfile(1);state.SelectProfile(0);
        state.ResidentJobs.Enable(99,JobCalendar.At(2027,7,15,12));Array.Clear(state.Pockets);
        state.Pockets[0]=Catalog.WritingPaper;state.Pockets[1]=Catalog.PlainWritingPaper;state.Pockets[2]=Catalog.Pear;
        state.Pockets[3]=Catalog.Shovel;state.Pockets[4]=JobContent.Letter;state.Pockets[5]=JobContent.Parcel;
        state.Town.Jobs.SetBinding(0,5,state.Town.Jobs.Allocate());
        var g=new TownGame(state){screen="item",slot=1};
        int[] before=(int[])state.Pockets.Clone();
        g.OpenPocketLetter(PocketAction.WriteLetter);
        check(g.screen=="letter-recipient"&&g.letterPaper==1,"stationery action starts the real composer on the selected sheet");
        g.MailButton(ControllerButton.B);
        check(g.screen=="menu"&&g.menuTab==0&&g.selected==1&&before.SequenceEqual(state.Pockets),"recipient back returns to its backpack slot without consuming paper");
        g.screen="item";g.OpenPocketLetter(PocketAction.WriteLetter);g.MailButton(ControllerButton.A);
        g.MailButton(ControllerButton.Y);
        check(g.screen=="letter-gift"&&g.letterGiftCount==3&&g.letterGiftRows.Take(3).SequenceEqual(new[]{-1,0,2}),"attachment rows include spare stationery and ordinary gifts, excluding used paper, tools, letters and bound parcels");
        g.MailButton(ControllerButton.DPadDown);g.MailButton(ControllerButton.A);
        check(g.letterGift==0&&g.screen=="letter-compose","spare stationery is selectable as a real letter present");
        g.MailButton(ControllerButton.B);g.MailButton(ControllerButton.DPadDown);g.MailButton(ControllerButton.A);
        check(g.screen=="menu"&&g.selected==1&&before.SequenceEqual(state.Pockets),"discarding a draft returns to the backpack and preserves paper and present");

        g.slot=2;g.screen="item";g.OpenPocketLetter(PocketAction.AttachToLetter);
        check(g.letterGift==2&&g.letterPaper==0,"gift action preselects the exact gift and a separate sheet");
        g.MailButton(ControllerButton.A);g.letterDraft="Hello there!";
        g.MailButton(ControllerButton.Start);g.MailButton(ControllerButton.DPadDown);g.MailButton(ControllerButton.A);
        check(g.screen=="dialogue"&&state.Pockets[0]==0&&state.Pockets[2]==0&&state.Pockets[1]==Catalog.PlainWritingPaper,"sending through the actual UI consumes only selected paper and gift");

        g.slot=1;g.screen="item";g.OpenPocketLetter(PocketAction.AttachToLetter);
        check(g.screen=="dialogue"&&g.returnToMenu&&g.returnMenuTab==0&&g.returnMenuSelection==1&&state.Pockets[1]==Catalog.PlainWritingPaper,"attaching the only remaining sheet explains the missing separate paper and preserves it");
        g.screen="menu";g.menuTab=4;g.selected=5;g.OpenLetterComposer();g.MailButton(ControllerButton.B);
        check(g.screen=="menu"&&g.menuTab==4&&g.selected==5,"town-hall tab composer back returns to that entry rather than another screen");
        g.screen="post";g.OpenLetterComposer();g.MailButton(ControllerButton.B);
        check(g.screen=="post"&&g.selected==5,"physical town-hall composer back retains its origin");

        g.screen="item";g.slot=1;g.OpenPocketLetter(PocketAction.WriteLetter);g.MailButton(ControllerButton.A);g.letterDraft="\ud800";
        g.MailButton(ControllerButton.Start);g.MailButton(ControllerButton.DPadDown);g.MailButton(ControllerButton.A);
        check(g.screen=="letter-compose"&&state.Pockets[1]==Catalog.PlainWritingPaper,"invalid text keeps the selected sheet and returns to editing");
        g.letterDraft="Hello there!";
        var ledger=state.Town.Jobs;
        for(int i=OfflineJobLedger.InboxCount;i<ledger.Mail.Length;i++)ledger.Mail.Set(i,new JobMailRecord{State=JobMailState.Queued});
        g.MailButton(ControllerButton.Start);g.MailButton(ControllerButton.DPadDown);g.MailButton(ControllerButton.A);
        check(g.screen=="letter-compose"&&g.letterDraft=="Hello there!"&&state.Pockets[1]==Catalog.PlainWritingPaper&&g.letterStatus.Contains("full"),"a full outbox preserves both draft and selected paper with useful feedback");

        // A missing selected sheet never silently consumes another one.
        state.Pockets[0]=Catalog.WritingPaper;g.screen="item";g.slot=1;g.OpenPocketLetter(PocketAction.WriteLetter);
        g.MailButton(ControllerButton.A);g.letterDraft="Hello there!";state.Pockets[1]=0;
        g.MailButton(ControllerButton.Start);g.MailButton(ControllerButton.DPadDown);g.MailButton(ControllerButton.A);
        check(g.screen=="letter-compose"&&state.Pockets[0]==Catalog.WritingPaper&&g.letterStatus.Contains("writing paper"),"send failure keeps the draft and never substitutes unselected paper");
    }
}
