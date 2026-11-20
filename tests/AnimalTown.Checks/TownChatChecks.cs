using AnimalTown;

internal static class TownChatChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var state=new GameState(73){Name="Emi"};state.Town.Residents[0].Name="Rowan";
        var plain=TownChat.Submit(state,"  Hello, neighbours!  ",false);
        check(plain.Kind==TownChatKind.Player&&plain.Speaker=="Emi"&&plain.Text=="Hello, neighbours!"&&plain.Villager==-1,"ordinary chat uses the active human villager's name");
        check(TownChat.Submit(state,"Hello, neighbours!",true).Kind==TownChatKind.Player,"debug mode preserves ordinary player speech");
        var attended=TownChat.Submit(state,"/attend rowan Come over here, Emi!",true);
        check(attended.Kind==TownChatKind.Attend&&attended.Villager==0&&attended.Speaker=="Rowan"&&attended.Text=="Come over here, Emi!","attention selects the exact NPC and supplied text");
        var disabled=TownChat.Submit(state,"/attend rowan Hello!",false);
        check(disabled.Kind==TownChatKind.Player&&disabled.Speaker=="Emi"&&disabled.Text=="/attend rowan Hello!"&&disabled.Villager==-1,"hidden debug menus cannot execute slash commands");
        var spaced=TownChat.Submit(state," /attend   rowan   Hello,  Emi!  ",true);
        check(spaced.Kind==TownChatKind.Attend&&spaced.Text=="Hello,  Emi!","command spacing is parsed without losing message punctuation or internal spaces");
        foreach(string input in new[]{"","   ","/attend","/attend ","/attend rowan","/attend rowan   ","/attend Rowan Hello","/attend row Hello","/attend nobody Hello","/attendx rowan Hello","/unknown","/attend rowan\nHello",new string('x',TownChat.MaximumLength+1),"Hello\0there","Hello\tthere","café"}){
            check(TownChat.Submit(state,input,true).Kind==TownChatKind.Error,"invalid chat/command has no speaker side effects: "+input);
        }
        check(TownChat.Submit(state,new string('x',TownChat.MaximumLength),false).Text.Length==TownChat.MaximumLength,"the full supported message budget is accepted");
        for(int i=0;i<6;i++){
            var npc=state.Town.Residents[i];string lower=npc.Name.ToLowerInvariant();int space=npc.Space;float x=npc.PosX,z=npc.PosZ;int wallet=state.Wallet,friendship=npc.Friendship;
            var line=TownChat.Submit(state,"/attend "+lower+" Hello!",true);
            check(line.Kind==TownChatKind.Attend&&line.Villager==i&&line.Speaker==npc.Name,"each generated name resolves to its NPC");
            check(npc.Space==space&&npc.PosX==x&&npc.PosZ==z&&state.Wallet==wallet&&npc.Friendship==friendship,"debug speech cannot teleport or change villager gameplay/save data");
        }
        var entry=new ChatEntry();
        check(entry.Text==""&&entry.Selected==0&&!entry.Uppercase,"chat entry starts empty and lowercase");
        entry.Move(1);check(entry.Selected==9,"keyboard wraps left inside its row");entry.Move(2);check(entry.Selected==0,"keyboard wraps right inside its row");
        entry.Move(8);check(entry.Selected==40,"keyboard wraps up preserving the column");entry.Move(4);check(entry.Selected==0,"keyboard wraps down preserving the column");
        entry.Type();entry.ToggleCase();entry.Type();entry.Append(' ');entry.Append('/');
        check(entry.Text=="qQ /"&&entry.Uppercase,"typing supports both letter cases, spaces and command punctuation");
        entry.Erase();check(entry.Text=="qQ "&&entry.CountLabel=="3 / 120","erase updates the bounded draft and counter");
        check(!entry.Append('\n')&&entry.Text=="qQ ","unsupported input cannot enter the native IPL draft");
        bool slash=false,spaceKey=false;
        for(int row=0;row<ChatEntry.Rows;row++){
            for(int col=0;col<ChatEntry.Columns;col++){
                string label=entry.Label(entry.Selected);slash|=label=="/";spaceKey|=label=="SP";
                check(label.Length>0,"every keyboard position has a cached visible label");entry.Move(2);
            }
            entry.Move(4);
        }
        check(slash&&spaceKey,"the controller keyboard can type command separators");
        var full=new ChatEntry();for(int i=0;i<TownChat.MaximumLength;i++)check(full.Append('x'),"draft accepts characters inside its limit");
        check(!full.Append('y')&&full.Text.Length==TownChat.MaximumLength&&full.Status.Length>0,"full drafts report capacity without truncating or overflowing");
        full.Erase();check(full.Append('y')&&full.Status=="","editing recovers from the length warning");
    }
    internal static void Layout(Action<bool,string> check)
    {
        var widths=new float[95];Array.Fill(widths,10);widths['W'-32]=24;
        var words=new ChatSpeech("Hello neighbours! How are you?",widths,100);
        check(words.Count==2,"measured text advances into additional three-line pages");
        var narrow=new ChatSpeech(new string('W',120),widths,100);
        check(narrow.Count==10,"long unbroken words wrap and paginate without exceeding the panel");
        string restored="";
        for(int p=0;p<narrow.Count;p++){
            string page=narrow.Page(p);var lines=page.Split('\n');check(lines.Length<=3,"each page fits the normal speech bubble");
            foreach(string line in lines){check(line.Length*24<=100,"wide glyph runs stay inside the text width");restored+=line;}
            check(ReferenceEquals(page,narrow.Page(p)),"drawing reuses cached speech pages");
        }
        check(restored==new string('W',120),"pagination preserves every character of long text");
        check(new ChatSpeech("",widths,100).Page(0)=="","empty editor layout remains bounded");
        foreach(float bad in new[]{0f,-1,float.NaN,float.PositiveInfinity}){
            bool rejected=false;try{_ =new ChatSpeech("Hello",widths,bad);}catch(ArgumentException){rejected=true;}
            check(rejected,"invalid viewport widths cannot hang text layout");
        }
    }
}
