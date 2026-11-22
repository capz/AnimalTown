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
    private readonly MenuNavigation menuNavigation=new MenuNavigation();
    private readonly KeyboardFont font=new KeyboardFont();
    private readonly KeyboardFont strongFont=new KeyboardFont();
    private sealed class KeyboardFont {internal float Measure(string text,float scale)=>text.Length*8*scale;}
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
    private void Strong(string text,float x,float y,Color32 ink,float scale=1){}
    private void Rounded(float x,float y,float w,float h,float radius,Color32 ink){}
    private void MailButton(ControllerButton button)
    {MailFrame(button);}
    private void MailFrame(ControllerButton button=0,float x=0,float y=0)
    {
        Clock.Timestamp+=200000;
        MailUiInput(new ControllerState(1,(int)button,(int)button,0,new StickState(x,y),default,0,0));
    }
    internal static void CheckMailControls(Action<bool,string> check)
    {
        CheckLetterKeyboard(check);
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
        check(g.screen=="dialogue"&&g.letterEntry==null&&state.Pockets[0]==0&&state.Pockets[2]==0&&state.Pockets[1]==Catalog.PlainWritingPaper,"sending through the actual UI consumes only selected paper and gift and releases the draft keyboard");

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
    private static void CheckLetterKeyboard(Action<bool,string> check)
    {
        var state=new GameState(33){Tutorial=7};state.ResidentJobs.Enable(100,JobCalendar.At(2027,7,15,12));Array.Clear(state.Pockets);
        state.Pockets[0]=Catalog.WritingPaper;state.Pockets[1]=Catalog.Pear;
        var g=new TownGame(state);g.OpenLetterComposer(paper:0);g.MailButton(ControllerButton.A);
        var entry=g.letterEntry!;
        check(g.screen=="letter-compose"&&entry.Selected==0&&!entry.Uppercase&&entry.CountLabel=="0 / 1000","the letter editor opens chat's lowercase QWERTY keyboard with the mail budget");
        g.MailButton(ControllerButton.DPadLeft);g.MailButton(ControllerButton.A);
        g.MailButton(ControllerButton.DPadUp);g.MailButton(ControllerButton.A);
        g.MailButton(ControllerButton.DPadRight);g.MailButton(ControllerButton.A);
        g.MailButton(ControllerButton.DPadDown);g.MailButton(ControllerButton.L);g.MailButton(ControllerButton.A);
        g.MailButton(ControllerButton.R);
        check(g.letterDraft=="p+\nQ "&&entry.Selected==0&&entry.Uppercase,"real letter controls share chat's row wrapping, case toggle and R-space, while NL preserves line breaks");
        g.MailButton(ControllerButton.X);g.MailButton(ControllerButton.DPadRight);g.MailButton(ControllerButton.A);
        check(g.letterDraft=="p+\nQW"&&entry.CountLabel=="5 / 1000"&&entry.Selected==1,"erase and type use the same keyboard draft and counter");
        g.MailButton(ControllerButton.Y);g.MailButton(ControllerButton.DPadDown);g.MailButton(ControllerButton.A);
        check(g.screen=="letter-compose"&&g.letterGift==1&&ReferenceEquals(entry,g.letterEntry)&&entry.Selected==1&&entry.Uppercase&&g.letterDraft=="p+\nQW","selecting a present preserves the draft, case and keyboard selection");
        g.MailButton(ControllerButton.B);g.MailButton(ControllerButton.A);
        check(g.screen=="letter-compose"&&ReferenceEquals(entry,g.letterEntry)&&entry.Selected==1&&entry.Uppercase,"keeping a draft preserves keyboard state across discard confirmation");
        g.MailButton(ControllerButton.Start|ControllerButton.A);
        check(g.screen=="letter-confirm"&&g.letterDraft=="p+\nQW"&&state.Pockets[0]==Catalog.WritingPaper&&state.Pockets[1]==Catalog.Pear,"START takes priority over simultaneous typing and never consumes paper or a gift before confirmation");
        g.MailButton(ControllerButton.B);
        while(entry.Text.Length<1000)g.MailButton(ControllerButton.A);
        string full=g.letterDraft;g.MailButton(ControllerButton.A);
        check(g.letterDraft==full&&entry.CountLabel=="1000 / 1000"&&entry.Status.Length>0,"the real letter controls stop at 1000 without silently truncating the draft");
        g.MailButton(ControllerButton.X);g.MailButton(ControllerButton.R);
        check(g.letterDraft.Length==1000&&g.letterDraft.EndsWith(" ")&&entry.Status.Length==0&&g.letterPreview.Split('\n').Length<=3,"editing recovers from a full draft and its preview fits the shared panel");
        g.MailButton(ControllerButton.B|ControllerButton.A);
        check(g.screen=="letter-cancel"&&g.letterDraft.Length==1000,"B takes priority over typing and protects the draft with its existing confirmation");
        g.MailButton(ControllerButton.DPadDown);g.MailButton(ControllerButton.A);
        check(g.screen=="world"&&g.letterEntry==null&&g.letterDraft==""&&state.Pockets[0]==Catalog.WritingPaper&&state.Pockets[1]==Catalog.Pear,"discard releases the shared keyboard while preserving stationery and the present");
        g.OpenLetterComposer(paper:0);g.MailButton(ControllerButton.A);g.MailFrame();g.MailFrame(y:-.9f);
        check(g.letterEntry!.Selected==10,"the letter keyboard accepts the same fresh stick gesture as chat");
        g.MailFrame(ControllerButton.Y,y:-.9f);g.MailFrame(ControllerButton.A,y:-.9f);g.MailFrame(y:-.9f);
        check(g.screen=="letter-compose"&&g.letterEntry.Selected==10,"a stick held across the present dialog cannot move the keyboard on return");
        g.MailFrame();g.MailFrame(y:-.9f);
        check(g.letterEntry.Selected==20,"recentering rearms the shared keyboard after returning from a dialog");
        g.MailButton(ControllerButton.A);g.MailButton(ControllerButton.Start);g.MailFrame();g.MailButton(ControllerButton.B);
        g.MailFrame(ControllerButton.Start,y:-.9f);g.MailFrame(y:-.9f);
        check(g.screen=="letter-confirm"&&g.letterCursor==0,"reopening send confirmation with a held stick keeps the safe default selected");
        g.MailFrame(ControllerButton.A,y:-.9f);
        check(g.screen=="letter-compose"&&state.Pockets[0]==Catalog.WritingPaper,"confirming the default after a held-stick return keeps the draft and stationery");
        g.ReturnFromLetter();
        g.OpenLetterComposer(true);g.MailButton(ControllerButton.A);g.MailButton(ControllerButton.R);g.MailButton(ControllerButton.DPadUp);g.MailButton(ControllerButton.A);g.MailButton(ControllerButton.Y);
        check(g.screen=="letter-compose"&&g.letterDraft=="q \n"&&g.letterEntry!.CountLabel=="3 / 1000","notices share chat's editing controls and line breaks without opening a present selector");
    }
}
