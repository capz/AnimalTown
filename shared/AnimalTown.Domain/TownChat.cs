namespace AnimalTown;

public enum TownChatKind { Player,Attend,Error }

// Transient presentation only: chatting never mutates money, relationships,
// journeys or saves. The live debug flag is checked on every submission.
public readonly struct TownChatLine
{
    public readonly TownChatKind Kind;
    public readonly string Speaker,Text;
    public readonly int Villager;
    public TownChatLine(TownChatKind kind,string speaker,string text,int villager=-1)
    {Kind=kind;Speaker=speaker;Text=text;Villager=villager;}
}

public static class TownChat
{
    public const int MaximumLength=120;
    public const string AttendUsage="Use /attend lowercase-name text";
    public static bool Supported(string text)
    {for(int i=0;i<text.Length;i++)if(text[i]<' '||text[i]>'~')return false;return true;}
    private static TownChatLine Error(string text)=>new TownChatLine(TownChatKind.Error,"",text);
    public static TownChatLine Submit(GameState state,string input,bool debugEnabled)
    {
        if(input==null||input.Length>MaximumLength)return Error("Messages can use up to 120 characters.");
        if(!Supported(input))return Error("Use letters, numbers and punctuation.");
        string text=input.Trim();if(text.Length==0)return Error("Type a message first.");
        // Without debug menus, slash text is ordinary player speech. It never
        // invokes a hidden command, including after the flag changes mid-entry.
        if(!debugEnabled||text[0]!='/')return new TownChatLine(TownChatKind.Player,state.Name,text);
        int commandEnd=text.IndexOf(' ');
        if(commandEnd<0||text.Substring(0,commandEnd)!="/attend")
            return Error(text=="/attend"?AttendUsage:"Unknown command. Try /attend name text.");
        int start=commandEnd+1;while(start<text.Length&&text[start]==' ')start++;
        int end=start;while(end<text.Length&&text[end]!=' ')end++;
        if(start==end||end==text.Length)return Error(AttendUsage);
        for(int i=start;i<end;i++)if(text[i]<'a'||text[i]>'z')return Error("Use the villager's lowercase name.");
        int target=-1;
        for(int i=0;i<Town.NpcActorCount;i++){
            if(!state.NpcPresent(i))continue;
            string name=state.Town.NpcAt(i).Name;if(name.Length!=end-start)continue;
            bool same=true;
            for(int c=0;c<name.Length;c++){
                char value=name[c];if(value>='A'&&value<='Z')value=(char)(value+32);
                if(value!=text[start+c]){same=false;break;}
            }
            if(same){target=i;break;}
        }
        if(target<0)return Error("Villager not found. Use a lowercase name.");
        while(end<text.Length&&text[end]==' ')end++;
        if(end==text.Length)return Error(AttendUsage);
        return new TownChatLine(TownChatKind.Attend,state.Town.NpcAt(target).Name,text.Substring(end),target);
    }
}

// Chat and mail share the same controller keyboard and cached key labels.
// R provides spaces, so multiline entry can use the SP key for a newline.
public sealed class ChatEntry
{
    public const string Screen="chat";
    public const int Columns=10,Rows=5;
    private const string Keys="qwertyuiopasdfghjkl'zxcvbnm,./0123456789 !?-:;()@+";
    private readonly string[] lower=new string[Columns*Rows],upper=new string[Columns*Rows];
    public string Text {get;private set;}="";
    public string CountLabel {get;private set;}
    public int MaximumLength {get;}
    public bool Multiline {get;}
    public string Status="";
    public int Selected {get;private set;}
    public bool Uppercase {get;private set;}
    public ChatEntry(int maximumLength=TownChat.MaximumLength,bool multiline=false)
    {
        if(maximumLength<1)throw new ArgumentOutOfRangeException(nameof(maximumLength));
        MaximumLength=maximumLength;Multiline=multiline;CountLabel="0 / "+MaximumLength;
        for(int i=0;i<Keys.Length;i++){
            char c=Keys[i];lower[i]=c==' '?(multiline?"NL":"SP"):new string(c,1);
            upper[i]=c>='a'&&c<='z'?new string((char)(c-32),1):lower[i];
        }
    }
    public string Label(int index)=>Uppercase?upper[index]:lower[index];
    public void Move(int direction)
    {
        int row=Selected/Columns,col=Selected%Columns;
        if(direction==1)col=(col+Columns-1)%Columns;
        if(direction==2)col=(col+1)%Columns;
        if(direction==4)row=(row+1)%Rows;
        if(direction==8)row=(row+Rows-1)%Rows;
        Selected=row*Columns+col;
    }
    public void ToggleCase(){Uppercase=!Uppercase;}
    public void Type()
    {
        char c=Keys[Selected];if(Uppercase&&c>='a'&&c<='z')c=(char)(c-32);
        Append(Multiline&&c==' '?'\n':c);
    }
    public bool Append(char c)
    {
        if((c<' '||c>'~')&&!(Multiline&&c=='\n')){Status="Use letters, numbers and punctuation.";return false;}
        if(Text.Length>=MaximumLength){Status="Message full. Erase a character to edit.";return false;}
        Text+=new string(c,1);Changed();return true;
    }
    public void Erase(){if(Text.Length>0)Text=Text.Substring(0,Text.Length-1);Changed();}
    private void Changed(){Status="";CountLabel=Text.Length+" / "+MaximumLength;}
}

// Measured wrapping also splits long unbroken words. Each cached page fits
// the existing three-line speech panel; pressing A/B advances normally.
public sealed class ChatSpeech
{
    private readonly string[] pages;
    public int Count=>pages.Length;
    public string Page(int index)=>pages[index];
    public ChatSpeech(string text,float[] advances,float width)
    {
        if(text.Length>TownChat.MaximumLength||!TownChat.Supported(text)||advances.Length!=95||!float.IsFinite(width)||width<=0)throw new ArgumentException("Invalid chat layout");
        for(int i=0;i<advances.Length;i++)if(!float.IsFinite(advances[i])||advances[i]<0)throw new ArgumentException("Invalid glyph width");
        var lines=new string[Math.Max(1,text.Length)];int count=0,start=0;
        while(start<text.Length){
            int end=start,space=-1;float used=0;
            while(end<text.Length){
                float next=advances[text[end]-32];if(end>start&&used+next>width)break;
                if(text[end]==' ')space=end;used+=next;end++;
            }
            if(end<text.Length&&space>start)end=space;
            int stop=end;while(stop>start&&text[stop-1]==' ')stop--;
            lines[count++]=text.Substring(start,stop-start);start=end;
            while(start<text.Length&&text[start]==' ')start++;
        }
        if(count==0){lines[count++]="";}
        pages=new string[(count+2)/3];
        for(int i=0;i<count;i++)pages[i/3]=(i%3==0?"":pages[i/3]+"\n")+lines[i];
    }
}
